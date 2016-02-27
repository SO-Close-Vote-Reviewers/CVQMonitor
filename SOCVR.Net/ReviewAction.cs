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
    /// Describes the action the user took in a cv review.
    /// </summary>
    public enum ReviewAction
    {
        /// <summary>
        /// The user voted to leave the post open.
        /// </summary>
        LeaveOpen,

        /// <summary>
        /// The user voted to close the vote.
        /// </summary>
        Close,

        /// <summary>
        /// The user edited the post within the review queue.
        /// </summary>
        Edit
    }
}