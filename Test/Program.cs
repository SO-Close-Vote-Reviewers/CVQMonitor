using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CVQMonitor;
using System.Threading.Tasks;
using System.Threading;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            RequestScheduler.RequestsPerMinute = 30;

            var user = new User(2246344);
            user.ItemReviewed += (o, e) => Console.WriteLine(e.Item2.ID + " reviewed");
            user.ReviewingStarted += (o, e) => Console.WriteLine("reviewing started");
            user.ReviewLimitReached += (o, e) => Console.WriteLine("limit reached");

            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
