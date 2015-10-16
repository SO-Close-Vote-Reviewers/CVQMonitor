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
using System.Linq;

namespace SOCVRDotNet
{
    public class User :  IDisposable
    {
        private readonly Dictionary<string, DateTime> tagTimestamps = new Dictionary<string, DateTime>();
        private List<string> prevTags;
        private EventManager evMan;
        private int reviewsSinceCurrentTags;
        private string fkey;
        private bool dispose;

        public int ID { get; private set; }

        public UserReviewStatus ReviewStatus { get; private set; }

        public EventManager EventManager { get { return evMan; } }



        public User(string fkey, int userID)
        {
            this.fkey = fkey;
            evMan = new EventManager();
            ID = userID;
            ReviewStatus = new UserReviewStatus(
                userID,
                () => EventManager.CallListeners(UserEventType.ReviewLimitReached),
                ref evMan);
        }

        ~User()
        {
            Dispose();
        }



        public void Dispose()
        {
            if (dispose) { return; }
            dispose = true;

            ReviewStatus.Dispose();
            EventManager.Dispose();

            GC.SuppressFinalize(this);
        }



        /// <summary>
        /// Fetches the latest (unprocessed) reviews from a user's profile.
        /// </summary>
        /// <returns>The total number of reviews fetched and processed.</returns>
        internal int ProcessReviews()
        {
            var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, ReviewStatus.QueuedReviews);

            foreach (var id in ids)
            {
                var review = new ReviewItem(id, fkey);
                ReviewStatus.Reviews.Add(review);

                CheckTags(review);

                EventManager.CallListeners(UserEventType.ItemReviewed, review);
                ReviewStatus.QueuedReviews--;
            }

            return ids.Count;
        }

        internal void ResetDailyData(string fkey, int availableReviews)
        {
            this.fkey = fkey;

            // Misc. review data
            ReviewStatus.Reviews.Clear();
            ReviewStatus.ReviewsCompletedCount = 0;
            ReviewStatus.ReviewLimit = availableReviews > 1000 ? 40 : 20;

            // Tag data.
            prevTags = null;
            reviewsSinceCurrentTags = 0;
            tagTimestamps.Clear();
            ReviewStatus.ReviewedTags.Clear();
        }



        private void CheckTags(ReviewItem review)
        {
            var timestamp = review.Results.First(rr => rr.UserID == ID).Timestamp;

            foreach (var tag in review.Tags)
            {
                if (ReviewStatus.ReviewedTags.ContainsKey(tag))
                {
                    ReviewStatus.ReviewedTags[tag]++;
                }
                else
                {
                    ReviewStatus.ReviewedTags[tag] = 1;
                }

                tagTimestamps[tag] = timestamp;
            }

            if (prevTags != null && prevTags.Count != 0)
            {
                if (prevTags.Any(t => review.Tags.Contains(t)))
                {
                    reviewsSinceCurrentTags = 0;
                }
                else
                {
                    reviewsSinceCurrentTags++;
                }
            }

            // NOT ENOUGH DATAZ.
            if (ReviewStatus.Reviews.Count < 9) { return; }

            var tagsSum = ReviewStatus.ReviewedTags.Sum(t => t.Value);
            var highKvs = ReviewStatus.ReviewedTags.Where(t => t.Value >= tagsSum * (1F / 15)).ToDictionary(t => t.Key, t => t.Value);

            // Not enough (accurate) data to continue analysis.
            if (highKvs.Count == 0) { return; }

            var maxTag = highKvs.Max(t => t.Value);
            var topTags = highKvs.Where(t => t.Value >= ((maxTag / 3) * 2)).Select(t => t.Key).ToList();
            var avgNoiseFloor = ReviewStatus.ReviewedTags.Where(t => !highKvs.ContainsKey(t.Key)).Average(t => t.Value);
            prevTags = prevTags ?? topTags;

            // They've started reviewing a different tag.
            if (topTags.Count > 3 ||
                reviewsSinceCurrentTags >= 3 ||
                topTags.Any(t => !prevTags.Contains(t)))
            {
                HandleTagChange(ref topTags, avgNoiseFloor);
            }
        }

        private void HandleTagChange(ref List<string> topTags, float avgNoiseFloor)
        {
            try
            {
                while (topTags.Count > 3)
                {
                    var oldestTag = new KeyValuePair<string, DateTime>(null, DateTime.MaxValue);
                    foreach (var tag in topTags)
                    {
                        if (tagTimestamps[tag] < oldestTag.Value)
                        {
                            oldestTag = new KeyValuePair<string, DateTime>(tag, tagTimestamps[tag]);
                        }
                    }
                    ReviewStatus.ReviewedTags[oldestTag.Key] = avgNoiseFloor;
                    topTags.Remove(oldestTag.Key);
                }

                List<string> finishedTags;

                if (reviewsSinceCurrentTags >= 3)
                {
                    finishedTags = prevTags;
                }
                else
                {
                    var tt = topTags;
                    finishedTags = prevTags.Where(t => !tt.Contains(t)).ToList();
                }

                EventManager.CallListeners(UserEventType.CurrentTagsChanged, finishedTags);

                foreach (var tag in finishedTags)
                {
                    ReviewStatus.ReviewedTags[tag] = avgNoiseFloor;
                }

                prevTags = null;
                reviewsSinceCurrentTags = 0;
            }
            catch (Exception ex)
            {
                EventManager.CallListeners(UserEventType.InternalException, ex);
            }
        }
    }
}
