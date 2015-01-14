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
        public Question Post { get; private set; }
        public Audit AuditData { get; private set; }
        public ReviewResult Results { get; private set; }
        public ReviewAction OverallAction { get; private set; }



        public ReviewItem(int reviewID)
        {
            ID = reviewID;

            string res;

            using (var wb = new WebClient())
            {
                var data = new NameValueCollection { { "taskTypeId", "2" } };

                res = Encoding.UTF8.GetString(wb.UploadValues("http://stackoverflow.com/review/next-task/" + reviewID, "POST", data));
            }

            dynamic json = JsonConvert.DeserializeObject(res);

            var html = (string)json["instructions"];

            var dom = CQ.Create(html);
        }
    }
}
