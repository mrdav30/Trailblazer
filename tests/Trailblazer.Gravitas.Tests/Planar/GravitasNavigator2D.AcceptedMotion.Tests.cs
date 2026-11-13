using System;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator2DAcceptedMotionTests
{
    private const string MapId = "planar-wall";
    private static readonly Fixed64 HostY = (Fixed64)7;
    private static readonly NavigationAgentProfile2D CircleProfile = new(NavigationMode2D.TopDown,
        Fixed64.Half, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
        TraversalMedia.Solid, TraversalCapability.None);

    [Fact]
    public void TopDownWall_ShouldCommitOnlyThePoseAcceptedByGravitas()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);

        var wall = new LSAABBoxCollider2D(new Vector2d(Fixed64.One, (Fixed64)4));
        wall.InitializeWithNoBody(new GravitasFixtureMatterAgent(physics,
            new FixedTransform(new Vector3d(Fixed64.Zero, (Fixed64)99, Fixed64.Zero),
                FixedQuaternion.Identity, Vector3d.One)));
        Vector2d start = -Vector2d.Right * (Fixed64)4;
        SolidBody2D body = CreateCircle(physics, start);
        using var navigator = CreateNavigator(navigation, body,
            new Guid("d42827f4-46e6-41d6-86ba-145eeec65a41"), (Fixed64)128);

        navigation.Simulate();
        physics.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);

        body.Agent.Transform.WorldPosition.Should().Be(new Vector3d((Fixed64)4, HostY, Fixed64.Zero));
        body.Position.Should().Be(start);
        navigator.Pose.Center.Should().Be(start);

        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();

        Vector2d accepted = -Vector2d.Right;
        body.Position.Should().Be(accepted);
        body.Collider.Center.Should().Be(accepted);
        body.Agent.Transform.WorldPosition.Should().Be(new Vector3d(-Fixed64.One, HostY, Fixed64.Zero));
        navigator.Pose.Center.Should().Be(accepted);
        navigator.Pose.RotationRadians.Should().Be(body.Rotation);
        navigator.Velocity.Should().Be(Vector2d.Right * (Fixed64)48);
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.NotApplicable);
        navigator.LastCommittedCell!.Value.Address.Should().Be(new NavigationCellAddress(MapId, new VoxelIndex(15, 0, 16)));
    }

    [Fact]
    public void TopDownBatch_ShouldPrepareAllActorsBeforeOneSharedResolve()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);

        Vector2d firstStart = new(-4, -4);
        Vector2d secondStart = new(4, 4);
        SolidBody2D firstBody = CreateCircle(physics, firstStart);
        SolidBody2D secondBody = CreateCircle(physics, secondStart);
        using var first = CreateNavigator(navigation, firstBody,
            new Guid("d42827f4-46e6-41d6-86ba-145eeec65a41"), (Fixed64)16);
        using var second = CreateNavigator(navigation, secondBody,
            new Guid("d42827f4-46e6-41d6-86ba-145eeec65a42"), (Fixed64)16);

        navigation.Simulate();
        physics.Simulate();
        first.PrepareFrameMotion(Vector2d.Right);
        second.PrepareFrameMotion(Vector2d.Forward);
        firstBody.Position.Should().Be(firstStart);
        secondBody.Position.Should().Be(secondStart);

        physics.LateSimulate();
        firstBody.Position.Should().Be(firstStart + Vector2d.Right);
        secondBody.Position.Should().Be(secondStart + Vector2d.Forward);
        first.Pose.Center.Should().Be(firstStart);
        second.Pose.Center.Should().Be(secondStart);

        first.CommitFrameMotion();
        second.CommitFrameMotion();
        navigation.LateSimulate();
        first.Pose.Center.Should().Be(firstBody.Position);
        second.Pose.Center.Should().Be(secondBody.Position);
        first.Pose.RotationRadians.Should().Be(firstBody.Rotation);
        second.Pose.RotationRadians.Should().Be(secondBody.Rotation);
        second.Pose.RotationRadians.Should().BeGreaterThan(Fixed64.Zero);
        firstBody.Agent.Transform.WorldPosition.Y.Should().Be(HostY);
        secondBody.Agent.Transform.WorldPosition.Y.Should().Be(HostY);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public void TopDownCommit_ShouldRejectMissingRepeatedOrStalePhysicsPass(int passes, bool advancePhysics)
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        SolidBody2D body = CreateCircle(physics, Vector2d.Zero);
        using var navigator = CreateNavigator(navigation, body,
            new Guid("d42827f4-46e6-41d6-86ba-145eeec65a41"), (Fixed64)16);

        navigation.Simulate();
        physics.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);
        for (int i = 0; i < passes; i++)
            physics.LateSimulate();
        if (advancePhysics)
            physics.Simulate();

        Action commit = navigator.CommitFrameMotion;
        commit.Should().Throw<InvalidOperationException>();
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
        navigator.Velocity.Should().Be(Vector2d.Zero);
        navigator.LastCommittedCell.Should().BeNull();
        commit.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TopDownPosture_ShouldResizeTheCenteredCircleInPlace()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        var start = new Vector2d(-3, 2);
        SolidBody2D body = CreateCircle(physics, start);
        using var navigator = CreateNavigator(navigation, body,
            new Guid("d42827f4-46e6-41d6-86ba-145eeec65a41"), (Fixed64)16);
        LSCollider2D collider = body.Collider;
        var smaller = new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.Quarter,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);

        navigator.TryReplaceNavigationProfile(smaller).Should().Be(NavigationProfileReplacementStatus.Applied);
        navigator.NavigationProfile.Should().Be(smaller);
        body.Collider.Should().BeSameAs(collider);
        body.Collider.Should().BeOfType<LSCircleCollider2D>().Which.Radius.Should().Be(smaller.Radius);
        body.Position.Should().Be(start);
        navigator.Pose.Center.Should().Be(start);
        body.Agent.Transform.WorldPosition.Should().Be(start.ToVector3d(HostY));
    }

    [Fact]
    public void Binding_ShouldRejectParentedTransformsAndMismatchedColliderFamilies()
    {
        using var world = new GridWorld();
        using TrailblazerWorldContext navigation = TrailblazerWorldContext.Attach(world);
        using GravitasWorldContext physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        SolidBody2D body = CreateCircle(physics, Vector2d.Zero);
        var id = new Guid("d42827f4-46e6-41d6-86ba-145eeec65a41");
        var settings = new NavigationMotorSettings2D(Fixed64.One, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var sideView = new NavigationAgentProfile2D(NavigationMode2D.SideView, Fixed64.Half,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Half, TraversalMedia.Solid, TraversalCapability.None);

        Action bindCapsule = () => new GravitasNavigator2D(navigation, body, MapId, id, sideView, settings,
            supportProbeDistance: Fixed64.Quarter);
        bindCapsule.Should().Throw<ArgumentException>().WithParameterName("body");

        body.Agent.Transform.SetParentKeepingLocal(new FixedTransform(Vector3d.Zero,
            FixedQuaternion.Identity, Vector3d.One));
        Action bindCircle = () => CreateNavigator(navigation, body, id, Fixed64.One);
        bindCircle.Should().Throw<ArgumentException>().WithParameterName("body");
        body.Agent.Transform.SetParentKeepingLocal(null);
        using var navigator = CreateNavigator(navigation, body, id, Fixed64.One);
        navigator.Body.Should().BeSameAs(body);
    }

    private static void PublishPlanarMap(GridWorld world, TrailblazerWorldContext navigation, GravitasWorldContext physics)
    {
        navigation.SetFrameRate(16);
        physics.SetFrameRate(16);
        physics.Settings.RuntimeMode = PhysicsRuntimeMode.TwoD;
        var grid = new GridConfiguration(new Vector3d(-16, 0, -16), new Vector3d(16, 0, 16));
        world.TryAddGrid(grid, out _).Should().BeTrue();
        var open = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default, Fixed64.Zero,
            Fixed64.One, Fixed64.One);
        var builder = new NavigationMapBuilder(MapId, grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(open);
        GravitasPlanarMapFixture.Publish(navigation, physics, builder.Build());
    }

    private static SolidBody2D CreateCircle(GravitasWorldContext context, Vector2d center)
    {
        var transform = new FixedTransform(new Vector3d(center.X, HostY, center.Y),
            FixedQuaternion.Identity, Vector3d.One);
        var body = new SolidBody2D(new GravitasFixtureMatterAgent(context, transform),
            new LSCircleCollider2D(CircleProfile.Radius))
        {
            Mass = Fixed64.One,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
        };
        body.Initialize(center, motionType: PhysicsMobilityType.Kinematic);
        return body;
    }

    private static GravitasNavigator2D CreateNavigator(TrailblazerWorldContext context, SolidBody2D body, Guid id,
        Fixed64 speed) => new(context, body, MapId, id, CircleProfile,
            new NavigationMotorSettings2D(speed, Fixed64.Pi * (Fixed64)16, Fixed64.Zero, Fixed64.Zero));
}
