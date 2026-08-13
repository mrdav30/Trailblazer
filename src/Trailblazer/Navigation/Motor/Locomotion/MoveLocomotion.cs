//=======================================================================
// MoveLocomotion.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles the scout's movement, including speed limits, acceleration, and velocity calculations.
/// </summary>
/// <remarks>
/// This locomotion module governs how the scout accelerates, decelerates, and interacts with terrain slopes.
/// It tracks position changes and velocity updates for consistent movement behavior.
/// </remarks>
public class MoveLocomotion : ILocomotion
{
    #region Constants

    /// <summary>
    /// The minimum velocity threshold below which movement is considered negligible.
    /// </summary>
    public static readonly Fixed64 VelocityEpsilon = Fixed64.FromRaw(0x418938L); //0.001f;

    /// <summary>
    /// Default maximum walking speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxSlowSpeed = Fixed64.FromFraction(1, 10);//1d;

    /// <summary>
    /// Default maximum jogging speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxModerateSpeed = Fixed64.Quarter;//2d;

    /// <summary>
    /// Default maximum sprinting speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxFastSpeed = Fixed64.Half;//3d;

    /// <summary>
    /// Default maximum sideways movement speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxSidewaysSpeed = Fixed64.FromFraction(3, 20);//2d;

    /// <summary>
    /// Default maximum backward movement speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxBackwardsSpeed = Fixed64.FromFraction(3, 20);//2d;

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
            new FixedCurveKey((Fixed64)(-90), Fixed64.FromFraction(3, 2)),  // Full downward slope boosts speed 1.5x
            new FixedCurveKey((Fixed64)(-45), Fixed64.FromFraction(6, 5)),  // Moderate downward slope boosts speed 1.2x
            new FixedCurveKey(Fixed64.Zero, Fixed64.One),                  // Flat ground, normal speed
            new FixedCurveKey((Fixed64)45, Fixed64.FromFraction(4, 5)),    // Moderate uphill slows down
            new FixedCurveKey((Fixed64)90, Fixed64.Zero)                   // Full uphill completely stops movement
        );

    /// <summary>
    /// The default maximum slope angle before a surface is considered too steep for normal control.
    /// </summary>
    public static readonly Fixed64 DefaultSlopeLimit = Fixed64.FromRaw(0x2D00000000L); // 45f;

    /// <summary>
    /// The default passive drag factor applied while moving through water.
    /// </summary>
    public static readonly Fixed64 DefaultWaterDragFactor = Fixed64.FromRaw(0x10000000L); // ~0.0625

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

    /// <summary>
    /// The slope angle threshold at which a surface becomes too steep for normal movement control.
    /// </summary>
    public Fixed64 SlopeLimit = DefaultSlopeLimit;

    /// <summary>
    /// Passive drag applied whenever the motor is in water, even if the water locomotion is absent or inactive.
    /// </summary>
    public Fixed64 WaterDragFactor = DefaultWaterDragFactor;

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
                this.ClearTransientState();
        }
    }

    /// <summary>
    /// The scout’s current velocity in world space.
    /// </summary>
    [Transient]
    public Vector3d FrameVelocity { get; set; }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "IsEnabled", true);
        RecordValues.Look(chronicler, ref MaxSlowSpeed, "MaxSlowSpeed", DefaultMaxSlowSpeed);
        RecordValues.Look(chronicler, ref MaxModerateSpeed, "MaxModerateSpeed", DefaultMaxModerateSpeed);
        RecordValues.Look(chronicler, ref MaxFastSpeed, "MaxFastSpeed", DefaultMaxFastSpeed);
        RecordValues.Look(chronicler, ref MaxSidewaysSpeed, "MaxSidewaysSpeed", DefaultMaxSidewaysSpeed);
        RecordValues.Look(chronicler, ref MaxBackwardsSpeed, "MaxBackwardsSpeed", DefaultMaxBackwardsSpeed);
        RecordValues.Look(chronicler, ref MaxGroundAcceleration, "MaxGroundAcceleration", DefaultMaxGroundAcceleration);
        RecordValues.Look(chronicler, ref MaxAirAcceleration, "MaxAirAcceleration", DefaultMaxAirAcceleration);
        RecordValues.Look(chronicler, ref MoveSpeedMultiplier, "MoveSpeedMultiplier", Fixed64.One);
        RecordValues.Look(chronicler, ref ModifySpeedOnSlope, "ModifySpeedOnSlope", true);
        RecordValues.Look(chronicler, ref SlopeSpeedMultiplier, "SlopeSpeedMultiplier", DefaultSlopeSpeedModifier);
        RecordValues.Look(chronicler, ref SlopeLimit, "SlopeLimit", DefaultSlopeLimit);
        RecordValues.Look(chronicler, ref WaterDragFactor, "WaterDragFactor", DefaultWaterDragFactor);

        Vector3d frameVelocity = FrameVelocity;
        RecordValues.Look(chronicler, ref frameVelocity, "FrameVelocity", Vector3d.Zero);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            FrameVelocity = frameVelocity;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }

    #endregion
}
