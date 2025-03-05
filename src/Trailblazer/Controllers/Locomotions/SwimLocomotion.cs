using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// A class that handles the swimming movement of the scout.
    /// </summary>
    [System.Serializable]
    public class SwimLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// How long the character can hold its breath
        /// </summary>
        public static readonly Fixed64 DefaultHoldBreathTime = (Fixed64)60;

        /// <summary>
        /// How much breath should be regenerated
        /// </summary>
        public static readonly Fixed64 DefaultBreathRegenerateIncrement = (Fixed64)10;

        /// <summary>
        /// The default maximum swim speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSwimSpeed = (Fixed64)0.25d;

        /// <summary>
        /// The default maximum swim sideways speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSwimSidewaysSpeed = (Fixed64)0.15d;

        /// <summary>
        /// The default maximum swim acceleration.
        /// </summary>  
        public static readonly Fixed64 DefaultMaxSwimAcceleration = (Fixed64)5;

        /// <summary>
        /// The default swim acceleration modifier.
        /// </summary>
        public static readonly Fixed64 DefaultSwimAccelerationModifier = (Fixed64)10;

        /// <summary>
        /// The default buoyancy factor.
        /// </summary>
        public static readonly Fixed64 DefaultBouyancyFactor = Fixed64.One;

        /// <summary>
        /// The default water drag factor.
        /// </summary>
        public static readonly Fixed64 DefaultWaterDragFactor = Fixed64.FromRaw(0x10000000L); // ~0.0625

        #endregion

        #region Configuration State

        /// <summary>
        /// Can the character swim?
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// Can the character jump while swimming?
        /// </summary>
        public bool CanBreachWater;

        /// <summary>
        /// Can the scout drown?
        /// </summary>
        public bool CanDrown = true;

        /// <summary>
        /// The maximum swim speed.
        /// </summary>
        public Fixed64 MaxSwimSpeed = DefaultMaxSwimSpeed;

        /// <summary>
        /// The maximum swim sideways speed.
        /// </summary>
        public Fixed64 MaxSwimSidewaysSpeed = DefaultMaxSwimSidewaysSpeed;

        /// <summary>
        /// Except this... Higher is Slower some how...
        /// </summary>
        public Fixed64 MaxWaterAcceleration = DefaultMaxSwimAcceleration;

        /// <summary>
        /// The swim acceleration modifier.
        /// </summary>
        public Fixed64 SwimAccelerationModifier = DefaultSwimAccelerationModifier;

        /// <summary>
        /// The buoyancy factor.
        /// </summary>
        public Fixed64 BuoyancyFactor = DefaultBouyancyFactor;

        /// <summary>
        /// The water drag factor.
        /// </summary>
        public Fixed64 WaterDragFactor = DefaultWaterDragFactor; // ~0.0625

        /// <summary>
        /// How long the character can hold its breath
        /// </summary>
        public Fixed64 HoldBreathTime = DefaultHoldBreathTime;

        /// <summary>
        /// How much breath should be regenerated
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
        /// Is the scout diving?
        /// </summary>
        public bool IsDiving { get; set; }  // TODO: create logic to determine if we're diving (aka below the water line)

        /// <summary>
        /// How long the scout has been underwater
        /// </summary>
        public Fixed64 UnderwaterTimer { get; set; }

        /// <summary>
        /// The maximum velocity.
        /// </summary>
        public Fixed64 MaxVelocity => MaxWaterAcceleration * SwimAccelerationModifier;

        /// <summary>
        /// The buoyant force.
        /// </summary>
        public Fixed64 BuoyantForce => MaxVelocity * BuoyancyFactor;

        /// <summary>
        /// Is the scout drowning?
        /// </summary>
        public bool IsDrowning
        {
            get
            {
                if (!_isEnabled || !CanDrown) return false;
                return UnderwaterTimer >= HoldBreathTime + Fixed64.FromRaw(0x20000000L); // Small delay;
            }
        }

        #endregion

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

        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not SwimLocomotion other) return;

            UnderwaterTimer = other.UnderwaterTimer;
        }

        /// <inheritdoc cref="ITransientLocomotion.ClearState"/>
        public void ClearState()
        {
            IsDiving = false;
            UnderwaterTimer = Fixed64.Zero;
        }
    }
}