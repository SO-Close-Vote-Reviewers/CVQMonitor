module internal CVQMonitor.UserProfileScraper

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open RestSharp

let private baseReviewHistoryTabUrl = "http://stackoverflow.com/ajax/users/tab/$USERID$?tab=activity&sort=reviews&page=$PAGENO$"
let private baseReviewCountUrl = "http://stackoverflow.com/review/user-info/2/$USERID$"
let private baseUserProfileUrl = "https://stackoverflow.com/users/$USERID$"
let private regOpts = RegexOptions.Compiled ||| RegexOptions.CultureInvariant
let private reviewItemReg = new Regex("""(?is)<tr\s+?class=""\s+?data-postid="\d+">.*?</tr>""", regOpts)
let private timestampReg = new Regex("""(?is)<div\s+?class="date(_brick)?"\s+?title="([0-9-:Z ]+)">""", regOpts)
let private reviewIDReg = new Regex("""(?is)<a\s+?href="\/review\/close\/(\d+?)"\s+?class="reviewed-action">""", regOpts)
let private reviewCountReg = new Regex("""today\s+?(\d+)""", regOpts)

let GetReviewsByPage (userID : int) (pageNo : int) =
    let mutable url = baseReviewHistoryTabUrl
    url <- url.Replace("$USERID$", userID.ToString())
    url <- url.Replace("$PAGENO$", pageNo.ToString())
    let req = new RestRequest(url, Method.GET)
    let res = RequestScheduler.ProcessRequest req
    let revs = List<int * DateTime>()
    if String.IsNullOrWhiteSpace res.Content |> not then
        let reviewItems = reviewItemReg.Matches res.Content
        for revItem in reviewItems do
            let closeRevID = reviewIDReg.Match revItem.Value
            if closeRevID.Success then
                let id = Int32.Parse closeRevID.Groups.[1].Value
                let revTime = timestampReg.Match revItem.Value
                let time = (DateTime.Parse revTime.Groups.[2].Value).ToUniversalTime()
                revs.Add(id, time)
    revs

let GetCloseVoteReviewsToday (userID : int) =
    let revs = new List<int * DateTime>()
    let mutable reviewsToday = true
    let mutable currentPage = 1
    while reviewsToday do
        let pageRevs = GetReviewsByPage userID currentPage
        for rev in pageRevs do
            if fst rev > 0 && (snd rev).Date = DateTime.UtcNow.Date then
                revs.Add rev
            elif (snd rev).Date < DateTime.UtcNow.Date then
                reviewsToday <- false
        currentPage <- currentPage + 1
    revs

let GetTodayReviewCount userID =
    let url = baseReviewCountUrl.Replace("$USERID$", userID.ToString())
    let req = new RestRequest(url, Method.GET)
    let res = RequestScheduler.ProcessRequest req
    if String.IsNullOrWhiteSpace res.Content then
        0
    else
        let m = reviewCountReg.Match res.Content
        Int32.Parse m.Groups.[1].Value

let IsModerator userID =
    let url = baseUserProfileUrl.Replace("$USERID$", userID.ToString())
    let req = new RestRequest(url, Method.GET)
    let res = RequestScheduler.ProcessRequest req
    if String.IsNullOrWhiteSpace res.Content then
        false
    else
        res.Content.Contains """<span class="mod-flair" title="moderator">&#9830;</span>"""