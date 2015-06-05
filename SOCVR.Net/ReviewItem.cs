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
using System.Collections.Specialized;
using System.Net;
using System.Text;
using CsQuery;
using ServiceStack.Text;

namespace SOCVRDotNet
{
    public class ReviewItem
    {
        /// <summary>
        /// The ID number of the review task.
        /// </summary>
        public int ID { get; private set; }

        /// <summary>
        /// If null, this task was NOT an audit, otherwise this task was an audit (true if the user passed, otherwise false).
        /// </summary>
        public bool? AuditPassed { get; private set; }

        /// <summary>
        /// A list of ReviewResults holding data regarding all the users's actions taken.
        /// </summary>
        public List<ReviewResult> Results { get; private set; }

        /// <summary>
        /// A list of tags that the reviewed questions was tagged with.
        /// </summary>
        public List<string> Tags { get; private set; }



        public ReviewItem(int reviewID, string fkey)
        {
            if (string.IsNullOrEmpty(fkey)) { throw new ArgumentException("'fkey' must not be null or empty.", "fkey"); }
            ID = reviewID;

            string res;

            using (var wb = new WebClient())
            {
                var data = new NameValueCollection
                {
                    { "taskTypeId", "2" },
                    { "fkey" , fkey }
                };

                res = Encoding.UTF8.GetString(wb.UploadValues("http://stackoverflow.com/review/next-task/" + reviewID, "POST", data));
            }

            var json = JsonSerializer.DeserializeFromString<Dictionary<string, object>>(res);
            var instructionsDom = CQ.Create((string)json["instructions"]);
            var contentDom = CQ.Create((string)json["content"]);
            PopulateResults(instructionsDom);
            PopulateTags(contentDom);
            CheckAudit(instructionsDom);
        }



        private void PopulateResults(CQ dom)
        {
            Results = new List<ReviewResult>();

            foreach (var i in dom[".review-results"])
            {
                var userId = 0;
                var username = "";
                var timeStamp = new DateTime();
                var action = ReviewAction.LeaveOpen;

                foreach (var j in i.ChildElements)
                {
                    if (j.Attributes["title"] == "moderator") { continue; }

                    if (j.Attributes["href"] != null && j.Attributes["href"].StartsWith(@"/users/"))
                    {
                        var id = j.Attributes["href"];
                        id = id.Remove(0, 7);
                        id = id.Remove(id.IndexOf('/'));
                        userId = int.Parse(id);
                        username = WebUtility.UrlDecode(j.InnerHTML);
                    }
                    else if (j.Attributes["title"] != null)
                    {
                        timeStamp = DateTime.Parse(j.Attributes["title"]);
                        timeStamp = timeStamp.ToUniversalTime();
                    }
                    else
                    {
                        switch (j.InnerHTML)
                        {
                            case "Close":
                            {
                                action = ReviewAction.Close;
                                break;
                            }
                            case "Leave Open":
                            {
                                action = ReviewAction.LeaveOpen;
                                break;
                            }
                            case "Edit":
                            {
                                action = ReviewAction.Edit;
                                break;
                            }
                        }
                    }
                }

                Results.Add(new ReviewResult(userId, username, action, timeStamp));
            }
        }

        private void PopulateTags(CQ dom)
        {
            Tags = new List<string>();

            foreach (var tag in dom[".js-tab-question .post-taglist a"])
            {
                var t = tag.Attributes["href"];

                t = WebUtility.UrlDecode(t.Remove(0, t.LastIndexOf('/') + 1));

                Tags.Add(t);
            }
        }

        private void CheckAudit(CQ dom)
        {
            var title = dom["strong"][0] == null ? "" : dom["strong"][0].InnerHTML.TrimStart();

            if (title.StartsWith("Review audit passed"))
            {
                AuditPassed = true;
            }
            else if (title.StartsWith("Review audit failed"))
            {
                AuditPassed = false;
            }
        }
    }
}