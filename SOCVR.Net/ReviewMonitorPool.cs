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
using System.Threading;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    internal static class ReviewMonitorPool
    {
        private static ConcurrentDictionary<int, ReviewMonitor> monitors = new ConcurrentDictionary<int, ReviewMonitor>();



        static ReviewMonitorPool()
        {
            Task.Run(() => UpdatePollPeriods());
        }



        public static ReviewMonitor NewMonitor(int userID, DateTime startTime, List<ReviewItem> todaysCVReviews, double avgReviewsMin)
        {
            if (monitors.ContainsKey(userID))
            {
                throw new ArgumentException("Cannot create dupelicate 'ReviewMonitor's for the same user.", "userID");
            }

            var m = new ReviewMonitor(userID, startTime, todaysCVReviews, avgReviewsMin);

            monitors[userID] = m;

            return m;
        }

        public static void CleanUpMonitor(int userID)
        {
            if (!monitors.ContainsKey(userID))
            {
                throw new KeyNotFoundException();
            }

            ReviewMonitor temp;
            monitors.TryRemove(userID, out temp);
        }



        private static void UpdatePollPeriods()
        {
            while (true)
            {
                // No need to complicate things with waithandles.
                Thread.Sleep(1000);

                if (monitors.Count == 0)
                {
                    continue;
                }

                var activeMonitors = monitors.Where(kv => kv.Value.IsMonitoring).ToArray();

                if (activeMonitors.Length == 0)
                {
                    continue;
                }

                var rawReqsMin = activeMonitors.Sum(kv => kv.Value.AvgReviewsPerMin);
                var usageFactor = Math.Max(rawReqsMin / 12, 0.25);

                foreach (var kv in activeMonitors)
                {
                    var mins = Math.Max(1 / monitors[kv.Key].AvgReviewsPerMin * usageFactor, 1D / 12);
                    monitors[kv.Key].PollInterval = TimeSpan.FromMinutes(mins);
                }
            }
        }
    }
}
