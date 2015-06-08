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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsQuery;

namespace SOCVRDotNet
{
    public class UserWatcher : IDisposable
    {
        private static readonly ManualResetEvent reviewsRefreshMre = new ManualResetEvent(false);
        private bool dispose;

        public int UserID { get; private set; }

        public EventManager EventManager { get; private set; }

        /// <summary>
        /// A collection of review items completed by the user this UTC day.
        /// </summary>
        public List<ReviewItem> TodaysCVReviews { get; private set; }

        /// <summary>
        /// True if the user is actively reviewing.
        /// </summary>
        public bool IsReviewing { get; private set; }

        /// <summary>
        /// The multiplicative factor used when determining whether
        /// the current reviewing session has completed, based on
        /// the duration of inactivity after completing a review.
        /// (Default 4.)
        /// </summary>
        public float IdleFactor { get; set; }

        /// <summary>
        /// The multiplicative factor used when determining whether
        /// the current reviewing session has completed, based on
        /// the duration of inactivity after failing an audit.
        /// (Default 2.)
        /// </summary>
        public float AuditFailureFactor { get; set; }

        public double AvgReviewsPerMin { get; private set; }



        public UserWatcher(int userID, double avgReviewsPerMins = 0)
        {
            IdleFactor = 4;
            AuditFailureFactor = 2;
            AvgReviewsPerMin = avgReviewsPerMins;
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
                    // Check again, since we're waiting a pretty long time.
                    if (dispose) { return; }
                    TodaysCVReviews = LoadTodaysReviews();
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
            GC.SuppressFinalize(this);
        }



        private void StartWatcher()
        {
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(UserEventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || IsReviewing || dispose || id != UserID) { return; }
                IsReviewing = true;
                var startTime = DateTime.UtcNow - TimeSpan.FromSeconds(10);
                EventManager.CallListeners(UserEventType.StartedReviewing);
                Task.Run(() => MonitorReviews(startTime));
            };
        }

        private void MonitorReviews(DateTime startTime)
        {
            var mre = new ManualResetEvent(false);
            var reviewsAvailable = GetReviewsAvailable();
            var sessionReviews = new List<ReviewItem>();
            var fkey = User.GetFKey();
            var latestTimestamp = DateTime.MaxValue;
            var updateAvg = new Action(() =>
            {
                var sessionAvg = (DateTime.UtcNow - startTime).TotalMinutes / sessionReviews.Count;
                AvgReviewsPerMin = (AvgReviewsPerMin + sessionAvg) / (AvgReviewsPerMin == 0 ? 1 : 2);
            });
            var endSession = new Action(() =>
            {
                mre.Dispose();
                TodaysCVReviews.AddRange(sessionReviews);
                IsReviewing = false;
                startTime = startTime.AddMinutes(-AvgReviewsPerMin);
                updateAvg();
                EventManager.CallListeners(UserEventType.FinishedReviewing, startTime, latestTimestamp, sessionReviews);
            });
            ReviewItem latestReview = null;

            Task.Run(() => MonitorTags());

            while (!dispose)
            {
                var pageReviews = LoadSinglePageCVReviews(fkey, 1);

                foreach (var review in pageReviews)
                {
                    if (sessionReviews.Any(r => r.ID == review.ID) ||
                        review.Results.First(r => r.UserID == UserID).Timestamp < startTime)
                    {
                        continue;
                    }

                    sessionReviews.Add(review);

                    // Notify audit listeners if necessary.
                    if (review.AuditPassed != null)
                    {
                        var type = review.AuditPassed == false
                            ? UserEventType.FailedAudit
                            : UserEventType.PassedAudit;
                        EventManager.CallListeners(type, review);
                    }

                    latestTimestamp = review.Results.First(r => r.UserID == UserID).Timestamp;
                    latestReview = review;
                    EventManager.CallListeners(UserEventType.ReviewedItem, review);
                }

                updateAvg();

                if (latestReview != null && latestReview.AuditPassed == false &&
                    (DateTime.UtcNow - latestTimestamp).TotalMinutes > AvgReviewsPerMin * AuditFailureFactor)
                {
                    // We can be pretty sure they've been temporarily banned.
                    endSession();
                    return;
                }

                if (sessionReviews.Count + TodaysCVReviews.Count == (reviewsAvailable > 1000 ? 40 : 20))
                {
                    // They've ran out of reviews.
                    endSession();
                    return;
                }

                if ((DateTime.UtcNow - latestTimestamp).TotalMinutes > AvgReviewsPerMin * IdleFactor)
                {
                    // They've probably finished.
                    endSession();
                    return;
                }

                mre.WaitOne(TimeSpan.FromSeconds(15));
            }
        }

        /// <summary>
        /// Warning, this method is still experimental.
        /// </summary>
        private void MonitorTags()
        {
            var lastTagReviewTimestamp = DateTime.MaxValue;
            var reviewsSinceActiveTag = 0;
            var allTags = new ConcurrentDictionary<string, float>();
            var highestKv = new KeyValuePair<string, float>(null, -1);
            var reviewCount = 0;
            var addTag = new Action<ReviewItem>(r =>
            {
                if (r.AuditPassed != null) { return; }

                reviewCount++;

                foreach (var tag in r.Tags)
                {
                    if (allTags.ContainsKey(tag))
                    {
                        allTags[tag]++;
                    }
                    else
                    {
                        allTags[tag] = 1;
                    }
                }

                if (!string.IsNullOrEmpty(highestKv.Key))
                {
                    if (r.Tags.Contains(highestKv.Key))
                    {
                        lastTagReviewTimestamp = r.Results.First(rr => rr.UserID == UserID).Timestamp;
                        reviewsSinceActiveTag = 0;
                    }
                    else
                    {
                        reviewsSinceActiveTag++;
                    }
                }
            });
            var reviewedTag = new Action(() =>
            {
                EventManager.CallListeners(UserEventType.ReviewedTag, highestKv, lastTagReviewTimestamp);
                allTags[highestKv.Key] = 0;
                highestKv = new KeyValuePair<string, float>(null, -1);
            });

            EventManager.ConnectListener(UserEventType.ReviewedItem, addTag);

            while (IsReviewing)
            {
                Thread.Sleep(1000);

                if (reviewCount < 9) { continue; }

                var tagsSum = allTags.Sum(t => t.Value);
                var highKvs = allTags.Where(t => t.Value >= tagsSum * (1F / 15)).ToDictionary(t => t.Key, t => t.Value);
                var firstVal = highKvs.Values.FirstOrDefault();

                if (highKvs.Count > 3 ||
                    (highKvs.All(t => t.Value == firstVal) && highKvs.Count > 1) ||
                    reviewsSinceActiveTag >= 3 ||
                    (DateTime.UtcNow - lastTagReviewTimestamp).TotalSeconds > (60 / AvgReviewsPerMin) * 3)
                {
                    // The data set doesn't contain enough data to determine
                    // the actual tag being reviewed, or, the user's tag scope
                    // is too broad for this analysis, or, the last review with
                    // the active tag was completed > 5 * AvgSecsPerReview seconds ago (or > 5 reviews ago).

                    // The user has been reviewing a tag, so they've
                    // must likely switched to a different one (or simply stopped).
                    if (!string.IsNullOrEmpty(highestKv.Key) && IsReviewing)
                    {
                        reviewedTag();
                    }

                    continue;
                }

                var prevTag = highestKv.Key;
                foreach (var kv in allTags)
                {
                    if (kv.Value > highestKv.Value)
                    {
                        highestKv = kv;
                    }
                }

                if (highestKv.Key != prevTag && !string.IsNullOrEmpty(prevTag))
                {
                    reviewedTag();
                }
            }

            EventManager.DisconnectListener(UserEventType.ReviewedItem, addTag);
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
                    currentPageNo++;

                    var pageReviews = LoadSinglePageCVReviews(fkey, currentPageNo);

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

        private List<ReviewItem> LoadSinglePageCVReviews(string fkey, int page)
        {
            if (page < 1) { throw new ArgumentOutOfRangeException("page", "Must be more than 0."); }

            var reviews = new List<ReviewItem>();

            try
            {
                var reqUrl = "http://stackoverflow.com/ajax/users/tab/" + UserID + "?tab=activity&sort=reviews&page=" + page;
                var pageHtml = new WebClient { Encoding = Encoding.UTF8 }.DownloadString(reqUrl);
                var dom = CQ.Create(pageHtml);

                foreach (var j in dom["td"])
                {
                    if (j.FirstElementChild == null ||
                        string.IsNullOrEmpty(j.FirstElementChild.Attributes["href"]) ||
                        !j.FirstElementChild.Attributes["href"].StartsWith(@"/review/close/"))
                    {
                        continue;
                    }

                    var url = j.FirstElementChild.Attributes["href"];
                    var reviewID = url.Remove(0, url.LastIndexOf('/') + 1);
                    var id = int.Parse(reviewID);

                    if (reviews.Any(r => r.ID == id)) { continue; }
                    reviews.Add(new ReviewItem(id, fkey));
                }
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
            }

            return reviews;
        }

        private static int GetReviewsAvailable()
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

            return -1;
        }
    }
}
