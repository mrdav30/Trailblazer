using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// A class that handles the sliding movement of the scout.
    /// </summary>
    [System.Serializable]
    public class SlideLocomotion : ILocomotion
    {
        #region Constants

        /// <summary>
        /// The default slope limit.
        /// </summary>
        public static readonly Fixed64 DefaultSlopeLimit = Fixed64.FromRaw(0x2D00000000L); // 45f;

        /// <summary>
        /// The default sliding speed.
        /// </summary>
        public static readonly Fixed64 DefaultSlidingSpeed = (Fixed64)30;

        /// <summary>
        /// The default sideways control.
        /// </summary>
        public static readonly Fixed64 DefaultSidewaysControl = (Fixed64)1;

        /// <summary>
        /// The default speed control.
        /// </summary>
        public static readonly Fixed64 DefaultSpeedControl = (Fixed64)0.5d;

        #endregion

        #region Configuration State

        /// <summary>
        /// Does the scout slide on too steep surfaces?
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The slope limit.
        /// </summary>
        public Fixed64 SlopeLimit = DefaultSlopeLimit;

        /// <summary>
        /// How fast does the scout slide on steep surfaces?
        /// </summary>
        public Fixed64 SlidingSpeed = DefaultSlidingSpeed;

        /// <summary>
        /// How much can the scout control the sliding direction?
        /// If the value is 0.5, the scout can slide sideways with half the speed of the downwards sliding speed.
        /// </summary> 
        public Fixed64 SidewaysControl = DefaultSidewaysControl;

        /// <summary>
        /// How much can the scout influence the sliding speed?
        /// If the value is 0.5, the scout can speed the sliding up to 150% or slow it down to 50%.
        /// </summary>
        public Fixed64 SpeedControl = DefaultSpeedControl;

        #endregion

        #region Transient State

        /// <inheritdoc cref="ILocomotion.IsEnabled"/>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        #endregion
    }
}