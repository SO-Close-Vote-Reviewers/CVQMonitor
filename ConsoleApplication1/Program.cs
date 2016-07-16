using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CVQMonitor;
using RestSharp;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            //var url = "http://chat.stackoverflow.com/rooms/41570/so-close-vote-reviewers";
            //var req = new RestRequest(url, Method.GET);
            //var ere = RequestScheduler.ProcessRequest(req);
            var client = new RestClient("http://sdgsdgfsd");
            var req = new RestRequest("/dsgdsfgsdfg", Method.GET);

            var erdgfs = client.Execute(req);


            var watcher = new User(2246344);
            watcher.NonAuditReviewed += (o, r) => Console.WriteLine($"Non-audit item reviewed: {r}");

            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
