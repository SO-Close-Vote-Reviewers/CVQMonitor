using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CVQMonitor;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var user = new User(2246344);

            user.ItemReviewed += (o, e) => Console.WriteLine($"Item {e.Item2.ID} reviewed");
            user.ReviewingStarted += (o, e) => Console.WriteLine("Started");
            user.ReviewLimitReached += (o, e) => Console.WriteLine("Finished");

            while (true)
            {
                System.Threading.Thread.Sleep(1000);
            }
        }
    }
}
