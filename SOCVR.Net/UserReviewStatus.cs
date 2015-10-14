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
    public class UserReviewStatus
    {
        private DateTime lastReview;
        private HashSet<DateTime> reviews;
        private Action reviewLimitReachedCallback;
        private int reviewsCompleted;

        public int QueuedReviews { get; set; }

        public int ReviewsToday
        {
            get
            {
                return reviewsCompleted;
            }

            internal set
            {
                if (value == ReviewLimit && reviewLimitReachedCallback != null)
                {
                    reviewLimitReachedCallback();
                }

                reviewsCompleted = value;
            }
        }

        public double AvgReviewsPerMin
        {
            get
            {
                if (reviews.Count == 0) { return 0; }
                reviews = new HashSet<DateTime>(reviews.Where(r => r.AddHours(2) > DateTime.UtcNow));
                if (reviews.Count == 0) { return 0; }
                return reviews.Count / (reviews.Max() - reviews.Min()).TotalMinutes;
            }
        }

        internal DateTime LastReview
        {
            get
            {
                return lastReview;
            }

            set
            {
                reviews.Add(value);
                ReviewsToday++;
                lastReview = value;
            }
        }

        internal int ReviewLimit { get; set; }



        public UserReviewStatus(Action reviewLimitReachedCallback)
        {
            this.reviewLimitReachedCallback = reviewLimitReachedCallback;
            LastReview = DateTime.MinValue;
        }
    }
}
