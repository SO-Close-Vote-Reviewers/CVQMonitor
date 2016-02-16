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

namespace SOCVRDotNet
{
    public static class RequestThrottler
    {
        private static float bgScraperFactor = 8;
        private static float reqTp = 30;

        internal static ConcurrentDictionary<int, ushort> ReviewsPending { get; set; } = new ConcurrentDictionary<int, ushort>();



        /// <summary>
        /// The maximum number of reviews (per minutes) to be processed.
        /// (Default: 30.)
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
        /// Default 8.
        /// </summary>
        public static float BackgroundScraperPollFactor
        {
            get
            {
                return bgScraperFactor;
            }
            set
            {
                if (value < 0 && value != 0) throw new ArgumentOutOfRangeException("value", "Must be a positive number (and not zero).");

                bgScraperFactor = value;
            }
        }
    }
}
