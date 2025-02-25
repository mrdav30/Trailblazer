using FixedMathSharp;

namespace Trailblazer.Utility.Coroutines
{
    /// <summary>
    /// src - https://stackoverflow.com/questions/30056471/how-to-make-the-script-wait-sleep-in-a-simple-way-in-unity
    /// </summary>
    public class WaitForRealSeconds : LockedYieldInstruction
    {
        private Fixed64 _accumulator;
        private Fixed64 _waitTime;

        public WaitForRealSeconds(Fixed64 seconds)
        {
            _accumulator = Fixed64.Zero;
            _waitTime = seconds;
        }

        public override bool KeepWaiting
        {
            get
            {
                _accumulator += TrailblazerSettings.DeltaTime;
                return _accumulator < _waitTime;
            }
        }
    }
}
