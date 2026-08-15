using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

[Collection("PathingCollection")]
public sealed class PathRequestRecordTests : IDisposable
{
    public PathRequestRecordTests()
    {
        if (TestWorld.IsActive)
            TestWorld.Reset();
        else
            TestWorld.Setup();

        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryCreateRequest_ShouldRoundTripSupportedRequestKinds()
    {
        PathTestFactory.RegisterLineChart(TestWorld.Context, "PathRecordSolid", Vector3d.Zero, 5);
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 2), TraversalMedium.Gas, 5, "PathRecordGas");

        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true));
        flowField.MaxClimbHeight = (Fixed64)2;
        flowField.ExtraFloodRange = 21;
        flowField.MaxPathSearchRange = 13;

        VolumePathRequest volume = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(4, 0, 2),
            Fixed64.One,
            HeuristicMethod.Manhattan,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas));
        volume.MaxPathSearchRange = 9;

        AssertRoundTrip(flowField, PathRequestRecordKind.FlowField, recreated =>
        {
            FlowFieldPathRequest recreatedFlowField = Assert.IsType<FlowFieldPathRequest>(recreated);
            recreatedFlowField.AllowTraversalTransitions.Should().BeTrue();
            recreatedFlowField.AllowUnwalkableEndpoints.Should().BeTrue();
            recreatedFlowField.MaxClimbHeight.Should().Be((Fixed64)2);
            recreatedFlowField.ExtraFloodRange.Should().Be(21);
            recreatedFlowField.MaxPathSearchRange.Should().Be(13);
        });

        AssertRoundTrip(volume, PathRequestRecordKind.Volume, recreated =>
        {
            VolumePathRequest recreatedVolume = Assert.IsType<VolumePathRequest>(recreated);
            recreatedVolume.AllowUnwalkableEndpoints.Should().BeTrue();
            recreatedVolume.Heuristic.Should().Be(HeuristicMethod.Manhattan);
            recreatedVolume.Medium.Should().Be(TraversalMedium.Gas);
            recreatedVolume.MaxPathSearchRange.Should().Be(9);
        });
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreNonDefaultVolumeHeuristicFromLegacyWireKey(bool useMemoryPack)
    {
        PathTestFactory.RegisterVolumeLine(
            TestWorld.Context,
            new Vector3d(0, 0, 2),
            TraversalMedium.Gas,
            5,
            "LegacyPathRequestHeuristic");
        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(
            TestWorld.Context,
            new Vector3d(0, 0, 2),
            new Vector3d(4, 0, 2),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            medium: TraversalMedium.Gas));
        var source = new PathRequestRecord();
        source.Capture(request, guide: null);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            HeuristicMethod.Euclidean,
            "AStarHeuristic");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "VolumeHeuristic");
        var target = new PathRequestRecord();

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.TryCreateRequest(TestWorld.Context, out IPathRequest? recreated).Should().BeTrue();
        Assert.IsType<VolumePathRequest>(TestRequire.NotNull(recreated))
            .Heuristic.Should().Be(HeuristicMethod.Euclidean);
    }

    [Fact]
    public void TryCreateGuide_ShouldRestoreWaypointIndices_ForWaypointGuides()
    {
        PathTestFactory.RegisterLineChart(TestWorld.Context, "PathRecordGuideSolid", new Vector3d(0, 2, 0), 5);
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 2), TraversalMedium.Gas, 5, "PathRecordGuideGas");

        VolumePathRequest volume = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(4, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide volumeGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(volume, out VolumeGuide? createdVolumeGuide),
            createdVolumeGuide);
        volumeGuide.AdvanceWaypoint();
        AssertGuideRoundTrip(volume, volumeGuide, expectedWaypointIndex: 2);
        PathGuideFactory.ReturnGuide(volumeGuide, dispose: true);
    }

    [Fact]
    public void ResetAndEdgeCases_ShouldClearRecord_AndRejectUnsupportedCapture()
    {
        PathRequestRecord record = new();

        record.TryCreateRequest(TestWorld.Context, out IPathRequest? emptyRequest).Should().BeTrue();
        emptyRequest.Should().BeNull();
        record.TryCreateGuide(null, out IGuide? emptyGuide).Should().BeFalse();
        emptyGuide.Should().BeNull();

        PathTestFactory.RegisterLineChart(TestWorld.Context, "PathRecordUnsupported", Vector3d.Zero, 2);
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

        Action captureUnsupported = () => record.Capture(new UnsupportedRequest(start, end), null);
        captureUnsupported.Should().Throw<NotSupportedException>();

        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One));
        record.Capture(flowField, null);
        record.Reset();

        record.Kind.Should().Be(PathRequestRecordKind.None);
        record.TargetPosition.Should().Be(Vector3d.Zero);
        record.MaxPathSearchRange.Should().Be(0);
        record.HasGuide.Should().BeFalse();
        record.WaypointIndex.Should().Be(-1);
    }

    [Fact]
    public void TryCreateRequest_ShouldRejectUnsupportedKinds_AndFailedRecreationPaths()
    {
        PathRequestRecord record = new();

        record.Kind = (PathRequestRecordKind)999;
        record.TryCreateRequest(TestWorld.Context, out IPathRequest? unsupportedKind).Should().BeFalse();
        unsupportedKind.Should().BeNull();

        record.Reset();
        record.Kind = (PathRequestRecordKind)1;
        record.Origin = Vector3d.Zero;
        record.TargetPosition = new Vector3d(1, 0, 0);
        record.TryCreateRequest(TestWorld.Context, out IPathRequest? retiredAStar).Should().BeFalse();
        retiredAStar.Should().BeNull();

        record.Reset();
        record.Kind = PathRequestRecordKind.FlowField;
        record.Origin = new Vector3d(-20, 0, 0);
        record.TargetPosition = new Vector3d(-18, 0, 0);
        record.TryCreateRequest(TestWorld.Context, out IPathRequest? failedFlowField).Should().BeFalse();
        failedFlowField.Should().BeNull();

        record.Reset();
        record.Kind = PathRequestRecordKind.Volume;
        record.Origin = new Vector3d(-20, 0, 2);
        record.TargetPosition = new Vector3d(-18, 0, 2);
        record.Medium = TraversalMedium.Gas;
        record.TryCreateRequest(TestWorld.Context, out IPathRequest? failedVolume).Should().BeFalse();
        failedVolume.Should().BeNull();

        record.Reset();
        record.Kind = (PathRequestRecordKind)4;
        record.Origin = Vector3d.Zero;
        record.TargetPosition = new Vector3d(4, 0, 0);
        record.TryCreateRequest(TestWorld.Context, out IPathRequest? retiredHybrid).Should().BeFalse();
        retiredHybrid.Should().BeNull();
    }

    [Fact]
    public void TryCreateGuide_ShouldReturnFalse_WhenGuideCannotBeRecreated()
    {
        PathTestFactory.RegisterLineChart(TestWorld.Context, "PathRecordFlowField", new Vector3d(0, 4, 0), 3);

        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(0, 4, 0),
            new Vector3d(2, 4, 0),
            Fixed64.One));

        FlowFieldGuide flowGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(flowField, out FlowFieldGuide? createdFlowGuide),
            createdFlowGuide);
        PathRequestRecord flowRecord = new();
        flowRecord.Capture(flowField, flowGuide);
        IGuide recreatedFlowGuide = TestRequire.Created(
            flowRecord.TryCreateGuide(flowField, out IGuide? createdRecreatedFlowGuide),
            createdRecreatedFlowGuide);
        recreatedFlowGuide.Should().BeOfType<FlowFieldGuide>();

        PathGuideFactory.ReturnGuide(recreatedFlowGuide, dispose: true);
        PathGuideFactory.ReturnGuide(flowGuide, dispose: true);

        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "PathRecordDisconnectedStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "PathRecordDisconnectedEnd", new Vector3d(4, 0, 0));

        PathRequestRecord failureRecord = new()
        {
            HasGuide = true
        };
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(4, 0, 0));
        var disconnected = new UnsupportedRequest(start, end);

        failureRecord.TryCreateGuide(disconnected, out IGuide? failedGuide).Should().BeFalse();
        failedGuide.Should().BeNull();
    }

    private static void AssertRoundTrip(
        IPathRequest request,
        PathRequestRecordKind expectedKind,
        Action<IPathRequest> assertRequest)
    {
        PathRequestRecord record = new();
        record.Capture(request, null);
        record.Kind.Should().Be(expectedKind);

        record.TryCreateRequest(TestWorld.Context, out IPathRequest? recreated).Should().BeTrue();
        assertRequest(TestRequire.NotNull(recreated));
    }

    private static void AssertGuideRoundTrip(
        IPathRequest request,
        IGuide guide,
        int expectedWaypointIndex)
    {
        PathRequestRecord record = new();
        record.Capture(request, guide);
        record.HasGuide.Should().BeTrue();
        record.WaypointIndex.Should().Be(expectedWaypointIndex);

        record.TryCreateRequest(TestWorld.Context, out IPathRequest? recreatedRequest).Should().BeTrue();
        IPathRequest restoredRequest = TestRequire.NotNull(recreatedRequest);
        IGuide recreatedGuide = TestRequire.Created(
            record.TryCreateGuide(restoredRequest, out IGuide? createdRecreatedGuide),
            createdRecreatedGuide);
        recreatedGuide.Should().BeAssignableTo<IWaypointGuide>()
            .Which.CurrentWaypointIndex.Should().Be(expectedWaypointIndex);

        PathGuideFactory.ReturnGuide(recreatedGuide, dispose: true);
    }

    private sealed class UnsupportedRequest : IPathRequest
    {
        public UnsupportedRequest(Voxel start, Voxel end)
        {
            Origin = start.WorldPosition;
            StartNode = start;
            TargetPosition = end.WorldPosition;
            EndNode = end;
        }

        public TrailblazerWorldContext Context => TestWorld.Context;

        public Vector3d Origin { get; }

        public Voxel StartNode { get; }

        public Vector3d TargetPosition { get; }

        public Voxel EndNode { get; }

        public Fixed64 UnitSize => Fixed64.One;

        public bool HasZeroDisplacement => StartNode == EndNode;

        public bool AllowUnwalkableEndpoints => false;

        public int MaxPathSearchRange { get; set; } = 1;

        public bool HasOrigin => true;

        public bool HasDestination => true;

        public bool HasValidEndpoints => true;

        public bool IsValid => true;

        public PathRequestCacheKey RequestCacheKey => TestPathRequest.CreateCacheKey(99);

        public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

        public bool TrySetUnitSize(Fixed64 unitSize) => false;
    }
}
