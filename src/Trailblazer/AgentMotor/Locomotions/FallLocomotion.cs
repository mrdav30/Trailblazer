using FixedMathSharp;

namespace Trailblazer.AgentMotor.Locomotions
{
    [System.Serializable]
    public class FallLocomotion : ILocomotion
    {
        #region Constants

        /// <summary>
        /// Default limit on the Y axis the driver can fall.
        /// </summary>
        public static readonly Fixed64 DefaultMaxFallHeight = (Fixed64)30;

        #endregion

        public bool IsEnabled = true;

        public bool IsFalling { get; internal set; }

        /// <summary>
        /// The y component of <see cref="IDrive.WorldPosition"/> where the driver started to fall.
        /// </summary>
        public Fixed64 FallStart { get; internal set; }

        /// <summary>
        /// The y component of <see cref="IDrive.WorldPosition"/> where the driver landed.
        /// </summary>
        public Fixed64 FallEnd { get; internal set; }

        /// <summary>
        /// The distance between <see cref="FallStart"/> and <see cref="FallEnd"/>
        /// </summary>
        public Fixed64 FallHeight => FallStart - FallEnd;

        /// <summary>
        /// How far on the Y axis the driver can fall before death applies
        /// </summary>
        public Fixed64 MaxFallHeight = DefaultMaxFallHeight;
    }
}