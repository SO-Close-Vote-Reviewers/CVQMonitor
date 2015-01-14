using System;
using System.Net;
using System.Text;
using CsQuery;
using System.Collections.Generic;



namespace SOCVRDotNet
{
    public class User
    {
        private int currentPageNo;

        public int ID { get; private set; }
        public List<ReviewItem> Reviews { get; private set; }



        public User(int id, int initialPageCount = 10)
        {
            ID = id;
            Reviews = new List<ReviewItem>();

            LoadMoreReviews(initialPageCount);
        }



        public void LoadMoreReviews(int pageCount = 10)
        {
            for (var i = 1; i < pageCount; i++, currentPageNo++)
            {
                var pageHtml = new WebClient { Encoding = Encoding.UTF8 }.DownloadString("https://stackoverflow.com/ajax/users/tab/" + ID + "?tab=activity&sort=reviews&page=" + currentPageNo);
                var dom = CQ.Create(pageHtml);

                foreach (var j in dom["td"])
                {
                    if (j.FirstElementChild == null || String.IsNullOrEmpty(j.FirstElementChild.Attributes["href"]) || !j.FirstElementChild.Attributes["href"].StartsWith(@"/review/close/")) { continue; }

                    var url = j.FirstElementChild.Attributes["href"];
                    var reviewID = url.Remove(0, url.LastIndexOf('/') + 1);

                    Reviews.Add(new ReviewItem(int.Parse(reviewID)));
                }
            }
        }
    }
}
