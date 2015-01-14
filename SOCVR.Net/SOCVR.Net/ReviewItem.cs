using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using CsQuery;
using Newtonsoft.Json;


namespace SOCVRDotNet
{
    public class ReviewItem
    {
        public int ID { get; private set; }
        //public Question Post { get; private set; }
        public bool? AuditPassed { get; private set; }
        public List<ReviewResult> Results { get; private set; }



        public ReviewItem(int reviewID)
        {
            ID = reviewID;

            string res;

            using (var wb = new WebClient())
            {
                var data = new NameValueCollection { { "taskTypeId", "2" } };

                res = Encoding.UTF8.GetString(wb.UploadValues("http://stackoverflow.com/review/next-task/" + reviewID, "POST", data));
            }

            PopulateResults(res);
            CheckAudit(res);
        }



        private void PopulateResults(string postResponse)
        {
            Results = new List<ReviewResult>();

            dynamic json = JsonConvert.DeserializeObject(postResponse);
            var html = (string)json["instructions"];
            var dom = CQ.Create(html);

            foreach (var i in dom[".review-results"])
            {
                var username = "";
                var timeStamp = new DateTime();
                var action = ReviewAction.LeaveOpen;

                foreach (var j in i.ChildElements)
                {
                    if (j.Attributes["href"] != null && j.Attributes["href"].StartsWith(@"/users/"))
                    {
                        username = j.InnerHTML;
                    }
                    else if (j.Attributes["title"] != null)
                    {
                        timeStamp = DateTime.Parse(j.Attributes["title"]);
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

                Results.Add(new ReviewResult(username, action, timeStamp));
            }
        }

        private void CheckAudit(string postResponse)
        {
            dynamic json = JsonConvert.DeserializeObject(postResponse);
            var html = (string)json["instructions"];
            var dom = CQ.Create(html);

            var title = dom["strong"][0] == null ? "" : dom["strong"][0].InnerHTML.TrimStart();

            if (title.StartsWith("Review audit passed"))
            {
                AuditPassed = true;
            }

            if (title.StartsWith("Review audit failed"))
            {
                AuditPassed = false;
            }
        }
    }
}