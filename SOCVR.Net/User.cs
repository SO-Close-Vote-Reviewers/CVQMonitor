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
    /// A Stack Overflow Chat user.
    /// </summary>
    public class User : IDisposable
    {
        private readonly ManualResetEvent initialisationMre = new ManualResetEvent(false);
        private readonly ManualResetEvent scraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent cvrCountUpdaterMre = new ManualResetEvent(false);
        private readonly ManualResetEvent dailyResetMre = new ManualResetEvent(false);
        private readonly Queue<int> revIDCache = new Queue<int>();
        private EventManager evMan = new EventManager();
        private bool initialised;
        private ScraperStatus ss;
        private int completedReviewsCount;
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
                if ((Reviews?.Count ?? 0) > 1)
                {
                    var revRes = Reviews.Select(r => r.Results.First(rr => rr.UserID == ID));
                    var revDur = revRes.Max(r => r.Timestamp) - revRes.Min(r => r.Timestamp);

                    return new TimeSpan(revDur.Ticks / (CompletedReviewsCount - 1));
                }

                return TimeSpan.MinValue;
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
                    var revRes = Reviews.Select(r => r.Results.First(rr => rr.UserID == ID));
                    return revRes.Max(r => r.Timestamp) - revRes.Min(r => r.Timestamp);
                }

                return TimeSpan.MinValue;
            }
        }

        public EventManager EventManager => evMan;

        /// <summary>
        /// Gets a list of all logged reviews for the user.
        /// </summary>
        public HashSet<ReviewItem> Reviews { get; } = new HashSet<ReviewItem>();

        public TimeSpan DetectionLatency { get; private set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// The number of completed reviews logged for the user.
        /// </summary>
        public int CompletedReviewsCount { get { return Math.Max(Reviews.Count, completedReviewsCount); } }

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
            if (RequestThrottler.ReviewsPending.ContainsKey(userID))
            {
                throw new ArgumentException("Cannot create duplicate User instances.", nameof(userID));
            }

            isMod = IsModerator(userID);
            ID = userID;
            fkey = GlobalCacher.FkeyCached;
            RequestThrottler.ReviewsPending[userID] = -1;
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(EventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != ID || dispose) return;

                if (RequestThrottler.ReviewsPending[ID] != -1)
                {
                    RequestThrottler.ReviewsPending[ID]++;
                }
                else
                {
                    RequestThrottler.ReviewsPending[ID] = 1;
                }

                if (!reviewing)
                {
                    reviewing = true;
                    evMan.CallListeners(EventType.ReviewingStarted);
                }
            };

            // So many tasks, such little time.
            Task.Run(() => ResetDailyData());
            Task.Run(() => FetchTodaysReviews());
            Task.Run(() => CheckForLimitReached());
            Task.Run(() => CvrCountUpdater());
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

        private void FetchTodaysReviews()
        {
            var throttle = new Action(() => initialisationMre.WaitOne(2000));
            var idsToFetch = (int)Math.Round(Math.Max(GetThrottlePeriod().TotalSeconds / 5, 3));
            var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch, throttle)
                .Where(id => Reviews.All(r => r.ID != id));

            if (!isMod)
            {
                idsToFetch = Math.Max(GlobalCacher.ReviewLimitCached() - revIDCache.Count, idsToFetch);
            }

            foreach (var id in ids)
            {
                ProcessReview(id, throttle, false);
                throttle();
            }

            completedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

            initialised = true;
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
                fkey = GlobalCacher.FkeyCached;
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
            ss = ScraperStatus.Active;

            var throttle = new Action(() => scraperThrottleMre.WaitOne(GetThrottlePeriod()));

            while (ss == ScraperStatus.Active)
            {
                scraperThrottleMre.WaitOne(TimeSpan.FromMilliseconds(100));

                if (RequestThrottler.ReviewsPending[ID] < 1) continue;

                try
                {
                    // Check the review before the current to
                    // catch any audits we may have missed.
                    var idsToFetch = RequestThrottler.ReviewsPending[ID] + 1;
                    var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch, throttle);

                    foreach (var id in ids)
                    {
                        ProcessReview(id, throttle);
                        RequestThrottler.ReviewsPending[ID]--;
                    }

                    // Compensate for the extra decrement when checking for audits.
                    RequestThrottler.ReviewsPending[ID]++;

                    var avg = ((DateTime.UtcNow - Reviews.Last().Results.Single(x => x.UserID == ID).Timestamp).TotalMilliseconds / 2) + (DetectionLatency.Milliseconds / 2);
                    DetectionLatency = TimeSpan.FromMilliseconds(avg);
                }
                catch (Exception ex)
                {
                    evMan.CallListeners(EventType.InternalException, ex);
                }
            }

            ss = ScraperStatus.Inactive;
        }

        private void ProcessReview(int reviewID, Action throttle, bool raiseEvent = true)
        {
            // ID cache control.
            if (revIDCache.Contains(reviewID)) return;
            revIDCache.Enqueue(reviewID);
            while (revIDCache.Count > GlobalCacher.ReviewLimitCached()) revIDCache.Dequeue();

            throttle();
            var rev = new ReviewItem(reviewID, fkey);

            Reviews.Add(rev);
            RequestThrottler.ReviewsProcessed.Enqueue(DateTime.UtcNow);

            if (raiseEvent)
            {
                if (rev.AuditPassed != null)
                {
                    var evType = rev.AuditPassed == true ?
                        EventType.AuditPassed :
                        EventType.AuditFailed;

                    evMan.CallListeners(evType, rev);
                }

                evMan.CallListeners(EventType.ItemReviewed, rev);
            }
        }

        private void CheckForLimitReached()
        {
            DateTime? limitHit = null;

            while (!dispose)
            {
                // Gasp, Thread.Sleep()... whatever.
                Thread.Sleep(200);

                if (!initialised)
                {
                    continue;
                }
                else
                {
                    if (limitHit == null)
                    {
                        if (CompletedReviewsCount >= GlobalCacher.ReviewLimitCached(isMod))
                        {
                            limitHit = DateTime.UtcNow.Date;
                        }
                        else
                        {
                            limitHit = DateTime.UtcNow.AddDays(-1);
                        }
                    }
                }

                if (limitHit?.Date != DateTime.UtcNow.Date &&
                    CompletedReviewsCount >= GlobalCacher.ReviewLimitCached(isMod))
                {
                    limitHit = DateTime.UtcNow.Date;
                    reviewing = false;

                    // Check if the last review just happened to be an audit.
                    if (Reviews.Count != GlobalCacher.ReviewLimitCached())
                    {
                        var throttle = new Action(() => scraperThrottleMre.WaitOne(GetThrottlePeriod()));
                        var id = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, 1, throttle).Single();
                        ProcessReview(id, throttle);
                    }

                    var revCpy = new HashSet<ReviewItem>(Reviews);
                    evMan.CallListeners(EventType.ReviewingCompleted, revCpy);
                }
            }
        }

        private void CvrCountUpdater()
        {
            while (!dispose)
            {
                cvrCountUpdaterMre.WaitOne(GetThrottlePeriod(true));

                var revsPend = RequestThrottler.ReviewsPending[ID];

                if (!reviewing)
                {
                    RequestThrottler.ReviewsPending[ID] = revsPend == 0 ? -1 : revsPend;
                    continue;
                }
                else
                {
                    RequestThrottler.ReviewsPending[ID] = revsPend == -1 ? 0 : revsPend;
                }

                try
                {
                    completedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);
                }
                catch (Exception ex)
                {
                    evMan.CallListeners(EventType.InternalException, ex);
                }
            }
        }

        private TimeSpan GetThrottlePeriod(bool bgPoller = false)
        {
            DateTime timestamp;
            while (RequestThrottler.ReviewsProcessed.TryPeek(out timestamp) && (DateTime.UtcNow - timestamp).TotalMinutes > 1)
            {
                DateTime temp;
                RequestThrottler.ReviewsProcessed.TryDequeue(out temp);
            }

            var fct = RequestThrottler.BackgroundScraperPollFactor;
            var reqsProcessed = RequestThrottler.ReviewsProcessed.Count * RequestThrottler.ReqsPerReview;
            var bgPollers = RequestThrottler.ReviewsPending.Values.Count(x => x > -1);
            var totalReqsPerMin = reqsProcessed * (1 + ((1 / fct) * bgPollers));
            totalReqsPerMin = totalReqsPerMin == 0 ? 1 / bgPollers : totalReqsPerMin;
            var secsPerReq = 60 / (RequestThrottler.RequestThroughputMin - totalReqsPerMin);
            secsPerReq *= bgPoller ? fct : 1;
            secsPerReq = bgPoller ? secsPerReq < fct ? fct : secsPerReq : secsPerReq;

            return TimeSpan.FromSeconds(secsPerReq);
        }
    }
}