namespace CVQMonitor

open System
open RestSharp

type Review (reviewID : int) =

    let reviewBaseUrl = "http://stackoverflow.com/review/next-task/"

    do
        let reviewUrl = reviewBaseUrl + reviewID.ToString ()
        let req = new RestRequest (reviewUrl, Method.POST)
        req.AddParameter ("taskTypeId", "2") |> ignore
        req.AddParameter ("fkey", "") |> ignore
        let res = RequestScheduler.ProcessRequest req
        ()

    member this.ID = reviewID

    member this.Timestamp = DateTime.MinValue

    member this.Action = ReviewAction.Close

    member this.AuditPassed = new Nullable<bool> ()