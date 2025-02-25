namespace Trailblazer.Utility.Coroutines
{
    public class WaitForNextSimulate : LockedYieldInstruction
    {
        private readonly int _checkedInFrameCount;

        public WaitForNextSimulate()
        {
            _checkedInFrameCount = TrailblazerSettings.FrameCount;
        }

        public override bool KeepWaiting
        {
            get
            {
                return TrailblazerSettings.FrameCount <= _checkedInFrameCount;
            }
        }
    }
}
