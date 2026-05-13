using Chronicler;
using FixedMathSharp;
using System;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles water traversal mechanics, including active swimming, buoyancy, water resistance, and breath control.
/// </summary>
/// <remarks>
/// This locomotion module owns liquid-medium runtime state. Active swim input is one capability inside
/// that model alongside passive floating, sinking, breach behavior, and dive-time tracking.
/// </remarks>
public class WaterLocomotion : ILocomotion
{
    private TrailblazerWorldContext? _context;

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
    /// The default maximum sideways swimming speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxSwimSidewaysSpeed = (Fixed64)1d;

    /// <summary>
    /// The default maximum acceleration while swimming.
    /// </summary>
    public static readonly Fixed64 DefaultMaxSwimAcceleration = (Fixed64)5;

    /// <summary>
    /// The default swim acceleration multiplier.
    /// </summary>
    public static readonly Fixed64 DefaultSwimAccelerationModifier = (Fixed64)1;

    /// <summary>
    /// The default buoyancy factor, controlling how strongly the scout floats in water.
    /// </summary>
    public static readonly Fixed64 DefaultBouyancyFactor = Fixed64.One;

    /// <summary>
    /// Default multiplier applied to jump force when breaching from water into air.
    /// </summary>
    public static readonly Fixed64 DefaultBreachJumpMultiplier = (Fixed64)0.75d;

    #endregion

    #region Configuration State

    /// <summary>
    /// Determines whether water traversal mechanics are enabled.
    /// </summary>
    private bool _isEnabled = true;

    /// <summary>
    /// Determines whether the scout can actively swim while in water.
    /// </summary>
    public bool CanSwim = true;

    /// <summary>
    /// Determines whether the scout can breach the water surface when jumping.
    /// </summary>
    public bool CanBreachWater = true;

    /// <summary>
    /// Determines whether the scout can drown if underwater for too long.
    /// </summary>
    public bool CanDrown = true;

    /// <summary>
    /// The maximum forward swimming speed when active swim control is engaged.
    /// </summary>
    public Fixed64 MaxSwimSpeed = DefaultMaxSwimSpeed;

    /// <summary>
    /// The maximum sideways swimming speed when active swim control is engaged.
    /// </summary>
    public Fixed64 MaxSwimSidewaysSpeed = DefaultMaxSwimSidewaysSpeed;

    /// <summary>
    /// The maximum acceleration while actively swimming.
    /// </summary>
    public Fixed64 MaxWaterAcceleration = DefaultMaxSwimAcceleration;

    /// <summary>
    /// The acceleration multiplier applied to active swimming movement.
    /// </summary>
    public Fixed64 SwimAccelerationModifier = DefaultSwimAccelerationModifier;

    /// <summary>
    /// The buoyancy factor determining how strongly the scout floats or sinks in water.
    /// </summary>
    public Fixed64 BuoyancyFactor = DefaultBouyancyFactor;

    /// <summary>
    /// Multiplier applied to the jump velocity when the scout breaches water.
    /// Controls how forcefully the scout exits the water.
    /// </summary>
    /// <remarks>
    /// A value less than 1 results in a lower jump arc compared to standard ground jumps.
    /// </remarks>
    public Fixed64 BreachJumpMultiplier = DefaultBreachJumpMultiplier;

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
                this.ClearTransientState();
        }
    }

    /// <summary>
    /// Indicates whether the scout is currently under active swimming control.
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
    /// Stores the authored swim intent for the active traversal so entry into liquid can resolve after state refresh.
    /// </summary>
    [Transient]
    public bool RequestedSwimThisTraversal { get; set; }

    /// <summary>
    /// The effective maximum acceleration while swimming, factoring in the acceleration modifier.
    /// </summary>
    public Fixed64 MaxSwimAcceleration => MaxWaterAcceleration * SwimAccelerationModifier;

    /// <summary>
    /// Determines whether the scout is drowning due to prolonged underwater exposure.
    /// </summary>
    public bool IsDrowning
    {
        get
        {
            if (!_isEnabled || !CanDrown)
                return false;

            return UnderwaterTimer >= HoldBreathTime;
        }
    }

    #endregion

    private TrailblazerWorldContext RequireContext() =>
        _context ?? throw new InvalidOperationException("WaterLocomotion requires an explicit TrailblazerWorldContext.");

    private Fixed64 DeltaTime => RequireContext().DeltaTime;

    /// <summary>
    /// Updates the dive timer, tracking underwater duration and regenerating breath when resurfacing.
    /// </summary>
    public void UpdateDiveTime()
    {
        if (IsDiving)
        {
            UnderwaterTimer += DeltaTime;
            return;
        }

        if (UnderwaterTimer == Fixed64.Zero)
            return;

        Fixed64 time = DeltaTime * BreathRegenerateIncrement;
        UnderwaterTimer -= time;
        if (UnderwaterTimer < Fixed64.Zero)
            UnderwaterTimer = Fixed64.Zero;
    }

    internal void BindContext(TrailblazerWorldContext context)
    {
        Trailblazer.Pathing.PathRequestContextResolver.ThrowIfUnusable(context);
        _context = context;
    }

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "IsEnabled", true);
        RecordValues.Look(chronicler, ref CanSwim, "CanSwim", true);
        RecordValues.Look(chronicler, ref CanBreachWater, "CanBreachWater", true);
        RecordValues.Look(chronicler, ref CanDrown, "CanDrown", true);
        RecordValues.Look(chronicler, ref MaxSwimSpeed, "MaxSwimSpeed", DefaultMaxSwimSpeed);
        RecordValues.Look(chronicler, ref MaxSwimSidewaysSpeed, "MaxSwimSidewaysSpeed", DefaultMaxSwimSidewaysSpeed);
        RecordValues.Look(chronicler, ref MaxWaterAcceleration, "MaxWaterAcceleration", DefaultMaxSwimAcceleration);
        RecordValues.Look(chronicler, ref SwimAccelerationModifier, "SwimAccelerationModifier", DefaultSwimAccelerationModifier);
        RecordValues.Look(chronicler, ref BuoyancyFactor, "BuoyancyFactor", DefaultBouyancyFactor);
        RecordValues.Look(chronicler, ref BreachJumpMultiplier, "BreachJumpMultiplier", DefaultBreachJumpMultiplier);
        RecordValues.Look(chronicler, ref HoldBreathTime, "HoldBreathTime", DefaultHoldBreathTime);
        RecordValues.Look(chronicler, ref BreathRegenerateIncrement, "BreathRegenerateIncrement", DefaultBreathRegenerateIncrement);

        bool isSwimming = IsSwimming;
        bool isDiving = IsDiving;
        Fixed64 underwaterTimer = UnderwaterTimer;

        RecordValues.Look(chronicler, ref isSwimming, "IsSwimming", false);
        RecordValues.Look(chronicler, ref isDiving, "IsDiving", false);
        RecordValues.Look(chronicler, ref underwaterTimer, "UnderwaterTimer", Fixed64.Zero);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsSwimming = isSwimming;
            IsDiving = isDiving;
            UnderwaterTimer = underwaterTimer;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }
}
