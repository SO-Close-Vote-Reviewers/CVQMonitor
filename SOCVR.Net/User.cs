/*
 * SOCVR.Net. A .Net (4.5) library for fetching Stack Overflow user close vote review data.
 * Copyright © 2015, SO-Close-Vote-Reviewers.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    /// <summary>
    /// A Stack Overflow user.
    /// </summary>
    public class User : IDisposable
    {
        private readonly ManualResetEvent initialisationMre = new ManualResetEvent(false);
        private readonly ManualResetEvent scraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent cvrCountUpdaterMre = new ManualResetEvent(false);
        private readonly ManualResetEvent dailyResetMre = new ManualResetEvent(false);
        private readonly Queue<int> revIDCache = new Queue<int>();
        private readonly Func<ReviewItem, DateTime> revItemSelector;
        private EventManager evMan = new EventManager();
        private DateTime revStartTime;
        private int reviewsPending;
        private bool reviewing;
        private bool dispose;
        private bool isMod;
        private string fkey;


        /// <summary>
        /// Calculates the average review speed for all recorded reviews.
        /// </summary>
        public TimeSpan AvgDurationPerReview
        {
            get
            {
                if (ReviewSessionDuration != TimeSpan.MinValue)
                {
                    return TimeSpan.FromSeconds(ReviewSessionDuration.TotalSeconds / Reviews.Count);
                }

                return TimeSpan.FromSeconds(20);
            }
        }

        /// <summary>
        /// Gets the time between the first and last logged review.
        /// </summary>
        public TimeSpan ReviewSessionDuration
        {
            get
            {
                if ((Reviews?.Count ?? 0) > 1)
                {
                    var latestRevTime = Reviews.Max(revItemSelector);
                    var firstRevTime = Reviews.Min(revItemSelector);
                    var durRaw = latestRevTime - firstRevTime;

                    return TimeSpan.FromSeconds((durRaw.TotalSeconds / Reviews.Count) * (Reviews.Count + 1));
                }

                return TimeSpan.MinValue;
            }
        }

        /// <summary>
        /// Provides a means to (dis)connect event listeners (Delegates).
        /// </summary>
        public EventManager EventManager => evMan;

        /// <summary>
        /// Gets a list of all logged reviews for the user.
        /// </summary>
        public HashSet<ReviewItem> Reviews { get; } = new HashSet<ReviewItem>();

        /// <summary>
        /// The average duration between a user completing a
        /// review item to the ItemReviewed event being raised.
        /// </summary>
        public TimeSpan DetectionLatency { get; private set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The total number of completed reviews for the user.
        /// </summary>
        public int CompletedReviewsCount { get; private set; }

        /// <summary>
        /// The user's profile Id number.
        /// </summary>
        public int ID { get; private set; }

        /// <summary>
        /// Creates a new User object instance using a given profile Id.
        /// </summary>
        /// <param name="userID">The profile id of the user. This is the same number as in the profile url.</param>
        public User(int userID)
        {
            isMod = IsModerator(userID);
            ID = userID;
            fkey = GlobalCacher.Fkey;
            revItemSelector = new Func<ReviewItem, DateTime>(r => r.Results.Single(rr => rr.UserID == ID).Timestamp);
            RequestThrottler.ActiveUsers[ID] = false;
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(EventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != ID || dispose) return;

                reviewsPending++;

                if (!reviewing)
                {
                    reviewing = true;
                    revStartTime = DateTime.UtcNow;
                    RequestThrottler.ActiveUsers[ID] = true;
                    evMan.CallListeners(EventType.ReviewingStarted);
                }
            };

            Task.Run(() => ResetDailyData());
            Task.Run(() => ScrapeData());
        }

        /// <summary>
        /// Deconstructor for this instance.
        /// </summary>
        ~User()
        {
            Dispose();
        }

        /// <summary>
        /// Disposes all resources used by this instance.
        /// </summary>
        public void Dispose()
        {
            if (dispose) return;
            dispose = true;

            dailyResetMre?.Set();
            cvrCountUpdaterMre?.Set();
            initialisationMre?.Set();
            scraperThrottleMre?.Set();

            dailyResetMre?.Dispose();
            cvrCountUpdaterMre?.Dispose();
            initialisationMre?.Dispose();
            scraperThrottleMre?.Dispose();

            evMan?.Dispose();

            var temp = false;
            RequestThrottler.ActiveUsers.TryRemove(ID, out temp);

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Tells if the given user is a moderator on Stack Overflow.
        /// </summary>
        /// <param name="userID">The user id to look up.</param>
        /// <returns></returns>
        public static bool IsModerator(int userID)
        {
            try
            {
                return new WebClient()
                    .DownloadString($"http://stackoverflow.com/users/{userID}")
                    .Contains("<span class=\"mod-flair\" title=\"moderator\">");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }

        private void ResetDailyData()
        {
            while (!dispose)
            {
                var wait = TimeSpan.FromHours(24 - DateTime.UtcNow.TimeOfDay.TotalHours);

                dailyResetMre.WaitOne(wait);

                reviewing = false;
                RequestThrottler.ActiveUsers[ID] = false;
                fkey = GlobalCacher.Fkey;
                Reviews.Clear();
            }
        }

        private void ScrapeData()
        {
            var throttle = new Action(() => scraperThrottleMre.WaitOne(TimeSpan.FromSeconds(RequestThrottler.ActiveUsers.Values.Count(x => x) * RequestThrottler.ThrottleFactor)));

            while (!dispose)
            {
                scraperThrottleMre.WaitOne(100);

                if (!reviewing) continue;

                try
                {
                    throttle();
                    var idsToFetch = (int)Math.Round(Math.Max(reviewsPending, 1) * 1.5);
                    reviewsPending = 0;
                    var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch);

                    foreach (var id in ids)
                    {
                        ProcessReview(id, throttle);
                    }

                    throttle();
                    CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

                    if (CompletedReviewsCount >= GlobalCacher.ReviewLimit(isMod))
                    {
                        HandleReviewingCompleted();
                    }
                }
                catch (Exception ex)
                {
                    evMan.CallListeners(EventType.InternalException, ex);
                }
            }
        }

        private void ProcessReview(int reviewID, Action throttle)
        {
            // ID cache control.
            if (revIDCache.Contains(reviewID)) return;
            revIDCache.Enqueue(reviewID);
            while (revIDCache.Count > GlobalCacher.ReviewLimit()) revIDCache.Dequeue();

            throttle();
            var rev = new ReviewItem(reviewID, fkey);
            var revTime = rev.Results.Single(x => x.UserID == ID).Timestamp;

            if (revTime.Date != DateTime.UtcNow.Date ||
                (revStartTime > revTime &&
                (revStartTime - revTime).TotalMinutes > 3))
            {
                return;
            }

            var avg = ((DateTime.UtcNow - rev.Results.Single(x => x.UserID == ID).Timestamp).TotalMilliseconds / 2) + (DetectionLatency.Milliseconds / 2);
            DetectionLatency = TimeSpan.FromMilliseconds(avg);

            Reviews.Add(rev);

            if (rev.AuditPassed != null)
            {
                var evType = rev.AuditPassed == true ?
                    EventType.AuditPassed :
                    EventType.AuditFailed;

                evMan.CallListeners(evType, rev);
            }

            evMan.CallListeners(EventType.ItemReviewed, rev);
        }

        private void HandleReviewingCompleted()
        {
            reviewing = false;
            RequestThrottler.ActiveUsers[ID] = false;
            evMan.CallListeners(EventType.ReviewingCompleted, Reviews);
        }
    }
}