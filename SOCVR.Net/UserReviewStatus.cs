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
        //private DateTime lastReview;
        //private HashSet<DateTime> reviewTimestamps;
        private Action reviewLimitReachedCallback;
        private int reviewsCompleted;

        public int QueuedReviews { get; set; }

        public HashSet<ReviewItem> Reviews { get; set; }

        public int ReviewsCompletedCount
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

        // Do we even need this?
        //public double AvgReviewsPerMin
        //{
        //    get
        //    {
        //        if (reviewTimestamps.Count == 0) { return 0; }
        //        reviewTimestamps = new HashSet<DateTime>(reviewTimestamps.Where(r => r.AddHours(2) > DateTime.UtcNow));
        //        if (reviewTimestamps.Count == 0) { return 0; }
        //        return reviewTimestamps.Count / (reviewTimestamps.Max() - reviewTimestamps.Min()).TotalMinutes;
        //    }
        //}

        // Do we even need this?
        //internal DateTime LastReview
        //{
        //    get
        //    {
        //        return lastReview;
        //    }

        //    set
        //    {
        //        reviewTimestamps.Add(value);
        //        ReviewsCompletedCount++;
        //        lastReview = value;
        //    }
        //}

        internal int ReviewLimit { get; set; }

        internal Dictionary<string, float> ReviewedTags { get; set; }



        public UserReviewStatus(Action reviewLimitReachedCallback)
        {
            this.reviewLimitReachedCallback = reviewLimitReachedCallback;
            //reviewTimestamps = new HashSet<DateTime>();
            Reviews = new HashSet<ReviewItem>();
            ReviewedTags = new Dictionary<string, float>();
            //LastReview = DateTime.MinValue;
        }
    }
}
