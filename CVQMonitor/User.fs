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
    let reviewCache = HashSet<Review>()
    let mutable dispose = false
    let mutable isReviewing = false
    let mutable lastReviewTime = DateTime.MinValue
    let mutable trueReviewCount = 0
    let mutable isMod = false

    let updateTrueReviewCount() =
        trueReviewCount <- UserProfileScraper.GetTodayReviewCount userID

    let checkLimitReached() =
        if not isMod && isReviewing && this.TrueReviewCount >= reviewLimit then
            isReviewing <- false
            reviewLimitReachedEv.Trigger this

    let addNewReviews() =
        let revsToCheck =
            UserProfileScraper.GetReviewsByPage userID 1
            |> Seq.filter (fun x ->
                fst x > 0 &&
                (snd x).Date = DateTime.UtcNow.Date &&
                reviewCache
                |> Seq.exists (fun z -> z.ID = fst x)
                |> not
            )
        for rev in revsToCheck do
            let review = new Review(fst rev, userID)
            reviewCache.Add(review) |> ignore
            itemReviewedEv.Trigger(this, review)
            if review.Timestamp > lastReviewTime then
                lastReviewTime <- review.Timestamp

    let reviewPoller() =
        while not dispose do
            Thread.Sleep 500
            if isReviewing then
                addNewReviews()
                updateTrueReviewCount()
                checkLimitReached()
                if (DateTime.UtcNow - lastReviewTime).TotalMinutes < 3.0 then
                    // Sleep just 10 secs if they're active.
                    Thread.Sleep 10000
                else
                    // Otherwise, check every 2 mins.
                    Thread.Sleep(1000 * 60 * 2)

    do
        isMod <- UserProfileScraper.IsModerator userID
        let revsToday = UserProfileScraper.GetCloseVoteReviewsToday userID
        for rev in revsToday do
            let review = new Review(fst rev, userID)
            reviewCache.Add(review) |> ignore
        CVQActivityMonitor.NonAuditReviewed.Add (fun id ->
            if id = userID then
                isReviewing <- true
                if lastReviewTime.Date <> DateTime.UtcNow.Date then
                    reviewCache.Clear() |> ignore
                    trueReviewCount <- 0
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

    member this.ReviewsToday = reviewCache

    member this.TrueReviewCount = Math.Max(trueReviewCount, reviewCache.Count)

    interface IDisposable with
        member this.Dispose () =
            if not dispose then
                dispose <- true
                isReviewing <- false
                GC.SuppressFinalize this
