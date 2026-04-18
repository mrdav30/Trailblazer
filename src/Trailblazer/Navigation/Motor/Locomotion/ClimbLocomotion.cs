using Chronicler;
using FixedMathSharp;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Stores climb locomotion configuration and runtime attachment state.
/// </summary>
/// <remarks>
/// Phase 1 only introduces the profile and serialization surface for climbing.
/// Runtime climb motion is intentionally deferred to a later implementation phase.
/// </remarks>
public class ClimbLocomotion : ILocomotion
{
    /// <summary>
    /// Default maximum speed while climbing.
    /// </summary>
    public static readonly Fixed64 DefaultMaxClimbSpeed = Fixed64.One;

    /// <summary>
    /// Default maximum acceleration while climbing.
    /// </summary>
    public static readonly Fixed64 DefaultMaxClimbAcceleration = (Fixed64)8;

    /// <summary>
    /// Default gravity compensation applied while attached to a climb affordance.
    /// </summary>
    public static readonly Fixed64 DefaultGravityCompensationWhileClimbing = Fixed64.One;

    private bool _isEnabled = true;

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
    /// Determines whether this locomotion may engage with climb affordances.
    /// </summary>
    public bool CanClimb = true;

    /// <summary>
    /// Maximum climb speed while attached.
    /// </summary>
    public Fixed64 MaxClimbSpeed = DefaultMaxClimbSpeed;

    /// <summary>
    /// Maximum acceleration while attached.
    /// </summary>
    public Fixed64 MaxClimbAcceleration = DefaultMaxClimbAcceleration;

    /// <summary>
    /// Amount of gravity canceled while actively climbing.
    /// </summary>
    public Fixed64 GravityCompensationWhileClimbing = DefaultGravityCompensationWhileClimbing;

    /// <summary>
    /// Whether lateral traverse across a climb surface is allowed.
    /// </summary>
    public bool AllowLateralTraverse = true;

    /// <summary>
    /// Whether active climb movement is currently attached to an affordance.
    /// </summary>
    [Transient]
    public bool IsClimbing { get; set; }

    /// <summary>
    /// Whether the locomotion is currently in a mantle/top-out phase.
    /// </summary>
    [Transient]
    public bool IsMantling { get; set; }

    /// <summary>
    /// Kind of affordance currently attached, when active.
    /// </summary>
    [Transient]
    public ClimbAffordanceKind ActiveClimbKind { get; set; }

    /// <summary>
    /// Stable host affordance identity when available.
    /// </summary>
    [Transient]
    public int? AttachmentId { get; set; }

    /// <summary>
    /// Stored attachment point for the current climb interaction.
    /// </summary>
    [Transient]
    public Vector3d AttachmentPoint { get; set; }

    /// <summary>
    /// Stored attachment surface normal for the current climb interaction.
    /// </summary>
    [Transient]
    public Vector3d AttachedSurfaceNormal { get; set; }

    /// <summary>
    /// Stored climb up direction for the current climb interaction.
    /// </summary>
    [Transient]
    public Vector3d AttachedUpDirection { get; set; }

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", true);
        RecordValues.Look(chronicler, ref CanClimb, "canClimb", true);
        RecordValues.Look(chronicler, ref MaxClimbSpeed, "maxClimbSpeed", DefaultMaxClimbSpeed);
        RecordValues.Look(chronicler, ref MaxClimbAcceleration, "maxClimbAcceleration", DefaultMaxClimbAcceleration);
        RecordValues.Look(chronicler, ref GravityCompensationWhileClimbing, "gravityCompensationWhileClimbing", DefaultGravityCompensationWhileClimbing);
        RecordValues.Look(chronicler, ref AllowLateralTraverse, "allowLateralTraverse", true);

        bool isClimbing = IsClimbing;
        bool isMantling = IsMantling;
        ClimbAffordanceKind activeClimbKind = ActiveClimbKind;
        int? attachmentId = AttachmentId;
        Vector3d attachmentPoint = AttachmentPoint;
        Vector3d attachedSurfaceNormal = AttachedSurfaceNormal;
        Vector3d attachedUpDirection = AttachedUpDirection;

        RecordValues.Look(chronicler, ref isClimbing, "isClimbing", false);
        RecordValues.Look(chronicler, ref isMantling, "isMantling", false);
        RecordValues.Look(chronicler, ref activeClimbKind, "activeClimbKind", ClimbAffordanceKind.None);
        RecordValues.Look(chronicler, ref attachmentId, "attachmentId", null);
        RecordValues.Look(chronicler, ref attachmentPoint, "attachmentPoint", Vector3d.Zero);
        RecordValues.Look(chronicler, ref attachedSurfaceNormal, "attachedSurfaceNormal", Vector3d.Zero);
        RecordValues.Look(chronicler, ref attachedUpDirection, "attachedUpDirection", Vector3d.Zero);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsClimbing = isClimbing;
            IsMantling = isMantling;
            ActiveClimbKind = activeClimbKind;
            AttachmentId = attachmentId;
            AttachmentPoint = attachmentPoint;
            AttachedSurfaceNormal = attachedSurfaceNormal;
            AttachedUpDirection = attachedUpDirection;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }
}
