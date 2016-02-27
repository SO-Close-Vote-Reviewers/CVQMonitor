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

namespace SOCVRDotNet
{
    /// <summary>
    /// Describes the type of event that the User Tracker has triggered.
    /// </summary>
    public enum EventType
    {
        /// <summary>
        /// An exception happened within the library.
        /// </summary>
        InternalException = -1,

        /// <summary>
        /// A standard review item has been completed.
        /// </summary>
        ItemReviewed,

        /// <summary>
        /// An audit has been passed.
        /// </summary>
        AuditPassed,

        /// <summary>
        /// An audit has been failed.
        /// </summary>
        AuditFailed,
        
        /// <summary>
        /// The library detected the first review of the day for this user.
        /// </summary>
        ReviewingStarted,

        /// <summary>
        /// The library detected the user has completed all review items for the day.
        /// </summary>
        ReviewingCompleted,

        /// <summary>
        /// The library detected that the user has changed the tags they are reviewing.
        /// </summary>
        CurrentTagsChanged
    }
}