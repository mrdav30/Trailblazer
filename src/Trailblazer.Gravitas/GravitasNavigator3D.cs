//=======================================================================
// GravitasNavigator3D.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Support;
using SwiftCollections.Diagnostics;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Adapts one Trailblazer navigator to a host-owned Gravitas kinematic body.
/// </summary>
/// <remarks>
/// The host advances Trailblazer and Gravitas, calls <see cref="Simulate"/> to
/// publish the requested pose, advances Gravitas late simulation once for the
/// actor batch, and then calls <see cref="CommitFrameMotion"/> to accept the
/// resolved pose.
/// </remarks>
public sealed class GravitasNavigator3D : Navigator
{
    private readonly SolidBody _body;
    private readonly Func<LSCollider, int>? _carrierIdResolver;
    private readonly Fixed64 _supportProbeRadius;
    private readonly Fixed64 _supportProbeOffset;
    private readonly Fixed64 _supportProbeDistance;
    private GravitasContact _contact;
    private Vector3d _requestedPosition;
    private FixedQuaternion _requestedRotation = FixedQuaternion.Identity;
    private int _preparedFrame = -1;
    private bool _hasPreparedFrame;

    /// <summary>
    /// Creates an adapter bound to contexts that share the same GridForge world.
    /// </summary>
    /// <param name="context">The Trailblazer context that owns navigation state.</param>
    /// <param name="body">The active, host-owned Gravitas kinematic body.</param>
    /// <param name="supportLayers">Non-trigger layers eligible to provide support.</param>
    /// <param name="supportProbeRadius">Positive radius of the downward support sweep.</param>
    /// <param name="supportProbeOffset">Non-negative height above the accepted foot point where the sweep starts.</param>
    /// <param name="supportProbeDistance">Positive downward sweep distance.</param>
    /// <param name="carrierIdResolver">
    /// Optional host resolver for stable non-zero moving-platform identities.
    /// Returning zero records an inert surface contact.
    /// </param>
    public GravitasNavigator3D(
        TrailblazerWorldContext context,
        SolidBody body,
        PhysicsLayerMask supportLayers,
        Fixed64 supportProbeRadius,
        Fixed64 supportProbeOffset,
        Fixed64 supportProbeDistance,
        Func<LSCollider, int>? carrierIdResolver = null)
    {
        SwiftThrowHelper.ThrowIfNull(body, nameof(body));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            supportProbeRadius <= Fixed64.Zero,
            actualValue: null,
            nameof(supportProbeRadius),
            "Support probe radius must be positive.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            supportProbeOffset < Fixed64.Zero,
            actualValue: null,
            nameof(supportProbeOffset),
            "Support probe offset cannot be negative.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            supportProbeDistance <= Fixed64.Zero,
            actualValue: null,
            nameof(supportProbeDistance),
            "Support probe distance must be positive.");

        _body = body;
        _carrierIdResolver = carrierIdResolver;
        _supportProbeRadius = supportProbeRadius;
        _supportProbeOffset = supportProbeOffset;
        _supportProbeDistance = supportProbeDistance;
        SupportLayers = supportLayers;
        BindContext(context);
    }

    /// <summary>Gets the host-owned kinematic body resolved for this navigator.</summary>
    public SolidBody Body => _body;

    /// <summary>Gets the physical layers sampled for support after resolution.</summary>
    public PhysicsLayerMask SupportLayers { get; }

    /// <inheritdoc/>
    public override void BindContext(TrailblazerWorldContext context)
    {
        SwiftThrowHelper.ThrowIfNull(context, nameof(context));
        SwiftThrowHelper.ThrowIfArgument(
            !ReferenceEquals(context.World, _body.World),
            nameof(context),
            "Trailblazer and Gravitas must share the same GridForge world.");
        base.BindContext(context);
    }

    /// <inheritdoc/>
    public override void Simulate()
    {
        SwiftThrowHelper.ThrowIfTrue(
            _hasPreparedFrame,
            nameof(GravitasNavigator3D),
            "The current frame is already prepared and must be committed or reset.");
        ValidateBody();

        base.Simulate();
        Vector3d requestedPosition = Position + _positionDelta + _locomotionDisplacement;
        FixedQuaternion requestedRotation = Rotation * _rotationDelta;
        SwiftThrowHelper.ThrowIfTrue(
            !_body.PositionTransform.TrySetWorldPose(requestedPosition, requestedRotation),
            nameof(FixedTransform),
            "The Gravitas host transform cannot represent the requested world pose.");
        _requestedPosition = _body.PositionTransform.WorldPosition;
        _requestedRotation = _body.RotationTransform.WorldRotation;
        _preparedFrame = Context!.FrameCount;
        _hasPreparedFrame = true;
    }

    /// <inheritdoc/>
    public override void CommitFrameMotion()
    {
        SwiftThrowHelper.ThrowIfTrue(
            !_hasPreparedFrame,
            nameof(GravitasNavigator3D),
            "The current frame must be prepared before it can be committed.");
        SwiftThrowHelper.ThrowIfTrue(
            Context!.FrameCount != _preparedFrame,
            nameof(GravitasNavigator3D),
            "Prepared motion cannot be committed after the Trailblazer frame advances.");
        ValidateBody();

        Vector3d acceptedPosition = _body.Position3d;
        FixedQuaternion acceptedRotation = _body.Rotation;

        _contact = GravitasContactTranslator.Sample(
            _body,
            acceptedPosition,
            BodyShape.RootToFootOffsetY,
            SupportLayers,
            _supportProbeRadius,
            _supportProbeOffset,
            _supportProbeDistance,
            _carrierIdResolver);

        _positionDelta = acceptedPosition - Position;
        _locomotionDisplacement = Vector3d.Zero;
        _rotation = acceptedRotation;
        _rotationDelta = FixedQuaternion.Identity;

        if (acceptedPosition != _requestedPosition || acceptedRotation != _requestedRotation)
            NotifyCollision();

        base.CommitFrameMotion();
        _hasPreparedFrame = false;
        _preparedFrame = -1;
    }

    /// <inheritdoc/>
    public override void CheckTrekCondition()
    {
        if (_contact.HasSupport)
        {
            SetGroundContact(
                _contact.SurfaceLevel,
                _contact.SurfaceNormal,
                _contact.Platform,
                _contact.SurfaceFriction,
                updateMotorState: false);
            return;
        }

        SetAirborne(
            surfaceLevel: FootPosition.Y,
            updateMotorState: false);
    }

    /// <inheritdoc/>
    protected override bool TryCommitPhysicalProfileReplacement(
        NavigationAgentProfile replacement,
        Vector3d replacementRootPosition,
        Action publishNavigationState,
        out Exception? notificationException)
    {
        notificationException = null;
        KinematicBodyShape shape = replacement.Shape;
        bool hasUnitScale = _body.PositionTransform.TryGetLossyScale(
                out Vector3d physicalScale)
            & physicalScale == Vector3d.One;
        if (!hasUnitScale
            || _body.Collider is not LSCapsuleCollider
            || shape.Radius <= Fixed64.Zero
            || Fixed64.CompareProducts(
                shape.Height,
                Fixed64.One,
                shape.Radius,
                Fixed64.Two) < 0)
        {
            return false;
        }

        Fixed64 localOffsetY = shape.Height * Fixed64.Half
            - shape.RootToFootOffsetY;
        ValidateBody();
        ColliderReconfigurationStatus status = _body.TryReconfigureCollider(
            ColliderShapeDefinition.Capsule(shape.Radius, shape.Height),
            Vector3d.Up * localOffsetY,
            replacementRootPosition,
            out _,
            out notificationException,
            publishSynchronizedState: publishNavigationState);
        return status is ColliderReconfigurationStatus.Applied
            or ColliderReconfigurationStatus.Unchanged;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        if (Motor?.TraversalInProgress == true)
            Motor.AbortTraversalFrame();

        _positionDelta = Vector3d.Zero;
        _locomotionDisplacement = Vector3d.Zero;
        _rotationDelta = FixedQuaternion.Identity;
        ClearPreparedMotion();
        base.Reset();
    }

    /// <inheritdoc/>
    public override void RecordData(IChronicler chronicler)
    {
        base.RecordData(chronicler);
        // A rejected base population must leave the live prepared frame intact.
        // The body is restored by the host; do not sample or mutate it here.
        if (chronicler.Mode == SerializationMode.Loading)
            ClearPreparedMotion();
    }

    private void ClearPreparedMotion()
    {
        _contact = default;
        _requestedPosition = Vector3d.Zero;
        _requestedRotation = FixedQuaternion.Identity;
        _hasPreparedFrame = false;
        _preparedFrame = -1;
    }

    private void ValidateBody()
    {
        SwiftThrowHelper.ThrowIfTrue(
            !_body.Active,
            nameof(SolidBody),
            "Gravitas body must remain active while the adapter is in use.");
        SwiftThrowHelper.ThrowIfTrue(
            !_body.IsKinematic,
            nameof(SolidBody),
            "GravitasNavigator3D requires a kinematic body.");
    }
}
