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
    public class User : IDisposable
    {
        private readonly ManualResetEvent scraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent quietScraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent dailyResetMre = new ManualResetEvent(false);
        private EventManager evMan = new EventManager();
        private Stack<int> revIDCache = new Stack<int>();
        private DateTime lastPing;
        private ScraperStatus s1, s2;
        private bool dispose;
        private bool isMod;
        private string fkey;

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

        public HashSet<ReviewItem> Reviews { get; } = new HashSet<ReviewItem>();

        public int CompletedReviewsCount { get; private set; }

        public int ID { get; private set; }



        public User(int userID)
        {
            isMod = IsModerator(userID);
            ID = userID;
            fkey = GlobalCacher.FkeyCached;
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(EventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != ID || dispose) return;

                lastPing = DateTime.UtcNow;

                if (s1 == ScraperStatus.Inactive && s2 == ScraperStatus.Inactive)
                {
                    evMan.CallListeners(EventType.ReviewingStarted);
                }

                if (s1 == ScraperStatus.Inactive)
                {
                    Task.Run(() => ScrapeData());
                }
            };

            Task.Run(() => ResetDailyData());
        }

        ~User()
        {
            Dispose();
        }



        public void Dispose()
        {
            if (dispose) return;
            dispose = true;

            s1 = ScraperStatus.ShutdownRequested;
            s2 = ScraperStatus.ShutdownRequested;
            scraperThrottleMre?.Set();
            quietScraperThrottleMre?.Set();
            dailyResetMre?.Set();
            scraperThrottleMre?.Dispose();
            quietScraperThrottleMre?.Dispose();
            dailyResetMre?.Dispose();
            evMan?.Dispose();

            GC.SuppressFinalize(this);
        }

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

                if (s1 != ScraperStatus.Inactive)
                {
                    s1 = ScraperStatus.ShutdownRequested;
                }
                if (s2 != ScraperStatus.Inactive)
                {
                    s2 = ScraperStatus.ShutdownRequested;
                }
                fkey = GlobalCacher.FkeyCached;

                while (s1 != ScraperStatus.Inactive || s2 != ScraperStatus.Inactive)
                {
                    // Yes yes, I know we shouldn't use Thread.Sleep(), yada yada yada.
                    Thread.Sleep(1000);
                }

                Reviews.Clear();
            }
        }

        private void ScrapeData()
        {
            s1 = ScraperStatus.Active;
            RequestThrottler.LiveUserInstances++;

            var lastRev = lastPing;
            var throttle = new Action(() => scraperThrottleMre.WaitOne(GetThrottlePeriod()));

            try
            {
                while ((DateTime.UtcNow - lastRev).TotalMinutes < 5 && s1 == ScraperStatus.Active)
                {
                    // No need to fetch all the reviews after filling the cache.
                    // Also account for long throttle periods where we may miss
                    // some reviews if we were to simply set a hard-coded value.
                    var idsToFetch = (int)Math.Round(Math.Min(GlobalCacher.ReviewLimitCached(isMod) - revIDCache.Count, Math.Max(GetThrottlePeriod().TotalSeconds / 5, 5)));
                    var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch, throttle)
                        .Where(id => Reviews.All(r => r.ID != id));

                    foreach (var id in ids)
                    {
                        ProcessReview(id, GlobalCacher.ReviewLimitCached(), throttle, ref lastRev);
                    }

                    throttle();
                    CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

                    if (CompletedReviewsCount >= GlobalCacher.ReviewLimitCached(isMod))
                    {
                        LimitReached();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                evMan.CallListeners(EventType.InternalException, ex);
            }

            // If the user hasn't used all their reviews
            // and if they're inactive and if we successfully
            // managed to fetch the total reviews available count,
            // fire off the quiet scraper (safeguard against missing
            // any reviews (namely audits of deleted posts) which we
            // may have missed).
            // If the user is a mod, don't bother checking up on them,
            // we only fire off this scraper to trigger the ReviewingCompleted
            // event (which is not applicable for mods).
            if (CompletedReviewsCount < GlobalCacher.ReviewLimitCached() && 
                s1 == ScraperStatus.Active && !isMod)
            {
                Task.Run(() => QuietScraper());
            }

            RequestThrottler.LiveUserInstances--;
            s1 = ScraperStatus.Inactive;
        }

        private void ProcessReview(int reviewID, int revLimit, Action throttle, ref DateTime lastReview)
        {
            // ID cache control.
            if (revIDCache.Contains(reviewID)) return;
            revIDCache.Push(reviewID);
            while (revIDCache.Count > revLimit) revIDCache.Pop();

            throttle();
            var rev = new ReviewItem(reviewID, fkey);
            var revTime = rev.Results.First(r => r.UserID == ID).Timestamp;

            if (revTime.Day == DateTime.UtcNow.Day)
            {
                Reviews.Add(rev);

                if (rev.AuditPassed != null)
                {
                    var evType = rev.AuditPassed == true ?
                        EventType.AuditPassed :
                        EventType.AuditFailed;

                    evMan.CallListeners(evType, rev);
                }

                evMan.CallListeners(EventType.ItemReviewed, rev);

                lastReview = lastPing < revTime ? revTime : lastPing;
            }
        }

        private void QuietScraper()
        {
            s2 = ScraperStatus.Active;
            var factor = RequestThrottler.BackgroundScraperPollFactor;
            RequestThrottler.LiveUserInstances += 1 / factor;

            try
            {
                // No need to continue scraping if the main scraper is active.
                while ((DateTime.UtcNow - lastPing).TotalMinutes >= 5 && s2 == ScraperStatus.Active)
                {
                    CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

                    if (CompletedReviewsCount >= GlobalCacher.ReviewLimitCached(isMod))
                    {
                        LimitReached();
                        break;
                    }

                    var period = GetThrottlePeriod(Math.Pow(factor, 2));
                    quietScraperThrottleMre.WaitOne(period);
                }
            }
            catch (Exception ex)
            {
                evMan.CallListeners(EventType.InternalException, ex);
            }

            RequestThrottler.LiveUserInstances -= 1 / factor;
            s2 = ScraperStatus.Inactive;
        }

        private void LimitReached()
        {
            var revCpy = new HashSet<ReviewItem>(Reviews);
            evMan.CallListeners(EventType.ReviewingCompleted, revCpy);
        }

        private TimeSpan GetThrottlePeriod(double multiplier = 1)
        {
            var reqsPerMin = RequestThrottler.LiveUserInstances / RequestThrottler.RequestThroughputMin;
            var secsPerReq = Math.Max(reqsPerMin * 60, 5) * multiplier;

            return TimeSpan.FromSeconds(secsPerReq);
        }
    }
}
