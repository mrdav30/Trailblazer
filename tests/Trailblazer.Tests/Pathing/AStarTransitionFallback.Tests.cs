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
public class AStarTransitionFallbackTests : IDisposable
{
    public AStarTransitionFallbackTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AStarRequest_ShouldUseTransitionFallback_ForDisconnectedJumpLink()
    {
        PathTestFactory.RegisterSingleWalkablePoint("JumpStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("JumpEnd", new Vector3d(2, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "jump-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(2, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean);
        request.Should().NotBeNull();
        request.AllowTraversalTransitions = true;

        PathGuideFactory.RequestGuide(request, out AStarGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
        guide.ActiveWaypoints.Should().HaveCount(2);
        guide.ActiveWaypoints[0].Position.Should().Be(Vector3d.Zero);
        guide.ActiveWaypoints[1].Position.Should().Be(new Vector3d(2, 0, 0));
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void AStarRequest_ShouldUseTransitionFallback_ForChartToWaterToChartRoute()
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
            destination: TraversalTransitionAnchor.WaterVolume(new Vector3d(0, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "water-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.WaterVolume(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(3, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        AStarPathRequest request = AStarPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(3, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean);
        request.Should().NotBeNull();
        request.AllowTraversalTransitions = true;

        PathGuideFactory.RequestGuide(request, out AStarGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
        guide.ActiveWaypoints.Should().NotBeEmpty();
        guide.ActiveWaypoints[0].Position.Should().Be(new Vector3d(-1, 0, 0));
        guide.ActiveWaypoints[^1].Position.Should().Be(new Vector3d(3, 0, 0));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(0, 0, 0));
        guide.ActiveWaypoints.Should().Contain(waypoint =>
            waypoint.Position.x > Fixed64.Zero
            && waypoint.Position.x < (Fixed64)3);
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void AStarRequest_ShouldNotUseTransitionFallback_WhenOnlyAllowUnwalkableIsEnabled()
    {
        PathTestFactory.RegisterSingleWalkablePoint("NoFallbackStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("NoFallbackEnd", new Vector3d(2, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "no-fallback-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(2, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowUnwalkableEndNode: true);
        request.Should().NotBeNull();
        request.AllowTraversalTransitions.Should().BeFalse();

        PathGuideFactory.RequestGuide(request, out AStarGuide guide).Should().BeFalse();
        guide.Should().BeNull();
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, VolumeTraversalMode.Water, "AStarFallbackWater");
    }
}
