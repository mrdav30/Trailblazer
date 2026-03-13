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
/// Handles movement adjustments when the scout is standing on a moving platform or surface.
/// </summary>
/// <remarks>
/// This locomotion system tracks platform velocity, rotation, and movement transfer behavior.
/// It allows the scout to inherit motion from platforms and supports different transfer states.
/// </remarks>
[Serializable]
[MemoryPackable]
public partial class PlatformLocomotion : ITransientLocomotion
{
    #region Constants

    /// <summary>
    /// Default height adjustment applied when standing on a moving platform.
    /// </summary>
    public static readonly Fixed64 DefaultHeightAdjust = Fixed64.FromRaw(0x80000000L); // 0.5f;

    /// <summary>
    /// Maximum number of frames the scout can remain attached to a platform before release.
    /// </summary>
    public const int MaxHoldPlatformFrames = 2;

    #endregion

    #region Configuration State

    /// <summary>
    /// Determines whether platform locomotion is enabled.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    private bool _isEnabled = true;

    /// <summary>
    /// The height offset applied when interacting with moving platforms.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed64 HeightAdjust = DefaultHeightAdjust;

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
    /// Indicates whether the scout has just landed on a new platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public bool IsNewPlatform { get; set; }

    /// <summary>
    /// Defines how movement is transferred from the platform to the scout.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public MotionTransfer MovementTransfer { get; set; }

    /// <summary>
    /// The platform object the scout is currently standing on.
    /// </summary>
    [Transient]
    [JsonIgnore]
    [MemoryPackIgnore]
    public object ActivePlatform { get; set; }

    /// <summary>
    /// The transformation matrix of the active platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed4x4 ActiveTransform { get; set; } = Fixed4x4.Identity;

    /// <summary>
    /// The transformation matrix of the last known platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public Fixed4x4 LastTransform { get; set; } = Fixed4x4.Identity;

    /// <summary>
    /// The local position of the scout relative to the platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public Vector3d ScoutLocalPoint { get; set; }

    /// <summary>
    /// The local rotation of the scout relative to the platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public FixedQuaternion ScoutLocalRotation { get; set; } = FixedQuaternion.Identity;

    /// <summary>
    /// The velocity of the platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public Vector3d PlatformVelocity { get; set; }

    /// <summary>
    /// The last known platform velocity when the scout is airborne.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public Vector3d FramePlatformVelocity { get; set; }

    /// <summary>
    /// Indicates whether the scout is currently holding onto a platform.
    /// </summary>
    [Transient]

    [JsonInclude]
    [MemoryPackInclude]
    public bool IsHoldingPlatform { get; private set; }

    /// <summary>
    /// The last known platform the scout was attached to.
    /// </summary>
    [Transient]
    [JsonIgnore]
    [MemoryPackIgnore]
    public object HoldPlatform { get; private set; }

    /// <summary>
    /// The number of frames the scout has been holding onto a platform.
    /// </summary>
    [Transient]
    [JsonInclude]
    [MemoryPackInclude]
    public int HoldPlatformFrames { get; private set; }

    #endregion

    /// <summary>
    /// Assigns the scout to a platform, initiating a hold state.
    /// </summary>
    /// <param name="platform">The platform object to attach to.</param>
    public void SetHoldPlatform(object platform)
    {
        HoldPlatform = platform;
        HoldPlatformFrames = 0;
        IsHoldingPlatform = true;
    }

    /// <summary>
    /// Updates the platform hold state, releasing the hold if the hold duration expires.
    /// </summary>
    /// <returns>True if the scout should detach from the platform; otherwise, false.</returns>
    public bool CanReleaseHoldOnPlatform()
    {
        if (!IsHoldingPlatform)
            return false;

        HoldPlatformFrames++;
        if (HoldPlatformFrames >= MaxHoldPlatformFrames)
        {
            HoldPlatformFrames = 0;
            if (HoldPlatform != ActivePlatform)
                return true;
        }

        return false;
    }
}
