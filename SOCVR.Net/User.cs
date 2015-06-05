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





using System.Net;
using System.Text;
using CsQuery;
using System.Collections.Generic;
using System.Linq;

namespace SOCVRDotNet
{
    public static class User
    {
        /// <summary>
        /// Scraps a user's activity tab for CV review data.
        /// </summary>
        /// <param name="reviewCount">The number of reviews to fetch.</param>
        public static List<ReviewItem> FetchReviews(int userID, int reviewCount = 10)
        {
            var fkey = GetFKey();
            var currentPageNo = 0;
            var reviews = new List<ReviewItem>();

            while (reviews.Count < reviewCount)
            {
                currentPageNo++;
                var reqUrl = "http://stackoverflow.com/ajax/users/tab/" + userID + "?tab=activity&sort=reviews&page=" + currentPageNo;
                var pageHtml = new WebClient { Encoding = Encoding.UTF8 }.DownloadString(reqUrl);
                if (pageHtml.Contains("This user has no reviews") && pageHtml.Length < 3000) { break; }
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

                    if (reviews.Any(r => r.ID == id) || reviews.Count >= reviewCount) { continue; }
                    reviews.Add(new ReviewItem(id, fkey));
                }
            }

            return reviews;
        }

        internal static string GetFKey()
        {
            var html = new WebClient().DownloadString("https://stackoverflow.com/users/login");
            var dom = CQ.Create(html);
            var fkeyE = dom["input"].FirstOrDefault(e => e.Attributes["name"] != null && e.Attributes["name"] == "fkey");
            return fkeyE == null ? null : fkeyE.Attributes["value"];
        }
    }
}
