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
//let mutable private cts = new CancellationTokenSource()
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

let listenerLoop() =
    while true do
        try
            let socket = new ClientWebSocket()
            socket.ConnectAsync(endpoint, CancellationToken.None).Wait()
            socket.SendAsync(onOpenMsg, WebSocketMessageType.Text, true, CancellationToken.None).Wait()
            while socket.State = WebSocketState.Open do
                let bf = ArraySegment(Array.zeroCreate<byte>(1024 * 10))
                let responseResult =
                    socket.ReceiveAsync(bf, CancellationToken.None(*cts.Token*))
                    |> Async.AwaitTask
                    |> Async.RunSynchronously
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

//let initSocket() =
//    try
//        cts.Cancel()
//        cts.Dispose()
//        cts <- new CancellationTokenSource()
//        if socket.State = WebSocketState.Open then
//            socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait()
//            socket.Dispose()
//        socket <- new ClientWebSocket()
//        socket.ConnectAsync(endpoint, CancellationToken.None).Wait()
//        socket.SendAsync(onOpenMsg, WebSocketMessageType.Text, true, CancellationToken.None).Wait()
//    with
//    | _ as e -> Console.WriteLine(e)
//    Task.Run listenerLoop |> ignore

//let socketRecovery() =
//    while true do
//        Thread.Sleep 1000
//        if DateTime.UtcNow - lastMsg > TimeSpan.FromSeconds 30.0 then
//            initSocket()
//            lastMsg <- DateTime.MaxValue

do
    Task.Run listenerLoop |> ignore
    //initSocket()
    //Task.Run socketRecovery |> ignore