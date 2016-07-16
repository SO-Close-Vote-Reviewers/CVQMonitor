module CVQMonitor.FkeyCollector

open System
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open RestSharp

let private fkeyReg = new Regex ("""(?is)<input.*?name="fkey"\s*?value="([0-9a-f]+)">""", RegexOptions.Compiled ||| RegexOptions.CultureInvariant)

let mutable Fkey = ""

let private updateFkey () =
    let req = new RestRequest ("https://stackoverflow.com/users/login", Method.GET)
    let res = RequestScheduler.ProcessRequest req
    if String.IsNullOrWhiteSpace res.Content |> not then
        let m = fkeyReg.Match res.Content
        if not m.Success then
            failwith "unable to fetch fkey"
        else
            Fkey <- m.Groups.[1].Value

do
    updateFkey ()
    Task.Run (fun () ->
        while true do
            let wait = TimeSpan.FromHours (24.0 - DateTime.UtcNow.TimeOfDay.TotalHours)
            Thread.Sleep wait
            updateFkey ()
    )
    |> ignore