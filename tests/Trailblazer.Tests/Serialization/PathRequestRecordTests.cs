using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Trailblazer.Serialization;
using Xunit;

namespace Trailblazer.Tests.Serialization;

[Collection("PathingCollection")]
public sealed class PathRequestRecordTests : IDisposable
{
    public PathRequestRecordTests()
    {
        if (TrailblazerWorldManager.IsActive)
            TrailblazerWorldManager.Reset();
        else
            TrailblazerWorldManager.Setup();

        TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryCreateRequest_ShouldRoundTripSupportedRequestKinds()
    {
        RegisterLineChart("PathRecordSolid", Vector3d.Zero, 5);
        RegisterVolumeLine(new Vector3d(0, 0, 2), TraversalMedium.Gas, 5, "PathRecordGas");

        AStarPathRequest aStar = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true));
        aStar.MaxClimbHeight = (Fixed64)3;
        aStar.MaxPathSearchRange = 17;

        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true));
        flowField.MaxClimbHeight = (Fixed64)2;
        flowField.ExtraFloodRange = 21;
        flowField.MaxPathSearchRange = 13;

        VolumePathRequest volume = TestRequire.NotNull(VolumePathRequest.Create(
            new Vector3d(0, 0, 2),
            new Vector3d(4, 0, 2),
            Fixed64.One,
            HeuristicMethod.Manhattan,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas));
        volume.MaxPathSearchRange = 9;

        HybridPathRequest hybrid = TestRequire.NotNull(HybridPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            (Fixed64)4,
            allowUnwalkableEndpoints: true));
        hybrid.MaxPathSearchRange = 11;

        AssertRoundTrip(aStar, PathRequestRecordKind.AStar, recreated =>
        {
            AStarPathRequest recreatedAStar = Assert.IsType<AStarPathRequest>(recreated);
            recreatedAStar.AllowTraversalTransitions.Should().BeTrue();
            recreatedAStar.AllowUnwalkableEndpoints.Should().BeTrue();
            recreatedAStar.Heuristic.Should().Be(HeuristicMethod.Euclidean);
            recreatedAStar.MaxClimbHeight.Should().Be((Fixed64)3);
            recreatedAStar.MaxPathSearchRange.Should().Be(17);
        });

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

        AssertRoundTrip(hybrid, PathRequestRecordKind.Hybrid, recreated =>
        {
            HybridPathRequest recreatedHybrid = Assert.IsType<HybridPathRequest>(recreated);
            recreatedHybrid.AllowUnwalkableEndpoints.Should().BeTrue();
            recreatedHybrid.Heuristic.Should().Be(HeuristicMethod.Euclidean);
            recreatedHybrid.MaxClimbHeight.Should().Be((Fixed64)4);
            recreatedHybrid.MaxPathSearchRange.Should().Be(11);
        });
    }

    [Fact]
    public void TryCreateGuide_ShouldRestoreWaypointIndices_ForWaypointGuides()
    {
        RegisterLineChart("PathRecordGuideSolid", new Vector3d(0, 2, 0), 5);
        RegisterVolumeLine(new Vector3d(0, 0, 2), TraversalMedium.Gas, 5, "PathRecordGuideGas");
        GuidedPathTestScene.RegisterTransitionFallbackScene();

        AStarPathRequest aStar = TestRequire.NotNull(AStarPathRequest.Create(
            new Vector3d(0, 2, 0),
            new Vector3d(4, 2, 0),
            Fixed64.One));
        AStarGuide aStarGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(aStar, out AStarGuide? createdAStarGuide),
            createdAStarGuide);
        aStarGuide.AdvanceWaypoint();
        aStarGuide.AdvanceWaypoint();
        AssertGuideRoundTrip(aStar, aStarGuide, expectedWaypointIndex: 2);
        PathGuideFactory.ReturnGuide(aStarGuide, dispose: true);

        VolumePathRequest volume = TestRequire.NotNull(VolumePathRequest.Create(
            new Vector3d(0, 0, 2),
            new Vector3d(4, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide volumeGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(volume, out VolumeGuide? createdVolumeGuide),
            createdVolumeGuide);
        volumeGuide.AdvanceWaypoint();
        AssertGuideRoundTrip(volume, volumeGuide, expectedWaypointIndex: 2);
        PathGuideFactory.ReturnGuide(volumeGuide, dispose: true);

        AStarPathRequest transitionAwareRequest = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowTraversalTransitions: true));
        HybridPathRequest hybrid = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(transitionAwareRequest));
        HybridGuide hybridGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(hybrid, out HybridGuide? createdHybridGuide),
            createdHybridGuide);
        hybridGuide.AdvanceWaypoint();
        AssertGuideRoundTrip(hybrid, hybridGuide, expectedWaypointIndex: 2);
        PathGuideFactory.ReturnGuide(hybridGuide, dispose: true);
    }

    [Fact]
    public void ResetAndEdgeCases_ShouldClearRecord_AndRejectUnsupportedCapture()
    {
        PathRequestRecord record = new();

        record.TryCreateRequest(out IPathRequest? emptyRequest).Should().BeTrue();
        emptyRequest.Should().BeNull();
        record.TryCreateGuide(null, out IGuide? emptyGuide).Should().BeFalse();
        emptyGuide.Should().BeNull();

        RegisterLineChart("PathRecordUnsupported", Vector3d.Zero, 2);
        Voxel start = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        Action captureUnsupported = () => record.Capture(new UnsupportedRequest(start, end), null);
        captureUnsupported.Should().Throw<NotSupportedException>();

        AStarPathRequest aStar = TestRequire.NotNull(AStarPathRequest.Create(Vector3d.Zero, new Vector3d(1, 0, 0), Fixed64.One));
        record.Capture(aStar, null);
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
        record.TryCreateRequest(out IPathRequest? unsupportedKind).Should().BeFalse();
        unsupportedKind.Should().BeNull();

        record.Reset();
        record.Kind = PathRequestRecordKind.FlowField;
        record.Origin = new Vector3d(-20, 0, 0);
        record.TargetPosition = new Vector3d(-18, 0, 0);
        record.TryCreateRequest(out IPathRequest? failedFlowField).Should().BeFalse();
        failedFlowField.Should().BeNull();

        record.Reset();
        record.Kind = PathRequestRecordKind.Volume;
        record.Origin = new Vector3d(-20, 0, 2);
        record.TargetPosition = new Vector3d(-18, 0, 2);
        record.Medium = TraversalMedium.Gas;
        record.TryCreateRequest(out IPathRequest? failedVolume).Should().BeFalse();
        failedVolume.Should().BeNull();

        record.Reset();
        record.Kind = PathRequestRecordKind.Hybrid;
        record.Origin = Vector3d.Zero;
        record.TargetPosition = new Vector3d(4, 0, 0);
        record.TryCreateRequest(out IPathRequest? failedHybrid).Should().BeFalse();
        failedHybrid.Should().BeNull();
    }

    [Fact]
    public void TryCreateGuide_ShouldReturnFalse_WhenGuideCannotBeRecreated()
    {
        RegisterLineChart("PathRecordFlowField", new Vector3d(0, 4, 0), 3);

        FlowFieldPathRequest flowField = TestRequire.NotNull(FlowFieldPathRequest.Create(
            new Vector3d(0, 4, 0),
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

        PathTestFactory.RegisterSingleWalkablePoint("PathRecordDisconnectedStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("PathRecordDisconnectedEnd", new Vector3d(4, 0, 0));

        PathRequestRecord failureRecord = new()
        {
            HasGuide = true
        };
        AStarPathRequest disconnected = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true)
            ?? throw new InvalidOperationException("Expected disconnected AStar request to resolve endpoints.");

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

        record.TryCreateRequest(out IPathRequest? recreated).Should().BeTrue();
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

        record.TryCreateRequest(out IPathRequest? recreatedRequest).Should().BeTrue();
        IPathRequest restoredRequest = TestRequire.NotNull(recreatedRequest);
        IGuide recreatedGuide = TestRequire.Created(
            record.TryCreateGuide(restoredRequest, out IGuide? createdRecreatedGuide),
            createdRecreatedGuide);
        recreatedGuide.Should().BeAssignableTo<IWaypointGuide>()
            .Which.CurrentWaypointIndex.Should().Be(expectedWaypointIndex);

        PathGuideFactory.ReturnGuide(recreatedGuide, dispose: true);
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        bool[,,] data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }

    private static void RegisterVolumeLine(Vector3d start, TraversalMedium medium, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                new Vector3d(start.x + i, start.y, start.z),
                medium,
                chartNamePrefix);
        }
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

        public int RequestCacheKey => 99;

        public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

        public bool TrySetUnitSize(Fixed64 unitSize) => false;
    }
}
