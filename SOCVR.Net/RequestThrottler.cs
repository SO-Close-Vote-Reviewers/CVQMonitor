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
using System.Threading;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    /// <summary>
    /// Manages the request throughput for all created User instances.
    /// </summary>
    public static class RequestThrottler
    {
        private static float reqTp = 60;

        internal static float RequestsRemaining { get; set; }

        internal static ConcurrentDictionary<int, bool> ActiveUsers { get; set; } = new ConcurrentDictionary<int, bool>();

        /// <summary>
        /// The maximum number of requests (per minutes) to be processed.
        /// (Default: 60.)
        /// </summary>
        public static float RequestThroughputMin
        {
            get
            {
                return reqTp;
            }
            set
            {
                if (reqTp < 1) throw new ArgumentOutOfRangeException("value", "Must be a positive number.");

                reqTp = value;
            }
        }

        /// <summary>
        /// A number used to multiple the throttle duration.
        /// </summary>
        public static float ThrottleFactor { get; set; } = 1;

        //static RequestThrottler()
        //{
        //    Task.Run(() =>
        //    {
        //        RequestsRemaining = RequestThroughputMin;

        //        Thread.Sleep(60000); // 1 min.
        //    });
        //}
    }
}