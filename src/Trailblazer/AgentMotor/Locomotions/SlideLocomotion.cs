using FixedMathSharp;

namespace Trailblazer.AgentMotor.Locomotions
{
    [System.Serializable]
    public class SlideLocomotion : ILocomotion
    {
        #region Constants

        public static readonly Fixed64 DefaultSlopeLimit = Fixed64.FromRaw(0x2D00000000L); // 45f;

        public static readonly Fixed64 DefaultSlidingSpeed = (Fixed64)30;

        public static readonly Fixed64 DefaultSidewaysControl = (Fixed64)1;

        public static readonly Fixed64 DefaultSpeedControl = (Fixed64)0.5d;

        public static readonly Fixed64 DefaultSurfaceFriction = Fixed64.FromRaw(0x20000000L); // ~0.125

        #endregion

        /// <summary>
        /// Does the driver slide on too steep surfaces?
        /// </summary>
        public bool IsEnabled = true;

        public Fixed64 SlopeLimit = DefaultSlopeLimit;

        /// <summary>
        /// How fast does the driver slide on steep surfaces?
        /// </summary>
        public Fixed64 SlidingSpeed = DefaultSlidingSpeed;

        public Fixed64 CurrentSurfaceFriction = DefaultSurfaceFriction;

        /// <summary>
        /// How much can the driver control the sliding direction?
        /// If the value is 0.5, the driver can slide sideways with half the speed of the downwards sliding speed.
        /// </summary> 
        public Fixed64 SidewaysControl = DefaultSidewaysControl;

        /// <summary>
        /// How much can the driver influence the sliding speed?
        /// If the value is 0.5, the driver can speed the sliding up to 150% or slow it down to 50%.
        /// </summary>
        public Fixed64 SpeedControl = DefaultSpeedControl;

        public Fixed64 AdjustedSlidingSpeed => SlidingSpeed * (Fixed64.One - CurrentSurfaceFriction);
    }
}