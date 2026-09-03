//=======================================================================
// PlatformLocomotion.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Tracks moving-platform attachment and departure-velocity transfer for a scout.
/// </summary>
/// <remarks>
/// Attached carry uses transform-derived displacement and rotation. Departure transfer uses
/// velocity sampled at the scout's local attachment point, not a physical force or impulse.
/// </remarks>
public class PlatformLocomotion : ILocomotion
{
    private TrailblazerWorldContext? _context;

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
                this.ClearTransientState();
            }
        }
    }

    /// <summary>
    /// Indicates whether the scout has just landed on a new platform.
    /// </summary>
    [Transient]
    public bool IsNewPlatform { get; set; }

    /// <summary>
    /// The currently active platform the scout is standing on, if any.
    /// </summary>
    [Transient]
    public PlatformSnapshot? ActivePlatform { get; set; }

    /// <summary>
    /// The previously active platform, used for calculating platform velocity and movement transfer.
    /// This is cleared when the scout is not on a platform or when platform locomotion is disabled.
    ///
    /// </summary>
    [Transient]
    public PlatformSnapshot? PreviousPlatform { get; set; }

    /// <summary>
    /// A flag to preserve the previous platform's transform for attachment calculations,
    /// used when refreshing the same platform with a new transform.
    /// </summary>
    private bool _preservePreviousTransformForAttachment;

    /// <summary>
    /// The platform snapshot that the scout is currently holding onto, if any.
    /// </summary>
    [Transient]
    public PlatformSnapshot? HoldPlatform { get; set; }

    /// <summary>
    /// Selects departure-velocity transfer or extended attachment. Ordinary grounded carry
    /// also applies with <see cref="MotionTransfer.None"/>.
    /// </summary>
    [Transient]
    public MotionTransfer MovementTransfer { get; set; }

    /// <summary>
    /// The attachment point in platform-local coordinates, including <see cref="HeightAdjust"/>.
    /// </summary>
    [Transient]
    public Vector3d ScoutLocalPoint { get; set; }

    /// <summary>
    /// The local rotation of the scout relative to the platform.
    /// </summary>
    [Transient(typeof(FixedQuaternion), nameof(FixedQuaternion.Identity))]
    public FixedQuaternion ScoutLocalRotation { get; set; } = FixedQuaternion.Identity;

    /// <summary>
    /// The sampled world-space velocity of <see cref="ScoutLocalPoint"/>, in world units per second.
    /// </summary>
    /// <remarks>
    /// Includes motion of that attachment point due to platform translation and rotation.
    /// It is not necessarily the velocity of the platform's center.
    /// </remarks>
    [Transient]
    public Vector3d PlatformVelocity { get; set; }

    /// <summary>
    /// The platform velocity captured for departure transfer on a solid-to-gas transition.
    /// </summary>
    /// <remarks>
    /// <see cref="MotionTransfer.PermaTransfer"/> adds its horizontal contribution to ordinary
    /// desired locomotion velocity. Platform-state refresh clears this value; it is not the
    /// scout's total airborne velocity.
    /// </remarks>
    [Transient]
    public Vector3d FramePlatformVelocity { get; set; }

    private TrailblazerWorldContext RequireContext() =>
        _context ?? throw new InvalidOperationException("PlatformLocomotion requires an explicit TrailblazerWorldContext.");

    private Fixed64 InvDeltaTime => RequireContext().InvDeltaTime;

    /// <summary>
    /// The number of frames the scout has been holding onto a platform.
    /// </summary>
    [Transient]
    public int HoldPlatformFrames { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the component is currently active and supports kinematic motion.
    /// </summary>
    public bool IsActive => IsEnabled && ActivePlatform?.SupportsKinematicMotion == true;

    /// <summary>
    /// Gets whether extended platform attachment is selected by <see cref="MotionTransfer.PermaLocked"/>.
    /// The motor still checks platform eligibility and jump, flight, and climb state before carrying the scout.
    /// </summary>
    public bool IsLockedToPlatform => MovementTransfer == MotionTransfer.PermaLocked;

    /// <summary>
    /// Gets a value indicating whether the current object is holding a platform that supports kinematic motion.
    /// </summary>
    public bool IsHoldingPlatform => IsEnabled && HoldPlatform?.SupportsKinematicMotion == true;

    /// <summary>
    /// Gets whether the enabled module's transfer mode permits initial velocity transfer.
    /// </summary>
    /// <remarks>
    /// This is a configuration check, not a record that a transfer has already occurred.
    /// </remarks>
    public bool InertiaApplied => IsEnabled
        && (MovementTransfer == MotionTransfer.InitTransfer || MovementTransfer == MotionTransfer.PermaTransfer);

    #endregion

    #region Methods

    /// <summary>
    /// Samples the world-space velocity of the local attachment point over the fixed timestep.
    /// </summary>
    /// <remarks>
    /// Transforms the same <see cref="ScoutLocalPoint"/> through the current and previous platform
    /// snapshots, then divides its displacement by the context's timestep. A newly attached platform
    /// establishes snapshot history before sampling. Disabled modules do not update velocity;
    /// an enabled module without an active kinematic platform clears it.
    /// </remarks>
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
            Vector3d currentPoint = ActivePlatform.Value.Transform.TransformPoint(ScoutLocalPoint);
            Vector3d previousPoint = PreviousPlatform?.Transform.TransformPoint(ScoutLocalPoint) ?? Vector3d.Zero;

            // Sample attachment-point velocity for departure and landing transfer accounting.
            PlatformVelocity = (currentPoint - previousPoint) * InvDeltaTime;
        }

        PreviousPlatform = ActivePlatform;
        IsNewPlatform = false;
        _preservePreviousTransformForAttachment = false;
    }

    internal void BindContext(TrailblazerWorldContext context)
    {
        TrailblazerWorldContext.ThrowIfUnusable(context);
        _context = context;
    }

    /// <summary>
    /// Calculates the displacement and rotation delta for the stored platform attachment.
    /// </summary>
    /// <remarks>
    /// Returns motion deltas without changing the host's transform. The displacement is already
    /// in world units; do not multiply it by the timestep or add another platform-velocity displacement.
    /// </remarks>
    /// <param name="position">The scout's world-space foot position, or origin when no foot snapshot is available.</param>
    /// <param name="rotation">The scout's current world rotation.</param>
    /// <param name="positionDelta">World-space displacement from the height-adjusted position to the platform attachment point.</param>
    /// <param name="rotationDelta">The platform attachment's rotation delta relative to the supplied rotation.</param>
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
        position.Y += HeightAdjust;
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
            if (!ActivePlatform.HasValue || HoldPlatform!.Value != ActivePlatform.Value)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Handles changes in the platform state based on the specified ground condition. Updates platform-related movement and state accordingly.
    /// </summary>
    /// <remarks>
    /// This method should be called whenever the underlying platform or ground condition changes,
    /// such as when stepping onto a new platform or leaving one.
    /// It resets and updates platform movement and transfer state as needed.
    /// </remarks>
    /// <param name="condition">The ground condition representing the current platform state. May be null if there is no platform contact.</param>
    public void HandlePlatformChange(GroundCondition? condition)
    {
        // If we hit a new platform, reset platform state
        if (!IsEnabled)
            return;

        // Clear it to avoid double-applying next frame
        FramePlatformVelocity = Vector3d.Zero;
        PlatformSnapshot? refreshedPlatform = NormalizeKinematicPlatform(condition?.Platform);
        MovementTransfer = ResolveMovementTransfer(refreshedPlatform, condition);
        ClearHoldPlatformIfInactive(refreshedPlatform);

        if (TryRefreshExistingPlatform(refreshedPlatform))
            return;

        SwapActivePlatform(refreshedPlatform);
    }

    /// <summary>
    /// Determines if the object has transitioned onto a different platform.
    /// </summary>
    /// <param name="newPlatform"></param>
    /// <returns>True if the object is on a new platform; otherwise, false.</returns>
    private bool DidPlatformChange(PlatformSnapshot? newPlatform) => ActivePlatform != newPlatform;

    private static PlatformSnapshot? NormalizeKinematicPlatform(PlatformSnapshot? platform)
    {
        return platform?.SupportsKinematicMotion == true
            ? platform
            : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MotionTransfer ResolveMovementTransfer(PlatformSnapshot? refreshedPlatform, GroundCondition? condition)
    {
        return refreshedPlatform?.SupportsKinematicMotion == true
            // refreshedPlatform comes from condition.Platform, so a kinematic snapshot implies condition exists.
            ? condition!.Value.MotionTransferState
            : MotionTransfer.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearHoldPlatformIfInactive(PlatformSnapshot? refreshedPlatform)
    {
        if (refreshedPlatform?.SupportsKinematicMotion == true)
            return;

        HoldPlatform = null;
        HoldPlatformFrames = 0;
    }

    private bool TryRefreshExistingPlatform(PlatformSnapshot? refreshedPlatform)
    {
        if (DidPlatformChange(refreshedPlatform))
            return false;

        bool hasTransformRefresh = ActivePlatform?.SupportsKinematicMotion == true
            && !ActivePlatform.Value.Transform.Equals(refreshedPlatform!.Value.Transform);

        if (hasTransformRefresh)
            PreviousPlatform = ActivePlatform;

        // Same platform id, newer transform: refresh the snapshot without marking a platform swap.
        ActivePlatform = refreshedPlatform;
        _preservePreviousTransformForAttachment = hasTransformRefresh;
        return true;
    }

    private void SwapActivePlatform(PlatformSnapshot? refreshedPlatform)
    {
        PreviousPlatform = ActivePlatform?.SupportsKinematicMotion != true
            ? refreshedPlatform
            : ActivePlatform;
        ActivePlatform = refreshedPlatform;

        IsNewPlatform = refreshedPlatform?.SupportsKinematicMotion == true;
        _preservePreviousTransformForAttachment = false;
    }

    /// <summary>
    /// Updates the platform-local attachment point and rotation from the scout's accepted world pose.
    /// </summary>
    /// <remarks>
    /// Stores attachment state for subsequent platform carry; it does not move or rotate the host.
    /// </remarks>
    /// <param name="position">The accepted world-space foot position, or origin when no foot snapshot is available.</param>
    /// <param name="rotation">The accepted world rotation.</param>
    public void HandlePlatformMovement(Vector3d position, FixedQuaternion rotation)
    {
        position.Y += HeightAdjust;
        Fixed4x4 attachmentTransform = GetAttachmentTransform();
        ScoutLocalPoint = Fixed4x4.InverseTransformPoint(
            attachmentTransform,
            position);

        ScoutLocalRotation = attachmentTransform.Rotation.Inverse() * rotation;
    }

    private Fixed4x4 GetAttachmentTransform()
    {
        if (_preservePreviousTransformForAttachment)
            return PreviousPlatform!.Value.Transform;

        return ActivePlatform?.Transform ?? Fixed4x4.Identity;
    }

    #endregion

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "IsEnabled", true);
        RecordValues.Look(chronicler, ref HeightAdjust, "HeightAdjust", DefaultHeightAdjust);

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

        RecordValues.Look(chronicler, ref isNewPlatform, "IsNewPlatform", false);
        RecordValues.Look(chronicler, ref activePlatform, "ActivePlatform", null);
        RecordValues.Look(chronicler, ref previousPlatform, "PreviousPlatform", null);
        RecordValues.Look(chronicler, ref holdPlatform, "HoldPlatform", null);
        RecordValues.Look(chronicler, ref movementTransfer, "MovementTransfer", MotionTransfer.None);
        RecordValues.Look(chronicler, ref scoutLocalPoint, "ScoutLocalPoint", Vector3d.Zero);
        RecordValues.Look(chronicler, ref scoutLocalRotation, "ScoutLocalRotation", FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref platformVelocity, "PlatformVelocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref framePlatformVelocity, "FramePlatformVelocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref holdPlatformFrames, "HoldPlatformFrames", 0);

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
                this.ClearTransientState();
        }
    }
}
