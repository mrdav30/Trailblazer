namespace Trailblazer.Utility.Coroutines
{
    /// <summary>
    /// src - https://forum.unity.com/threads/coroutine-wait-x-frames-not-seconds.550168/
    /// </summary>
    public class WaitForFrames : LockedYieldInstruction
    {
        private readonly int _targetFrameCount;

        public WaitForFrames(int numberOfFrames)
        {
            _targetFrameCount = TrailblazerSettings.FrameCount + numberOfFrames;
        }

        public override bool KeepWaiting
        {
            get
            {
                return TrailblazerSettings.FrameCount < _targetFrameCount;
            }
        }
    }
}
