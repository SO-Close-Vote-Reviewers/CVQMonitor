using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using CVQMonitor;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var t = new User(2246344);

            while (true)

            {
                System.Threading.Thread.Sleep(100);
            }
            //Test().Wait();
        }

        private static async Task Test()
        {
            var ws = new ClientWebSocket();
            var buffer = new ArraySegment<byte>(new byte[1024 * 10]);
            await ws.ConnectAsync(new Uri("ws://qa.sockets.stackexchange.com"), new System.Threading.CancellationToken(false));
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("1-review-dashboard-update")), WebSocketMessageType.Text, true, new System.Threading.CancellationToken(false));

            while (true)
            {
                var res = await ws.ReceiveAsync(buffer, new System.Threading.CancellationToken(false));
                Console.WriteLine(new string(Encoding.UTF8.GetString(buffer.Array).Where(c => c != 0).ToArray()));
            }
        }
    }
}
