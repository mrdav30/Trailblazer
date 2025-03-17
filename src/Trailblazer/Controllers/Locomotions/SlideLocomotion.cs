using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Handles the scout's sliding behavior when traversing steep surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion module determines when the scout should slide based on terrain steepness
    /// and controls how much influence the scout has over the slide direction and speed.
    /// </remarks>
    [System.Serializable]
    public class SlideLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The default maximum slope angle (in degrees) before sliding begins.
        /// </summary>
        public static readonly Fixed64 DefaultSlopeLimit = Fixed64.FromRaw(0x2D00000000L); // 45f;

        /// <summary>
        /// The default speed at which the scout slides down steep surfaces.
        /// </summary>
        public static readonly Fixed64 DefaultSlidingSpeed = (Fixed64)30;

        /// <summary>
        /// The default amount of control the scout has while sliding sideways.
        /// </summary>
        /// <remarks>
        /// A value of 0.5 allows the scout to slide sideways at half the speed of downward sliding.
        /// </remarks>
        public static readonly Fixed64 DefaultSidewaysControl = (Fixed64)1;

        /// <summary>
        /// The default amount the scout can influence sliding speed.
        /// </summary>
        /// <remarks>
        /// A value of 0.5 allows the scout to increase sliding speed up to 150% or reduce it to 50%.
        /// </remarks>
        public static readonly Fixed64 DefaultSpeedControl = (Fixed64)0.5d;

        #endregion

        #region Configuration State

        /// <summary>
        /// Determines whether sliding mechanics are enabled.
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The slope angle threshold at which sliding begins.
        /// </summary>
        public Fixed64 SlopeLimit = DefaultSlopeLimit;

        /// <summary>
        /// The speed at which the scout slides when on a steep surface.
        /// </summary>
        public Fixed64 SlidingSpeed = DefaultSlidingSpeed;

        /// <summary>
        /// Determines how much control the scout has while sliding sideways.
        /// </summary>
        /// <remarks>
        /// A higher value increases lateral movement freedom during a slide.
        /// </remarks>
        public Fixed64 SidewaysControl = DefaultSidewaysControl;

        /// <summary>
        /// Determines how much the scout can influence sliding speed.
        /// </summary>
        public Fixed64 SpeedControl = DefaultSpeedControl;

        #endregion

        #region Transient State

        /// <inheritdoc cref="ILocomotion.IsEnabled"/>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (!_isEnabled)
                    ClearState();
            }
        }

        /// <summary>
        /// Indicates whether the scout is currently sliding.
        /// </summary>
        [Transient]
        public bool IsSliding { get; set; }

        #endregion

        /// <summary>
        /// Synchronizes sliding state with another <see cref="SlideLocomotion"/> instance.
        /// </summary>
        /// <param name="locomotion">The locomotion instance to sync with.</param>
        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not SlideLocomotion other) return;
            this.SyncTransientState(other);
        }

        /// <summary>
        /// Resets sliding state, stopping any active slide.
        /// </summary>
        public void ClearState()
        {
            this.ClearState();
        }
    }
}