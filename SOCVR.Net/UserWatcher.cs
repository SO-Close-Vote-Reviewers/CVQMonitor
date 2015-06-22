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

        public double AvgReviewsPerMin { get; private set; }

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

        /// <summary>
        /// Sets whether or not to monitor/analyse the user's tag filter.
        /// </summary>
        public bool TagTrackingEnabled { get; set; }

        /// <summary>
        /// The interval at which to actively poll a user's profile
        /// for CV review data whilst they're reviewing. (Default 10 seconds.)
        /// </summary>
        public TimeSpan PollInterval { get; set; }



        public UserWatcher(int userID, double avgReviewsPerMins = 1)
        {
            if (avgReviewsPerMins <= 0)
            {
                throw new ArgumentOutOfRangeException("avgReviewsPerMins", "'avgReviewsPerMins' must be non-negative and more than 0.");
            }

            IdleFactor = 4;
            AuditFailureFactor = 2;
            PollInterval = TimeSpan.FromSeconds(10);
            TagTrackingEnabled = true;
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
            GC.SuppressFinalize(this);
        }



        private void StartWatcher()
        {
            GlobalDashboardWatcher.OnException += ex => EventManager.CallListeners(UserEventType.InternalException, ex);
            GlobalDashboardWatcher.UserEnteredQueue += (q, id) =>
            {
                if (q != ReviewQueue.CloseVotes || IsReviewing || dispose || id != UserID) { return; }
                IsReviewing = true;
                var startTime = DateTime.UtcNow.AddSeconds(-15);
                EventManager.CallListeners(UserEventType.ReviewingStarted);
                Task.Run(() => MonitorReviews(startTime));
            };
        }

        private void MonitorReviews(DateTime startTime)
        {
            try
            {
                var mre = new ManualResetEvent(false);
                var reviewsAvailable = GetReviewsAvailable();
                var sessionReviews = new List<ReviewItem>();
                var fkey = User.GetFKey();
                var latestTimestamp = DateTime.MaxValue;
                var updateAvg = new Action(() =>
                {
                    if (sessionReviews.Count < 3) { return; }
                    var sessionAvg = sessionReviews.Count / (DateTime.UtcNow - startTime).TotalMinutes;
                    AvgReviewsPerMin = (AvgReviewsPerMin + sessionAvg) / 2;
                });
                var endSession = new Action(() =>
                {
                    mre.Dispose();
                    TodaysCVReviews.AddRange(sessionReviews);
                    IsReviewing = false;
                    // Correct offset based on average.
                    startTime = startTime.AddSeconds(-((60 / AvgReviewsPerMin) - 15));
                    EventManager.CallListeners(UserEventType.ReviewingFinished, startTime, latestTimestamp, sessionReviews);
                });
                ReviewItem latestReview = null;

                if (TagTrackingEnabled)
                {
                    Task.Run(() => MonitorTags());
                }

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
                                ? UserEventType.AuditFailed
                                : UserEventType.AuditPassed;
                            EventManager.CallListeners(type, review);
                        }

                        updateAvg();

                        latestTimestamp = review.Results.First(r => r.UserID == UserID).Timestamp;
                        latestReview = review;
                        EventManager.CallListeners(UserEventType.ItemReviewed, review);
                    }

                    if (sessionReviews.Count + TodaysCVReviews.Count == (reviewsAvailable > 1000 ? 40 : 20))
                    {
                        // They've ran out of reviews.
                        endSession();
                        return;
                    }

                    if (latestReview != null && latestReview.AuditPassed == false &&
                        (DateTime.UtcNow - latestTimestamp).TotalSeconds > (60 / AvgReviewsPerMin) * AuditFailureFactor)
                    {
                        // We can be pretty sure they've been temporarily banned.
                        endSession();
                        return;
                    }

                    if ((DateTime.UtcNow - latestTimestamp).TotalSeconds > (60 / AvgReviewsPerMin) * IdleFactor)
                    {
                        // They've probably finished.
                        endSession();
                        return;
                    }

                    mre.WaitOne(PollInterval);
                }
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
            }
        }

        /// <summary>
        /// Warning, this method is still experimental.
        /// </summary>
        private void MonitorTags()
        {
            try
            {
                var reviewsSinceCurrentTags = 0;
                var allTags = new ConcurrentDictionary<string, float>();
                var tagTimestamps = new ConcurrentDictionary<string, DateTime>();
                List<string> prevTags = null;
                var reviewCount = 0;
                var addTag = new Action<ReviewItem>(r =>
                {
                    if (r.AuditPassed != null) { return; }

                    reviewCount++;

                    var timestamp = r.Results.First(rr => rr.UserID == UserID).Timestamp;

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
                        tagTimestamps[tag] = timestamp;
                    }

                    if (prevTags != null && prevTags.Count != 0)
                    {
                        if (prevTags.Any(t => r.Tags.Contains(t)))
                        {
                            reviewsSinceCurrentTags = 0;
                        }
                        else
                        {
                            reviewsSinceCurrentTags++;
                        }
                    }
                });

                EventManager.ConnectListener(UserEventType.ItemReviewed, addTag);

                while (IsReviewing)
                {
                    var rate = TimeSpan.FromSeconds((60 / AvgReviewsPerMin) / 2);
                    Thread.Sleep(rate);

                    // NOT ENOUGH DATAZ (insert "y u no ..." meme here).
                    if (reviewCount < 9) { continue; }

                    var tagsSum = allTags.Sum(t => t.Value);
                    var highKvs = allTags.Where(t => t.Value >= tagsSum * (1F / 15)).ToDictionary(t => t.Key, t => t.Value);

                    // Not enough (accurate) data to continue analysis.
                    if (highKvs.Count == 0) { continue; }

                    var maxTag = highKvs.Max(t => t.Value);
                    var topTags = highKvs.Where(t => t.Value >= ((maxTag / 3) * 2)).Select(t => t.Key).ToList();
                    var avgNoiseFloor = allTags.Where(t => !highKvs.ContainsKey(t.Key)).Average(t => t.Value);
                    prevTags = prevTags ?? topTags;

                    // They've started reviewing a different tag.
                    if (topTags.Count > 3 ||
                        reviewsSinceCurrentTags >= 3 ||
                        topTags.Any(t => !prevTags.Contains(t)))
                    {
                        while (topTags.Count > 3)
                        {
                            var oldestTag = new KeyValuePair<string, DateTime>(null, DateTime.MaxValue);
                            foreach (var tag in topTags)
                            {
                                if (tagTimestamps[tag] < oldestTag.Value)
                                {
                                    oldestTag = new KeyValuePair<string, DateTime>(tag, tagTimestamps[tag]);
                                }
                            }
                            allTags[oldestTag.Key] = avgNoiseFloor;
                            topTags.Remove(oldestTag.Key);
                        }

                        List<string> finishedTags;

                        if (reviewsSinceCurrentTags >= 3)
                        {
                            finishedTags = prevTags;
                        }
                        else
                        {
                            finishedTags = prevTags.Where(t => !topTags.Contains(t)).ToList();
                        }

                        EventManager.CallListeners(UserEventType.CurrentTagsChanged, finishedTags);

                        foreach (var tag in finishedTags)
                        {
                            allTags[tag] = avgNoiseFloor;
                        }

                        // I think that should fix the bug.
                        prevTags = null; //finishedTags;
                        reviewsSinceCurrentTags = 0;
                    }
                }

                EventManager.DisconnectListener(UserEventType.ItemReviewed, addTag);
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
            }
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
                    Thread.Sleep((int)Math.Max((PollInterval.TotalMilliseconds / 3), 1000));

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
            catch (WebException ex)
            {
                if (ex.Response != null && ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    Thread.Sleep(15000);
                    return LoadSinglePageCVReviews(fkey, page);
                }
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
            }

            return reviews;
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
