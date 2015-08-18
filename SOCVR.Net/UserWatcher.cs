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
    public class UserWatcher : IDisposable
    {
        private readonly ManualResetEvent reviewsRefreshMre = new ManualResetEvent(false);
        private readonly double avgReviewsMin;
        private ReviewMonitor reviewMonitor;
        private TagMonitor tagMonitor;
        private bool dispose;

        public int UserID { get; private set; }

        public ReviewMonitor ReviewMonitor { get { return reviewMonitor; } }

        public TagMonitor TagMonitor { get { return tagMonitor; } }

        public EventManager EventManager { get; private set; }

        /// <summary>
        /// A collection of review items completed by the user within the current UTC day.
        /// </summary>
        public List<ReviewItem> TodaysCVReviews { get; private set; }

        /// <summary>
        /// True if the user is actively reviewing.
        /// </summary>
        public bool IsReviewing { get; private set; }



        public UserWatcher(int userID, double avgReviewsPerMins = 1)
        {
            if (avgReviewsPerMins <= 0)
            {
                throw new ArgumentOutOfRangeException("avgReviewsPerMins", "'avgReviewsPerMins' must be non-negative and more than 0.");
            }

            avgReviewsMin = avgReviewsPerMins;
            UserID = userID;
            EventManager = new EventManager();
            TodaysCVReviews = LoadTodaysReviews();
            Task.Run(() => StartWatcher());
            Task.Run(() =>
            {
                while (!dispose)
                {
                    var waitTime = (int)(24 - DateTime.UtcNow.TimeOfDay.TotalHours) * 3600 * 1000;
                    reviewsRefreshMre.WaitOne(waitTime);
                    while (IsReviewing && !dispose) { Thread.Sleep(1000); }
                    TodaysCVReviews.Clear();
                }
            });
        }

        ~UserWatcher()
        {
            Dispose();
        }



        public void Dispose()
        {
            if (dispose) { return; }
            dispose = true;

            reviewsRefreshMre.Set();
            EventManager.Dispose();
            ReviewMonitorPool.CleanUpMonitor(UserID);

            GC.SuppressFinalize(this);
        }



        private void StartWatcher()
        {
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(UserEventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || id != UserID || IsReviewing || dispose) { return; }

                var startTime = DateTime.UtcNow.AddSeconds(-15);
                IsReviewing = true;
                EventManager.CallListeners(UserEventType.ReviewingStarted);

                // Clean up the last used monitor if necessary.
                if (reviewMonitor != null)
                {
                    ReviewMonitorPool.CleanUpMonitor(UserID);
                }

                reviewMonitor = ReviewMonitorPool.NewMonitor(UserID, startTime, TodaysCVReviews, avgReviewsMin);
                reviewMonitor.Start();

                tagMonitor = new TagMonitor(ref reviewMonitor);

                MergeEventManagers();
            };
        }

        private void MergeEventManagers()
        {
            reviewMonitor.EventManager.ConnectListener(UserEventType.InternalException, new Action<Exception>(e =>
            {
                EventManager.CallListeners(UserEventType.InternalException, e);
            }));
            reviewMonitor.EventManager.ConnectListener(UserEventType.ItemReviewed, new Action<ReviewItem>(r =>
            {
                EventManager.CallListeners(UserEventType.ItemReviewed, r);
            }));
            reviewMonitor.EventManager.ConnectListener(UserEventType.ReviewingFinished, new Action<DateTime, DateTime, List<ReviewItem>>((s, e, r) =>
            {
                EventManager.CallListeners(UserEventType.ReviewingFinished, s, e, r);
            }));
            reviewMonitor.EventManager.ConnectListener(UserEventType.AuditFailed, new Action<ReviewItem>(r =>
            {
                EventManager.CallListeners(UserEventType.AuditFailed, r);
            }));
            reviewMonitor.EventManager.ConnectListener(UserEventType.AuditPassed, new Action<ReviewItem>(r =>
            {
                EventManager.CallListeners(UserEventType.AuditPassed, r);
            }));

            tagMonitor.EventManager.ConnectListener(UserEventType.InternalException, new Action<Exception>(e =>
            {
                EventManager.CallListeners(UserEventType.InternalException, e);
            }));
            tagMonitor.EventManager.ConnectListener(UserEventType.CurrentTagsChanged, new Action<List<string>>(t =>
            {
                EventManager.CallListeners(UserEventType.CurrentTagsChanged, t);
            }));
        }

        private List<ReviewItem> LoadTodaysReviews()
        {
            try
            {
                var fkey = User.GetFKey();
                var currentPageNo = 0;
                var reviews = new List<ReviewItem>();

                while (true)
                {
                    Thread.Sleep(3000);

                    currentPageNo++;

                    var pageReviews = User.LoadSinglePageCVReviews(fkey, UserID, currentPageNo);

                    foreach (var review in pageReviews)
                    {
                        if (review.Results.First(r => r.UserID == UserID).Timestamp.Day == DateTime.UtcNow.Day
                            && reviews.All(r => r.ID != review.ID))
                        {
                            reviews.Add(review);
                        }
                        else
                        {
                            return reviews;
                        }
                    }

                    // They probably haven't reviewed anything today after 8 pages...
                    if (currentPageNo > 8) { return reviews; }
                }
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
                return null;
            }
        }
    }
}
