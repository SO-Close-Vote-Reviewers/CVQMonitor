namespace CVQMonitor

open System

type User (userID : int) =
    let mutable dispose = false
    let mutable reviewedItemIDs = [||]
    let mutable reviewsTodayCache : Review [] = [||]
    let nonAuditReviewedEv = new Event<Review> ()

    let HandleNonAuditReviewed () =
        let review = new Review 0
        nonAuditReviewedEv.Trigger review

    do
        CVQActivityMonitor.NonAuditReviewed.Add (fun id -> if id = userID then HandleNonAuditReviewed ())

    [<CLIEvent>]
    member this.NonAuditReviewed = nonAuditReviewedEv.Publish

    member this.ID = userID

    member this.ReviewsToday
        with get () = reviewsTodayCache

    interface IDisposable with
        member this.Dispose () =
            if not dispose then
                dispose <- true
                GC.SuppressFinalize(this)
