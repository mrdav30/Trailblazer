using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class PathRequestCoverageTests : IDisposable
{
    public PathRequestCoverageTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);

        RegisterLineChart("BaseGridZero", Vector3d.Zero, 3);
        RegisterGasLine(new Vector3d(0, 0, 2), 3, "VolumeGridZero");
        RegisterLineChart("HybridSingle", new Vector3d(4, 0, 0), 1);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PathRequest_ShouldHandleInvalidSetters_AndSearchRangeResets()
    {
        var request = new TestPathRequest();
        request.TrySetOrigin(Vector3d.Zero).Should().BeFalse();
        request.TrySetDestination(Vector3d.Zero).Should().BeFalse();
        request.TrySetUnitSize(Fixed64.Two).Should().BeFalse();

        request.UpdateRequest(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        int originalRange = request.MaxPathSearchRange;
        originalRange.Should().BeGreaterThan(0);

        request.TrySetOrigin(new Vector3d(0.2, 0, 0)).Should().BeTrue();
        request.MaxPathSearchRange.Should().Be(originalRange);

        request.TrySetOrigin(new Vector3d(1, 0, 0), resetSearchRange: true).Should().BeTrue();
        request.StartNode.WorldPosition.Should().Be(new Vector3d(1, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetDestination(new Vector3d(2, 0, 0), resetSearchRange: true).Should().BeTrue();
        request.EndNode.WorldPosition.Should().Be(new Vector3d(2, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PathRequest_UpdateRequest_ShouldInvalidateEndpoints_WhenResolutionFails()
    {
        var request = new TestPathRequest();
        request.UpdateRequest(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();

        request.UpdateRequest(new Vector3d(-20, 0, 0), new Vector3d(-18, 0, 0), Fixed64.One).Should().BeFalse();
        request.HasValidEndpoints.Should().BeFalse();
        request.IsValid.Should().BeFalse();
        request.StartNode.Should().BeNull();
        request.EndNode.Should().BeNull();
    }

    [Fact]
    public void VolumePathRequest_ShouldResetSearchRange_AndTrackVersionedHash()
    {
        VolumePathRequest request = VolumePathRequest.Create(
            new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas);
        request.Should().NotBeNull();

        int originalHash = request.GetHashCode();

        request.TrySetOrigin(new Vector3d(1, 0, 2), resetSearchRange: true).Should().BeTrue();
        request.StartNode.WorldPosition.Should().Be(new Vector3d(1, 0, 2));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetDestination(new Vector3d(1, 0, 2), resetSearchRange: true).Should().BeTrue();
        request.StartNode.GridIndex.Should().Be(request.EndNode.GridIndex);
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetUnitSize(Fixed64.One).Should().BeFalse();
        request.TrySetUnitSize(Fixed64.Two).Should().BeTrue();
        request.UnitSize.Should().Be(Fixed64.Two);

        VolumeMediumRules.SetGasVoxelRule(static _ => true);
        request.GetHashCode().Should().NotBe(originalHash);
    }

    [Fact]
    public void HybridPathRequest_ShouldCreateFromChartRequests_AndClearRouteWhenInvalidated()
    {
        AStarPathRequest aStar = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One);
        FlowFieldPathRequest flowField = FlowFieldPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One);

        HybridPathRequest.CreateFromAStar(null!).Should().BeNull();
        HybridPathRequest.CreateFromFlowField(null!).Should().BeNull();

        HybridPathRequest aStarHybrid = HybridPathRequest.CreateFromAStar(aStar);
        aStarHybrid.Should().NotBeNull();
        aStarHybrid.ChartRequestKind.Should().Be(HybridChartRequestKind.AStar);
        aStarHybrid.RoutePlan.Should().NotBeNull();
        aStarHybrid.RoutePlan.Steps.Should().HaveCount(1);
        aStarHybrid.RoutePlan.DirectedTransitions.Should().BeEmpty();

        HybridPathRequest flowHybrid = HybridPathRequest.CreateFromFlowField(flowField);
        flowHybrid.Should().NotBeNull();
        flowHybrid.ChartRequestKind.Should().Be(HybridChartRequestKind.FlowField);

        aStarHybrid.TrySetUnitSize(Fixed64.One).Should().BeFalse();
        aStarHybrid.UpdateRequest(new Vector3d(-20, 0, 0), new Vector3d(-18, 0, 0), Fixed64.One).Should().BeFalse();
        aStarHybrid.RoutePlan.Should().BeNull();
        aStarHybrid.MaxPathSearchRange.Should().Be(0);
        aStarHybrid.HasValidEndpoints.Should().BeFalse();
        aStarHybrid.TrySetOrigin(Vector3d.Zero).Should().BeFalse();
        aStarHybrid.TrySetDestination(Vector3d.Zero).Should().BeFalse();
    }

    [Fact]
    public void HybridPathRequest_ShouldReportZeroDisplacement_ForSinglePointRoute()
    {
        HybridPathRequest.TryCreate(
            new Vector3d(4, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One,
            out HybridPathRequest request).Should().BeTrue();

        request.Should().NotBeNull();
        request.IsValid.Should().BeTrue();
        request.RoutePlan.Should().NotBeNull();
        request.RoutePlan.DirectedTransitions.Should().BeEmpty();
        request.HasZeroDisplacement.Should().BeTrue();
    }

    [Fact]
    public void HybridPathRequest_ShouldRebuildAndHashTransitionAwarePlans()
    {
        PathTestFactory.RegisterSingleWalkablePoint("HybridGapStart", new Vector3d(5, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("HybridGapEnd", new Vector3d(7, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybrid-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(5, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(7, 0, 0)),
            pathCostModifier: 3)).Should().BeTrue();

        AStarPathRequest request = AStarPathRequest.Create(
            new Vector3d(5, 0, 0),
            new Vector3d(7, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true);
        request.Should().NotBeNull();

        HybridPathRequest hybrid = HybridPathRequest.CreateFromAStar(request);
        hybrid.Should().NotBeNull();
        hybrid.RoutePlan.DirectedTransitions.Should().HaveCount(1);
        hybrid.HasZeroDisplacement.Should().BeFalse();

        int transitionHash = hybrid.GetHashCode();

        hybrid.TrySetOrigin(new Vector3d(5, 0, 0)).Should().BeTrue();
        hybrid.TrySetDestination(new Vector3d(7, 0, 0)).Should().BeTrue();
        hybrid.GetHashCode().Should().Be(transitionHash);
        hybrid.Equals(HybridPathRequest.CreateFromAStar(request)).Should().BeTrue();
    }

    [Fact]
    public void HybridPathRequest_TryCreate_ShouldFail_WhenEndpointsCannotResolve()
    {
        HybridPathRequest.TryCreate(
            new Vector3d(-20, 0, 0),
            new Vector3d(-18, 0, 0),
            Fixed64.One,
            out HybridPathRequest request).Should().BeFalse();

        request.Should().BeNull();
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }

    private static void RegisterGasLine(Vector3d start, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                new Vector3d(start.x + i, start.y, start.z),
                TraversalMedium.Gas,
                chartNamePrefix);
        }
    }

    private sealed class TestPathRequest : PathRequest
    {
        public override int GetHashCode() =>
            (StartNode?.SpawnToken ?? 0,
                EndNode?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkableEndpoints,
                MaxPathSearchRange).CombineHashCodes();
    }
}
