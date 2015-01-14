using System;



namespace SOCVRDotNet
{
    public class ReviewResult
    {
        public string UserName { get; private set; }
        public ReviewAction Action { get; private set; }
        public DateTime TimeStamp { get; private set; }



        public ReviewResult(string userName, ReviewAction action, DateTime timeStamp)
        {
            UserName = userName;
            Action = action;
            TimeStamp = timeStamp;
        }
    }
}
