using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Trailblazer.Tests;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class HybridPathRequestTests : IDisposable
{
    public HybridPathRequestTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        GlobalGridManager.TryAddGrid(config, out _);
        VolumeTraversalRules.SetWaterVoxelPartition<TestWaterPartition>();
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void HybridPathRequest_Should_PlanJumpLink_BetweenDisconnectedCharts()
    {
        PathTestFactory.RegisterSingleWalkablePoint("JumpStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("JumpEnd", new Vector3d(2, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "jump-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(2, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        HybridPathRequest request = HybridPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean);

        request.Should().NotBeNull();
        request.RoutePlan.Should().NotBeNull();
        request.RoutePlan.DirectedTransitions.Should().ContainSingle();
        request.RoutePlan.DirectedTransitions[0].Id.Should().Be("jump-gap");
        request.RoutePlan.DirectedTransitions[0].Type.Should().Be(TraversalTransitionType.Jump);

        PathGuideFactory.RequestGuide(request, out HybridGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
        guide.ActiveWaypoints.Should().HaveCount(2);
        guide.ActiveWaypoints[0].Position.Should().Be(Vector3d.Zero);
        guide.ActiveWaypoints[1].Position.Should().Be(new Vector3d(2, 0, 0));
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
        guide.CurrentWaypointIndex.Should().Be(1);
    }

    [Fact]
    public void HybridPathRequest_Should_PlanChartToWaterToChartRoute()
    {
        PathTestFactory.RegisterSingleWalkablePoint("WaterStart", new Vector3d(-1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("WaterEnd", new Vector3d(3, 0, 0));

        AddWater(new Vector3d(0, 0, 0));
        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "water-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Chart(new Vector3d(-1, 0, 0)),
            destination: TraversalTransitionAnchor.Volume(new Vector3d(0, 0, 0), VolumeTraversalMode.Water),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "water-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Volume(new Vector3d(2, 0, 0), VolumeTraversalMode.Water),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(3, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        HybridPathRequest request = HybridPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(3, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean);

        request.Should().NotBeNull();
        request.RoutePlan.Should().NotBeNull();
        request.RoutePlan.DirectedTransitions.Should().HaveCount(2);
        request.RoutePlan.DirectedTransitions[0].Id.Should().Be("water-entry");
        request.RoutePlan.DirectedTransitions[1].Id.Should().Be("water-exit");

        PathGuideFactory.RequestGuide(request, out HybridGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
        guide.ActiveWaypoints.Should().NotBeEmpty();
        guide.ActiveWaypoints[0].Position.Should().Be(new Vector3d(-1, 0, 0));
        guide.ActiveWaypoints[^1].Position.Should().Be(new Vector3d(3, 0, 0));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(0, 0, 0));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(2, 0, 0));
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
        guide.CurrentWaypointIndex.Should().Be(1);
    }

    private static void AddWater(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        voxel.TryAddPartition(new TestWaterPartition()).Should().BeTrue();
    }
}
