using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
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

        request.TrySetOrigin(Vector3d.FromDouble(0.2, 0, 0)).Should().BeTrue();
        request.MaxPathSearchRange.Should().Be(originalRange);

        request.TrySetOrigin(new Vector3d(1, 0, 0), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.StartNode).WorldPosition.Should().Be(new Vector3d(1, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);

        request.TrySetDestination(new Vector3d(2, 0, 0), resetSearchRange: true).Should().BeTrue();
        TestRequire.NotNull(request.EndNode).WorldPosition.Should().Be(new Vector3d(2, 0, 0));
        request.MaxPathSearchRange.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Requests_ShouldRecomputeSearchRangeAfterSameSlotGridRespawn()
    {
        FlowFieldPathRequest solid = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        VolumePathRequest volume = TestRequire.NotNull(VolumePathRequest.Create(
            TestWorld.Context,
            new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        int originalRange = solid.MaxPathSearchRange;
        volume.MaxPathSearchRange.Should().Be(originalRange);
        ushort gridIndex = (ushort)TestRequire.NotNull(solid.StartNode).GridIndex;

        TestWorld.World.TryRemoveGrid(gridIndex).Should().BeTrue();
        TestWorld.Context.Pathing.FlushPendingGridChanges();

        var replacementConfig = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(10, 10, 10));
        TestWorld.World.TryAddGrid(replacementConfig, out ushort replacementGridIndex).Should().BeTrue();
        replacementGridIndex.Should().Be(gridIndex);
        TestWorld.Context.Pathing.FlushPendingGridChanges();

        int replacementRange = TestRequire.Grid(TestWorld.Context, replacementGridIndex).ConfiguredVoxelCount;
        replacementRange.Should().NotBe(originalRange);

        solid.TrySetOrigin(Vector3d.Zero).Should().BeTrue();
        solid.TrySetDestination(new Vector3d(2, 0, 0)).Should().BeTrue();
        solid.MaxPathSearchRange.Should().Be(replacementRange);

        volume.TrySetOrigin(new Vector3d(0, 0, 2)).Should().BeTrue();
        volume.TrySetDestination(new Vector3d(2, 0, 2)).Should().BeTrue();
        volume.MaxPathSearchRange.Should().Be(replacementRange);
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
    public void FlowFieldPathRequest_Equality_ShouldRemainDestinationCentric()
    {
        FlowFieldPathRequest first = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        FlowFieldPathRequest second = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One));

        first.RequestCacheKey.Should().Be(second.RequestCacheKey);
        first.Should().Be(second);
    }

    [Fact]
    public void HybridPathRequest_ShouldCreateImmutableRoutePlanFromFlowField()
    {
        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        HybridPathRequest.CreateFromFlowField(null!).Should().BeNull();

        HybridPathRequest flowHybrid = TestRequire.NotNull(HybridPathRequest.CreateFromFlowField(flowField));
        HybridRoutePlan flowPlan = TestRequire.NotNull(flowHybrid.RoutePlan);
        flowPlan.Steps.Should().HaveCount(1);
        flowPlan.DirectedTransitions.Should().BeEmpty();
        flowHybrid.Context.Should().BeSameAs(flowField.Context);
        flowHybrid.Origin.Should().Be(flowField.Origin);
        flowHybrid.TargetPosition.Should().Be(flowField.TargetPosition);
        flowHybrid.UnitSize.Should().Be(flowField.UnitSize);
    }

    [Fact]
    public void HybridPathRequest_ShouldReturnNull_WhenNoRouteExists()
    {
        FlowFieldPathRequest disconnected = FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One)
            ?? throw new InvalidOperationException("Expected disconnected flow-field endpoints to resolve.");
        HybridPathRequest.CreateFromFlowField(disconnected).Should().BeNull();
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

    private static void RegisterGasLine(Vector3d start, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                TestWorld.Context, new Vector3d(start.X + i, start.Y, start.Z),
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

        public override PathRequestCacheKey RequestCacheKey => default;
    }
}
