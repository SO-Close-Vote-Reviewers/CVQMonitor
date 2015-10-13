///*
// * SOCVR.Net. A .Net (4.5) library for fetching Stack Overflow user close vote review data.
// * Copyright © 2015, SO-Close-Vote-Reviewers.
// *
// * This program is free software: you can redistribute it and/or modify
// * it under the terms of the GNU General Public License as published by
// * the Free Software Foundation, either version 3 of the License, or
// * (at your option) any later version.
// *
// * This program is distributed in the hope that it will be useful,
// * but WITHOUT ANY WARRANTY; without even the implied warranty of
// * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// * GNU General Public License for more details.
// *
// * You should have received a copy of the GNU General Public License
// * along with this program.  If not, see <http://www.gnu.org/licenses/>.
// */





//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//namespace SOCVRDotNet
//{
//    public static class ReviewMonitorPool
//    {
//        private static ConcurrentDictionary<int, ReviewMonitor> monitors = new ConcurrentDictionary<int, ReviewMonitor>();

//        /// <summary>
//        /// The number of requests per minute to maintain while ReviewMonitors are active. Default: 10.
//        /// </summary>
//        public static double RequestThroughput
//        {
//            get; set;
//        }



//        static ReviewMonitorPool()
//        {
//            RequestThroughput = 10;
//            Task.Run(() => UpdatePollPeriods());
//        }



//        internal static ReviewMonitor NewMonitor(int userID, DateTime startTime, List<ReviewItem> todaysCVReviews, double avgReviewsMin)
//        {
//            if (monitors.ContainsKey(userID))
//            {
//                throw new ArgumentException("Cannot create duplicate 'ReviewMonitor's for the same user.", "userID");
//            }

//            var m = new ReviewMonitor(userID, startTime, todaysCVReviews, avgReviewsMin);

//            monitors[userID] = m;

//            return m;
//        }



//        private static void UpdatePollPeriods()
//        {
//            while (true)
//            {
//                // No need to complicate things with waithandles.
//                Thread.Sleep(1000);

//                // Remove inactive monitors.
//                var inactiveMonitors = monitors.Where(m => !m.Value.IsMonitoring);
//                foreach (var monitor in inactiveMonitors)
//                {
//                    ReviewMonitor m;
//                    monitors.TryRemove(monitor.Key, out m);
//                }

//                if (monitors.Count == 0)
//                {
//                    continue;
//                }

//                var monitorsCpy = monitors;
//                var rawReqsMin = monitorsCpy.Sum(kv => kv.Value.AvgReviewsPerMin);
//                var usageFactor = Math.Max(rawReqsMin / RequestThroughput, 0.25);

//                foreach (var kv in monitorsCpy)
//                {
//                    var mins = Math.Max(1 / monitors[kv.Key].AvgReviewsPerMin * usageFactor, 1D / RequestThroughput);
//                    monitors[kv.Key].PollInterval = TimeSpan.FromMinutes(mins * 2);
//                    // ^ Multiple by 2 as each monitor makes at least 2 reqs per poll.
//                }
//            }
//        }
//    }
//}
