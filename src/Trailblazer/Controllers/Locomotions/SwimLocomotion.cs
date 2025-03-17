using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Handles swimming mechanics, including movement, buoyancy, water resistance, and breath control.
    /// </summary>
    /// <remarks>
    /// This locomotion module governs how the scout moves in water, applies drag and buoyancy forces,
    /// and tracks dive time for breath management.
    /// </remarks>
    [System.Serializable]
    public class SwimLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The default duration the scout can hold its breath underwater before drowning begins.
        /// </summary>
        public static readonly Fixed64 DefaultHoldBreathTime = (Fixed64)60;

        /// <summary>
        /// The default amount of breath regenerated per tick when resurfacing.
        /// </summary>
        public static readonly Fixed64 DefaultBreathRegenerateIncrement = (Fixed64)10;

        /// <summary>
        /// The default maximum swimming speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSwimSpeed = (Fixed64)1.5d;

        /// <summary>
        /// The default maximum acceleration while swimming.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSwimSidewaysSpeed = (Fixed64)1d;

        /// <summary>
        /// The default maximum acceleration while swimming.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSwimAcceleration = (Fixed64)5;

        /// <summary>
        /// The default swim acceleration multiplier.
        /// </summary>
        public static readonly Fixed64 DefaultSwimAccelerationModifier = (Fixed64)10;

        /// <summary>
        /// The default buoyancy factor, controlling how strongly the scout floats in water.
        /// </summary>
        public static readonly Fixed64 DefaultBouyancyFactor = Fixed64.One;

        /// <summary>
        /// The default water drag factor, reducing movement speed in water.
        /// </summary>
        public static readonly Fixed64 DefaultWaterDragFactor = Fixed64.FromRaw(0x10000000L); // ~0.0625

        /// <summary>
        /// The delay before the scout enters a drowning state after exceeding breath capacity.
        /// </summary>
        public static readonly Fixed64 DrowningStatusDelay = Fixed64.FromRaw(0x20000000L); // 0.125

        #endregion

        #region Configuration State

        /// <summary>
        /// Determines whether swimming mechanics are enabled.
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// Determines whether the scout can breach the water surface when jumping.
        /// </summary>
        public bool CanBreachWater;

        /// <summary>
        /// Determines whether the scout can drown if underwater for too long.
        /// </summary>
        public bool CanDrown = true;

        /// <summary>
        /// The maximum swimming speed.
        /// </summary>
        public Fixed64 MaxSwimSpeed = DefaultMaxSwimSpeed;

        /// <summary>
        /// The maximum sideways swimming speed.
        /// </summary>
        public Fixed64 MaxSwimSidewaysSpeed = DefaultMaxSwimSidewaysSpeed;

        /// <summary>
        /// The maximum acceleration while swimming.
        /// </summary>
        public Fixed64 MaxWaterAcceleration = DefaultMaxSwimAcceleration;

        /// <summary>
        /// The acceleration multiplier applied to swimming movement.
        /// </summary>
        public Fixed64 SwimAccelerationModifier = DefaultSwimAccelerationModifier;

        /// <summary>
        /// The buoyancy factor determining how strongly the scout floats in water.
        /// </summary>
        public Fixed64 BuoyancyFactor = DefaultBouyancyFactor;

        /// <summary>
        /// The water drag factor, slowing movement in water.
        /// </summary>
        public Fixed64 WaterDragFactor = DefaultWaterDragFactor; // ~0.0625

        /// <summary>
        /// The maximum time the scout can hold its breath underwater before drowning.
        /// </summary>
        public Fixed64 HoldBreathTime = DefaultHoldBreathTime;

        /// <summary>
        /// The amount of breath the scout regenerates per tick when resurfacing.
        /// </summary>
        public Fixed64 BreathRegenerateIncrement = DefaultBreathRegenerateIncrement;

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
        /// Indicates whether the scout is currently swimming.
        /// </summary>
        [Transient]
        public bool IsSwimming { get; set; }

        /// <summary>
        /// Indicates whether the scout is currently diving (fully submerged).
        /// </summary>
        [Transient]
        public bool IsDiving { get; set; }

        /// <summary>
        /// The amount of time the scout has been underwater.
        /// </summary>
        [Transient]
        public Fixed64 UnderwaterTimer { get; set; }

        /// <summary>
        /// The effective maximum acceleration while swimming, factoring in the acceleration modifier.
        /// </summary>
        public Fixed64 MaxSwimAcceleration => MaxWaterAcceleration * SwimAccelerationModifier;

        /// <summary>
        /// The effective buoyant force applied while swimming.
        /// </summary>
        public Fixed64 BuoyantForce => MaxSwimAcceleration * BuoyancyFactor;

        /// <summary>
        /// Determines whether the scout is drowning due to prolonged underwater exposure.
        /// </summary>
        public bool IsDrowning
        {
            get
            {
                if (!_isEnabled || !CanDrown) return false;
                return UnderwaterTimer >= HoldBreathTime + DrowningStatusDelay; // Small delay;
            }
        }

        #endregion

        /// <summary>
        /// Updates the dive timer, tracking underwater duration and regenerating breath when resurfacing.
        /// </summary>
        public void UpdateDiveTime()
        {
            if (IsDiving)
            {
                UnderwaterTimer += TrailblazerManager.DeltaTime;
                return;
            }

            if (UnderwaterTimer == Fixed64.Zero)
                return;

            Fixed64 time = TrailblazerManager.DeltaTime * BreathRegenerateIncrement;
            UnderwaterTimer -= time;
            if (UnderwaterTimer < Fixed64.Zero)
                UnderwaterTimer = Fixed64.Zero;
        }

        /// <summary>
        /// Synchronizes swimming state with another <see cref="SwimLocomotion"/> instance.
        /// </summary>
        /// <param name="locomotion">The locomotion instance to sync with.</param>
        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not SwimLocomotion other) return;
            this.SyncTransientState(other);
        }

        /// <summary>
        /// Resets swimming-related state, clearing dive status and breath timers.
        /// </summary>
        public void ClearState()
        {
            this.ClearTransientState();
        }
    }
}