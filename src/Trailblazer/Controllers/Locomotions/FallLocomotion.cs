using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Handles the scout’s behavior when falling, including tracking fall distance and applying movement constraints.
    /// </summary>
    [System.Serializable]
    public class FallLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The default maximum height a scout can fall before a fatal impact occurs.
        /// </summary>
        public static readonly Fixed64 DefaultMaxFallHeight = (Fixed64)30;

        /// <summary>
        /// Default movement control multiplier when falling.
        /// Reduces movement responsiveness to simulate loss of control while airborne.
        /// </summary>
        public static readonly Fixed64 DefaultFallControlMultiplier = (Fixed64)0.1875f; // 50% control when falling

        #endregion

        #region Configuration State

        /// <summary>
        /// Determines whether falling mechanics are enabled.
        /// If disabled, the scout will not experience fall behavior.
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The maximum allowable fall height before the scout reaches a critical threshold (e.g., death or heavy impact).
        /// </summary>
        public Fixed64 MaxFallHeight = DefaultMaxFallHeight;

        /// <summary>
        /// A multiplier controlling how much movement input affects the scout while falling.
        /// Lower values reduce movement responsiveness.
        /// </summary>
        public Fixed64 FallControlMultiplier = DefaultFallControlMultiplier;

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
        /// Indicates whether the scout is currently falling.
        /// </summary>
        [Transient]
        public bool IsFalling { get; set; }

        /// <summary>
        /// The vertical position where the scout started falling.
        /// </summary>
        [Transient]
        public Fixed64 FallStart { get; set; }

        /// <summary>
        /// The vertical position where the scout landed.
        /// </summary>
        [Transient]
        public Fixed64 FallEnd { get; set; }

        /// <summary>
        /// The total distance fallen, calculated as the difference between <see cref="FallStart"/> and <see cref="FallEnd"/>.
        /// </summary>
        public Fixed64 FallHeight => FallStart - FallEnd;

        #endregion

        /// <summary>
        /// Synchronizes the falling state with another <see cref="FallLocomotion"/> instance.
        /// </summary>
        /// <param name="locomotion">The locomotion instance to sync with.</param>
        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not FallLocomotion other) return;
            this.SyncTransientState(other);
        }

        /// <summary>
        /// Resets all fall-related properties, including the start and end height.
        /// </summary>
        public void ClearState() {
            this.ClearTransientState();
        }
    }
}