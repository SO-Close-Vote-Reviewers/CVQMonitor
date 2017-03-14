module internal CVQMonitor.CVQActivityMonitor

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Net.Sockets
open System.Net.WebSockets

let mutable private lastMsg = DateTime.MaxValue
let private endpoint = Uri "ws://qa.sockets.stackexchange.com"
let private onOpenMsg = ArraySegment<byte>(Encoding.UTF8.GetBytes "1-review-dashboard-update")
let private pongMsg = ArraySegment<byte>(Encoding.UTF8.GetBytes """{"action":"hb","data":"hb"}""")
let private userEv = new Event<int>()

[<CLIEvent>]
let NonAuditReviewed = userEv.Publish

let private handleMessage (msg : String) =
    lastMsg <- DateTime.UtcNow
    let data = Json.GetField msg "data"
    match data with
    | "hb" -> true
    | _ as d when d <> "" ->
        let dataEscaped = Json.EscapeData data
        let i = Json.GetField dataEscaped "i"
        let u = Json.GetField dataEscaped "u"
        if i = "2" && u <> "" then
            let userID = Int32.Parse u
            userEv.Trigger userID
        false
    | _ -> false

let listenerLoop = async {
    while true do
        try
            let socket = new ClientWebSocket()
            socket.ConnectAsync(endpoint, CancellationToken.None).Wait()
            socket.SendAsync(onOpenMsg, WebSocketMessageType.Text, true, CancellationToken.None).Wait()
            Console.WriteLine(Counter.Get() + "WebSocket connected.")
            while socket.State = WebSocketState.Open do
                let bf = ArraySegment(Array.zeroCreate<byte>(1024 * 10))
                let responseResult =
                    socket.ReceiveAsync(bf, CancellationToken.None)
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
                Console.WriteLine(Counter.Get() + "WebSocket message received.")
                let sendPong =
                    match responseResult with
                    | _ as r when r.Count > 0 ->
                        let msgChars =
                            Encoding.UTF8.GetString(bf.Array)
                            |> Seq.filter (fun c -> int c <> 0)
                            |> Array.ofSeq
                        let msg = new string(msgChars)
                        handleMessage(msg)
                    | _ -> false
                if sendPong then
                    socket.SendAsync(pongMsg, WebSocketMessageType.Text, true, CancellationToken.None).Wait()
        with
        | _ as e when e.InnerException = null |> not && e.InnerException.InnerException = null |> not && e.InnerException.InnerException :? SocketException && (e.InnerException.InnerException :?> SocketException).ErrorCode = 10053 -> ()
        | :? OperationCanceledException -> ()
        | _ as e ->
            Console.WriteLine(e)
        Thread.Sleep 5000
    }

do
    listenerLoop |> Async.Start