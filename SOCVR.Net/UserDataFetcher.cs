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
using System;
using System.Threading;
using System.Text.RegularExpressions;

namespace SOCVRDotNet
{
    public static class UserDataFetcher
    {
        private static readonly Regex todaysReviewCount = new Regex(@"(?i)today \d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);



        /// <summary>
        /// Fetches the latest close vote review data.
        /// </summary>
        /// <param name="userID">The user ID to fetch close vote data for.</param>
        /// <param name="reviewCount">The number of reviews to fetch.</param>
        public static List<ReviewItem> FetchReviews(int userID, int reviewCount = 10)
        {
            var fkey = GetFkey();
            var currentPageNo = 0;
            var reviews = new List<ReviewItem>();

            while (reviews.Count < reviewCount)
            {
                currentPageNo++;
                var reqUrl = $"http://stackoverflow.com/ajax/users/tab/{userID}?tab=activity&sort=reviews&page={currentPageNo}";
                var pageHtml = new WebClient { Encoding = Encoding.UTF8 }.DownloadString(reqUrl);
                if (pageHtml.Contains("This user has no reviews") && pageHtml.Length < 3000) break;
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

                    if (reviews.Any(r => r.ID == id) || reviews.Count >= reviewCount) continue;
                    reviews.Add(new ReviewItem(id, fkey));
                }
            }

            return reviews;
        }

        public static string GetFkey()
        {
            var html = new WebClient().DownloadString("https://stackoverflow.com/users/login");
            var dom = CQ.Create(html);
            var fkeyE = dom["input"].FirstOrDefault(e => e?.Attributes["name"] == "fkey");
            return fkeyE?.Attributes["value"];
        }



        internal static List<int> GetLastestCVReviewIDs(string fkey, int userID, int reviewCount, Action throttler = null)
        {
            if (reviewCount < 1) throw new ArgumentOutOfRangeException("reviewCount", "Must be more than 0.");

            var reviews = new List<int>();
            var page = 1;

            try
            {
                while (reviews.Count < reviewCount)
                {
                    if (throttler != null) throttler();
                    var reqUrl = $"http://stackoverflow.com/ajax/users/tab/{userID}?tab=activity&sort=reviews&page={page}";
                    var dom = CQ.CreateFromUrl(reqUrl);

                    foreach (var j in dom["td"])
                    {
                        if (j.FirstElementChild == null ||
                            string.IsNullOrEmpty(j.FirstElementChild.Attributes["href"]) ||
                            !j.FirstElementChild.Attributes["href"].StartsWith("/review/close/"))
                        {
                            continue;
                        }

                        var url = j.FirstElementChild.Attributes["href"];
                        var reviewID = url.Remove(0, url.LastIndexOf('/') + 1);
                        var id = int.Parse(reviewID);

                        if (reviews.Contains(id)) continue;
                        reviews.Add(id);
                        if (reviews.Count == reviewCount) break;
                    }

                    page++;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null && ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    Thread.Sleep(15000);
                    return GetLastestCVReviewIDs(fkey, userID, reviewCount);
                }
                else
                {
                    throw ex;
                }
            }

            return reviews;
        }

        internal static int FetchTodaysUserReviewCount(string fkey, int userID, ref EventManager evMan)
        {
            var html = new WebClient().DownloadString("http://stackoverflow.com/review/user-info/2/" + userID);
            var match = todaysReviewCount.Match(html);
            var reviewCount = -1;

            if (!int.TryParse(new string(match.Value.Where(char.IsDigit).ToArray()), out reviewCount))
            {
                evMan.CallListeners(EventType.InternalException, new Exception("Unable to parse ReviewCount string to int."));
            }

            return reviewCount;
        }
    }
}
