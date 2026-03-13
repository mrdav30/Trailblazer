using FixedMathSharp;
using System;
using Trailblazer.Support;
using MemoryPack;


#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#endif
#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Shim;
#endif

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles swimming mechanics, including movement, buoyancy, water resistance, and breath control.
/// </summary>
/// <remarks>
/// This locomotion module governs how the scout moves in water, applies drag and buoyancy forces,
/// and tracks dive time for breath management.
/// </remarks>
[Serializable]
[MemoryPackable]
public partial class SwimLocomotion : ITransientLocomotion
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
    [JsonInclude]
    [MemoryPackInclude]
    private bool _isEnabled = true;

    /// <summary>
    /// Determines whether the scout can actually swim.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public bool CanSwim = true;

    /// <summary>
    /// Determines whether the scout can breach the water surface when jumping.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public bool CanBreachWater = true;

    /// <summary>
    /// Determines whether the scout can drown if underwater for too long.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public bool CanDrown = true;

    /// <summary>
    /// The maximum swimming speed.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 MaxSwimSpeed = DefaultMaxSwimSpeed;

    /// <summary>
    /// The maximum sideways swimming speed.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 MaxSwimSidewaysSpeed = DefaultMaxSwimSidewaysSpeed;

    /// <summary>
    /// The maximum acceleration while swimming.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 MaxWaterAcceleration = DefaultMaxSwimAcceleration;

    /// <summary>
    /// The acceleration multiplier applied to swimming movement.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 SwimAccelerationModifier = DefaultSwimAccelerationModifier;

    /// <summary>
    /// The buoyancy factor determining how strongly the scout floats in water.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 BuoyancyFactor = DefaultBouyancyFactor;

    /// <summary>
    /// The water drag factor, slowing movement in water.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 WaterDragFactor = DefaultWaterDragFactor; // ~0.0625

    /// <summary>
    /// Multiplier applied to the jump velocity when the scout breaches water.
    /// Controls how forcefully the scout exits the water.
    /// </summary>
    /// <remarks>
    /// A value less than 1 results in a lower jump arc compared to standard ground jumps.
    /// </remarks>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 BreachJumpMultiplier = DefaultBreachJumpMultiplier;

    /// <summary>
    /// The maximum time the scout can hold its breath underwater before drowning.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 HoldBreathTime = DefaultHoldBreathTime;

    /// <summary>
    /// The amount of breath the scout regenerates per tick when resurfacing.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 BreathRegenerateIncrement = DefaultBreathRegenerateIncrement;

    #endregion

    #region Transient State

    /// <inheritdoc cref="ILocomotion.IsEnabled"/>
    [JsonIgnore]
    [MemoryPackIgnore]
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
    [JsonIgnore]
    [MemoryPackIgnore]
    public bool IsSwimming { get; set; }

    /// <summary>
    /// Indicates whether the scout is currently diving (fully submerged).
    /// </summary>
    [Transient]
    [JsonIgnore]
    [MemoryPackIgnore]
    public bool IsDiving { get; set; }

    /// <summary>
    /// The amount of time the scout has been underwater.
    /// </summary>
    [Transient]
    [JsonIgnore]
    [MemoryPackIgnore]
    public Fixed64 UnderwaterTimer { get; set; }

    /// <summary>
    /// The effective maximum acceleration while swimming, factoring in the acceleration modifier.
    /// </summary>
    [JsonIgnore]
    [MemoryPackIgnore]
    public Fixed64 MaxSwimAcceleration => MaxWaterAcceleration * SwimAccelerationModifier;

    /// <summary>
    /// Determines whether the scout is drowning due to prolonged underwater exposure.
    /// </summary>
    [JsonIgnore]
    [MemoryPackIgnore]
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
}
