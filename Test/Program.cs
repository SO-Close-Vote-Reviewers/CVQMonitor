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

            var user1 = new User(2246344); // Me
            var user2 = new User(3956566); // Yvette Colomb
            var user3 = new User(2756409); // TylerH

            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
