using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CVQMonitor;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            var user = new User(2246344);

            user.ReviewingStarted += (o, e) => Console.WriteLine("Started.");
            user.ReviewLimitReached += (o, e) => Console.WriteLine("Finished.");
            user.ItemReviewed += (o, e) => Console.WriteLine($"{e.Item2.ID} reviewed.");

            while (true)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }
}
