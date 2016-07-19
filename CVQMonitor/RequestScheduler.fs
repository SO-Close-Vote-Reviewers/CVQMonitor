module CVQMonitor.RequestScheduler

open System
open System.Collections.Concurrent
open System.Net
open System.Threading
open System.Threading.Tasks
open RestSharp

let private requestQueue = new ConcurrentDictionary<int, RestRequest * (RestResponse -> unit)> ()
let private queueProcessorMre = new ManualResetEvent false

let mutable RequestsPerSecond = 90.0

let internal ProcessRequest (req : RestRequest) =
    let waitMre = new ManualResetEvent false
    let mutable response = new RestResponse ()
    let id =
        match requestQueue.Count with 
        | 0 -> 0
        | _ -> requestQueue.Keys
               |> Seq.max
               |> (+) 1
    requestQueue.[id] <- (
        req,
        fun res ->
            response <- res
            waitMre.Set () |> ignore
    )
    waitMre.WaitOne () |> ignore
    response

let private wait multiplier = 
    (60.0 / RequestsPerSecond) * 1000.0
    |> (*) multiplier
    |> Math.Round
    |> int
    |> queueProcessorMre.WaitOne
    |> ignore

let private getNextQueuedItem () =
    match requestQueue.Count with
    | 1 ->
        let key = requestQueue.Keys |> Seq.head
        let value = requestQueue.[key]
        (key, value)
    | _ ->
        let reqID =
            requestQueue.Keys
            |> Seq.min
        let key =
            requestQueue.Keys
            |> Seq.filter (fun x -> x = reqID)
            |> Seq.head
        let value = requestQueue.[key]
        (key, value)

let rec private sendRequest (req : RestRequest) attempt =
    let reqUri = new Uri (req.Resource)
    let baseUrl = reqUri.Scheme + "://" + reqUri.Host
    let client = new RestClient (baseUrl)
    req.Resource <- reqUri.PathAndQuery
    let mutable response = new RestResponse ()
    try
        response <- client.Execute(req) :?> RestResponse
    with
    | ex ->
        if response.ErrorException <> ex then
            response.ErrorException <- ex
    let responseCode = (int response.StatusCode).ToString ()
    if responseCode.StartsWith "5" then
        if attempt < 3 then
            wait 3.0
            response <- attempt + 1 |> sendRequest req
    response

let private processQueue () =
    while true do
        wait 1.0
        if not requestQueue.IsEmpty then
            let kv = getNextQueuedItem ()
            let req = snd kv |> fst
            let callback = snd kv |> snd
            let response = sendRequest req 0
            callback response
            fst kv
            |> requestQueue.TryRemove
            |> ignore
    ()

do
    Task.Run processQueue |> ignore