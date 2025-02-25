using FixedMathSharp;

namespace Trailblazer
{
    public static class TrailblazerSettings
    {
        public const int FrameRate = 32;

        public static readonly Fixed64 FixedFrameRate = (Fixed64)32;

        /// <summary>
        /// Unscaled delta-time
        /// </summary>
        public static readonly Fixed64 DeltaTime = Fixed64.One / (Fixed64)FrameRate;

        public static int FrameCount;

        public static Fixed64 FixedGravity { get; private set; } = Fixed64.FromRaw(0x9CCCCCCCDL); //  9.8f

        public static void IncrementFrameCount() => FrameCount++;

        public static void ResetFrameCount() => FrameCount = 0;
    }
}
