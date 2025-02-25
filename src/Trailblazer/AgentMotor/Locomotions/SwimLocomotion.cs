using FixedMathSharp;

namespace Trailblazer.AgentMotor.Locomotions
{
    [System.Serializable]
    public class SwimLocomotion : ILocomotion
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

        public static readonly Fixed64 DefaultMaxSwimSpeed = (Fixed64)0.25d;

        public static readonly Fixed64 DefaultMaxSwimSidewaysSpeed = (Fixed64)0.15d;

        public static readonly Fixed64 DefaultMaxSwimAcceleration = (Fixed64)5;

        private static readonly Fixed64 DefaultSwimAccelerationModifier = (Fixed64)10;

        private static readonly Fixed64 DefaultBouyancyFactor = Fixed64.One;

        #endregion

        /// <summary>
        /// Can the character swim?
        /// </summary>
        public bool IsEnabled = true;

        public bool IsSwimming;

        // TODO: create logic to determine if we're diving (aka below the water line)
        public bool IsDiving;

        /// <summary>
        /// Can the character jump while swimming?
        /// </summary>
        public bool CanWaterBreach;

        public Fixed64 MaxSwimSpeed = DefaultMaxSwimSpeed;

        public Fixed64 MaxSwimSidewaysSpeed = DefaultMaxSwimSidewaysSpeed;

        /// <summary>
        /// Except this... Higher is Slower some how...
        /// </summary>
        public Fixed64 MaxWaterAcceleration = DefaultMaxSwimAcceleration;

        public Fixed64 SwimAccelerationModifier = DefaultSwimAccelerationModifier;

        public Fixed64 BuoyancyFactor = DefaultBouyancyFactor;

        public Fixed64 WaterDragFactor = Fixed64.FromRaw(0x10000000L); // ~0.0625

        public Fixed64 MaxVelocity => MaxWaterAcceleration * SwimAccelerationModifier;

        public Fixed64 BuoyantForce => MaxVelocity * BuoyancyFactor;

        /// <summary>
        /// How long the character can hold its breath
        /// </summary>
        public Fixed64 HoldBreathTime = DefaultHoldBreathTime;

        /// <summary>
        /// How much breath should be regenerated
        /// </summary>
        public Fixed64 BreathRegenerateIncrement = DefaultBreathRegenerateIncrement;

        /// <summary>
        /// How long the driver has been underwater
        /// </summary>
        public Fixed64 UnderwaterTimer { get; internal set; }

        public bool IsDrowning { get; internal set; }

        public bool IsDrowningStatus => HoldBreathTime < UnderwaterTimer;

        public Fixed64 MaxSwimSpeedInDirection(Vector3d desiredMovementDirection)
        {
            Fixed64 zAxisEllipseMultiplier = MaxSwimSpeed / MaxSwimSidewaysSpeed;
            Vector3d temp = new Vector3d(
                desiredMovementDirection.x,
                Fixed64.Zero,
                desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            Fixed64 length = new Vector3d(temp.x, Fixed64.Zero, temp.z * zAxisEllipseMultiplier).Magnitude
                * MaxSwimSpeed;
            return length;
        }

        public Vector3d ApplyWaterDrag(Vector3d velocity)
        {
            return velocity * (Fixed64.One - WaterDragFactor);
        }
    }
}