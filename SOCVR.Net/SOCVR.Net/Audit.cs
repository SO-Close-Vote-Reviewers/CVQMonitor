using System;



namespace SOCVRDotNet
{
    public class Audit
    {
        public string Url { get; private set; }
        public bool Passed { get; private set; }
        public Question Post { get; private set; }
        public DateTime ReviewDate { get; private set; }



        public Audit(string url)
        {
            
        }
    }
}
