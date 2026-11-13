using System;
using System.Collections.Generic;
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

public sealed class GravitasNavigator2DGuidanceTests
{
    private const string MapId = "planar-guidance";
    private static readonly Fixed64 HostY = (Fixed64)7;
    private static readonly Guid FirstId = new("3833213d-8e04-458c-bc30-7b7ba60d045b");
    private static readonly Guid SecondId = new("3833213d-8e04-458c-bc30-7b7ba60d045c");
    private static readonly NavigationAreaPolicy Policy = new(new NavigationAreaPolicyKey("planar-ground", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });
    private static readonly NavigationWorkBudget Budget = new(8192, 128, 128, 512, 512, 128, 128, 128, 1024, 4096, 0);

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void FittedPortal_ShouldAcceptTheGuideAndBlockInwardMotion(PathAlgorithm algorithm)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishCorridor(world, navigation, physics);
        AddPortalWalls(physics);

        var tangent = new Vector2d(2, 1);
        using (var control = CreateActor(navigation, physics, SecondId, tangent, Fixed64.Half))
        {
            foreach (Vector2d inward in new[] { -Vector2d.Forward, Vector2d.Forward })
            {
                Step(navigation, physics, control, firstMove: inward).Should().Be(tangent + inward * Fixed64.Quarter);
                AssertAcceptedPose(control, tangent);
                control.Velocity.Should().Be(Vector2d.Zero);
                control.LastCommittedCell!.Value.Address.Should().Be(Cell(2));
            }
            control.Body.Deactivate();
        }

        using var navigator = CreateActor(navigation, physics, FirstId, new Vector2d(0, 1), Fixed64.Half);
        var entries = new List<NavigationCellAddress>();
        navigator.CommittedCellChanged += state => entries.Add(state!.Value.Address);
        navigator.ApplyGuidedTrekRequest(Cell(4), Policy.Key, algorithm, Budget).Should().Be(NavigationGuideStatus.Success);
        navigator.GuideCost.Should().Be((Fixed64)4);
        // The default cursor commits the origin once, then covers four units in quarter-unit steps.
        for (int step = 0; step <= 16; step++)
        {
            navigator.HasArrived.Should().BeFalse();
            Step(navigation, physics, navigator);
            AssertAcceptedPose(navigator, new Vector2d((Fixed64)step * Fixed64.Quarter, Fixed64.One));
        }
        navigator.HasArrived.Should().BeTrue();
        navigator.LastCommittedCell!.Value.Address.Should().Be(Cell(4));
        entries.Should().Equal(Cell(0), Cell(1), Cell(2), Cell(3), Cell(4));
    }

    [Fact]
    public void GuidedGroup_ShouldCommitDistinctFormationCellsThroughOneSharedResolve()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishCorridor(world, navigation, physics);
        AddPortalWalls(physics);
        using var first = CreateActor(navigation, physics, FirstId, new Vector2d(0, 1), Fixed64.Quarter);
        using var second = CreateActor(navigation, physics, SecondId, new Vector2d(2, 1), Fixed64.Quarter);
        const int groupId = 9;
        first.ApplyGuidedTrekRequest(Cell(3), Policy.Key, PathAlgorithm.AStar, Budget, groupId)
            .Should().Be(NavigationGuideStatus.Success);
        second.ApplyGuidedTrekRequest(Cell(3), Policy.Key, PathAlgorithm.FlowField, Budget, groupId)
            .Should().Be(NavigationGuideStatus.Success);
        first.MovementGroupId.Should().Be(groupId);
        second.MovementGroupId.Should().Be(groupId);

        for (int step = 0; step < 24 && (!first.HasArrived || !second.HasArrived); step++)
        {
            Step(navigation, physics, first, second);
            AssertAcceptedPose(first, new Vector2d(first.Body.Position.X, Fixed64.One));
            AssertAcceptedPose(second, new Vector2d(second.Body.Position.X, Fixed64.One));
            (second.Body.Position.X - first.Body.Position.X).Should().BeGreaterThanOrEqualTo(Fixed64.Half);
        }
        first.HasArrived.Should().BeTrue();
        second.HasArrived.Should().BeTrue();
        AssertAcceptedPose(first, new Vector2d(2, 1));
        AssertAcceptedPose(second, new Vector2d(4, 1));
        first.LastCommittedCell!.Value.Address.Should().Be(Cell(2));
        second.LastCommittedCell!.Value.Address.Should().Be(Cell(4));
        first.MovementGroupId.Should().Be(-1);
        second.MovementGroupId.Should().Be(-1);
    }

    private static void PublishCorridor(GridWorld world, TrailblazerWorldContext navigation, GravitasWorldContext physics)
    {
        navigation.SetFrameRate(16);
        physics.SetFrameRate(16);
        physics.Settings.RuntimeMode = PhysicsRuntimeMode.TwoD;
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 3));
        world.TryAddGrid(grid, out _).Should().BeTrue();
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var builder = new NavigationMapBuilder(MapId, grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= 4; x++)
            builder.AddCell(Cell(x).Index, cell);
        var policy = new NavigationAreaPolicyCommitOperation(Policy, 2, navigation.FrameCount + 1);
        navigation.Pathing.Admit(policy).Should().BeTrue();
        GravitasPlanarMapFixture.Publish(navigation, physics, builder.Build());
        policy.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    private static NavigationCellAddress Cell(int x) => new(MapId, new VoxelIndex(x + 1, 1, 2));

    private static void AddPortalWalls(GravitasWorldContext physics)
    {
        var lower = new LSAABBoxCollider2D(new Vector2d(3, 1));
        lower.InitializeWithNoBody(CreateAgent(physics, new Vector2d(2, 0)));
        var upper = new LSAABBoxCollider2D(new Vector2d(3, 1));
        upper.InitializeWithNoBody(CreateAgent(physics, new Vector2d(2, 2)));
    }

    private static GravitasNavigator2D CreateActor(TrailblazerWorldContext navigation, GravitasWorldContext physics,
        Guid id, Vector2d center, Fixed64 radius)
    {
        var body = new SolidBody2D(CreateAgent(physics, center), new LSCircleCollider2D(radius))
        {
            Mass = Fixed64.One,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
        };
        body.Initialize(center, motionType: PhysicsMobilityType.Kinematic);
        var profile = new NavigationAgentProfile2D(NavigationMode2D.TopDown, radius, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        var settings = new NavigationMotorSettings2D((Fixed64)4, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        return new GravitasNavigator2D(navigation, body, MapId, id, profile, settings);
    }

    private static GravitasFixtureMatterAgent CreateAgent(GravitasWorldContext physics, Vector2d center) =>
        new(physics, new FixedTransform(center.ToVector3d(HostY), FixedQuaternion.Identity, Vector3d.One));

    private static Vector2d Step(TrailblazerWorldContext navigation, GravitasWorldContext physics,
        GravitasNavigator2D first, GravitasNavigator2D? second = null, Vector2d? firstMove = null)
    {
        navigation.Simulate();
        physics.Simulate();
        first.PrepareFrameMotion(firstMove);
        second?.PrepareFrameMotion();
        Vector2d firstTarget = first.Body.Agent.Transform.WorldPositionXZ;
        physics.LateSimulate();
        first.CommitFrameMotion();
        second?.CommitFrameMotion();
        navigation.LateSimulate();
        return firstTarget;
    }

    private static void AssertAcceptedPose(GravitasNavigator2D navigator, Vector2d expected)
    {
        navigator.Body.Position.Should().Be(expected);
        navigator.Body.Collider.Center.Should().Be(expected);
        navigator.Pose.Center.Should().Be(expected);
        navigator.Pose.RotationRadians.Should().Be(navigator.Body.Rotation);
        navigator.Body.Agent.Transform.WorldPosition.Should().Be(expected.ToVector3d(HostY));
    }
}
