using FixedMathSharp;
using System;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

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
    public static readonly Fixed64 DefaultMaxSlowSpeed = (Fixed64)0.1f;//1d;

    /// <summary>
    /// Default maximum jogging speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxModerateSpeed = (Fixed64)0.25d;//2d;

    /// <summary>
    /// Default maximum sprinting speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxFastSpeed = (Fixed64)0.5d;//3d;

    /// <summary>
    /// Default maximum sideways movement speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxSidewaysSpeed = (Fixed64)0.15d;//2d;

    /// <summary>
    /// Default maximum backward movement speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxBackwardsSpeed = (Fixed64)0.15d;//2d;

    /// <summary>
    /// Default maximum acceleration when moving on the ground.
    /// </summary>
    /// <remarks>
    /// This is timescaled, with a default TimeDelta of `0.03125` this resolves to 1.
    /// </remarks>
    public static readonly Fixed64 DefaultMaxGroundAcceleration = (Fixed64)32;

    /// <summary>
    /// Default maximum acceleration when moving in the air.
    /// </summary>
    /// <remarks>
    /// This is timescaled, with a default TimeDelta of `0.03125` this resolves to `0.625`.
    /// </remarks>
    public static readonly Fixed64 DefaultMaxAirAcceleration = (Fixed64)20;

    /// <summary>
    /// Default slope speed modifier curve, determining speed adjustments based on incline.
    /// </summary>
    /// <remarks>
    /// - Full downward slope (-90°) retains full speed.
    /// - Flat ground (0°) retains full speed.
    /// - Full upward slope (90°) reduces speed to zero.
    /// </remarks>
    public static readonly FixedCurve DefaultSlopeSpeedModifier = new(FixedCurveMode.Linear,
            new FixedCurveKey(-90, 1.5),  // Full downward slope boosts speed 1.5x
            new FixedCurveKey(-45, 1.2),  // Moderate downward slope boosts speed 1.2x
            new FixedCurveKey(0, 1),      // Flat ground, normal speed
            new FixedCurveKey(45, 0.8),   // Moderate uphill slows down
            new FixedCurveKey(90, 0)      // Full uphill completely stops movement
        );

    /// <summary>
    /// Represents a fixed-point acceleration force for gravity.
    /// </summary>
    /// <remarks>
    /// The default value is approximately 9.8 m/s².
    /// </remarks>
    public static readonly Fixed64 DefaultGravityForce = Fixed64.FromRaw(0x9CCCCCCCDL); //  9.8f

    /// <summary>
    /// The maximum downward velocity a scout can reach due to gravity.
    /// </summary>
    /// <remarks>
    /// Terminal velocity is roughly 53 m/s (190 km/h or ~120 mph).
    /// </remarks>
    public static readonly Fixed64 DefaultTerminalVelocity = (Fixed64)53f;

    #endregion

    #region Configuration State

    /// <summary>
    /// Determines whether movement mechanics are enabled.
    /// </summary>
    private bool _isEnabled = true;

    /// <summary>
    /// The maximum speed when walking.
    /// </summary>
    public Fixed64 MaxSlowSpeed = DefaultMaxSlowSpeed;

    /// <summary>
    /// The maximum speed when jogging.
    /// </summary>
    public Fixed64 MaxModerateSpeed = DefaultMaxModerateSpeed;

    /// <summary>
    /// The maximum speed when sprinting.
    /// </summary>
    public Fixed64 MaxFastSpeed = DefaultMaxFastSpeed;

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

    /// <inheritdoc cref="DefaultGravityForce"/>
    public Fixed64 GravityForce = DefaultGravityForce;

    /// <inheritdoc cref="DefaultTerminalVelocity"/>
    public Fixed64 TerminalVelocity = DefaultTerminalVelocity;

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
    /// The scout’s current velocity in world space.
    /// </summary>
    [Transient]
    public Vector3d FrameVelocity { get; set; }

    #endregion

    /// <summary>
    /// Synchronizes movement state with another <see cref="MoveLocomotion"/> instance.
    /// </summary>
    /// <param name="locomotion">The locomotion instance to sync with.</param>
    public void SyncState(ITransient locomotion)
    {
        if (locomotion is not MoveLocomotion other) return;
        this.SyncTransientState(other);
    }

    /// <summary>
    /// Resets movement-related state, clearing position and velocity values.
    /// </summary>
    public void ClearState()
    {
        this.ClearTransientState();
    }
}