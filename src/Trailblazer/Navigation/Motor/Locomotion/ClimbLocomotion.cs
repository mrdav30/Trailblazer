using Chronicler;
using FixedMathSharp;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Stores climb locomotion configuration and runtime attachment state.
/// </summary>
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

    /// <summary>
    /// Default positional tolerance used when validating attachment continuity without a stable affordance id.
    /// </summary>
    public static readonly Fixed64 DefaultClimbStartTolerance = Fixed64.Half;

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
    /// Positional tolerance used for initial attach and attachment continuity checks.
    /// </summary>
    public Fixed64 ClimbStartTolerance = DefaultClimbStartTolerance;

    /// <summary>
    /// Whether lateral traverse across a climb surface is allowed.
    /// </summary>
    public bool AllowLateralTraverse = true;

    /// <summary>
    /// Whether active mantle should query an optional host validator each frame.
    /// </summary>
    public bool ValidateActiveMantleWithHost;

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

    /// <summary>
    /// Whether lateral movement is allowed for the active frame snapshot.
    /// </summary>
    [Transient]
    public bool ActiveAllowLateralTraverse { get; set; }

    /// <summary>
    /// Whether descent is allowed for the active frame snapshot.
    /// </summary>
    [Transient]
    public bool ActiveAllowDescent { get; set; }

    /// <summary>
    /// Whether detaching via jump is allowed for the active frame snapshot.
    /// </summary>
    [Transient]
    public bool ActiveAllowDetachJump { get; set; }

    /// <summary>
    /// Whether mantling is allowed for the active frame snapshot.
    /// </summary>
    [Transient]
    public bool ActiveAllowMantle { get; set; }

    /// <summary>
    /// Current mantle target when a ledge affordance supplies one.
    /// </summary>
    [Transient]
    public Vector3d? MantleTargetPosition { get; set; }

    /// <summary>
    /// Applies a climb affordance snapshot to update the active attachment state for the current frame.
    /// </summary>
    /// <param name="snapshot"></param>
    public void ApplyClimbSnapshot(ClimbAffordanceSnapshot snapshot)
    {
        ActiveClimbKind = snapshot.Kind;
        AttachmentId = snapshot.AffordanceId;
        AttachmentPoint = snapshot.AttachmentPoint;
        AttachedSurfaceNormal = snapshot.SurfaceNormal;
        AttachedUpDirection = snapshot.UpDirection;
        ActiveAllowLateralTraverse = snapshot.AllowLateralTraverse && AllowLateralTraverse;
        ActiveAllowDescent = snapshot.AllowDescent;
        ActiveAllowDetachJump = snapshot.AllowDetachJump;
        ActiveAllowMantle = snapshot.AllowMantle && snapshot.MantleTargetPosition.HasValue;
        MantleTargetPosition = snapshot.MantleTargetPosition;
    }

    /// <summary>
    /// Creates a readonly snapshot for optional active mantle validation.
    /// </summary>
    public ActiveMantleState CreateActiveMantleState()
    {
        return new ActiveMantleState(
            ActiveClimbKind,
            AttachmentId,
            AttachmentPoint,
            AttachedSurfaceNormal,
            AttachedUpDirection,
            MantleTargetPosition ?? AttachmentPoint);
    }

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", true);
        RecordValues.Look(chronicler, ref CanClimb, "canClimb", true);
        RecordValues.Look(chronicler, ref MaxClimbSpeed, "maxClimbSpeed", DefaultMaxClimbSpeed);
        RecordValues.Look(chronicler, ref MaxClimbAcceleration, "maxClimbAcceleration", DefaultMaxClimbAcceleration);
        RecordValues.Look(chronicler, ref GravityCompensationWhileClimbing, "gravityCompensationWhileClimbing", DefaultGravityCompensationWhileClimbing);
        RecordValues.Look(chronicler, ref ClimbStartTolerance, "climbStartTolerance", DefaultClimbStartTolerance);
        RecordValues.Look(chronicler, ref AllowLateralTraverse, "allowLateralTraverse", true);
        RecordValues.Look(chronicler, ref ValidateActiveMantleWithHost, "validateActiveMantleWithHost", false);

        bool isClimbing = IsClimbing;
        bool isMantling = IsMantling;
        ClimbAffordanceKind activeClimbKind = ActiveClimbKind;
        int? attachmentId = AttachmentId;
        Vector3d attachmentPoint = AttachmentPoint;
        Vector3d attachedSurfaceNormal = AttachedSurfaceNormal;
        Vector3d attachedUpDirection = AttachedUpDirection;
        bool activeAllowLateralTraverse = ActiveAllowLateralTraverse;
        bool activeAllowDescent = ActiveAllowDescent;
        bool activeAllowDetachJump = ActiveAllowDetachJump;
        bool activeAllowMantle = ActiveAllowMantle;
        Vector3d? mantleTargetPosition = MantleTargetPosition;

        RecordValues.Look(chronicler, ref isClimbing, "isClimbing", false);
        RecordValues.Look(chronicler, ref isMantling, "isMantling", false);
        RecordValues.Look(chronicler, ref activeClimbKind, "activeClimbKind", ClimbAffordanceKind.None);
        RecordValues.Look(chronicler, ref attachmentId, "attachmentId", null);
        RecordValues.Look(chronicler, ref attachmentPoint, "attachmentPoint", Vector3d.Zero);
        RecordValues.Look(chronicler, ref attachedSurfaceNormal, "attachedSurfaceNormal", Vector3d.Zero);
        RecordValues.Look(chronicler, ref attachedUpDirection, "attachedUpDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref activeAllowLateralTraverse, "activeAllowLateralTraverse", false);
        RecordValues.Look(chronicler, ref activeAllowDescent, "activeAllowDescent", true);
        RecordValues.Look(chronicler, ref activeAllowDetachJump, "activeAllowDetachJump", true);
        RecordValues.Look(chronicler, ref activeAllowMantle, "activeAllowMantle", false);
        RecordValues.Look(chronicler, ref mantleTargetPosition, "mantleTargetPosition", null);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsClimbing = isClimbing;
            IsMantling = isMantling;
            ActiveClimbKind = activeClimbKind;
            AttachmentId = attachmentId;
            AttachmentPoint = attachmentPoint;
            AttachedSurfaceNormal = attachedSurfaceNormal;
            AttachedUpDirection = attachedUpDirection;
            ActiveAllowLateralTraverse = activeAllowLateralTraverse;
            ActiveAllowDescent = activeAllowDescent;
            ActiveAllowDetachJump = activeAllowDetachJump;
            ActiveAllowMantle = activeAllowMantle;
            MantleTargetPosition = mantleTargetPosition;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }
}
