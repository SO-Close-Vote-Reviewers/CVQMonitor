namespace CVQMonitor

open System
open RestSharp

type Review (reviewID : int, reviewerID : int) =
    let reviewBaseUrl = "http://stackoverflow.com/review/next-task/"
    
    let mutable reviewerName = ""
    let mutable timestamp = DateTime.MinValue
    let mutable tags = [""]
    let mutable action = ReviewAction.Close
    let mutable auditPassed = new Nullable<bool> ()

    do
        let reviewUrl = reviewBaseUrl + reviewID.ToString ()
        let req = new RestRequest (reviewUrl, Method.POST)
        req.AddParameter ("taskTypeId", "2") |> ignore
        req.AddParameter ("fkey", FkeyCollector.Fkey) |> ignore
        let res = RequestScheduler.ProcessRequest req
        if String.IsNullOrWhiteSpace res.Content |> not then
            let html = Json.GetField res.Content "instructions" |> Json.EscapeData
            let (name, time, act) = ReviewParser.GetReviewResultData html reviewerID
            reviewerName <- name
            timestamp <- time
            action <- act
        ()

    member this.ID = reviewID

    member this.ReviewerID = reviewerID

    member this.ReviewerName  = reviewerName

    member this.Timestamp = timestamp

    member this.Tags = tags

    member this.Action = action

    member this.AuditPassed = auditPassed