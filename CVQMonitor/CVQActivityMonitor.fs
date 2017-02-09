module internal CVQMonitor.CVQActivityMonitor

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Net.WebSockets

let mutable private socket = new ClientWebSocket()
let mutable private lastMsg = DateTime.MaxValue
let private endpoint = Uri "ws://qa.sockets.stackexchange.com"
let private sendMsg = ArraySegment<byte>(Encoding.UTF8.GetBytes "1-review-dashboard-update")
let private userEv = new Event<int>()

[<CLIEvent>]
let NonAuditReviewed = userEv.Publish

let private handleMessage (msg : String) =
    lastMsg <- DateTime.UtcNow
    let data = Json.GetField msg "data"
    if data <> "" then
        let dataEscaped = Json.EscapeData data
        let i = Json.GetField dataEscaped "i"
        let u = Json.GetField dataEscaped "u"
        if i = "2" && u <> "" then
            let userID = Int32.Parse u
            userEv.Trigger userID

let initSocket() = 
    if socket.State = WebSocketState.Open then
        socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait()
    socket <- new ClientWebSocket()
    socket.ConnectAsync(endpoint, CancellationToken.None).Wait()
    socket.SendAsync(sendMsg, WebSocketMessageType.Text, true, CancellationToken.None).Wait()

let listenerLoop() =
    while true do
        let bf = ArraySegment(Array.zeroCreate<byte>(1024 * 10))
        let responseResult =
            socket.ReceiveAsync(bf, CancellationToken.None)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        match responseResult with
        | _ as r when r.Count > 0 ->
            let msgChars =
                Encoding.UTF8.GetString(bf.Array)
                |> Seq.filter (fun c -> int c <> 0)
                |> Array.ofSeq
            let msg = new string(msgChars)
            handleMessage(msg)
        | _ -> ()

let socketRecovery() =
    while true do
        Thread.Sleep 1000
        if DateTime.UtcNow - lastMsg > TimeSpan.FromSeconds 30.0 then
            initSocket ()
            lastMsg <- DateTime.MaxValue

do
    initSocket()
    Task.Run socketRecovery |> ignore
    Task.Run listenerLoop |> ignore