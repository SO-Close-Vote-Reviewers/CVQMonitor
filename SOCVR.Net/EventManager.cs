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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    public class EventManager : IDisposable
    {
        private bool disposed;

        public ConcurrentDictionary<UserEventType, ConcurrentDictionary<int, Delegate>> ConnectedListeners { get; private set; }



        public EventManager()
        {
            ConnectedListeners = new ConcurrentDictionary<UserEventType, ConcurrentDictionary<int, Delegate>>();
        }

        ~EventManager()
        {
            Dispose();
        }



        internal void CallListeners(UserEventType eventType, params object[] args)
        {
            if (disposed) return; 
            if (!ConnectedListeners.ContainsKey(eventType)) return; 
            if (ConnectedListeners[eventType].Keys.Count == 0) return; 

            foreach (var listener in ConnectedListeners[eventType].Values)
            {
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        listener.DynamicInvoke(args);
                    }
                    catch (Exception ex)
                    {
                        if (eventType == UserEventType.InternalException) throw ex;

                        CallListeners(UserEventType.InternalException, ex);
                    }
                });
            }
        }

        public void Dispose()
        {
            if (disposed) return;

            disposed = true;
            if (ConnectedListeners != null)
            {
                ConnectedListeners.Clear();
            }
            GC.SuppressFinalize(this);
        }

        public void ConnectListener(UserEventType eventType, Delegate listener)
        {
            if (disposed) return;

            if (!ConnectedListeners.ContainsKey(eventType))
            {
                ConnectedListeners[eventType] = new ConcurrentDictionary<int, Delegate>();
            }
            else if (ConnectedListeners[eventType].Values.Contains(listener))
            {
                throw new Exception("'listener' has already been connected to this event type.");
            }

            if (ConnectedListeners[eventType].Count == 0)
            {
                ConnectedListeners[eventType][0] = listener;
            }
            else
            {
                var index = ConnectedListeners[eventType].Keys.Max() + 1;
                ConnectedListeners[eventType][index] = listener;
            }
        }

        public void DisconnectListener(UserEventType eventType, Delegate listener)
        {
            if (disposed) return;
            if (!ConnectedListeners.ContainsKey(eventType)) throw new KeyNotFoundException();
            if (!ConnectedListeners[eventType].Values.Contains(listener)) throw new KeyNotFoundException();

            var key = ConnectedListeners[eventType].Where(x => x.Value == listener).First().Key;
            Delegate temp;
            ConnectedListeners[eventType].TryRemove(key, out temp);
        }
    }
}