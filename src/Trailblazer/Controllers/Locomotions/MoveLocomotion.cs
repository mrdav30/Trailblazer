using FixedMathSharp;
using System;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// A class that handles the sliding movement of the scout.
    /// </summary>
    [Serializable]
    public class MoveLocomotion : ILocomotion
    {
        #region Constants

        public static readonly Fixed64 VelocityEpsilon = Fixed64.FromRaw(0x418938L); //0.001f;

        public static readonly Fixed64 DefaultMaxWalkSpeed = (Fixed64)1d;

        public static readonly Fixed64 DefaultMaxJogSpeed = (Fixed64)2d;

        public static readonly Fixed64 DefaultMaxSprintSpeed = (Fixed64)3d;

        public static readonly Fixed64 DefaultMaxSidewaysSpeed = (Fixed64)2d;

        public static readonly Fixed64 DefaultMaxBackwardsSpeed = (Fixed64)2d;

        public static readonly Fixed64 DefaultMaxGroundAcceleration = (Fixed64)30;

        public static readonly Fixed64 DefaultMaxAirAcceleration = (Fixed64)20;

        public static readonly FixedCurve DefaultAnimationCurve = new FixedCurve(FixedCurveMode.Step,
                new FixedCurveKey(-90, 1), // Full downward slope
                new FixedCurveKey(0, 1), // Flat ground
                new FixedCurveKey(90, 0) // Full upward slope
            );


        /// <summary>
        /// The default surface friction.
        /// </summary>
        public static readonly Fixed64 DefaultSurfaceFriction = Fixed64.FromRaw(0x20000000L); // ~0.125

        #endregion

        #region Configuration State

        /// <summary>
        /// Does the scout slide on too steep surfaces?
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The maximum horizontal speed when moving
        /// </summary>
        public Fixed64 MaxWalkSpeed = DefaultMaxWalkSpeed;

        public Fixed64 MaxJogSpeed = DefaultMaxJogSpeed;

        public Fixed64 MaxSprintSpeed = DefaultMaxSprintSpeed;

        public Fixed64 MaxSidewaysSpeed = DefaultMaxSidewaysSpeed;

        public Fixed64 MaxBackwardsSpeed = DefaultMaxBackwardsSpeed;

        /// <summary>
        /// How fast does the character change speeds?  Higher is faster.
        /// </summary>
        public Fixed64 MaxGroundAcceleration = DefaultMaxGroundAcceleration;

        public Fixed64 MaxAirAcceleration = DefaultMaxAirAcceleration;

        public Fixed64 MoveSpeedMultiplier = Fixed64.One;

        public bool ModifySpeedOnSlope = true;

        /// <summary>
        /// Curve for multiplying speed based on slope(negative = downwards)
        /// </summary>
        public FixedCurve SlopeSpeedMultiplier = DefaultAnimationCurve;

        /// <summary>
        /// The current surface friction.
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

        #endregion

    }
}