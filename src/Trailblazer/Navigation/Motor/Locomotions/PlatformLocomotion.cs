using FixedMathSharp;
using Trailblazer.Support;
using Trailblazer.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles movement adjustments when the scout is standing on a moving platform or surface.
/// </summary>
/// <remarks>
/// This locomotion system tracks platform velocity, rotation, and movement transfer behavior.
/// It allows the scout to inherit motion from platforms and supports different transfer states.
/// </remarks>
public class PlatformLocomotion : ITransientLocomotion, IRecordable
{
    private bool _preservePreviousTransformForAttachment;

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
    private bool _isEnabled = true;

    /// <summary>
    /// The height offset applied when interacting with moving platforms.
    /// </summary>
    public Fixed64 HeightAdjust = DefaultHeightAdjust;

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
            {
                _preservePreviousTransformForAttachment = false;
                ((ITransient)this).ClearTransientState();
            }
        }
    }

    /// <summary>
    /// Indicates whether the scout has just landed on a new platform.
    /// </summary>
    [Transient]
    public bool IsNewPlatform { get; set; }

    [Transient]
    public PlatformSnapshot? ActivePlatform { get; set; }

    [Transient]
    public PlatformSnapshot? PreviousPlatform { get; set; }

    [Transient]
    public PlatformSnapshot? HoldPlatform { get; set; }

    /// <summary>
    /// Defines how movement is transferred from the platform to the scout.
    /// </summary>
    [Transient]
    public MotionTransfer MovementTransfer { get; set; }

    /// <summary>
    /// The local position of the scout relative to the platform.
    /// </summary>
    [Transient]
    public Vector3d ScoutLocalPoint { get; set; }

    /// <summary>
    /// The local rotation of the scout relative to the platform.
    /// </summary>
    [Transient]
    public FixedQuaternion ScoutLocalRotation { get; set; } = FixedQuaternion.Identity;

    /// <summary>
    /// The velocity of the platform.
    /// </summary>
    [Transient]
    public Vector3d PlatformVelocity { get; set; }

    /// <summary>
    /// The last known platform velocity when the scout is airborne.
    /// </summary>
    [Transient]
    public Vector3d FramePlatformVelocity { get; set; }

    /// <summary>
    /// The number of frames the scout has been holding onto a platform.
    /// </summary>
    [Transient]
    public int HoldPlatformFrames { get; private set; }

    public bool IsActive => IsEnabled && ActivePlatform?.SupportsKinematicMotion == true;

    public bool IsLockedToPlatform => MovementTransfer == MotionTransfer.PermaLocked;

    public bool IsHoldingPlatform => IsEnabled && HoldPlatform?.SupportsKinematicMotion == true;

    /// <summary>
    /// Indicates whether platform inertia (initial velocity transfer) has been applied.
    /// </summary>
    public bool InteriaApplied => IsEnabled
        && (MovementTransfer == MotionTransfer.InitTransfer || MovementTransfer == MotionTransfer.PermaTransfer);

    #endregion

    #region Methods

    /// <summary>
    /// Updates the platform velocity based on movement from the last frame.
    /// </summary>
    public void UpdatePlatformVelocity()
    {
        if (!IsEnabled) return;

        if (ActivePlatform?.SupportsKinematicMotion != true)
        {
            PlatformVelocity = Vector3d.Zero;
            _preservePreviousTransformForAttachment = false;
            return;
        }

        if (!IsNewPlatform)
        {
            Vector3d currentPoint = ActivePlatform?.Transform.TransformPoint(ScoutLocalPoint) ?? Vector3d.Zero;
            Vector3d previousPoint = PreviousPlatform?.Transform.TransformPoint(ScoutLocalPoint) ?? Vector3d.Zero;

            // Store platform velocity to use as a canceling force
            PlatformVelocity = (currentPoint - previousPoint) * TrailblazerManager.InvDeltaTime;
        }

        PreviousPlatform = ActivePlatform;
        IsNewPlatform = false;
        _preservePreviousTransformForAttachment = false;
    }

    /// <summary>
    /// Applies movement adjustments due to platform motion, ensuring the navigator inherits platform movement correctly.
    /// </summary>
    /// <remarks>
    /// This method updates the navigator’s position and rotation based on the platform’s transform,
    /// preventing unwanted movement shifts when transitioning between platforms.
    /// </remarks>
    public void GetPlatformInfluence(
        Vector3d position,
        FixedQuaternion rotation,
        out Vector3d positionDelta,
        out FixedQuaternion rotationDelta)
    {
        // Apply platform rotation first THEN apply platform movement
        FixedQuaternion targetRotation = ActivePlatform?.Transform.Rotation * ScoutLocalRotation ?? FixedQuaternion.Identity;
        if (targetRotation != FixedQuaternion.Identity)
        {
            FixedQuaternion rotDelta = targetRotation * rotation.Inverse();
            rotationDelta = rotDelta;
        }
        else
            rotationDelta = FixedQuaternion.Identity;

        Vector3d newGlobalPoint = ActivePlatform?.Transform.TransformPoint(ScoutLocalPoint) ?? Vector3d.Zero;
        position.y += HeightAdjust;
        positionDelta = newGlobalPoint - position;
    }

    /// <summary>
    /// Assigns the scout to a sampled platform state, initiating a hold state.
    /// </summary>
    /// <param name="platform">The sampled platform snapshot to attach to.</param>
    public void SetHoldPlatform(PlatformSnapshot? platform)
    {
        HoldPlatform = NormalizeKinematicPlatform(platform);
        HoldPlatformFrames = 0;
    }

    /// <summary>
    /// Updates the platform hold state, releasing the hold if the hold duration expires.
    /// </summary>
    /// <returns>True if the scout should detach from the platform; otherwise, false.</returns>
    public bool TickHoldOnPlatform()
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


    public void HandlePlatformChange(GroundCondition? condition)
    {
        // If we hit a new platform, reset platform state
        if (!IsEnabled)
            return;

        // Clear it to avoid double-applying next frame
        FramePlatformVelocity = Vector3d.Zero;
        PlatformSnapshot? refreshedPlatform = NormalizeKinematicPlatform(condition?.Platform);
        MovementTransfer = refreshedPlatform?.SupportsKinematicMotion == true
            ? condition?.MotionTransferState ?? MotionTransfer.None
            : MotionTransfer.None;

        if (refreshedPlatform?.SupportsKinematicMotion != true)
        {
            HoldPlatform = null;
            HoldPlatformFrames = 0;
        }

        if (!DidPlatformChange(refreshedPlatform))
        {
            bool hasTransformRefresh = ActivePlatform?.SupportsKinematicMotion == true
                && refreshedPlatform?.SupportsKinematicMotion == true
                && !ActivePlatform.Value.Transform.Equals(refreshedPlatform.Value.Transform);

            if (hasTransformRefresh)
                PreviousPlatform = ActivePlatform;

            // Same platform id, newer transform: refresh the snapshot without marking a platform swap.
            ActivePlatform = refreshedPlatform;
            _preservePreviousTransformForAttachment = hasTransformRefresh;
            return;
        }

        PreviousPlatform = ActivePlatform?.SupportsKinematicMotion != true
            ? refreshedPlatform
            : ActivePlatform;
        ActivePlatform = refreshedPlatform;

        IsNewPlatform = refreshedPlatform?.SupportsKinematicMotion == true;
        _preservePreviousTransformForAttachment = false;
    }

    /// <summary>
    /// Determines if the navigator has transitioned onto a different platform.
    /// </summary>
    /// <param name="newPlatform"></param>
    /// <returns>True if the navigator is on a new platform; otherwise, false.</returns>
    private bool DidPlatformChange(PlatformSnapshot? newPlatform) => ActivePlatform != newPlatform;

    private static PlatformSnapshot? NormalizeKinematicPlatform(PlatformSnapshot? platform)
    {
        return platform?.SupportsKinematicMotion == true
            ? platform
            : null;
    }

    /// <summary>
    /// Updates platform movement by synchronizing the navigator's position and rotation with the platform it is standing on.
    /// </summary>
    /// <remarks>
    /// This method prevents unwanted movement shifts when transitioning between platforms, ensuring smooth locomotion.
    /// </remarks>
    public void HandlePlatformMovement(Vector3d position, FixedQuaternion rotation)
    {
        position.y += HeightAdjust;
        Fixed4x4 attachmentTransform = GetAttachmentTransform();
        ScoutLocalPoint = Fixed4x4.InverseTransformPoint(
            attachmentTransform,
            position);

        ScoutLocalRotation = attachmentTransform.Rotation.Inverse() * rotation;
    }

    private Fixed4x4 GetAttachmentTransform()
    {
        if (_preservePreviousTransformForAttachment && PreviousPlatform?.SupportsKinematicMotion == true)
            return PreviousPlatform.Value.Transform;

        return ActivePlatform?.Transform ?? Fixed4x4.Identity;
    }

    #endregion

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", _isEnabled);
        RecordValues.Look(chronicler, ref HeightAdjust, "heightAdjust", HeightAdjust);

        bool isNewPlatform = IsNewPlatform;
        PlatformSnapshot? activePlatform = ActivePlatform;
        PlatformSnapshot? previousPlatform = PreviousPlatform;
        PlatformSnapshot? holdPlatform = HoldPlatform;
        MotionTransfer movementTransfer = MovementTransfer;
        Vector3d scoutLocalPoint = ScoutLocalPoint;
        FixedQuaternion scoutLocalRotation = ScoutLocalRotation;
        Vector3d platformVelocity = PlatformVelocity;
        Vector3d framePlatformVelocity = FramePlatformVelocity;
        int holdPlatformFrames = HoldPlatformFrames;

        RecordValues.Look(chronicler, ref isNewPlatform, "isNewPlatform", isNewPlatform);
        RecordValues.Look(chronicler, ref activePlatform, "activePlatform", activePlatform);
        RecordValues.Look(chronicler, ref previousPlatform, "previousPlatform", previousPlatform);
        RecordValues.Look(chronicler, ref holdPlatform, "holdPlatform", holdPlatform);
        RecordValues.Look(chronicler, ref movementTransfer, "movementTransfer", movementTransfer);
        RecordValues.Look(chronicler, ref scoutLocalPoint, "scoutLocalPoint", scoutLocalPoint);
        RecordValues.Look(chronicler, ref scoutLocalRotation, "scoutLocalRotation", scoutLocalRotation);
        RecordValues.Look(chronicler, ref platformVelocity, "platformVelocity", platformVelocity);
        RecordValues.Look(chronicler, ref framePlatformVelocity, "framePlatformVelocity", framePlatformVelocity);
        RecordValues.Look(chronicler, ref holdPlatformFrames, "holdPlatformFrames", holdPlatformFrames);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            ActivePlatform = NormalizeKinematicPlatform(activePlatform);
            PreviousPlatform = NormalizeKinematicPlatform(previousPlatform);
            HoldPlatform = NormalizeKinematicPlatform(holdPlatform);
            IsNewPlatform = ActivePlatform?.SupportsKinematicMotion == true && isNewPlatform;
            MovementTransfer = ActivePlatform?.SupportsKinematicMotion == true
                ? movementTransfer
                : MotionTransfer.None;
            ScoutLocalPoint = scoutLocalPoint;
            ScoutLocalRotation = scoutLocalRotation;
            PlatformVelocity = ActivePlatform?.SupportsKinematicMotion == true
                ? platformVelocity
                : Vector3d.Zero;
            FramePlatformVelocity = ActivePlatform?.SupportsKinematicMotion == true
                ? framePlatformVelocity
                : Vector3d.Zero;
            HoldPlatformFrames = HoldPlatform?.SupportsKinematicMotion == true
                ? holdPlatformFrames
                : 0;
            _preservePreviousTransformForAttachment = false;

            if (!_isEnabled)
                ((ITransient)this).ClearTransientState();
        }
    }
}
