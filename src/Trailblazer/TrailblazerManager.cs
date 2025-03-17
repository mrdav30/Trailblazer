using FixedMathSharp;

namespace Trailblazer
{
    /// <summary>
    /// Provides global simulation parameters and timing management for the Trailblazer system.
    /// </summary>
    /// <remarks>
    /// This static class handles fixed-time updates, gravity settings, and frame progression.
    /// It ensures consistency across physics calculations and locomotion systems.
    /// </remarks>
    public static class TrailblazerManager
    {
        /// <summary>
        /// The fixed simulation frame rate.
        /// </summary>
        /// <remarks>
        /// This determines how frequently physics and movement updates occur.
        /// </remarks>
        public static int FrameRate { get; private set; } = 32;

        /// <summary>
        /// The fixed time step for each simulation frame.
        /// </summary>
        /// <remarks>
        /// This value is derived from <see cref="FrameRate"/> to ensure a consistent time step across updates.
        /// </remarks>
        public static Fixed64 DeltaTime { get; private set; } = Fixed64.One / (Fixed64)FrameRate;

        /// <summary>
        /// The number of frames elapsed since the simulation started.
        /// </summary>
        public static int FrameCount { get; private set; }

        /// <summary>
        /// Updates the simulation frame rate and recalculates the delta time.
        /// </summary>
        /// <param name="frameRate">The new frame rate value.</param>
        public static void SetFrameRate(int frameRate)
        {
            FrameRate = frameRate;
            DeltaTime = Fixed64.One / (Fixed64)FrameRate;
        }

        /// <summary>
        /// Advances the simulation by incrementing the frame count.
        /// </summary>
        public static void Simulate()
        {
            FrameCount++;
        }

        /// <summary>
        /// Resets the simulation frame count to zero.
        /// </summary>
        public static void Reset()
        {
            FrameCount = 0;
        }
    }
}
