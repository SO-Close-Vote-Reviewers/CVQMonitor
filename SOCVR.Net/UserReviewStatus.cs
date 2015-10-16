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
    public class UserReviewStatus : IDisposable
    {
        private readonly ManualResetEvent mre = new ManualResetEvent(false);
        private EventManager evMan;
        private Action reviewLimitReachedCallback;
        private int reviewsCompleted;
        private bool syncingReviewCount;
        private bool dispose;

        public int UserID { get; private set;}

        public int QueuedReviews { get; set; }

        public HashSet<ReviewItem> Reviews { get; set; }

        public int ReviewsCompletedCount
        {
            get
            {
                return reviewsCompleted;
            }

            internal set
            {
                if (value == ReviewLimit && reviewLimitReachedCallback != null)
                {
                    reviewLimitReachedCallback();
                }

                if (value > ReviewLimit * 0.85 && !syncingReviewCount)
                {
                    SyncReviewCount();
                }

                reviewsCompleted = value;
            }
        }

        internal int ReviewLimit { get; set; }

        internal Dictionary<string, float> ReviewedTags { get; set; }



        public UserReviewStatus(int userID, Action reviewLimitReachedCallback, ref EventManager eventManager)
        {
            this.reviewLimitReachedCallback = reviewLimitReachedCallback;
            evMan = eventManager;
            UserID = userID;
            Reviews = new HashSet<ReviewItem>();
            ReviewedTags = new Dictionary<string, float>();
        }



        public void Dispose()
        {
            if (dispose) { return; }
            dispose = true;

            mre.Set();

            GC.SuppressFinalize(this);
        }



        private void SyncReviewCount()
        {
            syncingReviewCount = true;

            Task.Run(() =>
            {
                var fkey = UserDataFetcher.GetFKey();
                var latestRevTime = DateTime.MaxValue;
                var avg = 0D;

                while (!dispose &&
                       reviewsCompleted < ReviewLimit &&
                       (DateTime.UtcNow - latestRevTime).TotalMinutes < avg * 5)
                {
                    ReviewsCompletedCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, UserID, ref evMan);

                    var activeRevs = new HashSet<ReviewItem>();
                    foreach (var rev in Reviews.OrderByDescending(r => r.Results.First(rr => rr.UserID == UserID).Timestamp))
                    {
                        if (activeRevs.Count < 5)
                        {
                            activeRevs.Add(rev);
                        }
                        else
                        {
                            break;
                        }
                    }

                    var firstRevTime = activeRevs.Min(x => x.Results.First(r => r.UserID == UserID).Timestamp);
                    latestRevTime = activeRevs.Max(x => x.Results.First(r => r.UserID == UserID).Timestamp);
                    avg = activeRevs.Count / (latestRevTime - firstRevTime).TotalMinutes;

                    mre.WaitOne(TimeSpan.FromMinutes(avg));
                }

                syncingReviewCount = false;
            });
        }
    }
}
