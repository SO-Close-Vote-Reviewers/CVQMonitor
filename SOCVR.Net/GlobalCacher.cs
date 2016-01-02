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
using System.Linq;
using CsQuery;

namespace SOCVRDotNet
{
    internal class GlobalCacher
    {
        private static string fkey;
        private static int? revCount = 0;
        private static DateTime lastFkeyFetch = DateTime.UtcNow;
        private static DateTime lastRevCountFetch = DateTime.UtcNow;

        public static string FkeyCached
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



        public static int ReviewLimitCached(bool isMod = false)
        {
            if (isMod) return int.MaxValue;

            if (DateTime.UtcNow.Day != lastRevCountFetch.Day || revCount == 0)
            {
                var doc = CQ.CreateFromUrl("http://stackoverflow.com/review/close/stats");
                var statsTable = doc.Find("table.task-stat-table");
                var cells = statsTable.Find("td");
                var needReview = new string(cells.ElementAt(0).FirstElementChild.InnerText.Where(c => char.IsDigit(c)).ToArray());
                var reviews = 0;

                if (int.TryParse(needReview, out reviews))
                {
                    revCount = reviews;
                }
                else
                {
                    revCount = null;
                }

                lastRevCountFetch = DateTime.UtcNow;
            }

            return (revCount ?? 1000) >= 1000 ? 40 : 20;
        }
    }
}
