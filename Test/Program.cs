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
            var client = new ChatExchangeDotNet.Client("", "");
            var room = client.JoinRoom("https://chat.stackoverflow.com/rooms/68414/socvr-testing-facility", true);

            Console.WriteLine("Joined chat.");

            Task.Run(() =>
            {
                try
                {
                    var user1 = new User(2246344); // Me
                    var user2 = new User(3956566); // Yvette Colomb
                    var user3 = new User(2756409); // TylerH
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                while (true)
                {
                    Thread.Sleep(1000);
                }
            });

            var lastLine = "";

            using (var strm = new StringWriter())
            {
                Console.SetOut(strm);
                while (true)
                {
                    Thread.Sleep(5000);
                    var lines = strm.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                    var msg = "";
                    foreach (var line in lines)
                    {
                        if (!lastLine.StartsWith(":") || !line.StartsWith(":") || !line.Split(':')[1].All(Char.IsDigit) || int.Parse(lastLine.Split(':')[1]) < int.Parse(line.Split(':')[1]))
                        {
                            if (line.StartsWith(":"))
                            {
                                lastLine = line;
                            }
                            msg += line + "\n";
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        room.PostMessageLight(msg);
                    }
                }
            }
            
        }
    }
}
