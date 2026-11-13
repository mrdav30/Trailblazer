using System;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Materials;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator2DSupportTests
{
    private const string MapId = "side-view-floor";
    private static readonly Fixed64 HostY = (Fixed64)7;
    private static readonly NavigationAgentProfile2D Standing = CreateProfile(Fixed64.One);
    private static readonly NavigationAgentProfile2D Compact = CreateProfile(Fixed64.Zero);

    [Fact]
    public void SideView_ShouldReportUnavailableForAnUnrepresentableSupportProbe()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        Vector2d start = -Vector2d.Forward;
        SolidBody2D body = CreateCapsule(physics, start, Compact);
        using var navigator = new GravitasNavigator2D(navigation, body, MapId,
            new Guid("3040c3be-0b7c-4600-91d2-f63c25bc189f"), Compact,
            new NavigationMotorSettings2D((Fixed64)16, Fixed64.Zero, (Fixed64)16, Fixed64.Zero),
            supportProbeDistance: Fixed64.MaxValue);

        navigator.RefreshSupport();

        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.Unavailable);
        navigator.Support.Should().BeNull();
        navigator.IsGrounded.Should().BeFalse();
        navigator.Pose.Center.Should().Be(start);
    }

    [Fact]
    public void SideView_ShouldCommitTheRealFloorWitnessAndClearItAfterRemoval()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        LSAABBoxCollider2D floor = AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        var start = new Vector2d(Fixed64.Zero, Fixed64.One + Fixed64.Half);
        SolidBody2D body = CreateCapsule(physics, start, Standing);
        using var navigator = CreateNavigator(navigation, body, Standing);

        navigator.RefreshSupport();
        navigator.IsGrounded.Should().BeTrue();
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.Found);
        navigator.Support!.Value.Point.Should().Be(Vector2d.Forward * Fixed64.Half);
        navigator.Support.Value.Normal.Should().Be(Vector2d.Forward);
        navigator.Support.Value.Friction.Should().Be(floor.Material.DynamicFriction);

        AdvanceRider(navigation, physics, navigator, Vector2d.Right);

        Vector2d accepted = start + Vector2d.Right;
        body.Position.Should().Be(accepted);
        navigator.Pose.Center.Should().Be(accepted);
        navigator.Pose.RotationRadians.Should().Be(Fixed64.Zero);
        navigator.Support!.Value.Point.Should().Be(new Vector2d(Fixed64.One, Fixed64.Half));
        body.Agent.Transform.WorldPosition.Should().Be(new Vector3d(accepted.X, HostY, accepted.Y));

        floor.Deactivate();
        navigator.RefreshSupport();
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.NoSupport);
        navigator.IsGrounded.Should().BeFalse();
        navigator.Support.Should().BeNull();
    }

    [Fact]
    public void SideView_ShouldCarryTheSampledAnchorOnceAndRebindAChangedCarrierIdentity()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        LSAABBoxCollider2D floor = AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        var start = new Vector2d(Fixed64.Quarter, Fixed64.One + Fixed64.Half);
        SolidBody2D body = CreateCapsule(physics, start, Standing);
        const int carrierId = 19;
        long lifetime = 1;
        bool registeredAsCarrier = true;
        using var navigator = CreateNavigator(navigation, body, Standing,
            collider => registeredAsCarrier && ReferenceEquals(collider, floor) ? (carrierId, lifetime) : null);

        navigator.RefreshSupport();
        var localAnchor = new Vector2d(Fixed64.Quarter, Fixed64.Half);
        NavigationCarrier2D carrier = navigator.Support!.Value.Carrier!.Value;
        carrier.StableId.Should().Be(carrierId);
        carrier.Lifetime.Should().Be(lifetime);
        carrier.Pose.Center.Should().Be(Vector2d.Zero);
        carrier.LocalAnchor.Should().Be(localAnchor);

        MoveFloor(Fixed64.One);
        AdvanceRider(navigation, physics, navigator);

        Vector2d carried = start + Vector2d.Right;
        body.Position.Should().Be(carried);
        navigator.Pose.Center.Should().Be(carried);
        navigator.Support!.Value.Point.Should().Be(localAnchor + Vector2d.Right);
        carrier = navigator.Support.Value.Carrier!.Value;
        carrier.Pose.Center.Should().Be(Vector2d.Right);
        carrier.LocalAnchor.Should().Be(localAnchor);

        AdvanceRider(navigation, physics, navigator);
        navigator.Pose.Center.Should().Be(carried);
        navigator.Velocity.Should().Be(Vector2d.Zero);

        lifetime++;
        MoveFloor((Fixed64)2);
        AdvanceRider(navigation, physics, navigator);
        navigator.Pose.Center.Should().Be(carried);
        body.Position.Should().Be(carried);
        navigator.IsGrounded.Should().BeTrue();
        carrier = navigator.Support!.Value.Carrier!.Value;
        carrier.StableId.Should().Be(carrierId);
        carrier.Lifetime.Should().Be(lifetime);
        carrier.LocalAnchor.Should().Be(localAnchor - Vector2d.Right);

        registeredAsCarrier = false;
        MoveFloor((Fixed64)3);
        AdvanceRider(navigation, physics, navigator);
        navigator.Pose.Center.Should().Be(carried);
        navigator.IsGrounded.Should().BeTrue();
        navigator.Support!.Value.Carrier.Should().BeNull();

        void MoveFloor(Fixed64 x)
        {
            floor.Agent.Transform.TrySetWorldPosition(new Vector3d(x, HostY, Fixed64.Zero)).Should().BeTrue();
            floor.Simulate();
            floor.Center.Should().Be(new Vector2d(x, Fixed64.Zero));
        }
    }

    [Fact]
    public void SideView_PostureShouldRejectTheRealCeilingAndExpandAtTheSameFootAfterRemoval()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        LSAABBoxCollider2D ceiling = AddBox(physics, new Vector2d(Fixed64.Zero, (Fixed64)2),
            new Vector2d((Fixed64)4, Fixed64.Half));
        Vector2d start = Vector2d.Forward;
        SolidBody2D body = CreateCapsule(physics, start, Compact);
        Action bindCircle = () => new GravitasNavigator2D(navigation, body, MapId,
            new Guid("3040c3be-0b7c-4600-91d2-f63c25bc189f"),
            new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.Half,
                Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None),
            new NavigationMotorSettings2D(Fixed64.One, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero));
        bindCircle.Should().Throw<ArgumentException>().WithParameterName("body");
        using var navigator = CreateNavigator(navigation, body, Compact);
        LSCollider2D originalCollider = body.Collider;
        navigator.RefreshSupport();
        Vector2d foot = navigator.Support!.Value.Point;

        navigator.TryReplaceNavigationProfile(Standing).Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        navigator.NavigationProfile.Should().Be(Compact);
        navigator.Pose.Center.Should().Be(start);
        navigator.Support!.Value.Point.Should().Be(foot);
        body.Position.Should().Be(start);
        body.Collider.Should().BeSameAs(originalCollider);

        ceiling.Deactivate();
        navigator.TryReplaceNavigationProfile(Standing).Should().Be(NavigationProfileReplacementStatus.Applied);
        navigator.NavigationProfile.Should().Be(Standing);
        Vector2d accepted = start + Vector2d.Forward * Fixed64.Half;
        navigator.Pose.Center.Should().Be(accepted);
        body.Position.Should().Be(accepted);
        body.Agent.Transform.WorldPosition.Should().Be(new Vector3d(accepted.X, HostY, accepted.Y));
        body.Collider.Should().BeSameAs(originalCollider);
        body.Collider.Should().BeOfType<LSCapsuleCollider2D>().Which.Height.Should().Be((Fixed64)2);
        body.Collider.Material.Should().Be(PhysicsMaterial.Frictionless);
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.Unavailable);

        navigator.RefreshSupport();
        navigator.Support!.Value.Point.Should().Be(foot);
        navigator.IsGrounded.Should().BeTrue();
    }

    private static void PublishFloorMap(GridWorld world, TrailblazerWorldContext navigation, GravitasWorldContext physics)
    {
        navigation.SetFrameRate(16);
        physics.SetFrameRate(16);
        physics.Settings.RuntimeMode = PhysicsRuntimeMode.TwoD;
        var grid = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        world.TryAddGrid(grid, out _).Should().BeTrue();
        var floor = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, (Fixed64)3);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var builder = new NavigationMapBuilder(MapId, grid)
            .SetPlanarLayer(NavigationMode2D.SideView, 0, Fixed64.Zero).SetDefaultCell(air);
        for (int x = -4; x <= 4; x++)
        {
            var index = new VoxelIndex(x + 4, 0, 4);
            var left = new Vector2d((Fixed64)x - Fixed64.Half, Fixed64.Half);
            builder.AddCell(index, floor).AddPlanarSupport(index, left, left + Vector2d.Right);
        }
        GravitasPlanarMapFixture.Publish(navigation, physics, builder.Build());
    }

    private static LSAABBoxCollider2D AddBox(GravitasWorldContext physics, Vector2d center, Vector2d size)
    {
        var collider = new LSAABBoxCollider2D(size);
        collider.InitializeWithNoBody(CreateAgent(physics, center));
        return collider;
    }

    private static SolidBody2D CreateCapsule(GravitasWorldContext physics, Vector2d center, NavigationAgentProfile2D profile)
    {
        var body = new SolidBody2D(CreateAgent(physics, center),
            new LSCapsuleCollider2D(profile.Radius, profile.AxisLength + profile.Radius * (Fixed64)2)
            {
                Material = PhysicsMaterial.Frictionless,
            })
        {
            Mass = Fixed64.One,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
        };
        body.Initialize(center, motionType: PhysicsMobilityType.Kinematic);
        return body;
    }

    private static GravitasFixtureMatterAgent CreateAgent(GravitasWorldContext physics, Vector2d center) => new(physics,
        new FixedTransform(new Vector3d(center.X, HostY, center.Y), FixedQuaternion.Identity, Vector3d.One));

    private static void AdvanceRider(TrailblazerWorldContext navigation, GravitasWorldContext physics,
        GravitasNavigator2D navigator, Vector2d? move = null)
    {
        navigation.Simulate();
        physics.Simulate();
        navigator.PrepareFrameMotion(move);
        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();
    }

    private static GravitasNavigator2D CreateNavigator(TrailblazerWorldContext context, SolidBody2D body,
        NavigationAgentProfile2D profile, Func<LSCollider2D, (int StableId, long Lifetime)?>? carrierIdentity = null) =>
        new(context, body, MapId,
            new Guid("3040c3be-0b7c-4600-91d2-f63c25bc189f"), profile,
            new NavigationMotorSettings2D((Fixed64)16, Fixed64.Zero, (Fixed64)16, Fixed64.Zero),
            supportLayers: PhysicsLayerMask.All, supportProbeDistance: Fixed64.Quarter, carrierIdentity: carrierIdentity);

    private static NavigationAgentProfile2D CreateProfile(Fixed64 axisLength) => new(NavigationMode2D.SideView,
        Fixed64.Half, axisLength, Fixed64.Zero, Fixed64.Zero, Fixed64.Half,
        TraversalMedia.Solid, TraversalCapability.None);
}
