using FixedMathSharp;

namespace Trailblazer
{
    public static class TrailblazerManager
    {
        public const int FrameRate = 32;

        public static readonly Fixed64 FixedFrameRate = (Fixed64)32;

        /// <summary>
        /// Unscaled delta-time
        /// </summary>
        public static readonly Fixed64 DeltaTime = Fixed64.One / (Fixed64)FrameRate;

        public static int FrameCount { get; private set; }

        /// <summary>
        /// Represent a fixed-point representation of Gravity as an acceleration force
        /// </summary>
        public static Fixed64 GravityForce { get; private set; } = Fixed64.FromRaw(0x9CCCCCCCDL); //  9.8f

        public static readonly Fixed64 MaxFallSpeed = (Fixed64)9.8f;

        public static void Simulate()
        {
            FrameCount++;
        }

        public static void Reset()
        {
            FrameCount = 0;
        }
    }
}
