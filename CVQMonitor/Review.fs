namespace CVQMonitor

open System
open System.Collections.Generic
open RestSharp

type Review (reviewID : int, reviewerID : int) =
    let reviewBaseUrl = "http://stackoverflow.com/review/next-task/"
    let mutable reviewerName = ""
    let mutable timestamp = DateTime.MinValue
    let mutable action = ReviewAction.Close
    let mutable auditPassed = new Nullable<bool>()
    let mutable tags = new List<string>()

    do
        let reviewUrl = reviewBaseUrl + reviewID.ToString()
        let req = new RestRequest (reviewUrl, Method.POST)
        req.AddParameter ("taskTypeId", "2") |> ignore
        req.AddParameter ("fkey", FkeyCollector.Fkey) |> ignore
        let res = RequestScheduler.ProcessRequest req
        if String.IsNullOrWhiteSpace res.Content |> not then
            let resultsHtml = Json.GetField res.Content "instructions" |> Json.EscapeData
            let postHtml = Json.GetField res.Content "content" |> Json.EscapeData
            let (name, time, act) = ReviewParser.GetReviewResultData resultsHtml reviewerID
            reviewerName <- name
            timestamp <- time
            action <- act
            auditPassed <- ReviewParser.AuditPassed resultsHtml
            tags <- ReviewParser.GetPostTags postHtml

    member this.ID = reviewID
    member this.ReviewerID = reviewerID
    member this.ReviewerName  = reviewerName
    member this.Timestamp = timestamp
    member this.Action = action
    member this.AuditPassed = auditPassed
    member this.Tags = tags