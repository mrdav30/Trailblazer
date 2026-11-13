using System;
using System.Collections.Generic;
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
using static Trailblazer.Gravitas.Tests.Support.GravitasPlanarPhysicsFixture;

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
    public void SideView_ShouldLandOnTheSlopeAndFollowItsTangent()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics, x => (x, (Fixed64)x - Fixed64.Half, (Fixed64)x + Fixed64.Half));
        var slope = new LSPolygonCollider2D(new Vector2d(-8, -8), new Vector2d(8, -8), new Vector2d(8, 8));
        slope.InitializeWithNoBody(CreateAgent(physics, Vector2d.Zero));
        Vector2d start = Vector2d.Forward * Fixed64.Two;
        SolidBody2D body = CreateCapsule(physics, start, Standing);
        using var navigator = CreateNavigator(navigation, body, Standing, gravity: (Fixed64)256);

        navigator.RefreshSupport();
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.NoSupport);
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);

        // The upper edge is z = x. The lower cap touches one radius above this line.
        var diagonal = Fixed64.FromRaw(3037000500);
        var accepted = new Vector2d(Fixed64.Zero, Fixed64.Half + diagonal);
        var witness = new Vector2d(diagonal * Fixed64.Half, diagonal * Fixed64.Half);
        var normal = new Vector2d(-diagonal, diagonal);
        AssertAcceptedPose(navigator, accepted);
        navigator.Velocity.Should().Be((accepted - start) * (Fixed64)16);
        navigator.Support!.Value.Point.Should().Be(witness);
        navigator.Support.Value.Normal.Should().Be(normal);
        navigator.IsGrounded.Should().BeTrue();

        AdvanceRider(navigation, physics, navigator, Vector2d.Right * Fixed64.Quarter);

        var tangent = new Vector2d(diagonal, diagonal) * Fixed64.Quarter;
        AssertAcceptedPose(navigator, accepted + tangent);
        navigator.Velocity.Should().Be(tangent * (Fixed64)16);
        navigator.Support!.Value.Normal.Should().Be(normal);
        navigator.IsGrounded.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(new NavigationCellAddress(MapId, new VoxelIndex(4, 0, 5)));
    }

    [Fact]
    public void SideView_ShouldLoseSupportAtTheLedgeAndCommitTheClippedLanding()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        Fixed64 lowerTop = -Fixed64.FromFraction(3, 8);
        PublishFloorMap(world, navigation, physics, x => x <= 0 ? (0, Fixed64.Half, Fixed64.Half) : (0, lowerTop, lowerTop));
        AddBox(physics, new Vector2d(-2, 0), new Vector2d(5, 1));
        AddBox(physics, new Vector2d((Fixed64)2 + Fixed64.Half, lowerTop - Fixed64.Half), new Vector2d(4, 1));
        var start = new Vector2d(Fixed64.Zero, Fixed64.One + Fixed64.Half);
        SolidBody2D body = CreateCapsule(physics, start, Standing);
        using var navigator = CreateNavigator(navigation, body, Standing, gravity: (Fixed64)128);

        navigator.RefreshSupport();
        navigator.IsGrounded.Should().BeTrue();
        navigator.Support!.Value.Point.Should().Be(Vector2d.Forward * Fixed64.Half);
        AdvanceRider(navigation, physics, navigator, Vector2d.Right);

        Vector2d ledgeExit = start + Vector2d.Right;
        AssertAcceptedPose(navigator, ledgeExit);
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.NoSupport);
        navigator.IsGrounded.Should().BeFalse();
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);

        Vector2d falling = ledgeExit - Vector2d.Forward * Fixed64.Half;
        AssertAcceptedPose(navigator, falling);
        navigator.Velocity.Should().Be(-Vector2d.Forward * (Fixed64)8);
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.NoSupport);
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);

        var landed = new Vector2d(Fixed64.One, lowerTop + Fixed64.One);
        AssertAcceptedPose(navigator, landed);
        navigator.Velocity.Should().Be(-Vector2d.Forward * (Fixed64)6);
        navigator.Support!.Value.Point.Should().Be(new Vector2d(Fixed64.One, lowerTop));
        navigator.IsGrounded.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(new NavigationCellAddress(MapId, new VoxelIndex(5, 0, 5)));

        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        AssertAcceptedPose(navigator, landed);
        navigator.Velocity.Should().Be(Vector2d.Zero);
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

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void SideView_ShouldReplayTheRotatingCarrierFromABodyFirstCheckpoint(bool memoryPack)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics, _ => (1, Fixed64.One, Fixed64.One));
        var square = new LSPolygonCollider2D(new Vector2d(-1, -1), new Vector2d(1, -1),
            new Vector2d(1, 1), new Vector2d(-1, 1));
        var carrier = new SolidBody2D(CreateAgent(physics, Vector2d.Zero), square);
        carrier.Initialize(Vector2d.Zero, motionType: PhysicsMobilityType.Static);
        var height = Fixed64.One + Fixed64.Half;
        var start = new Vector2d(Fixed64.One, height);
        SolidBody2D body = CreateCapsule(physics, start, Compact);
        long lifetime = 1;
        using var navigator = CreateNavigator(navigation, body, Compact,
            collider => ReferenceEquals(collider, square) ? (19, lifetime) : null);
        navigator.RefreshSupport();
        navigator.IsGrounded.Should().BeTrue();
        var corner = new Vector2d(1, 1);
        navigator.Support!.Value.Point.Should().Be(corner);
        navigator.Support.Value.Carrier!.Value.LocalAnchor.Should().Be(corner);

        // This square occupies the same space after rotation, but the tracked corner has moved.
        carrier.SetRotation(Fixed64.HalfPi);
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);

        var accepted = new Vector2d(-Fixed64.One, height);
        AssertAcceptedPose(navigator, accepted);
        navigator.Velocity.Should().Be(-Vector2d.Right * (Fixed64)32);
        navigator.IsGrounded.Should().BeTrue();
        navigator.Support!.Value.Point.Should().Be(new Vector2d(-1, 1));
        NavigationCarrier2D support = navigator.Support.Value.Carrier!.Value;
        support.LocalAnchor.Should().Be(corner);
        support.Pose.Center.Should().Be(Vector2d.Zero);
        support.Pose.RotationRadians.Should().Be(Fixed64.HalfPi);
        navigator.LastCommittedCell!.Value.Address.Should().Be(CarrierCell(-1));

        object bodyRecord = GravitasRecordSnapshot.Save(body, memoryPack);
        object navigationRecord = GravitasRecordSnapshot.Save(navigator, memoryPack);
        var entries = new List<NavigationCellAddress?>();
        navigator.CommittedCellChanged += state => entries.Add(state?.Address);
        Replay();
        var firstEntries = entries.ToArray();
        firstEntries.Should().Equal(CarrierCell(1), CarrierCell(-1), CarrierCell(1));

        carrier.SetRotation(Fixed64.HalfPi);
        GravitasRecordSnapshot.Load(body, bodyRecord);
        GravitasRecordSnapshot.Load(navigator, navigationRecord);
        lifetime++;
        navigator.RefreshSupport();
        var restored = navigator.Support!.Value.Carrier!.Value;
        restored.Lifetime.Should().Be(lifetime);
        restored.LocalAnchor.Should().Be(corner);
        entries.Clear();
        Replay();
        entries.Should().Equal(firstEntries);

        void Replay()
        {
            Fixed64 previousX = -Fixed64.One;
            for (int frame = 0; frame < 32; frame++)
            {
                bool rotated = frame / 8 % 2 == 0;
                carrier.SetRotation(rotated ? Fixed64.HalfPi : Fixed64.Zero);
                int x = rotated ? -1 : 1;
                var expected = new Vector2d((Fixed64)x, height);
                AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
                AssertAcceptedPose(navigator, expected);
                navigator.Velocity.Should().Be(new Vector2d(((Fixed64)x - previousX) * (Fixed64)16, Fixed64.Zero));
                navigator.IsGrounded.Should().BeTrue();
                var sample = navigator.Support!.Value.Carrier!.Value;
                sample.LocalAnchor.Should().Be(corner);
                sample.Pose.RotationRadians.Should().Be(rotated ? Fixed64.HalfPi : Fixed64.Zero);
                navigator.LastCommittedCell!.Value.Address.Should().Be(CarrierCell(x));
                previousX = (Fixed64)x;
            }
        }

        static NavigationCellAddress CarrierCell(int x) => new(MapId, new VoxelIndex(x + 4, 0, 5));
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

    private static void PublishFloorMap(GridWorld world, TrailblazerWorldContext navigation, GravitasWorldContext physics,
        Func<int, (int Z, Fixed64 Left, Fixed64 Right)>? surface = null)
    {
        Configure(navigation, physics);
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
            var span = surface?.Invoke(x) ?? (0, Fixed64.Half, Fixed64.Half);
            var index = new VoxelIndex(x + 4, 0, span.Z + 4);
            builder.AddCell(index, floor).AddPlanarSupport(index,
                new Vector2d((Fixed64)x - Fixed64.Half, span.Left),
                new Vector2d((Fixed64)x + Fixed64.Half, span.Right));
        }
        GravitasPlanarMapFixture.Publish(navigation, physics, builder.Build());
    }

    private static LSAABBoxCollider2D AddBox(GravitasWorldContext physics, Vector2d center, Vector2d size) =>
        GravitasPlanarPhysicsFixture.AddBox(physics, center, size, HostY);

    private static SolidBody2D CreateCapsule(GravitasWorldContext physics, Vector2d center, NavigationAgentProfile2D profile) =>
        CreateKinematic(physics, center, HostY,
            new LSCapsuleCollider2D(profile.Radius, profile.AxisLength + profile.Radius * (Fixed64)2)
            {
                Material = PhysicsMaterial.Frictionless,
            });

    private static GravitasFixtureMatterAgent CreateAgent(GravitasWorldContext physics, Vector2d center) =>
        GravitasPlanarPhysicsFixture.CreateAgent(physics, center, HostY);

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

    private static void AssertAcceptedPose(GravitasNavigator2D navigator, Vector2d expected)
    {
        navigator.Pose.Center.Should().Be(expected);
        navigator.Pose.RotationRadians.Should().Be(Fixed64.Zero);
        navigator.Body.Position.Should().Be(expected);
        navigator.Body.Collider.Center.Should().Be(expected);
        navigator.Body.Agent.Transform.WorldPosition.Should().Be(expected.ToVector3d(HostY));
    }

    private static GravitasNavigator2D CreateNavigator(TrailblazerWorldContext context, SolidBody2D body,
        NavigationAgentProfile2D profile, Func<LSCollider2D, (int StableId, long Lifetime)?>? carrierIdentity = null,
        Fixed64? gravity = null) =>
        new(context, body, MapId,
            new Guid("3040c3be-0b7c-4600-91d2-f63c25bc189f"), profile,
            new NavigationMotorSettings2D((Fixed64)16, Fixed64.Zero, gravity ?? (Fixed64)16, Fixed64.Zero),
            supportLayers: PhysicsLayerMask.All, supportProbeDistance: Fixed64.Quarter, carrierIdentity: carrierIdentity);

    private static NavigationAgentProfile2D CreateProfile(Fixed64 axisLength) => new(NavigationMode2D.SideView,
        Fixed64.Half, axisLength, Fixed64.Zero, Fixed64.Zero, Fixed64.Half,
        TraversalMedia.Solid, TraversalCapability.None);
}
