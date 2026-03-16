using FixedMathSharp;
using Trailblazer.Support;
using Trailblazer.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles swimming mechanics, including movement, buoyancy, water resistance, and breath control.
/// </summary>
/// <remarks>
/// This locomotion module governs how the scout moves in water, applies drag and buoyancy forces,
/// and tracks dive time for breath management.
/// </remarks>
public class SwimLocomotion : ITransientLocomotion, IRecordable
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
    public static readonly Fixed64 DefaultSwimAccelerationModifier = (Fixed64)1;

    /// <summary>
    /// The default buoyancy factor, controlling how strongly the scout floats in water.
    /// </summary>
    public static readonly Fixed64 DefaultBouyancyFactor = Fixed64.One;

    /// <summary>
    /// The default water drag factor, reducing movement speed in water.
    /// </summary>
    public static readonly Fixed64 DefaultWaterDragFactor = Fixed64.FromRaw(0x10000000L); // ~0.0625

    /// <summary>
    /// Default multiplier applied to jump force when breaching from water into air.
    /// </summary>
    public static readonly Fixed64 DefaultBreachJumpMultiplier = (Fixed64)0.75d;

    #endregion

    #region Configuration State

    /// <summary>
    /// Determines whether swimming mechanics are enabled.
    /// </summary>
    private bool _isEnabled = true;

    /// <summary>
    /// Determines whether the scout can actually swim.
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
                ((ITransient)this).ClearTransientState();
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
    /// Determines whether the scout is drowning due to prolonged underwater exposure.
    /// </summary>
    public bool IsDrowning
    {
        get
        {
            if (!_isEnabled || !CanDrown) return false;
            return UnderwaterTimer >= HoldBreathTime;
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

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", _isEnabled);
        RecordValues.Look(chronicler, ref CanSwim, "canSwim", CanSwim);
        RecordValues.Look(chronicler, ref CanBreachWater, "canBreachWater", CanBreachWater);
        RecordValues.Look(chronicler, ref CanDrown, "canDrown", CanDrown);
        RecordValues.Look(chronicler, ref MaxSwimSpeed, "maxSwimSpeed", MaxSwimSpeed);
        RecordValues.Look(chronicler, ref MaxSwimSidewaysSpeed, "maxSwimSidewaysSpeed", MaxSwimSidewaysSpeed);
        RecordValues.Look(chronicler, ref MaxWaterAcceleration, "maxWaterAcceleration", MaxWaterAcceleration);
        RecordValues.Look(chronicler, ref SwimAccelerationModifier, "swimAccelerationModifier", SwimAccelerationModifier);
        RecordValues.Look(chronicler, ref BuoyancyFactor, "buoyancyFactor", BuoyancyFactor);
        RecordValues.Look(chronicler, ref WaterDragFactor, "waterDragFactor", WaterDragFactor);
        RecordValues.Look(chronicler, ref BreachJumpMultiplier, "breachJumpMultiplier", BreachJumpMultiplier);
        RecordValues.Look(chronicler, ref HoldBreathTime, "holdBreathTime", HoldBreathTime);
        RecordValues.Look(chronicler, ref BreathRegenerateIncrement, "breathRegenerateIncrement", BreathRegenerateIncrement);

        bool isSwimming = IsSwimming;
        bool isDiving = IsDiving;
        Fixed64 underwaterTimer = UnderwaterTimer;

        RecordValues.Look(chronicler, ref isSwimming, "isSwimming", isSwimming);
        RecordValues.Look(chronicler, ref isDiving, "isDiving", isDiving);
        RecordValues.Look(chronicler, ref underwaterTimer, "underwaterTimer", underwaterTimer);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsSwimming = isSwimming;
            IsDiving = isDiving;
            UnderwaterTimer = underwaterTimer;

            if (!_isEnabled)
                ((ITransient)this).ClearTransientState();
        }
    }
}
