module internal CVQMonitor.Json

open System
open System.Text.RegularExpressions

// Evil I know...

let GetField json name =
    if String.IsNullOrWhiteSpace json then
        ""
    else
        let ptn = "\"" + name + """"\s*?:\s*?"?(.*?)"?(}\Z|,")"""
        let m = Regex.Match(json, ptn, RegexOptions.CultureInvariant)
        if m.Success then
            m.Groups.[1].Value
        else
            ""

let EscapeData (data : string) = 
    let mutable dataEscaped = data
    dataEscaped <- dataEscaped.Replace("\\\"", "\"")
    dataEscaped <- dataEscaped.Replace("\\r", "\r")
    dataEscaped <- dataEscaped.Replace("\\n", "\n")
    dataEscaped