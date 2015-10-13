///*
// * SOCVR.Net. A .Net (4.5) library for fetching Stack Overflow user close vote review data.
// * Copyright © 2015, SO-Close-Vote-Reviewers.
// *
// * This program is free software: you can redistribute it and/or modify
// * it under the terms of the GNU General Public License as published by
// * the Free Software Foundation, either version 3 of the License, or
// * (at your option) any later version.
// *
// * This program is distributed in the hope that it will be useful,
// * but WITHOUT ANY WARRANTY; without even the implied warranty of
// * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// * GNU General Public License for more details.
// *
// * You should have received a copy of the GNU General Public License
// * along with this program.  If not, see <http://www.gnu.org/licenses/>.
// */





//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//namespace SOCVRDotNet
//{
//    public class TagMonitor
//    {
//        private readonly ConcurrentDictionary<string, float> allTags = new ConcurrentDictionary<string, float>();
//        private readonly ConcurrentDictionary<string, DateTime> tagTimestamps = new ConcurrentDictionary<string, DateTime>();
//        private readonly ReviewMonitor revMonitor;
//        private List<string> prevTags;
//        private int reviewsSinceCurrentTags;
//        private int reviewCount;

//        internal EventManager EventManager { get; private set; }



//        public TagMonitor(ref ReviewMonitor reviewMonitor)
//        {
//            if (reviewMonitor == null) { throw new ArgumentNullException("reviewMonitor"); }

//            revMonitor = reviewMonitor;
//            EventManager = new EventManager();

//            Task.Run(() =>
//            {
//                while (true)
//                {
//                    if (revMonitor == null) { return; }

//                    if (!revMonitor.IsMonitoring)
//                    {
//                        Thread.Sleep(1000);
//                        continue;
//                    }

//                    MonitorLoop();
//                }
//            });
//        }



//        private void MonitorLoop()
//        {
//            revMonitor.EventManager.ConnectListener(UserEventType.ItemReviewed, new Action<ReviewItem>(AddTag));

//            try
//            {
//                while (revMonitor != null && revMonitor.IsMonitoring)
//                {
//                    var rate = TimeSpan.FromSeconds((60 / revMonitor.AvgReviewsPerMin) / 2);
//                    Thread.Sleep(rate);

//                    // NOT ENOUGH DATAZ.
//                    if (reviewCount < 9) { continue; }

//                    var tagsSum = allTags.Sum(t => t.Value);
//                    var highKvs = allTags.Where(t => t.Value >= tagsSum * (1F / 15)).ToDictionary(t => t.Key, t => t.Value);

//                    // Not enough (accurate) data to continue analysis.
//                    if (highKvs.Count == 0) { continue; }

//                    var maxTag = highKvs.Max(t => t.Value);
//                    var topTags = highKvs.Where(t => t.Value >= ((maxTag / 3) * 2)).Select(t => t.Key).ToList();
//                    var avgNoiseFloor = allTags.Where(t => !highKvs.ContainsKey(t.Key)).Average(t => t.Value);
//                    prevTags = prevTags ?? topTags;

//                    // They've started reviewing a different tag.
//                    if (topTags.Count > 3 ||
//                        reviewsSinceCurrentTags >= 3 ||
//                        topTags.Any(t => !prevTags.Contains(t)))
//                    {
//                        HandleTagChange(ref topTags, avgNoiseFloor);
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                EventManager.CallListeners(UserEventType.InternalException, ex);
//            }

//            if (revMonitor == null) { return; }

//            revMonitor.EventManager.DisconnectListener(UserEventType.ItemReviewed, new Action<ReviewItem>(AddTag));
//        }

//        private void HandleTagChange(ref List<string> topTags, float avgNoiseFloor)
//        {
//            try
//            {
//                while (topTags.Count > 3)
//                {
//                    var oldestTag = new KeyValuePair<string, DateTime>(null, DateTime.MaxValue);
//                    foreach (var tag in topTags)
//                    {
//                        if (tagTimestamps[tag] < oldestTag.Value)
//                        {
//                            oldestTag = new KeyValuePair<string, DateTime>(tag, tagTimestamps[tag]);
//                        }
//                    }
//                    allTags[oldestTag.Key] = avgNoiseFloor;
//                    topTags.Remove(oldestTag.Key);
//                }

//                List<string> finishedTags;

//                if (reviewsSinceCurrentTags >= 3)
//                {
//                    finishedTags = prevTags;
//                }
//                else
//                {
//                    var tt = topTags;
//                    finishedTags = prevTags.Where(t => !tt.Contains(t)).ToList();
//                }

//                EventManager.CallListeners(UserEventType.CurrentTagsChanged, finishedTags);

//                foreach (var tag in finishedTags)
//                {
//                    allTags[tag] = avgNoiseFloor;
//                }

//                prevTags = null;
//                reviewsSinceCurrentTags = 0;
//            }
//            catch (Exception ex)
//            {
//                EventManager.CallListeners(UserEventType.InternalException, ex);
//            }
//        }

//        private void AddTag(ReviewItem r)
//        {
//            if (r.AuditPassed != null) { return; }

//            reviewCount++;

//            var timestamp = r.Results.First(rr => rr.UserID == revMonitor.UserID).Timestamp;

//            foreach (var tag in r.Tags)
//            {
//                if (allTags.ContainsKey(tag))
//                {
//                    allTags[tag]++;
//                }
//                else
//                {
//                    allTags[tag] = 1;
//                }
//                tagTimestamps[tag] = timestamp;
//            }

//            if (prevTags != null && prevTags.Count != 0)
//            {
//                if (prevTags.Any(t => r.Tags.Contains(t)))
//                {
//                    reviewsSinceCurrentTags = 0;
//                }
//                else
//                {
//                    reviewsSinceCurrentTags++;
//                }
//            }
//        }
//    }
//}
