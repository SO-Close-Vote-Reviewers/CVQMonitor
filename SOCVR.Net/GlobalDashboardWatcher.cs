/*
 * SOCVR.Net. A .Net (4.5) library for fetching Stack Overflow user close vote review data.
 * Copyright © 2015, SO-Close-Vote-Reviewers.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */





using System;
using System.Collections.Generic;
using WebSocketSharp;
using ServiceStack.Text;

namespace SOCVRDotNet
{
    internal static class GlobalDashboardWatcher
    {
        private static bool cleanUp;
        private static WebSocket ws;

        public delegate void OnExceptionEventHandler(Exception ex);
        public delegate void UserEnteredQueueEventHandler(ReviewQueue queue, int userID);
        public static event UserEnteredQueueEventHandler UserEnteredQueue;
        public static event OnExceptionEventHandler OnException;



        static GlobalDashboardWatcher()
        {
            AppDomain.CurrentDomain.ProcessExit += (o, oo) => CleanUp();

            InitialiseWS();
        }



        private static void InitialiseWS()
        {
            ws = new WebSocket("ws://qa.sockets.stackexchange.com");
            // 1 = Stack Overflow.
            ws.OnOpen += (o, oo) => ws.Send("1-review-dashboard-update");
            ws.OnMessage += (o, oo) => HandleMessage(oo.Data);
            ws.OnError += (o, oo) => HandleException(oo.Exception);
            ws.OnClose += (o, oo) => HandleClose(oo);
            ws.Connect();
        }

        private static void HandleMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) { return; }

            try
            {
                var obj = JsonSerializer.DeserializeFromString<Dictionary<string, object>[]>(message);
                var data = JsonSerializer.DeserializeFromString<Dictionary<string, object>>(obj[0]["data"].ToString());
                var queue = (ReviewQueue)int.Parse(data["i"].ToString());
                var userID = int.Parse(data["u"].ToString());

                if (UserEnteredQueue != null)
                {
                    UserEnteredQueue(queue, userID);
                }
            }
            // Ignore exceptions from parsing malformed messages.
            catch (Exception) { }
        }

        private static void HandleException(Exception ex)
        {
            if (OnException == null) { throw ex; }

            OnException(ex);
        }

        private static void HandleClose(CloseEventArgs e)
        {
            if (e.WasClean || e.Code == (ushort)CloseStatusCode.Normal || cleanUp) { return; }

            InitialiseWS();
        }

        private static void CleanUp()
        {
            cleanUp = true;
            ws.Close();
        }
    }
}
