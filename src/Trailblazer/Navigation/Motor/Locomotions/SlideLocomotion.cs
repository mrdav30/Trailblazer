using FixedMathSharp;
using MemoryPack;
using System;
using Trailblazer.Support;
using Trailblazer.Serialization;

#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
#endif
#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Shim;
#endif

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles the scout's sliding behavior when traversing steep surfaces.
/// </summary>
/// <remarks>
/// This locomotion module determines when the scout should slide based on terrain steepness
/// and controls how much influence the scout has over the slide direction and speed.
/// </remarks>
[Serializable]
[MemoryPackable]
public partial class SlideLocomotion : ITransientLocomotion, IRecordable
{
    #region Constants

    /// <summary>
    /// The default maximum slope angle (in degrees) before sliding begins.
    /// </summary>
    public static readonly Fixed64 DefaultSlopeLimit = Fixed64.FromRaw(0x2D00000000L); // 45f;

    /// <summary>
    /// The default speed at which the scout slides down steep surfaces.
    /// </summary>
    public static readonly Fixed64 DefaultSlidingSpeed = (Fixed64)30;

    /// <summary>
    /// The default amount of control the scout has while sliding sideways.
    /// </summary>
    /// <remarks>
    /// A value of 0.5 allows the scout to slide sideways at half the speed of downward sliding.
    /// </remarks>
    public static readonly Fixed64 DefaultSidewaysControl = (Fixed64)1;

    /// <summary>
    /// The default amount the scout can influence sliding speed.
    /// </summary>
    /// <remarks>
    /// A value of 0.5 allows the scout to increase sliding speed up to 150% or reduce it to 50%.
    /// </remarks>
    public static readonly Fixed64 DefaultSpeedControl = (Fixed64)0.5d;

    #endregion

    #region Configuration State

    /// <summary>
    /// Determines whether sliding mechanics are enabled.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    private bool _isEnabled = true;

    /// <summary>
    /// The slope angle threshold at which sliding begins.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 SlopeLimit = DefaultSlopeLimit;

    /// <summary>
    /// The speed at which the scout slides when on a steep surface.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 SlidingSpeed = DefaultSlidingSpeed;

    /// <summary>
    /// Determines how much control the scout has while sliding sideways.
    /// </summary>
    /// <remarks>
    /// A higher value increases lateral movement freedom during a slide.
    /// </remarks>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 SidewaysControl = DefaultSidewaysControl;

    /// <summary>
    /// Determines how much the scout can influence sliding speed.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 SpeedControl = DefaultSpeedControl;

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
    /// Indicates whether the scout is currently sliding.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public bool IsSliding { get; set; }

    #endregion

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", _isEnabled);
        RecordValues.Look(chronicler, ref SlopeLimit, "slopeLimit", SlopeLimit);
        RecordValues.Look(chronicler, ref SlidingSpeed, "slidingSpeed", SlidingSpeed);
        RecordValues.Look(chronicler, ref SidewaysControl, "sidewaysControl", SidewaysControl);
        RecordValues.Look(chronicler, ref SpeedControl, "speedControl", SpeedControl);

        bool isSliding = IsSliding;
        RecordValues.Look(chronicler, ref isSliding, "isSliding", isSliding);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsSliding = isSliding;

            if (!_isEnabled)
                ((ITransient)this).ClearTransientState();
        }
    }
}
