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
    let pollerAre = new AutoResetEvent(false)
    let mutable lastPollerWait = DateTime.MinValue
    let mutable isPollerThrottled = false
    let mutable dispose = false
    let mutable isReviewing = false
    let mutable lastReviewTime = DateTime.MinValue
    let mutable reviewsToday = 0
    let mutable isMod = false

    let updateReviewsToday() =
        Console.WriteLine(Counter.Get() + "Fetching today's review count for " + userID.ToString())
        reviewsToday <- UserProfileScraper.GetTodayReviewCount userID

    let checkLimitReached() =
        Console.WriteLine(Counter.Get() + "Checking if review limit has been reached for " + userID.ToString())
        if not isMod && isReviewing && reviewsToday >= reviewLimit then
            isReviewing <- false
            reviewLimitReachedEv.Trigger this
            Console.WriteLine(Counter.Get() + "Triggered reviewLimitReached event for " + userID.ToString())

    // Scrapes a user's profile for new reviews, adding any 
    // to the reviewCache and calling the ItemReviewed event.
    let addNewReviews() =
        Console.WriteLine(Counter.Get() + "Checking for new reviews completed by " + userID.ToString())
        let revsToCheck =
            UserProfileScraper.GetReviewsByPage userID 1
            |> Seq.filter (fun x ->
                fst x > 0 &&
                reviewCache
                |> Seq.exists (fun z -> z.ID = fst x)
                |> not
            )
        for rev in revsToCheck do
            try
                let review = new Review(fst rev, userID)
                itemReviewedEv.Trigger(this, review)
                Console.WriteLine(Counter.Get() + "Triggered itemReviewed event for " + userID.ToString() + " @ " + (fst rev).ToString())
                reviewCache.Enqueue(review)
                if reviewCache.Count > 40 then
                    reviewCache.Dequeue() |> ignore
                if review.Timestamp > lastReviewTime then
                    lastReviewTime <- review.Timestamp
            with
            |_ as e -> Console.WriteLine(e)

    // Periodically scans a user's profile for new reviews.
    let reviewPoller = async {
        while not dispose do
            Thread.Sleep 500
            if isReviewing then
                addNewReviews()
                updateReviewsToday()
                checkLimitReached()
                lastPollerWait <- DateTime.UtcNow
                if (DateTime.UtcNow - lastReviewTime).TotalMinutes < 3.0 then
                    Console.WriteLine(Counter.Get() + "Poller active/unthrottled for " + userID.ToString())
                    // Sleep just 20 secs if they're active.
                    isPollerThrottled <- false
                    pollerAre.WaitOne(1000 * 20) |> ignore
                else
                    Console.WriteLine(Counter.Get() + "Poller active/throttled for  " + userID.ToString() + " due to inactivity")
                    // Otherwise, check every 5 mins.
                    isPollerThrottled <- true
                    pollerAre.WaitOne(1000 * 60 * 5) |> ignore
        }

    do
        isMod <- UserProfileScraper.IsModerator userID
        let revs = UserProfileScraper.GetReviewsByPage userID 1
        for rev in revs do
            let review = new Review(fst rev, userID)
            reviewCache.Enqueue(review)
        CVQActivityMonitor.NonAuditReviewed.Add (fun id ->
            if id = userID then
                isReviewing <- true
                if isPollerThrottled then
                    if (DateTime.UtcNow - lastPollerWait).TotalSeconds > 20.0 then
                        Console.WriteLine(Counter.Get() + "Unthrottling poller for " + userID.ToString() + " due to activity")
                        pollerAre.Set() |> ignore
                    else
                        async {
                            let wait = int(Math.Round(20.0 - (DateTime.UtcNow - lastPollerWait).TotalSeconds))
                            Thread.Sleep wait
                            Console.WriteLine(Counter.Get() + "Unthrottling poller for " + userID.ToString() + " due to activity")
                            pollerAre.Set() |> ignore
                        } |> Async.Start
                if lastReviewTime.Date <> DateTime.UtcNow.Date then
                    lastReviewTime <- DateTime.UtcNow.Date
                    reviewsToday <- 0
                    reviewingStartedEv.Trigger this
                    Console.WriteLine(Counter.Get() + "Triggered reviewingStarted event for " + userID.ToString())
        )
        reviewPoller |> Async.Start

    [<CLIEvent>]
    member this.ItemReviewed = itemReviewedEv.Publish
    
    [<CLIEvent>]
    member this.ReviewingStarted = reviewingStartedEv.Publish

    [<CLIEvent>]
    member this.ReviewLimitReached = reviewLimitReachedEv.Publish

    member this.ID = userID

    member this.IsMod = isMod

    member this.TrueReviewCount = reviewsToday

    member this.ReviewsToday =
        let x =
            reviewCache
            |> Seq.filter (fun r ->
                r.Timestamp.Date = DateTime.UtcNow.Date
            )
        List<Review>(x)

    interface IDisposable with
        member this.Dispose () =
            if not dispose then
                dispose <- true
                isReviewing <- false
                GC.SuppressFinalize this
