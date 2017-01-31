module internal CVQMonitor.CVQActivityMonitor

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open WebSocketSharp

let mutable private socket = new WebSocket "ws://qa.sockets.stackexchange.com"
let mutable private lastMsg = DateTime.MaxValue
let private userEv = new Event<int> ()
let private exEv = new Event<Exception> ()

[<CLIEvent>]
let NonAuditReviewed = userEv.Publish

[<CLIEvent>]
let ExceptionRaised  = exEv.Publish

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

let initSocket () = 
    if socket.ReadyState = WebSocketState.Open then
        socket.Close ()
    socket <- new WebSocket "ws://qa.sockets.stackexchange.com"
    socket.OnOpen.Add (fun e -> socket.Send "1-review-dashboard-update")
    socket.OnMessage.Add (fun e -> handleMessage e.Data)
    socket.OnError.Add (fun e -> exEv.Trigger e.Exception)
    socket.Connect ()

let socketRecovery () =
    while true do
        Thread.Sleep 1000
        if DateTime.UtcNow - lastMsg > TimeSpan.FromSeconds 30.0 then
            initSocket ()
            lastMsg <- DateTime.MaxValue

do
    initSocket ()
    Task.Run socketRecovery |> ignore