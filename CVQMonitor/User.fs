namespace CVQMonitor

open System

type User (userID : int) =
    let mutable dispose = false
    let mutable lastReviewTime = DateTime.MinValue
    let mutable reviewsTodayIDs = [||]
    let mutable reviewsTodayCache : Review [] = [||]
    let nonAuditReviewedEv = new Event<Review> ()
    let reviewingStartedEv = new Event<unit> ()

    let HandleNonAuditReviewed () =
        if lastReviewTime.Date <> DateTime.UtcNow.Date then
            reviewingStartedEv.Trigger ()
        let review = new Review 0
        lastReviewTime <- review.Timestamp
        nonAuditReviewedEv.Trigger review

    do
        CVQActivityMonitor.NonAuditReviewed.Add (fun id -> if id = userID then HandleNonAuditReviewed ())

    [<CLIEvent>]
    member this.NonAuditReviewed = nonAuditReviewedEv.Publish

    [<CLIEvent>]
    member this.ReviewingStarted = reviewingStartedEv.Publish

    member this.ID = userID

    member this.ReviewsToday = reviewsTodayCache

    interface IDisposable with
        member this.Dispose () =
            if not dispose then
                dispose <- true
                GC.SuppressFinalize(this)
