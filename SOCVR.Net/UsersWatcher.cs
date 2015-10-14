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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CsQuery;

namespace SOCVRDotNet
{
    public class UsersWatcher : IDisposable
    {
        private readonly Regex todaysReviewCount = new Regex(@"(?i)today \d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ManualResetEvent reviewsRefreshMre = new ManualResetEvent(false);
        private readonly ManualResetEvent reqHandlerMre = new ManualResetEvent(false);
        private string fkey;
        private bool dispose;

        public ConcurrentDictionary<int, User> Users { get; private set; }

        public EventManager EventManager { get; private set; }

        public int ReviewsCompleted { get; private set; }

        /// <summary>
        /// The maximum number of reviews (per minutes) to be processed.
        /// (Default: 100.)
        /// </summary>
        public int ReviewThroughput { get; set; }



        public UsersWatcher(IEnumerable<int> userIDs)
        {
            ReviewThroughput = 100;
            fkey = UserDataFetcher.GetFKey();
            EventManager = new EventManager();
            Users = new ConcurrentDictionary<int, User>();

            var availableReviews = GetReviewsAvailable();

            foreach (var user in userIDs)
            {
                Users[user] = new User(fkey, user);
                Users[user].ReviewStatus.ReviewsCompletedCount = FetchTodaysUserReviewCount(user);
                Users[user].ReviewStatus.ReviewLimit = availableReviews > 1000 ? 40 : 20;
                Thread.Sleep(3000);
            }

            Task.Run(() => AttachWatcherEventListeners());
            Task.Run(() => ResetDailyReviewData());
            Task.Run(() => HandleRequestQueue());
        }

        ~UsersWatcher()
        {
            Dispose();
        }



        public void Dispose()
        {
            if (dispose) { return; }
            dispose = true;

            reqHandlerMre.Set();
            reviewsRefreshMre.Set();
            EventManager.Dispose();

            GC.SuppressFinalize(this);
        }

        public void AddUser(int userID)
        {
            if (Users.ContainsKey(userID)) { return; }

            Users[userID] = new User(fkey, userID);
            Users[userID].ReviewStatus.ReviewsCompletedCount = FetchTodaysUserReviewCount(userID);
            Users[userID].ReviewStatus.ReviewLimit = GetReviewsAvailable() > 1000 ? 40 : 20;
        }



        private void ResetDailyReviewData()
        {
            while (!dispose)
            {
                var waitTime = (int)(24 - DateTime.UtcNow.TimeOfDay.TotalHours) * 3600 * 1000;

                reviewsRefreshMre.WaitOne(waitTime);

                fkey = UserDataFetcher.GetFKey();
                var availableReviews = GetReviewsAvailable();

                foreach (var id in Users.Keys)
                {
                    Users[id].ReviewStatus.ReviewsCompletedCount = 0;
                    Users[id].ReviewStatus.Reviews.Clear();
                    Users[id].ReviewStatus.ReviewLimit = availableReviews > 1000 ? 40 : 20;
                }
            }
        }

        private int FetchTodaysUserReviewCount(int userID)
        {
            try
            {
                var html = new WebClient().DownloadString("http://stackoverflow.com/review/user-info/2/" + userID);
                var match = todaysReviewCount.Match(html);
                var reviewCount = int.Parse(new string(match.Value.Where(char.IsDigit).ToArray()));
                return reviewCount;
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
                return -1;
            }
        }

        private void AttachWatcherEventListeners()
        {
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(UserEventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || !Users.ContainsKey(id) || dispose) { return; }

                Users[id].ReviewStatus.QueuedReviews++;
                Users[id].ReviewStatus.ReviewsCompletedCount++;
            };
        }

        private void HandleRequestQueue()
        {
            var lastUser = default(KeyValuePair<int, User>);

            while (!dispose)
            {
                var user = default(KeyValuePair<int, User>);
                var activeUsers = Users.Values.Count(x => x.ReviewStatus.QueuedReviews > 0);

                if (activeUsers == 0)
                {
                    reqHandlerMre.WaitOne(1000);
                    continue;
                }

                foreach (var u in Users)
                {
                    if (user.Value == null ||
                        (u.Value.ReviewStatus.QueuedReviews > user.Value.ReviewStatus.QueuedReviews &&
                        (activeUsers > 1 ? u.Key != lastUser.Key : true)))
                    {
                        user = u;
                    }
                }

                // Let the User object take control here
                // and make/process the reviews (along with
                // decrementing the QueuedReviews counter).
                var clearedReviews = user.Value.ProcessReviews();

                lastUser = user;
                reqHandlerMre.WaitOne(TimeSpan.FromSeconds((60D / ReviewThroughput) * clearedReviews));
            }
        }

        private int GetReviewsAvailable()
        {
            try
            {
                var doc = CQ.CreateFromUrl("http://stackoverflow.com/review/close/stats");
                var statsTable = doc.Find("table.task-stat-table");
                var cells = statsTable.Find("td");
                var needReview = new string(cells.ElementAt(0).FirstElementChild.InnerText.Where(c => char.IsDigit(c)).ToArray());
                var reviews = 0;

                if (int.TryParse(needReview, out reviews))
                {
                    return reviews;
                }
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
            }

            return -1;
        }
    }
}
