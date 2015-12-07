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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    public class User : IDisposable
    {
        private readonly ManualResetEvent dataScraperMre = new ManualResetEvent(false);
        private EventManager evMan = new EventManager();
        private DateTime lastPing;
        private bool scraping;
        private bool dispose;
        private string fkey;

        public EventManager EventManager => evMan;

        public HashSet<ReviewItem> Reviews { get; } = new HashSet<ReviewItem>();

        public int CompletedReviewsCount { get; private set; }

        public int ID { get; private set; }



        public User(int userID)
        {
            ID = userID;
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(UserEventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != ID || dispose) return;

                lastPing = DateTime.UtcNow;

                if (scraping) return;
                scraping = true;

                Task.Run(() => ScrapeData());
            };
        }

        ~User()
        {
            Dispose();
        }



        public void Dispose()
        {
            if (dispose) return;
            dispose = true;



            GC.SuppressFinalize(this);
        }



        private void ResetDailyData()
        {
            Reviews.Clear();
            fkey = RequestThrottler.FkeyCached;
        }

        private void ScrapeData()
        {
            var lastRev = lastPing;
            var throttle = new Action(() =>
            {
                var reqsPerMin = RequestThrottler.LiveUserInstances / RequestThrottler.RequestThroughputMin;
                var secsPerReq = Math.Max(60 / reqsPerMin, 5);
                dataScraperMre.WaitOne(TimeSpan.FromSeconds(secsPerReq));
            });

            RequestThrottler.LiveUserInstances++;

            while ((DateTime.UtcNow - lastRev).TotalMinutes < 5)
            {
                throttle();
                var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, 10).Where(id => Reviews.All(r => r.ID != id));
                // Probably best to save the latest x review item IDs
                // to save re-fetching the same items again for the next day.

                // Filter out reviews that weren't from today.
                // Fetch review items and parse accordingly (check tags/audits).

                throttle();
                CompletedReviewsCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, ID, ref evMan);
            }

            RequestThrottler.LiveUserInstances--;
        }
    }
}
