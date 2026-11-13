//=======================================================================
// GravitasNavigator2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler.Diagnostics;
using Chronicler.Timing;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Queries;
using Gravitas.Support;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>Binds native planar navigation to a host-owned Gravitas kinematic body in the same GridWorld.</summary>
/// <remarks>
/// Advance both contexts, prepare all actors, run Gravitas late simulation once, then commit all actors
/// before Trailblazer late simulation. The host owns successful completion of the Gravitas barrier.
/// Physics uses X/Z while preserving the host transform's world Y.
/// </remarks>
public sealed class GravitasNavigator2D : Navigator2D
{
    private readonly TrailblazerWorldContext _navigation;
    private readonly PhysicsLayerMask _supportLayers;
    private readonly Fixed64 _supportProbeDistance;
    private readonly Func<LSCollider2D, (int StableId, long Lifetime)?>? _carrierIdentity;
    private Physics2DSupportHit _carrierHit;
    private long _preparedPhysicsFrame;
    private ChronicleTimestamp _preparedPhysicsTime;
    private uint _expectedDistributionVersion;

    /// <summary>Attaches to an active kinematic body with a centered circle or upright capsule.</summary>
    /// <param name="context">The navigation context sharing the body's GridWorld.</param>
    /// <param name="body">The registered body in a pure TwoD Gravitas context with an unparented, unit-planar-scale transform.</param>
    /// <param name="initialMapId">The published planar map matching the profile mode.</param>
    /// <param name="globalId">The host-stable navigator ID.</param>
    /// <param name="profile">The profile matching the native collider dimensions.</param>
    /// <param name="settings">The planar motor settings.</param>
    /// <param name="medium">The initial navigation medium.</param>
    /// <param name="supportLayers">The physics layers to probe for side-view support. Null includes all layers.</param>
    /// <param name="supportProbeDistance">The positive side-view probe travel from the lower cap center.</param>
    /// <param name="carrierIdentity">
    /// Optional lookup of a supporting collider's positive host-stable ID and registration lifetime.
    /// Return null for inert support. The lookup must be deterministic and must not mutate the world.
    /// </param>
    public GravitasNavigator2D(TrailblazerWorldContext context, SolidBody2D body, string initialMapId,
        Guid globalId, NavigationAgentProfile2D profile, NavigationMotorSettings2D settings,
        TraversalMedium medium = TraversalMedium.Solid, PhysicsLayerMask? supportLayers = null,
        Fixed64 supportProbeDistance = default, Func<LSCollider2D, (int StableId, long Lifetime)?>? carrierIdentity = null)
        : base(context, initialMapId, globalId, ValidateBinding(context, body, profile, supportProbeDistance),
            profile, settings, medium)
    {
        Body = body;
        _navigation = context;
        _supportLayers = supportLayers ?? PhysicsLayerMask.All;
        _supportProbeDistance = supportProbeDistance;
        _carrierIdentity = carrierIdentity;
    }

    /// <summary>Gets the host-owned kinematic body.</summary>
    public SolidBody2D Body { get; }

    /// <inheritdoc/>
    protected override void SubmitMotionTarget(in NavigationMotionRequest2D request)
    {
        var physics = Body.Context;
        ValidateBodyAtNavigationPose();
        ChronicleGuard.ThrowIfTrue((physics.FrameCount == 0) | (physics.DeltaTime != _navigation.DeltaTime),
            message: "Advance Gravitas with the same fixed step before preparing planar motion.");
        var transform = Body.Agent.Transform;
        var target = request.Target;
        var position = target.Center.ToVector3d(transform.WorldPosition.Y);
        var rotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, -target.RotationRadians);
        ChronicleGuard.ThrowIfTrue(!transform.TrySetWorldPose(position, rotation),
            message: "The Gravitas host transform cannot represent the planar target.");
        _preparedPhysicsFrame = physics.FrameCount;
        _preparedPhysicsTime = physics.ElapsedTime;
        _expectedDistributionVersion = unchecked(physics.Collisions2D.Version + 1u);
    }

    /// <inheritdoc/>
    protected override NavigationMotionResult2D ReadAcceptedMotion(NavigationMotionRequest2D? request)
    {
        var physics = Body.Context;
        ValidateBody(_navigation, Body, NavigationProfile);
        ChronicleGuard.ThrowIfTrue(request.HasValue && ((physics.FrameCount != _preparedPhysicsFrame)
            | (physics.ElapsedTime != _preparedPhysicsTime) | (physics.DeltaTime != _navigation.DeltaTime)
            | (physics.Collisions2D.Version != _expectedDistributionVersion)),
            message: "Commit after one Gravitas collision pass in the prepared fixed frame.");

        var accepted = new NavigationPose2D(Body.Position, Body.Rotation);
        if (NavigationProfile.Mode == NavigationMode2D.TopDown)
            return new NavigationMotionResult2D(accepted, NavigationSupportStatus2D.NotApplicable);

        _carrierHit = default;
        ChronicleGuard.ThrowIfTrue(!FixedSegment2d.TryGetCenteredAxisEndpoint(accepted.Center, Vector2d.Forward,
            NavigationProfile.AxisLength, positive: false, out Vector2d lowerCap),
            message: "The registered capsule must have a representable lower cap.");

        var probe = new Physics2DSupportQuery(Body.Collider, lowerCap, NavigationProfile.Radius, Vector2d.Forward,
            _supportProbeDistance, NavigationProfile.MinimumSupportNormalDot, _supportLayers);
        var status = physics.Query2D.QuerySupport(probe, out var support);
        if (status == Physics2DSupportQueryStatus.NoSupport)
            return new NavigationMotionResult2D(accepted, NavigationSupportStatus2D.NoSupport);
        if ((status != Physics2DSupportQueryStatus.Found) | (support.SampledFrame != physics.FrameCount)
            | !support.IsCurrentLifetime | !support.Hit.TryGetPoint(out Vector2d point))
            return new NavigationMotionResult2D(accepted, NavigationSupportStatus2D.Unavailable);

        var collider = support.Hit.Collider;
        NavigationCarrier2D? carrier = null;
        if (_carrierIdentity?.Invoke(collider) is { } identity)
        {
            var pose = ReadCarrierPose(collider);
            ChronicleGuard.ThrowIfTrue(!Vector2d.TryInverseTransformScaledPoint(pose.Center, point, Vector2d.One,
                pose.RotationRadians, out Vector2d localAnchor),
                message: "The support witness must fit in the registered carrier's rigid frame.");
            carrier = new NavigationCarrier2D(identity.StableId, identity.Lifetime, pose, localAnchor);
            _carrierHit = support;
        }

        return new NavigationMotionResult2D(accepted, NavigationSupportStatus2D.Found,
            new NavigationSupport2D(point, support.Hit.Normal, collider.Material.DynamicFriction, carrier));
    }

    /// <inheritdoc/>
    protected override bool TryReadCarrierPose(int stableId, long lifetime, out NavigationPose2D pose)
    {
        if (_carrierHit.IsCurrentLifetime && _carrierIdentity!(_carrierHit.Hit.Collider) is { } identity
            && ((identity.StableId == stableId) & (identity.Lifetime == lifetime)))
        {
            pose = ReadCarrierPose(_carrierHit.Hit.Collider);
            return true;
        }
        pose = default;
        return false;
    }

    /// <inheritdoc/>
    protected override void ClearHostSupport() => _carrierHit = default;

    /// <inheritdoc/>
    protected override bool TryCommitPhysicalProfileReplacement(NavigationAgentProfile2D replacement,
        Vector2d replacementCenter, Action publishNavigationState, out Exception? notificationException)
    {
        ValidateBodyAtNavigationPose();
        var definition = replacement.Mode == NavigationMode2D.TopDown
            ? ColliderShapeDefinition2D.Circle(replacement.Radius)
            : ColliderShapeDefinition2D.Capsule(replacement.Radius, replacement.AxisLength + replacement.Radius * (Fixed64)2);
        return Body.TryReconfigureCollider(definition, Vector2d.Zero, replacementCenter,
            out _, out notificationException, publishNavigationState) != ColliderReconfigurationStatus.Blocked;
    }

    private static NavigationPose2D ReadCarrierPose(LSCollider2D collider) => new(collider.Center, collider.Rotation);

    private void ValidateBodyAtNavigationPose()
    {
        ValidateBody(_navigation, Body, NavigationProfile);
        ChronicleGuard.ThrowIfTrue((Body.Position != Pose.Center) | (Body.Rotation != Pose.RotationRadians),
            message: "The accepted body pose must match the navigator.");
    }

    private static NavigationPose2D ValidateBinding(TrailblazerWorldContext context, SolidBody2D body,
        NavigationAgentProfile2D profile, Fixed64 supportProbeDistance)
    {
        ChronicleGuard.ThrowIfNull(context, nameof(context));
        ChronicleGuard.ThrowIfNull(body, nameof(body));
        ValidateBody(context, body, profile);
        ChronicleGuard.ThrowIfArgument((profile.Mode == NavigationMode2D.SideView) & (supportProbeDistance <= Fixed64.Zero),
            nameof(supportProbeDistance), "Side-view navigation requires a positive support probe distance.");
        return new NavigationPose2D(body.Position, body.Rotation);
    }

    private static void ValidateBody(TrailblazerWorldContext context, SolidBody2D body, NavigationAgentProfile2D profile)
    {
        var physics = body.Context;
        var collider = body.Collider;
        ChronicleGuard.ThrowIfArgument(!ReferenceEquals(context.World, physics.World) | physics.IsDisposed
            | (physics.Settings.RuntimeMode != PhysicsRuntimeMode.TwoD) | !physics.Physics2D.SimulatePhysics
            | !body.Active | !body.IsKinematic | (body.DynamicId < 0) | !collider.IsActive | (collider.Id < 0)
            | !ReferenceEquals(collider.Body, body), nameof(body),
            "An active native 2D kinematic body in the navigation world is required.");

        bool shapeMatches = profile.Mode == NavigationMode2D.TopDown
            ? collider is LSCircleCollider2D circle && circle.ScaledRadius == profile.Radius
            : collider is LSCapsuleCollider2D capsule && ((capsule.ScaledRadius == profile.Radius)
                & (capsule.AxisLength == profile.AxisLength) & (body.Rotation == Fixed64.Zero));
        var transform = body.Agent.Transform;
        // A root transform keeps target and posture writes exact through Gravitas acceptance.
        ChronicleGuard.ThrowIfArgument(!shapeMatches | (collider.LocalOffset != Vector2d.Zero) | (transform.Parent != null)
            | (transform.LocalScaleXZ != Vector2d.One)
            | !collider.TryGetLocalScale(out var scale) | (scale != Vector2d.One), nameof(body),
            "The centered native collider must match the profile on an unparented unit-planar-scale transform.");
    }
}
