using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// A helper class for the fall locomotion.
    /// </summary>
    [System.Serializable]
    public class FallLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// Default limit on the Y axis the scout can fall.
        /// </summary>
        public static readonly Fixed64 DefaultMaxFallHeight = (Fixed64)30;

        public static readonly Fixed64 DefaultFallControlMultiplier = Fixed64.FromRaw(0x30000000L);

        #endregion

        #region Configuration State

        /// <summary>
        /// Can the character fall?
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// How far on the Y axis the scout can fall before death applies
        /// </summary>
        public Fixed64 MaxFallHeight = DefaultMaxFallHeight;

        public Fixed64 FallControlMultiplier = DefaultFallControlMultiplier; // 50% control when falling

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
        /// Whether the scout is falling.
        /// </summary>
        public bool IsFalling { get; set; }

        /// <summary>
        /// The y component of <see cref="IScout.WorldPosition"/> where the scout started to fall.
        /// </summary>
        public Fixed64 FallStart { get; set; }

        /// <summary>
        /// The y component of <see cref="IScout.WorldPosition"/> where the scout landed.
        /// </summary>
        public Fixed64 FallEnd { get; set; }

        /// <summary>
        /// The distance between <see cref="FallStart"/> and <see cref="FallEnd"/>
        /// </summary>
        public Fixed64 FallHeight => FallStart - FallEnd;

        #endregion

        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not FallLocomotion other) return;

            IsFalling = other.IsFalling;
            FallStart = other.FallStart;
            FallEnd = other.FallEnd;
        }

        /// <inheritdoc cref="ITransientLocomotion.ClearState"/>
        public void ClearState() {
            IsFalling = false;
            FallStart = Fixed64.Zero;
            FallEnd = Fixed64.Zero;
        }
    }
}