namespace Trailblazer.Pathing
{
    public abstract class SurveyResult : ISurveyResult
    {
        public bool IsValid { get; protected set; }

        public bool IsInUse { get; protected set; }

        public string[] ChartsUtilized { get; protected set; }

        public int LastUsedFrame { get; protected set; }

        public int RequestHashKey { get; protected set; }

        public virtual bool HasPath => false;

        public void Checkout() => IsInUse = true;

        public void Release()
        {
            IsInUse = false;
            LastUsedFrame = TrailblazerManager.FrameCount;
        }

        public virtual void Reset()
        {
            IsValid = false;
            IsInUse = false;
            ChartsUtilized = null;
            LastUsedFrame = -1;
            RequestHashKey = -1;
        }
    }
}
