module internal CVQMonitor.ReviewParser

open System
open System.Text.RegularExpressions

let private resultDataBasePtn = """(?is)<div\s*?class="review-results"\s*?>\s*?<a\s*?href="/users/$USERID$/.*?">(.*?)</a>.*?<span\s*?title="(.*?)".*?<b>([a-z\s]+)</b>\s*?</div>"""

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

let PassedAudit html =
    true