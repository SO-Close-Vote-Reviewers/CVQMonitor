namespace CVQMonitor

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

type User (userID : int) as this =
    //TODO: Ideally we should be fetching the review limit, not hardcoding it in.
    let reviewLimit = 40
    let itemReviewedEv = new Event<User * Review>()
    let reviewingStartedEv = new Event<User>()
    let reviewLimitReachedEv = new Event<User>()
    let reviewCache = Queue<Review>()
    let mutable dispose = false
    let mutable isReviewing = false
    let mutable lastReviewTime = DateTime.MinValue
    let mutable reviewsToday = 0
    let mutable isMod = false

    let updateReviewsToday() =
        reviewsToday <- UserProfileScraper.GetTodayReviewCount userID

    let checkLimitReached() =
        if not isMod && isReviewing && reviewsToday >= reviewLimit then
            isReviewing <- false
            reviewLimitReachedEv.Trigger this

    // Scrapes a user's profile for new reviews, adding any 
    // to the reviewCache and calling the ItemReviewed event.
    let addNewReviews() =
        let revsToCheck =
            UserProfileScraper.GetReviewsByPage userID 1
            |> Seq.filter (fun x ->
                fst x > 0 &&
                reviewCache
                |> Seq.exists (fun z -> z.ID = fst x)
                |> not
            )
        for rev in revsToCheck do
            let review = new Review(fst rev, userID)
            itemReviewedEv.Trigger(this, review)
            reviewCache.Enqueue(review)
            if reviewCache.Count > 40 then
                reviewCache.Dequeue() |> ignore
            if review.Timestamp > lastReviewTime then
                lastReviewTime <- review.Timestamp

    // Periodically scans a user's profile for new reviews.
    let reviewPoller() =
        while not dispose do
            Thread.Sleep 500
            if isReviewing then
                addNewReviews()
                updateReviewsToday()
                checkLimitReached()
                if (DateTime.UtcNow - lastReviewTime).TotalMinutes < 3.0 then
                    // Sleep just 20 secs if they're active.
                    Thread.Sleep(1000 * 20)
                else
                    // Otherwise, check every 5 mins.
                    Thread.Sleep(1000 * 60 * 5)

    do
        isMod <- UserProfileScraper.IsModerator userID
        let revsToday = UserProfileScraper.GetCloseVoteReviewsToday userID
        for rev in revsToday do
            let review = new Review(fst rev, userID)
            reviewCache.Enqueue(review)
        CVQActivityMonitor.NonAuditReviewed.Add (fun id ->
            if id = userID then
                isReviewing <- true
                if lastReviewTime.Date <> DateTime.UtcNow.Date then
                    reviewsToday <- 0
                    reviewingStartedEv.Trigger this
        )
        Task.Run reviewPoller |> ignore

    [<CLIEvent>]
    member this.ItemReviewed = itemReviewedEv.Publish
    
    [<CLIEvent>]
    member this.ReviewingStarted = reviewingStartedEv.Publish

    [<CLIEvent>]
    member this.ReviewLimitReached = reviewLimitReachedEv.Publish

    member this.ID = userID

    member this.IsMod = isMod

    member this.ReviewsToday =
        let x =
            reviewCache
            |> Seq.filter (fun r ->
                r.Timestamp.Date = DateTime.UtcNow.Date
            )
        List<Review>(x)

    member this.TrueReviewCount = reviewsToday

    interface IDisposable with
        member this.Dispose () =
            if not dispose then
                dispose <- true
                isReviewing <- false
                GC.SuppressFinalize this
