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
using System.Threading;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    public class User : IDisposable
    {
        private readonly ManualResetEvent scraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent quiestScraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent dailyResetMre = new ManualResetEvent(false);
        private EventManager evMan = new EventManager();
        private Stack<int> revIDCache = new Stack<int>();
        private DateTime lastPing;
        private bool resetScraper;
        private bool resetQScraper;
        private bool scraperResetDone = true;
        private bool qScraperResetDone = true;
        private bool scraping;
        private bool dispose;
        private bool started;
        private bool finished;
        private string fkey;

        public EventManager EventManager => evMan;

        public HashSet<ReviewItem> Reviews { get; } = new HashSet<ReviewItem>();

        public int CompletedReviewsCount { get; private set; }

        public int ID { get; private set; }



        public User(int userID)
        {
            ID = userID;
            fkey = RequestThrottler.FkeyCached;
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(EventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != ID || dispose) return;

                lastPing = DateTime.UtcNow;

                if (!started)
                {
                    started = true;
                    evMan.CallListeners(EventType.ReviewingStarted);
                }

                if (!scraping)
                {
                    scraping = true;
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

            scraperThrottleMre.Set();

            GC.SuppressFinalize(this);
        }



        private void ResetDailyData()
        {
            while (!dispose)
            {
                var wait = TimeSpan.FromHours(24 - DateTime.UtcNow.TimeOfDay.TotalHours);

                dailyResetMre.WaitOne(wait);

                started = false;
                resetScraper = true;
                resetQScraper = true;
                fkey = RequestThrottler.FkeyCached;

                while (!scraperResetDone || !qScraperResetDone)
                {
                    // Yes yes, I know we shouldn't use Thread.Sleep(), yada yada yada.
                    Thread.Sleep(1000);
                }

                if (CompletedReviewsCount > 0 && !finished)
                {
                    var revCopy = new HashSet<ReviewItem>(Reviews);
                    evMan.CallListeners(EventType.ReviewingCompleted, revCopy);
                }

                Reviews.Clear();
                resetScraper = false;
                resetQScraper = false;
                finished = false;
            }
        }

        private void ScrapeData()
        {
            RequestThrottler.LiveUserInstances++;
            scraperResetDone = false;
            var lastRev = lastPing;
            var throttle = new Action(() => scraperThrottleMre.WaitOne(GetThrottlePeriod()));
            var revLimit = -1;

            try
            {
                revLimit = (RequestThrottler.ReviewsAvailableCached ?? 1000) >= 1000 ? 40 : 20;

                while ((DateTime.UtcNow - lastRev).TotalMinutes < 5 && !resetScraper && !dispose)
                {
                    // No need to fetch all the reviews after filling the cache.
                    // Also account for long throttle periods where we may miss
                    // some reviews if we were to simply set a hard-coded value.
                    var idsToFetch = (int)Math.Round(Math.Max(revLimit - revIDCache.Count, Math.Max(GetThrottlePeriod().TotalSeconds / 5, 5)));
                    var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch, throttle)
                        .Where(id => Reviews.All(r => r.ID != id));

                    foreach (var id in ids) ProcessReview(id, revLimit, throttle, ref lastRev);

                    throttle();
                    CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

                    if (CompletedReviewsCount >= revLimit)
                    {
                        finished = true;
                        evMan.CallListeners(EventType.ReviewingCompleted, Reviews);
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
            if (revLimit != -1 && CompletedReviewsCount < revLimit && !resetScraper)
            {
                Task.Run(() => QuietScraper());
            }

            scraperResetDone = true;
            scraping = false;
            RequestThrottler.LiveUserInstances--;
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
            var factor = RequestThrottler.BackgroundScraperPollFactor;

            RequestThrottler.LiveUserInstances += 1 / factor;
            qScraperResetDone = false;

            try
            {
                // No need to continue scraping if the main scraper is active.
                while ((DateTime.UtcNow - lastPing).TotalMinutes >= 5 && !resetQScraper && !dispose)
                {
                    CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);

                    var period = GetThrottlePeriod(Math.Pow(factor, 2));
                    quiestScraperThrottleMre.WaitOne(period);
                }
            }
            catch (Exception ex)
            {
                evMan.CallListeners(EventType.InternalException, ex);
            }

            qScraperResetDone = true;
            RequestThrottler.LiveUserInstances -= 1 / factor;
        }

        private TimeSpan GetThrottlePeriod(double multiplier = 1)
        {
            var reqsPerMin = RequestThrottler.LiveUserInstances / RequestThrottler.RequestThroughputMin;
            var secsPerReq = Math.Max(reqsPerMin * 60, 5) * multiplier;

            return TimeSpan.FromSeconds(secsPerReq);
        }
    }
}
