module  CVQMonitor.RequestScheduler

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open RestSharp

let private requestQueue = new ConcurrentDictionary<int, RestRequest * (RestResponse -> unit)> ()
let private queueProcessorMre = new ManualResetEvent false

let mutable RequestsPerSecond = 90

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
    60 / RequestsPerSecond * 1000
    |> (*) multiplier
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
    req.Resource <- reqUri.AbsolutePath.Remove (baseUrl.Length)
    try
        client.Execute(req) :?> RestResponse
    with
    //TODO: Only retry if a WebException is found in the response's
    // ErrorException property. Otherwise just return a new
    // RestResponse and thrown in the caught exception into
    // the ErrorException prop.
    | ex ->
        if attempt >= 3 then
            let e = new RestResponse ()
            e.ErrorException <- ex
            e
        else
            wait 3
            attempt + 1 |> sendRequest req 

let private processQueue () =
    while true do
        wait 1
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