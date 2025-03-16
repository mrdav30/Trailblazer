using FixedMathSharp;

namespace Trailblazer
{
    public static class TrailblazerManager
    {
        public const int FrameRate = 32;

        /// <summary>
        /// Frames per second
        /// </summary>
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

        // Terminal velocity is roughly 53 m/s (190 km/h or ~120 mph)
        public static readonly Fixed64 TerminalFallVelocity = (Fixed64)53f;

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
