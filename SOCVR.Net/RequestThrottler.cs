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

namespace SOCVRDotNet
{
    public static class RequestThrottler
    {
        private static float reqTp = 100;
        private static string fkey;
        private static DateTime lastFkeyFetch = DateTime.UtcNow;

        internal static string FkeyCached
        {
            get
            {
                if (DateTime.UtcNow.Day != lastFkeyFetch.Day || fkey == null)
                {
                    fkey = UserDataFetcher.GetFkey();
                    lastFkeyFetch = DateTime.UtcNow;
                }

                return fkey;
            }
        }

        internal static int LiveUserInstances { get; set; }


        /// <summary>
        /// The maximum number of reviews (per minutes) to be processed.
        /// (Default: 100.)
        /// </summary>
        public static float RequestThroughputMin
        {
            get
            {
                return reqTp;
            }
            set
            {
                if (reqTp < 0) throw new ArgumentOutOfRangeException("value", "Must be a positive number or 0.");

                reqTp = value;
            }
        }
    }
}
