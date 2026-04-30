using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class AStarTransitionFallbackTests : IDisposable
{
    public AStarTransitionFallbackTests()
    {
        TrailblazerWorldManager.Setup();
        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        TrailblazerWorldManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
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
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        request.AllowTraversalTransitions = true;

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
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
            source: TraversalTransitionAnchor.Solid(new Vector3d(-1, 0, 0)),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(0, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "water-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            new Vector3d(-1, 0, 0),
            new Vector3d(3, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        request.AllowTraversalTransitions = true;

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
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
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowUnwalkableEndpoints: true));
        request.AllowTraversalTransitions.Should().BeFalse();

        PathGuideFactory.RequestGuide(request, out AStarGuide? guide).Should().BeFalse();
        guide.Should().BeNull();
    }

    [Fact]
    public void AStarRequest_ShouldUseTransitionFallback_ForGeneratedClimbTopology()
    {
        RegisterAuthoredClimbRoute("AStarClimbFallback");

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(3, 0, 1),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowUnwalkableEndpoints: true));
        request.MaxClimbHeight = Fixed64.Zero;
        request.AllowTraversalTransitions = true;

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(1, 0, 0));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(1, 1, 0));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(1, 1, 1));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(2, 1, 1));
        guide.ActiveWaypoints.Should().Contain(waypoint => waypoint.Position == new Vector3d(2, 0, 1));
        guide.ActiveWaypoints[^1].Position.Should().Be(new Vector3d(3, 0, 1));
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "AStarFallbackWater");
    }

    private static void RegisterAuthoredClimbRoute(string chartName)
    {
        string[,,] map = new string[2, 4, 2];
        map[0, 0, 0] = "S";
        map[0, 1, 0] = "SC!";
        map[1, 1, 0] = "SC";
        map[1, 1, 1] = "SC";
        map[1, 2, 1] = "SC!";
        map[0, 2, 1] = "S";
        map[0, 3, 1] = "S";

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName,
            map,
            Vector3d.Zero,
            Fixed64.One).Build();
        PathManager.Register(buildResult).Should().BeTrue();
    }
}
