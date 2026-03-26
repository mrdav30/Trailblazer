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
public class FlowFieldTransitionFallbackTests : IDisposable
{
    public FlowFieldTransitionFallbackTests()
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

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One);
        request.Should().NotBeNull();
        request.AllowTraversalTransitions = true;

        PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d toJumpSource).Should().BeTrue();
        toJumpSource.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 0, 0), out Vector3d acrossGap).Should().BeTrue();
        acrossGap.x.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d toGoal).Should().BeTrue();
        toGoal.x.Should().BeGreaterThan(Fixed64.Zero);
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

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(
            new Vector3d(-2, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One);
        request.Should().NotBeNull();
        request.AllowTraversalTransitions = true;

        PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
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

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowUnwalkableEndNode: true);
        request.Should().NotBeNull();
        request.AllowTraversalTransitions.Should().BeFalse();

        PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide).Should().BeFalse();
        guide.Should().BeNull();
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
}
