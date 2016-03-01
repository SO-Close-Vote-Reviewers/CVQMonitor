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
        private readonly Queue<DateTime> revTimes = new Queue<DateTime>();
        private readonly Queue<int> revIDCache = new Queue<int>();
        private readonly Func<ReviewItem, DateTime> revItemSelector;
        private EventManager evMan = new EventManager();
        private DateTime revStartTime;
        private DateTime lastRevTime;
        private ScraperStatus ss;
        private int reviewsPending; //TODO: Use this field for calcing how many reviews to fetch.
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
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(EventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != ID || dispose) return;

                while (revTimes.Count > 5) revTimes.Dequeue();
                revTimes.Enqueue(DateTime.UtcNow);
                lastRevTime = DateTime.UtcNow;
                reviewsPending++;

                if (!reviewing)
                {
                    reviewing = true;
                    evMan.CallListeners(EventType.ReviewingStarted);
                    revStartTime = DateTime.UtcNow;
                }

                if (reviewsPending + CompletedReviewsCount >= GlobalCacher.ReviewLimit(isMod))
                {
                    evMan.CallListeners(EventType.ReviewingCompleted);
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

            ss = ScraperStatus.ShutdownRequested;

            dailyResetMre?.Set();
            cvrCountUpdaterMre?.Set();
            initialisationMre?.Set();
            scraperThrottleMre?.Set();

            dailyResetMre?.Dispose();
            cvrCountUpdaterMre?.Dispose();
            initialisationMre?.Dispose();
            scraperThrottleMre?.Dispose();

            evMan?.Dispose();

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

                if (ss != ScraperStatus.Inactive)
                {
                    ss = ScraperStatus.ShutdownRequested;
                }
                fkey = GlobalCacher.Fkey;
                reviewing = false;

                // Wait for the scraper to die before clearing Reviews.
                while (ss != ScraperStatus.Inactive)
                {
                    // Yes yes, I know we shouldn't use Thread.Sleep(), yada yada yada.
                    Thread.Sleep(250);
                }

                Reviews.Clear();
                reviewing = false;
            }
        }

        private void ScrapeData()
        {
            var checkCount = false;
            ss = ScraperStatus.Active;

            while (ss == ScraperStatus.Active && !dispose)
            {
                scraperThrottleMre.WaitOne(500);

                if (!reviewing || HandleInactivity(ref checkCount)) continue;

                RevBasedThrottle(0.5);

                try
                {
                    var idsToFetch = (int)Math.Round(Math.Max(reviewsPending, 1) * 1.5);
                    reviewsPending = 0;
                    Throttle(1);
                    var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch);

                    foreach (var id in ids)
                    {
                        ProcessReview(id);
                    }

                    if (checkCount)
                    {
                        Throttle(1);
                        CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);
                    }
                    checkCount = !checkCount;

                    if (Math.Max(CompletedReviewsCount, Reviews.Count) >= GlobalCacher.ReviewLimit(isMod))
                    {
                        HandleReviewingCompleted();
                    }
                }
                catch (Exception ex)
                {
                    evMan.CallListeners(EventType.InternalException, ex);
                }
            }

            ss = ScraperStatus.Inactive;
        }

        private bool HandleInactivity(ref bool checkCount)
        {
            // Continue checking the review count if we don't get any new
            // reviews for a bit (the user may have reviewed a deleted audit).
            // (The user may also just be afk.)
            if ((DateTime.UtcNow - lastRevTime).TotalMinutes > 3)
            {
                RevBasedThrottle();

                Throttle(1);
                CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

                if (Math.Max(CompletedReviewsCount, Reviews.Count) >= GlobalCacher.ReviewLimit(isMod))
                {
                    HandleReviewingCompleted();
                }

                checkCount = false;
                return true;
            }

            return false;
        }

        private void ProcessReview(int reviewID)
        {
            // ID cache control.
            if (revIDCache.Contains(reviewID)) return;
            revIDCache.Enqueue(reviewID);
            while (revIDCache.Count > GlobalCacher.ReviewLimit()) revIDCache.Dequeue();

            Throttle(1);
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
            evMan.CallListeners(EventType.ReviewingCompleted, Reviews);
        }

        private void Throttle(int reqsMade)
        {
            while (RequestThrottler.RequestsRemaining - reqsMade < 0)
            {
                scraperThrottleMre.WaitOne(TimeSpan.FromSeconds(60D / RequestThrottler.RequestThroughputMin));
            }

            RequestThrottler.RequestsRemaining -= reqsMade;
        }

        private void RevBasedThrottle(double factor = 1)
        {
            if (revTimes.Count == 0) return;

            for (var i = 0; i < 3; i++)
            {
                var wait = TimeSpan.FromSeconds((revTimes.Average(t => (DateTime.UtcNow - t).TotalSeconds) / 3) * factor);
                scraperThrottleMre.WaitOne(wait);
            }
        }
    }
}