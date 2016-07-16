module internal CVQMonitor.ReviewParser

open System
open System.Collections.Generic
open System.Text.RegularExpressions

let private regOpts = RegexOptions.Compiled ||| RegexOptions.CultureInvariant
let private resultDataBasePtn = """(?is)<div\s*?class="review-results"\s*?>\s*?<a\s*?href="/users/$USERID$/.*?">(.*?)</a>.*?<span\s*?title="(.*?)".*?<b>([a-z\s]+)</b>\s*?</div>"""
let private auditPassedReg = new Regex ("""(?is)<strong>\s*?review audit (passed|failed)""", regOpts)
let private tagsReg = new Regex ("""(?is)<div\s*?class="post-taglist">(\s*?<a\s*?href="/questions/tagged/\S+?".*?>(\S+?)</a>)+""", regOpts)

let GetReviewResultData html userID =
    let ptn = resultDataBasePtn.Replace ("$USERID$", userID.ToString ())
    let m = Regex.Match (html, ptn)
    if not m.Success then
        (null, DateTime.MinValue, ReviewAction.Null)
    else
        let name = m.Groups.[1].Value
        let timestampStr = m.Groups.[2].Value
        let timestamp = (DateTime.Parse timestampStr).ToUniversalTime ()
        let action =
            match m.Groups.[3].Value.ToLowerInvariant () with
            | "close" -> ReviewAction.Close
            | "leave open" -> ReviewAction.LeaveOpen
            | _ -> ReviewAction.Null
        (name, timestamp, action)

let AuditPassed html =
    let m = auditPassedReg.Match html
    if not m.Success then
        new Nullable<bool> ()
    else
        new Nullable<bool> (m.Groups.[1].Value = "passed")

let GetPostTags html =
    let m = tagsReg.Match html
    let tags = new List<string> ()
    if m.Success then
        for (t : Capture) in m.Groups.[2].Captures do
            tags.Add t.Value
    tags