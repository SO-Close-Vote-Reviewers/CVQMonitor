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
        private bool syncingReviewData;
        private bool dispose;

        public int UserID { get; private set;}

        public int QueuedReviews { get; set; }

        public HashSet<ReviewItem> Reviews { get; set; }

        public List<ReviewItem> Audits { get; set; }

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

                if (value > 0 && Reviews.Count > 4 && !syncingReviewData)
                {
                    SyncReviewData();
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
            Audits = new List<ReviewItem>();
            ReviewedTags = new Dictionary<string, float>();
        }



        public void Dispose()
        {
            if (dispose) { return; }
            dispose = true;

            mre.Set();

            GC.SuppressFinalize(this);
        }



        private void SyncReviewData()
        {
            syncingReviewData = true;

            Task.Run(() =>
            {
                var fkey = UserDataFetcher.GetFKey();
                var latestRevTime = DateTime.MaxValue;
                var avg = 1D;

                while (!dispose &&
                       reviewsCompleted < ReviewLimit &&
                       (DateTime.UtcNow - latestRevTime).TotalMinutes < avg * 5)
                {
                    ReviewsCompletedCount = UserDataFetcher.FetchTodaysUserReviewCount(fkey, UserID, ref evMan);

                    CheckForAudits(fkey);

                    var activeRevs = new HashSet<ReviewItem>();
                    foreach (var rev in Reviews.OrderByDescending(r => r.Results.First(rr => rr.UserID == UserID).Timestamp))
                    {
                        if ((DateTime.UtcNow - rev.Results.First(rr => rr.UserID == UserID).Timestamp).TotalHours < 1 &&
                            activeRevs.Count < 5)
                        {
                            activeRevs.Add(rev);
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (activeRevs.Count > 0)
                    {
                        var firstRevTime = activeRevs.Min(x => x.Results.First(r => r.UserID == UserID).Timestamp);
                        latestRevTime = activeRevs.Max(x => x.Results.First(r => r.UserID == UserID).Timestamp);
                        avg = activeRevs.Count / (latestRevTime - firstRevTime).TotalMinutes;
                    }

                    mre.WaitOne(TimeSpan.FromMinutes(Math.Max(avg, 0.25)));
                }

                syncingReviewData = false;
            });
        }

        private void CheckForAudits(string fkey)
        {
            var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, UserID, 5);
            var auditIDs = new List<int>();

            foreach (var id in ids)
            {
                if (Reviews.Any(r => r.ID == id))
                {
                    continue;
                }
                else
                {
                    auditIDs.Add(id);
                }
            }

            // It's either empty or we've just been initialised
            // (highly unlikely the user was given 5 audits consecutively).
            if (auditIDs.Count == 0 || auditIDs.Count == 5) { return; }

            foreach (var id in auditIDs)
            {
                var item = new ReviewItem(id, fkey);

                if (item.AuditPassed == null ||
                    (DateTime.UtcNow - item.Results.First(rr => rr.UserID == UserID).Timestamp).TotalHours > 1)
                {
                    continue;
                }

                Reviews.Add(item);
                Audits.Add(item);

                var type = item.AuditPassed == false
                    ? UserEventType.AuditFailed
                    : UserEventType.AuditPassed;

                evMan.CallListeners(type, item);
            }
        }
    }
}
