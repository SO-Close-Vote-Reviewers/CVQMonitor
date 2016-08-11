namespace CVQMonitor

open System
open System.Threading
open System.Threading.Tasks

type User (userID : int) as this =
    //TODO: Ideally we should be fetching the review limit, not hardcoding it in.
    let reviewLimit = 40
    let itemReviewedEv = new Event<User * Review> ()
    let reviewingStartedEv = new Event<User> ()
    let reviewingLimitReachedEv = new Event<User> ()
    let mutable dispose = false
    let mutable isReviewing = false
    let mutable lastReviewTime = DateTime.MinValue
    let mutable reviewCache : Review list = []
    let mutable trueReviewCount = 0
    let mutable isMod = false

    let updateTrueReviewCount () =
        trueReviewCount <- UserProfileScraper.GetTodayReviewCount userID

    let checkLimitReached () =
        if not isMod && isReviewing && (trueReviewCount >= reviewLimit || reviewCache.Length >= reviewLimit) then
            isReviewing <- false
            reviewingLimitReachedEv.Trigger this

    let addNewReviews () =
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
            let review = new Review (fst rev, userID)
            reviewCache <- review :: reviewCache
            itemReviewedEv.Trigger (this, review)
            if review.Timestamp > lastReviewTime then
                lastReviewTime <- review.Timestamp

    let reviewPoller () =
        while not dispose do
            Thread.Sleep 15000
            if isReviewing then
                addNewReviews ()
                updateTrueReviewCount ()
                checkLimitReached ()
                if DateTime.UtcNow - lastReviewTime > TimeSpan.FromMinutes 2.5 then
                    isReviewing <- false

    let dailyReset () =
        while not dispose do
            let wait = 24.0 - DateTime.UtcNow.TimeOfDay.TotalHours
            TimeSpan.FromHours wait |> Thread.Sleep
            reviewCache <- []
            trueReviewCount <- 0
            isReviewing <- false

    do
        isMod <- UserProfileScraper.IsModerator userID
        let revsToday = UserProfileScraper.GetCloseVoteReviewsToday userID
        for rev in revsToday do
            let review = new Review (fst rev, userID)
            reviewCache <- review :: reviewCache
        CVQActivityMonitor.NonAuditReviewed.Add (fun id ->
            if id = userID then
                isReviewing <- true
                if lastReviewTime.Date <> DateTime.UtcNow.Date then
                    reviewingStartedEv.Trigger this
                    updateTrueReviewCount ()
                    checkLimitReached ()
        )
        Task.Run reviewPoller |> ignore
        Task.Run dailyReset |> ignore

    [<CLIEvent>]
    member this.ItemReviewed = itemReviewedEv.Publish
    
    [<CLIEvent>]
    member this.ReviewingStarted = reviewingStartedEv.Publish

    [<CLIEvent>]
    member this.ReviewingLimitReached = reviewingLimitReachedEv.Publish

    member this.ID = userID

    member this.IsMod = isMod

    member this.ReviewsToday = reviewCache

    member this.TrueReviewCount = Math.Max (trueReviewCount, reviewCache.Length)

    interface IDisposable with
        member this.Dispose () =
            if not dispose then
                dispose <- true
                isReviewing <- false
                GC.SuppressFinalize this
