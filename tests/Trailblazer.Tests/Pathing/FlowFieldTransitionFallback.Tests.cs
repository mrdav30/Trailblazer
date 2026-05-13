using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class FlowFieldTransitionFallbackTests : IDisposable
{
    public FlowFieldTransitionFallbackTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FlowFieldRequest_ShouldUseTransitionFallback_ForDisconnectedJumpLink()
    {
        RegisterTwoPointChart("JumpStart", Vector3d.Zero);
        RegisterTwoPointChart("JumpEnd", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "jump-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One));
        request.AllowTraversalTransitions = true;

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d toJumpSource).Should().BeTrue();
        toJumpSource.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 0, 0), out Vector3d acrossGap).Should().BeTrue();
        acrossGap.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d toGoal).Should().BeTrue();
        toGoal.x.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void FlowFieldRequest_ShouldReuseTransitionFallbackPlan_ForSecondIdenticalRequest()
    {
        RegisterTwoPointChart("WarmJumpStart", Vector3d.Zero);
        RegisterTwoPointChart("WarmJumpEnd", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "warm-jump-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        FlowFieldGuide firstGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdFirstGuide),
            createdFirstGuide);
        firstGuide.IsStaged.Should().BeTrue();
        PathGuideFactory.ReturnGuide(firstGuide);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        FlowFieldGuide secondGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdSecondGuide),
            createdSecondGuide);
        secondGuide.IsStaged.Should().BeTrue();
        PathGuideFactory.ReturnGuide(secondGuide);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(2_048);
    }

    [Fact]
    public void FlowFieldRequest_ShouldUseTransitionFallback_ForChartToWaterToChartRoute()
    {
        RegisterTwoPointChart("WaterStart", new Vector3d(-2, 0, 0));
        RegisterTwoPointChart("WaterEnd", new Vector3d(3, 0, 0));

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

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(-2, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One));
        request.AllowTraversalTransitions = true;

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(new Vector3d(-2, 0, 0), out Vector3d toShoreline).Should().BeTrue();
        toShoreline.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(-1, 0, 0), out Vector3d intoWater).Should().BeTrue();
        intoWater.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(0, 0, 0), out Vector3d throughWater).Should().BeTrue();
        throughWater.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 0, 0), out Vector3d ontoChart).Should().BeTrue();
        ontoChart.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d toGoal).Should().BeTrue();
        toGoal.x.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void FlowFieldRequest_ShouldNotUseTransitionFallback_WhenOnlyAllowUnwalkableIsEnabled()
    {
        RegisterTwoPointChart("NoFallbackStart", Vector3d.Zero);
        RegisterTwoPointChart("NoFallbackEnd", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "no-fallback-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        request.AllowTraversalTransitions.Should().BeFalse();

        PathGuideFactory.RequestGuide(request, out FlowFieldGuide? guide).Should().BeFalse();
        guide.Should().BeNull();
    }

    [Fact]
    public void FlowFieldRequest_ShouldUseTransitionFallback_ForGeneratedClimbTopology()
    {
        RegisterAuthoredClimbRoute("FlowFieldClimbFallback");

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(3, 0, 1),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        request.MaxClimbHeight = Fixed64.Zero;
        request.AllowTraversalTransitions = true;

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d toSeam).Should().BeTrue();
        toSeam.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 0, 0), out Vector3d upWall).Should().BeTrue();
        upWall.y.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 1, 0), out Vector3d aroundCorner).Should().BeTrue();
        aroundCorner.z.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 1, 1), out Vector3d towardExit).Should().BeTrue();
        towardExit.y.Should().BeLessThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 0, 1), out Vector3d toGoal).Should().BeTrue();
        toGoal.x.Should().BeGreaterThan(Fixed64.Zero);
    }

    private static void RegisterTwoPointChart(string chartName, Vector3d minBounds)
    {
        bool[,,] data = new bool[1, 2, 1];
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;
        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "FlowFieldFallbackWater");
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
