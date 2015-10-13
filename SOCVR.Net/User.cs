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
using System.Text;
using System.Threading.Tasks;

namespace SOCVRDotNet
{
    public class User
    {
        private string fkey;

        public int ID { get; private set; }

        public UserReviewStatus ReviewStatus { get; private set; }

        public EventManager EventManager { get; private set; }



        public User(string fkey, int userID, UserReviewStatus reviewStatus)
        {
            this.fkey = fkey;
            ID = userID;
            ReviewStatus = reviewStatus;
            EventManager = new EventManager();
        }



        internal void ProcessReviews()
        {
            var ids = UserDataFetcher.GetLastestCVReviewIDs(fkey, ID, ReviewStatus.QueuedReviews);

            foreach (var id in ids)
            {
                var review = new ReviewItem(id, fkey);

                // Notify audit listeners if necessary.
                if (review.AuditPassed != null)
                {
                    var type = review.AuditPassed == false
                        ? UserEventType.AuditFailed
                        : UserEventType.AuditPassed;

                    EventManager.CallListeners(type, review);
                }

                EventManager.CallListeners(UserEventType.ItemReviewed, review);
            }
        }
    }
}
