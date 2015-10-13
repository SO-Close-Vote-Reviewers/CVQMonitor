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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CsQuery;

namespace SOCVRDotNet
{
    public class ReviewMonitor
    {
        private readonly Regex lastActiveTime = new Regex(@"(?is)^\s*<div.*>.*</span></span></div>\s*|<br\s*?/>.*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ManualResetEvent mre = new ManualResetEvent(false);
        private readonly string fkey;
        private readonly List<ReviewItem> prevCVRs;
        private int reviewsAvailable;
        private double avgReviewingSpeed;
        private DateTime startTime;
        private DateTime lastActive;
        private ReviewItem latestReview;
        private DateTime lastActiveFetch = DateTime.MinValue;
        private DateTime latestReviewTime = DateTime.MaxValue;

        public int UserID { get; private set; }

        public bool IsMonitoring { get; private set; }

        internal EventManager EventManager { get; private set; }

        public List<ReviewItem> SessionReviews { get; private set; }

        /// <summary>
        /// The multiplicative factor used when determining whether
        /// the current reviewing session has completed, based on
        /// the duration of inactivity after completing a review.
        /// (Default 5.)
        /// </summary>
        public float IdleFactor { get; set; }

        /// <summary>
        /// The multiplicative factor used when determining whether
        /// the current reviewing session has completed, based on
        /// the duration of inactivity after failing an audit.
        /// (Default 4.)
        /// </summary>
        public float AuditFailureFactor { get; set; }

        /// <summary>
        /// The interval at which to actively poll a user's profile
        /// for CV review data whilst they're reviewing (a minimum 
        /// of 2 requests are sent per poll).
        /// </summary>
        public TimeSpan PollInterval { get; internal set; }

        public DateTime LastActive
        {
            get
            {
                if ((DateTime.UtcNow - lastActiveFetch).TotalSeconds < 15) { return lastActive; }

                var html = new WebClient().DownloadString("http://stackoverflow.com/review/user-info/2/" + UserID);
                var lastActiveStr = lastActiveTime.Replace(html, "");
                lastActiveStr = lastActiveStr.Remove(0, 7);
                var digits = int.Parse(new string(lastActiveStr.Where(char.IsDigit).ToArray()));

                if (lastActiveStr.Contains("sec"))
                {
                    lastActive = DateTime.UtcNow.Add(-TimeSpan.FromSeconds(digits));
                }
                else if (lastActiveStr.Contains("min"))
                {
                    lastActive = DateTime.UtcNow.Add(-TimeSpan.FromMinutes(digits));
                }
                else if (lastActiveStr.Contains("hour"))
                {
                    lastActive = DateTime.UtcNow.Add(-TimeSpan.FromHours(digits));
                }
                else
                {
                    lastActive = DateTime.MinValue;
                }

                return lastActive;
            }
        }

        public double AvgReviewsPerMin
        {
            get
            {
                if (SessionReviews == null || SessionReviews.Count < 3) { return avgReviewingSpeed; }

                var sessionAvg = SessionReviews.Count / (DateTime.UtcNow.AddSeconds(-(DateTime.UtcNow - latestReviewTime).TotalSeconds) - startTime).TotalMinutes;

                avgReviewingSpeed = (avgReviewingSpeed + sessionAvg) / 2;

                return avgReviewingSpeed;
            }
        }



        public ReviewMonitor(int userID, DateTime startTime, List<ReviewItem> prevCVReviews, double reviewsPerMin = 1)
        {
            if (startTime == null) { throw new ArgumentNullException("startTime"); }

            this.startTime = startTime;
            avgReviewingSpeed = reviewsPerMin;
            fkey = User.GetFKey();
            prevCVRs = prevCVReviews ?? new List<ReviewItem>();
            UserID = userID;
            SessionReviews = new List<ReviewItem>();
            EventManager = new EventManager();
            IdleFactor = 5;
            AuditFailureFactor = 4;
            PollInterval = TimeSpan.FromSeconds(10);
        }



        public void Start()
        {
            reviewsAvailable = GetReviewsAvailable();
            IsMonitoring = true;
            Task.Run(() => MonitorLoop());
        }

        public void Stop()
        {
            IsMonitoring = false;
            mre.Set();
        }



        private void MonitorLoop()
        {
            try
            {
                while (IsMonitoring)
                {
                    var pageReviews = User.LoadSinglePageCVReviews(fkey, UserID, 1);

                    foreach (var review in pageReviews)
                    {
                        if (SessionReviews.Any(r => r.ID == review.ID) ||
                            review.Results.First(r => r.UserID == UserID).Timestamp < startTime)
                        {
                            continue;
                        }

                        SessionReviews.Add(review);

                        // Notify audit listeners if necessary.
                        if (review.AuditPassed != null)
                        {
                            var type = review.AuditPassed == false
                                ? UserEventType.AuditFailed
                                : UserEventType.AuditPassed;

                            EventManager.CallListeners(type, review);
                        }

                        latestReviewTime = review.Results.First(r => r.UserID == UserID).Timestamp;
                        latestReview = review;
                        EventManager.CallListeners(UserEventType.ItemReviewed, review);
                    }

                    if (SessionReviews.Count + prevCVRs.Count == (reviewsAvailable > 1000 ? 40 : 20))
                    {
                        // They've ran out of reviews.
                        EndSession();
                        return;
                    }

                    if (latestReview != null && latestReview.AuditPassed == false &&
                       (DateTime.UtcNow - LastActive).TotalSeconds > (60 / AvgReviewsPerMin) * AuditFailureFactor)
                    {
                        // We can be pretty sure they've been temporarily banned.
                        EndSession();
                        return;
                    }

                    if ((DateTime.UtcNow - LastActive).TotalSeconds > (60 / AvgReviewsPerMin) * IdleFactor)
                    {
                        // They've probably finished.
                        EndSession();
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
