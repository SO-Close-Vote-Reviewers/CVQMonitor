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

namespace SOCVRDotNet
{
    /// <summary>
    /// This class holds data regarding a user's actions taken during a review.
    /// </summary>
    public class ReviewResult
    {
        /// <summary>
        /// The ID of the user.
        /// </summary>
        public int UserID { get; private set; }

        /// <summary>
        /// The user's display name.
        /// </summary>
        public string UserName { get; private set; }

        /// <summary>
        /// The action taken by the user.
        /// </summary>
        public ReviewAction Action { get; private set; }

        /// <summary>
        /// Date/time information (UTC) when the review action was taken.
        /// </summary>
        public DateTime Timestamp { get; private set; }



        public ReviewResult(int userId, string userName, ReviewAction action, DateTime timestamp)
        {
            UserID = userId;
            UserName = userName;
            Action = action;
            Timestamp = timestamp;
        }
    }
}
