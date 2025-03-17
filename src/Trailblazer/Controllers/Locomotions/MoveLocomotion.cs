using FixedMathSharp;
using System;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Handles the scout's movement, including speed limits, acceleration, and velocity calculations.
    /// </summary>
    /// <remarks>
    /// This locomotion module governs how the scout accelerates, decelerates, and interacts with terrain slopes.
    /// It tracks position changes and velocity updates for consistent movement behavior.
    /// </remarks>
    [Serializable]
    public class MoveLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The minimum velocity threshold below which movement is considered negligible.
        /// </summary>
        public static readonly Fixed64 VelocityEpsilon = Fixed64.FromRaw(0x418938L); //0.001f;

        /// <summary>
        /// Default maximum walking speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxWalkSpeed = (Fixed64)1d;

        /// <summary>
        /// Default maximum jogging speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxJogSpeed = (Fixed64)2d;

        /// <summary>
        /// Default maximum sprinting speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSprintSpeed = (Fixed64)3d;

        /// <summary>
        /// Default maximum sideways movement speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxSidewaysSpeed = (Fixed64)2d;

        /// <summary>
        /// Default maximum backward movement speed.
        /// </summary>
        public static readonly Fixed64 DefaultMaxBackwardsSpeed = (Fixed64)2d;

        /// <summary>
        /// Default maximum acceleration when moving on the ground.
        /// </summary>
        public static readonly Fixed64 DefaultMaxGroundAcceleration = (Fixed64)30;

        /// <summary>
        /// Default maximum acceleration when moving in the air.
        /// </summary>
        public static readonly Fixed64 DefaultMaxAirAcceleration = (Fixed64)20;

        /// <summary>
        /// Default slope speed modifier curve, determining speed adjustments based on incline.
        /// </summary>
        /// <remarks>
        /// - Full downward slope (-90°) retains full speed.
        /// - Flat ground (0°) retains full speed.
        /// - Full upward slope (90°) reduces speed to zero.
        /// </remarks>
        public static readonly FixedCurve DefaultSlopeSpeedModifier = new(FixedCurveMode.Step,
                new FixedCurveKey(-90, 1), // Full downward slope
                new FixedCurveKey(0, 1), // Flat ground
                new FixedCurveKey(90, 0) // Full upward slope
            );

        /// <summary>
        /// Default surface friction applied to movement.
        /// </summary>
        public static readonly Fixed64 DefaultSurfaceFriction = Fixed64.FromRaw(0x20000000L); // ~0.125 * One

        #endregion

        #region Configuration State

        /// <summary>
        /// Determines whether movement mechanics are enabled.
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The maximum speed when walking.
        /// </summary>
        public Fixed64 MaxWalkSpeed = DefaultMaxWalkSpeed;

        /// <summary>
        /// The maximum speed when jogging.
        /// </summary>
        public Fixed64 MaxJogSpeed = DefaultMaxJogSpeed;

        /// <summary>
        /// The maximum speed when sprinting.
        /// </summary>
        public Fixed64 MaxSprintSpeed = DefaultMaxSprintSpeed;

        /// <summary>
        /// The maximum speed when moving sideways.
        /// </summary>
        public Fixed64 MaxSidewaysSpeed = DefaultMaxSidewaysSpeed;

        /// <summary>
        /// The maximum speed when moving backward.
        /// </summary>
        public Fixed64 MaxBackwardsSpeed = DefaultMaxBackwardsSpeed;

        /// <summary>
        /// The maximum acceleration when moving on the ground.
        /// Higher values result in quicker acceleration.
        /// </summary>
        public Fixed64 MaxGroundAcceleration = DefaultMaxGroundAcceleration;

        /// <summary>
        /// The maximum acceleration when moving in the air.
        /// </summary>
        public Fixed64 MaxAirAcceleration = DefaultMaxAirAcceleration;

        /// <summary>
        /// A global multiplier applied to movement speed.
        /// </summary>
        public Fixed64 MoveSpeedMultiplier = Fixed64.One;

        /// <summary>
        /// Determines whether movement speed is adjusted based on the slope of the terrain.
        /// </summary>
        public bool ModifySpeedOnSlope = true;

        /// <summary>
        /// A curve controlling how speed is affected by terrain slope.
        /// </summary>
        public FixedCurve SlopeSpeedMultiplier = DefaultSlopeSpeedModifier;

        /// <summary>
        /// The current surface friction applied to movement.
        /// </summary>
        public Fixed64 SurfaceFriction = DefaultSurfaceFriction;

        #endregion

        #region Transient State

        /// <inheritdoc cref="ILocomotion.IsEnabled"/>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>
        /// The current world position of the scout.
        /// </summary>
        public Vector3d CurrentPosition { get; set; }

        /// <summary>
        /// The world position from the previous frame, used for velocity calculations.
        /// </summary>
        public Vector3d LastPosition { get; set; }

        /// <summary>
        /// The scout’s current velocity in world space.
        /// </summary>
        public Vector3d CurrentVelocity { get; set; }

        /// <summary>
        /// The scout’s velocity from the previous frame.
        /// </summary>
        public Vector3d LastVelocity { get; set; }

        #endregion

        /// <summary>
        /// Synchronizes movement state with another <see cref="MoveLocomotion"/> instance.
        /// </summary>
        /// <param name="locomotion">The locomotion instance to sync with.</param>
        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not MoveLocomotion other) return;

            CurrentPosition = other.CurrentPosition;
            LastPosition = other.LastPosition;
            CurrentVelocity = other.CurrentVelocity;
            LastVelocity = other.LastVelocity;
        }

        /// <summary>
        /// Resets movement-related state, clearing position and velocity values.
        /// </summary>
        public void ClearState()
        {
            CurrentPosition = Vector3d.Zero;
            LastPosition = Vector3d.Zero;
            CurrentVelocity = Vector3d.Zero;
            LastVelocity = Vector3d.Zero;
        }
    }
}