using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
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
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _);

        PathTestFactory.RegisterLineChart(TestWorld.Context, "BaseGridZero", Vector3d.Zero, 3);
        RegisterGasLine(new Vector3d(0, 0, 2), 3, "VolumeGridZero");
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridSingle", new Vector3d(4, 0, 0), 1);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
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
        TestRequire.NotNull(request.StartNode).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetDestination(new Vector3d(2, 0, 0), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.EndNode).WorldPosition.Should().Be(new Vector3d(2, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PathRequest_UpdateRequest_ShouldInvalidateEndpoints_WhenResolutionFails()
    {
        var request = new TestPathRequest();
        request.UpdateRequest(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.UpdateRequest(new Vector3d(-20, 0, 0), new Vector3d(-18, 0, 0), Fixed64.One).Should().BeFalse();
        request.HasValidEndpoints.Should().BeFalse();
        request.IsValid.Should().BeFalse();
        request.StartNode.Should().BeNull();
        request.EndNode.Should().BeNull();
        request.MaxPathSearchRange.Should().Be(0);
    }

    [Fact]
    public void VolumePathRequest_ShouldResetSearchRange_AndTrackVersionedHash()
    {
        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        int originalHash = request.GetHashCode();

        request.TrySetOrigin(new Vector3d(1, 0, 2), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.StartNode).WorldPosition.Should().Be(new Vector3d(1, 0, 2));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetDestination(new Vector3d(1, 0, 2), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.StartNode).GridIndex.Should().Be(TestRequire.NotNull(request.EndNode).GridIndex);
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetUnitSize(Fixed64.One).Should().BeFalse();
        request.TrySetUnitSize(Fixed64.Two).Should().BeTrue();
        request.UnitSize.Should().Be(Fixed64.Two);

        VolumeMediumRules.SetGasVoxelRule(static _ => true);
        request.GetHashCode().Should().NotBe(originalHash);
    }

    [Fact]
    public void VolumePathRequest_ShouldHandleFailedSetters_AndRevalidateUnitSizeChanges()
    {
        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        int originalRange = request.MaxPathSearchRange;

        request.TrySetOrigin(new Vector3d(1, 0, 2)).Should().BeTrue();
        request.MaxPathSearchRange.Should().Be(originalRange);

        request.TrySetDestination(new Vector3d(1, 0, 2)).Should().BeTrue();
        request.MaxPathSearchRange.Should().Be(originalRange);

        request.TrySetOrigin(new Vector3d(-20, 0, 2)).Should().BeFalse();
        request.TrySetDestination(new Vector3d(20, 0, 2)).Should().BeFalse();

        request.UpdateRequest(new Vector3d(-20, 0, 2), new Vector3d(-18, 0, 2), Fixed64.One).Should().BeFalse();
        request.TrySetOrigin(new Vector3d(0, 0, 2)).Should().BeFalse();
        request.TrySetDestination(new Vector3d(2, 0, 2)).Should().BeFalse();

        Vector3d boundaryPoint = new(-4, -4, -4);
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, boundaryPoint, TraversalMedium.Gas, "VolumeUnitSizeSingle");
        VolumePathRequest sizeSensitive = VolumePathRequest.Create(TestWorld.Context, boundaryPoint,
            boundaryPoint,
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid boundary Volume request.");

        sizeSensitive.TrySetUnitSize(Fixed64.Two).Should().BeFalse();
        sizeSensitive.HasValidEndpoints.Should().BeFalse();
        sizeSensitive.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VolumePathRequest_Equals_ShouldSupportObjectAndTypedOverloads()
    {
        VolumePathRequest a = VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid Volume request.");
        VolumePathRequest b = VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid Volume request.");
        VolumePathRequest c = VolumePathRequest.Create(TestWorld.Context, new Vector3d(1, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas)
            ?? throw new InvalidOperationException("Expected valid Volume request.");
        VolumePathRequest? missing = null;

        a.Equals((object)b).Should().BeTrue();
        a.Equals((object)c).Should().BeFalse();
        a.Equals(new object()).Should().BeFalse();
        a.Equals(missing).Should().BeFalse();
        a.Equals(b).Should().BeTrue();
        a.Equals(c).Should().BeFalse();
    }

    [Fact]
    public void FlowFieldPathRequest_ShouldCoverFactoryHelpers_Equality_AndInvalidRequests()
    {
        FlowFieldPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            out FlowFieldPathRequest? request).Should().BeTrue();

        FlowFieldPathRequest actualRequest = TestRequire.NotNull(request);
        FlowFieldPathRequest? missingRequest = null;
        object unrelatedObject = new();
        actualRequest.ExtraFloodRange.Should().Be(FlowFieldPathRequest.DefaultExtraFloodRange);
        actualRequest.Equals(actualRequest).Should().BeTrue();
        actualRequest.Equals(missingRequest).Should().BeFalse();
        actualRequest.Equals(unrelatedObject).Should().BeFalse();

        FlowFieldPathRequest.TryCreate(TestWorld.Context, new Vector3d(-20, 0, 0),
            new Vector3d(-18, 0, 0),
            out FlowFieldPathRequest? invalidDefault).Should().BeFalse();
        invalidDefault.Should().BeNull();

        FlowFieldPathRequest.TryCreateWithSize(TestWorld.Context, new Vector3d(-20, 0, 0),
            new Vector3d(-18, 0, 0),
            Fixed64.Two,
            out FlowFieldPathRequest? invalidSized).Should().BeFalse();
        invalidSized.Should().BeNull();
    }

    [Fact]
    public void HybridPathRequest_ShouldCreateFromChartRequests_AndClearRouteWhenInvalidated()
    {
        AStarPathRequest aStar = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));
        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        HybridPathRequest.CreateFromAStar(null!).Should().BeNull();
        HybridPathRequest.CreateFromFlowField(null!).Should().BeNull();

        HybridPathRequest aStarHybrid = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(aStar));
        aStarHybrid.ChartRequestKind.Should().Be(HybridChartRequestKind.AStar);
        HybridRoutePlan aStarPlan = TestRequire.NotNull(aStarHybrid.RoutePlan);
        aStarPlan.Steps.Should().HaveCount(1);
        aStarPlan.DirectedTransitions.Should().BeEmpty();

        HybridPathRequest flowHybrid = TestRequire.NotNull(HybridPathRequest.CreateFromFlowField(flowField));
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
    public void HybridPathRequest_ShouldReturnNull_WhenNoRouteExists_AndCoverEqualityAndSetterFailures()
    {
        HybridPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One).Should().BeNull();

        AStarPathRequest disconnected = AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One)
            ?? throw new InvalidOperationException("Expected endpoints to resolve for disconnected AStar request.");
        HybridPathRequest.CreateFromAStar(disconnected).Should().BeNull();

        HybridPathRequest hybrid = HybridPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One)
            ?? throw new InvalidOperationException("Expected valid Hybrid request.");
        HybridPathRequest other = HybridPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One)
            ?? throw new InvalidOperationException("Expected valid Hybrid request.");
        HybridPathRequest? missing = null;

        hybrid.Equals((object)other).Should().BeTrue();
        hybrid.Equals(new object()).Should().BeFalse();
        hybrid.Equals(missing).Should().BeFalse();

        hybrid.MaxPathSearchRange = 0;
        hybrid.IsValid.Should().BeFalse();
        hybrid.HasZeroDisplacement.Should().BeTrue();

        hybrid.RebuildPlan().Should().BeTrue();
        hybrid.TrySetOrigin(new Vector3d(-20, 0, 0)).Should().BeFalse();
        TestRequire.NotNull(hybrid.RoutePlan);
        hybrid.IsValid.Should().BeTrue();

        bool[,,] roomyChart = new bool[1, 3, 3]
        {
            {
                { true, true, true },
                { true, true, true },
                { true, true, true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "HybridUnitSizeRoomy", roomyChart, new Vector3d(-3, 0, -3));

        HybridPathRequest roomyHybrid = HybridPathRequest.Create(TestWorld.Context, new Vector3d(-3, 0, -3),
            new Vector3d(-1, 0, -1),
            Fixed64.One)
            ?? throw new InvalidOperationException("Expected roomy Hybrid request.");

        roomyHybrid.TrySetUnitSize(Fixed64.Two).Should().BeTrue();
        Assert.NotNull(roomyHybrid.RoutePlan);
        roomyHybrid.IsValid.Should().BeTrue();
    }

    [Fact]
    public void HybridPathRequest_ShouldReportZeroDisplacement_ForSinglePointRoute()
    {
        HybridPathRequest.TryCreate(TestWorld.Context, new Vector3d(4, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One,
            out HybridPathRequest? request).Should().BeTrue();
        HybridPathRequest actualRequest = TestRequire.NotNull(request);
        actualRequest.IsValid.Should().BeTrue();
        Assert.NotNull(actualRequest.RoutePlan);
        actualRequest.RoutePlan.DirectedTransitions.Should().BeEmpty();
        actualRequest.HasZeroDisplacement.Should().BeTrue();
    }

    [Fact]
    public void HybridPathRequest_ShouldRebuildAndHashTransitionAwarePlans()
    {
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridGapStart", new Vector3d(5, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridGapEnd", new Vector3d(7, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybrid-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(5, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(7, 0, 0)),
            pathCostModifier: 3)).Should().BeTrue();

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(5, 0, 0),
            new Vector3d(7, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        HybridPathRequest hybrid = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(request));
        HybridRoutePlan transitionPlan = TestRequire.NotNull(hybrid.RoutePlan);
        transitionPlan.DirectedTransitions.Should().HaveCount(1);
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
        HybridPathRequest.TryCreate(TestWorld.Context, new Vector3d(-20, 0, 0),
            new Vector3d(-18, 0, 0),
            Fixed64.One,
            out HybridPathRequest? request).Should().BeFalse();

        request.Should().BeNull();
    }

    [Fact]
    public void PathRequest_TrySetDestination_ShouldUpdateEndNode_WhenDestinationChanges()
    {
        var request = new TestPathRequest();
        request.UpdateRequest(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One).Should().BeTrue();

        // Changing to a different voxel on the same chart exercises the full update path.
        request.TrySetDestination(new Vector3d(1, 0, 0)).Should().BeTrue();
        TestRequire.NotNull(request.EndNode).WorldPosition.Should().Be(new Vector3d(1, 0, 0));

        // resetSearchRange: true exercises the search-range recompute block.
        request.TrySetDestination(new Vector3d(2, 0, 0), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.EndNode).WorldPosition.Should().Be(new Vector3d(2, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AStarPathRequest_Equals_ShouldSupportObjectAndTypedOverloads()
    {
        AStarPathRequest a = AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One)
            ?? throw new InvalidOperationException("Expected valid AStar request.");
        AStarPathRequest b = AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One)
            ?? throw new InvalidOperationException("Expected valid AStar request.");
        AStarPathRequest c = AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(1, 0, 0), Fixed64.One)
            ?? throw new InvalidOperationException("Expected valid AStar request.");

        a.Equals((object)b).Should().BeTrue();
        a.Equals((object)c).Should().BeFalse();
        a.Equals(new object()).Should().BeFalse();
        a.Equals(b).Should().BeTrue();
        a.Equals(c).Should().BeFalse();
    }

    private static void RegisterGasLine(Vector3d start, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                TestWorld.Context, new Vector3d(start.x + i, start.y, start.z),
                TraversalMedium.Gas,
                chartNamePrefix);
        }
    }

    private sealed class TestPathRequest : PathRequest
    {
        public TestPathRequest()
        {
            Context = TestWorld.Context;
        }

        public override int GetHashCode() =>
            (StartNode?.SpawnToken ?? 0,
                EndNode?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkableEndpoints,
                MaxPathSearchRange).CombineHashCodes();
    }
}
