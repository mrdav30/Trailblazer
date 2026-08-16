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
    [Fact]
    public void RawLegacyFlowKind_ShouldBeUnnamedAndRejectedBeforeRequestCreation()
    {
        PathTestFactory.RegisterLineChart(TestWorld.Context, "RetiredFlowRecord", Vector3d.Zero, 2);
        var record = new PathRequestRecord
        {
            Kind = (PathRequestRecordKind)2,
            Origin = Vector3d.Zero,
            TargetPosition = Vector3d.Right,
            UnitSize = Fixed64.One
        };

        Enum.GetNames(typeof(PathRequestRecordKind)).Should().NotContain("FlowField");
        record.TryCreateRequest(TestWorld.Context, out IPathRequest? request).Should().BeFalse();
        request.Should().BeNull();
    }

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
        record.Kind = (PathRequestRecordKind)2;
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
        VolumeGuide recreatedGuide = TestRequire.Created(
            record.TryCreateGuide(restoredRequest, out VolumeGuide? createdRecreatedGuide),
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
