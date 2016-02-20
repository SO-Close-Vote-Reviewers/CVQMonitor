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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    public class User : IDisposable
    {
        private readonly ManualResetEvent initialisationMre = new ManualResetEvent(false);
        private readonly ManualResetEvent scraperThrottleMre = new ManualResetEvent(false);
        private readonly ManualResetEvent cvrCountUpdaterMre = new ManualResetEvent(false);
        private readonly ManualResetEvent dailyResetMre = new ManualResetEvent(false);
        private EventManager evMan = new EventManager();
        private readonly Queue<int> revIDCache = new Queue<int>();
        private ScraperStatus ss;
        private DateTime initialised;
        private int completedReviewsCount;
        private bool isReviewing;
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

        public TimeSpan DetectionLatency { get; private set; } = TimeSpan.FromMilliseconds(500);

        public int CompletedReviewsCount { get { return Math.Max(Reviews.Count, completedReviewsCount); } }

        public int ID { get; private set; }



        public User(int userID)
        {
            if (RequestThrottler.ReviewsPending.ContainsKey(userID))
            {
                throw new ArgumentException("Cannot create duplicate User instances.", nameof(userID));
            }

            isMod = IsModerator(userID);
            ID = userID;
            fkey = GlobalCacher.FkeyCached;
            RequestThrottler.ReviewsPending[userID] = 0;
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
                
                if (!isReviewing)
                {
                    isReviewing = true;
                    evMan.CallListeners(EventType.ReviewingStarted);
                }
            };

            RequestThrottler.ReviewsPending[ID] = -1;

            // So many tasks, such little time.
            Task.Run(() => ResetDailyData());
            Task.Run(() => FetchTodaysReviews());
            Task.Run(() => CheckForLimitReached());
            Task.Run(() => CvrCountUpdater());
            Task.Run(() => ScrapeData());
        }

        ~User()
        {
            Dispose();
        }



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
                fkey = GlobalCacher.FkeyCached;

                while (ss != ScraperStatus.Inactive)
                {
                    // Yes yes, I know we shouldn't use Thread.Sleep(), yada yada yada.
                    Thread.Sleep(250);
                }

                Reviews.Clear();
                isReviewing = false;
            }
        }

        private void ScrapeData()
        {
            ss = ScraperStatus.Active;

            var throttle = new Action(() => scraperThrottleMre.WaitOne(GetThrottlePeriod()));

            while (ss == ScraperStatus.Active)
            {
                scraperThrottleMre.WaitOne(TimeSpan.FromMilliseconds(200));

                if (RequestThrottler.ReviewsPending[ID] == 0) continue;

                try
                {
                    var idsToFetch = RequestThrottler.ReviewsPending[ID];
                    var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch, throttle)
                        .Where(id => Reviews.All(r => r.ID != id));

                    foreach (var id in ids)
                    {
                        ProcessReview(id, throttle);
                        RequestThrottler.ReviewsPending[ID]--;
                    }

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

        private void FetchTodaysReviews()
        {
            var throttle = new Action(() => initialisationMre.WaitOne(GetThrottlePeriod()));
            var idsToFetch = (int)Math.Round(Math.Max(GetThrottlePeriod().TotalSeconds / 5, 3));
            var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, idsToFetch, throttle)
                .Where(id => Reviews.All(r => r.ID != id));

            if (!isMod)
            {
                idsToFetch = Math.Max(GlobalCacher.ReviewLimitCached() - revIDCache.Count, idsToFetch);
            }

            foreach (var id in ids)
            {
                ProcessReview(id, throttle);
            }
            
            initialised = DateTime.UtcNow;
        }

        private void ProcessReview(int reviewID, Action throttle, bool raiseEvents = true)
        {
            // ID cache control.
            if (revIDCache.Contains(reviewID)) return;
            revIDCache.Enqueue(reviewID);
            while (revIDCache.Count > GlobalCacher.ReviewLimitCached()) revIDCache.Dequeue();

            throttle();
            var rev = new ReviewItem(reviewID, fkey);
            var revTime = rev.Results.First(r => r.UserID == ID).Timestamp;

            if (revTime.Day == DateTime.UtcNow.Day)
            {
                Reviews.Add(rev);
                RequestThrottler.ProcessedReviews.Enqueue(DateTime.UtcNow);

                if (raiseEvents)
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
        }

        private void CheckForLimitReached()
        {
            var limitHit = DateTime.UtcNow.AddDays(-1);

            while (!dispose)
            {
                Thread.Sleep(250);
                
                if ((DateTime.UtcNow - initialised).TotalMinutes < 1)
                {
                    if (CompletedReviewsCount >= GlobalCacher.ReviewLimitCached(isMod))
                    {
                        limitHit = DateTime.UtcNow.Date;
                    }
                }
                else
                {
                    continue;
                }
                
                if (limitHit.Date != DateTime.UtcNow.Date &&
                    CompletedReviewsCount >= GlobalCacher.ReviewLimitCached(isMod))
                {
                    limitHit = DateTime.UtcNow.Date;
                    var revCpy = new HashSet<ReviewItem>(Reviews);
                    evMan.CallListeners(EventType.ReviewingCompleted, revCpy);
                }
            }
        }

        private void CvrCountUpdater()
        {
            while (!dispose)
            {
                try
                {
                    cvrCountUpdaterMre.WaitOne(GetThrottlePeriod(true));
                    
                    if (isReviewing)
                    {
                        completedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);
                    }
                }
                catch (Exception ex)
                {
                    evMan.CallListeners(EventType.InternalException, ex);
                }
            }
        }

        private TimeSpan GetThrottlePeriod(bool isBgScraper = false)
        {
            while (true)
            {
                DateTime temp;
                RequestThrottler.ProcessedReviews.TryPeek(out temp);
                if ((DateTime.UtcNow - temp).TotalMinutes > 1)
                {
                    RequestThrottler.ProcessedReviews.TryDequeue(out temp);
                }
                else
                {
                    break;
                }
            }
            var totalReqs = RequestThrottler.ProcessedReviews.Count;
            totalReqs += RequestThrottler.ReviewsPending.Values.Sum(x => x > -1 ? 1 / RequestThrottler.BackgroundScraperPollFactor : 0);
            
            var delayMin = toalReqs / RequestThrottler.RequestThroughputMin;
            var secsPerReq = reqsPerMin * 60;
            scesPerReq = isBgScraper ? secsPerReq * RequestThrottler.BackgroundScraperPollFactor : secsPerReq;

            return TimeSpan.FromSeconds(secsPerReq);
        }
    }
}
