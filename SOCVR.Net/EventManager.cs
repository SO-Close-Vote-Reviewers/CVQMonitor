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
    /// <summary>
    /// Provides a means of listening to chat events by "connecting listeners"
    /// (Delegates) to event types.
    /// </summary>
    public class EventManager : IDisposable
    {
        private bool disposed;

        /// <summary>
        /// The current collection of connected Delegates.
        /// </summary>
        public ConcurrentDictionary<EventType, ConcurrentDictionary<int, Delegate>> ConnectedListeners { get; private set; }

        internal EventManager()
        {
            ConnectedListeners = new ConcurrentDictionary<EventType, ConcurrentDictionary<int, Delegate>>();
        }

        /// <summary>
        /// Deconstructor for this instance.
        /// </summary>
        ~EventManager()
        {
            Dispose();
        }

        internal void CallListeners(EventType eventType, params object[] args)
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
                        if (eventType == EventType.InternalException) throw ex;

                        CallListeners(EventType.InternalException, ex);
                    }
                });
            }
        }

        /// <summary>
        /// Disposes all resources used by this instance.
        /// </summary>
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

        /// <summary>
        /// Registers a Delegate to the specified event type.
        /// </summary>
        /// <param name="eventType">The event type to listen to.</param>
        /// <param name="listener">The Delegate to invoke upon event activity.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown if the Delegate is already registered to the specified event type.
        /// </exception>
        public void ConnectListener(EventType eventType, Delegate listener)
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

        /// <summary>
        /// Unregisters a Delegate from the specified event type.
        /// </summary>
        /// <param name="eventType">
        /// The event type to which the Delegate was registered to.
        /// </param>
        /// <param name="listener">The Delegate to unregister.</param>
        public void DisconnectListener(EventType eventType, Delegate listener)
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