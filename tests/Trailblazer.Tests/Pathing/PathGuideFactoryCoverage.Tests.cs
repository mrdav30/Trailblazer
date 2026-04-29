using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class PathGuideFactoryCoverageTests : IDisposable
{
    public PathGuideFactoryCoverageTests()
    {
        TrailblazerWorldManager.Setup();
        TrailblazerWorldManager.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RequestGuide_ShouldRejectNullInvalidAndUnknownRequests()
    {
        PathGuideFactory.RequestGuide(null, out IGuide nullGuide).Should().BeFalse();
        nullGuide.Should().BeNull();

        var invalidRequest = new UnknownRequest { IsValidValue = false };
        PathGuideFactory.RequestGuide(invalidRequest, out IGuide invalidGuide).Should().BeFalse();
        invalidGuide.Should().BeNull();

        RegisterSolidLine("GuideFactoryUnknown", Vector3d.Zero, 2);
        TrailblazerWorldManager.TryGetVoxel(Vector3d.Zero, out Voxel start).Should().BeTrue();
        TrailblazerWorldManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel end).Should().BeTrue();

        var unknownRequest = new UnknownRequest
        {
            OriginValue = Vector3d.Zero,
            StartNodeValue = start,
            TargetValue = new Vector3d(1, 0, 0),
            EndNodeValue = end,
            UnitSizeValue = Fixed64.One,
            MaxPathSearchRangeValue = 1,
            IsValidValue = true
        };

        PathGuideFactory.RequestGuide(unknownRequest, out IGuide unknownGuide).Should().BeFalse();
        unknownGuide.Should().BeNull();
    }

    [Fact]
    public void PathGuideFactory_ShouldBuildHybridGuide_FromTransitionAwareRoute()
    {
        GuidedPathTestScene.RegisterTransitionFallbackScene();

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowTraversalTransitions: true);
        request.Should().NotBeNull();

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromAStar(request);
        hybridRequest.Should().NotBeNull();

        PathGuideFactory.RequestGuide(hybridRequest, out HybridGuide guide).Should().BeTrue();
        guide.Should().NotBeNull();
        guide.ActiveWaypoints.Should().NotBeEmpty();
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void PathGuideFactory_ShouldRespectFlushForceAndCullBehavior()
    {
        RegisterSolidLine("GuideFactoryFlush", Vector3d.Zero, 3);

        AStarPathRequest? request = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One);
        request.Should().NotBeNull();

        PathGuideFactory.RequestGuide(request, out _).Should().BeTrue();
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(1);
        PathGuideFactory.AnyInUse.Should().BeTrue();

        PathGuideFactory.FlushCache();
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(1);

        PathGuideFactory.FlushCache(force: true);
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(0);
        PathGuideFactory.AnyInUse.Should().BeFalse();

        PathGuideFactory.RequestGuide(request, out AStarGuide guide).Should().BeTrue();
        PathGuideFactory.ReturnGuide(guide);
        PathGuideFactory.IsPooling.Should().BeTrue();

        PathGuideFactory.CullExpiredGuides(currentFrame: 601);
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(0);
        PathGuideFactory.IsPooling.Should().BeFalse();
    }

    [Fact]
    public void PathGuideFactory_ShouldInvalidateChartAndVolumeCaches()
    {
        RegisterSolidLine("GuideFactoryInvalidateSolid", Vector3d.Zero, 3);
        AStarPathRequest aStarRequest = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One);
        PathGuideFactory.RequestGuide(aStarRequest, out AStarGuide aStarGuide).Should().BeTrue();
        PathGuideFactory.ReturnGuide(aStarGuide);
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor("GuideFactoryInvalidateSolid");
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(0);

        RegisterVolumeLine(new Vector3d(0, 0, 2), TraversalMedium.Gas, 3, "GuideFactoryInvalidateVolume");
        VolumePathRequest volumeRequest = VolumePathRequest.Create(
            new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas);
        PathGuideFactory.RequestGuide(volumeRequest, out VolumeGuide volumeGuide).Should().BeTrue();
        PathGuideFactory.ReturnGuide(volumeGuide);
        PathGuideFactory.ActiveVolumeGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateVolumeCache();
        PathGuideFactory.ActiveVolumeGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldIgnoreEmptyChartInvalidations()
    {
        RegisterSolidLine("GuideFactoryEmptyInvalidate", Vector3d.Zero, 3);

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);
        request.Should().NotBeNull();

        PathGuideFactory.RequestGuide(request, out AStarGuide guide).Should().BeTrue();
        PathGuideFactory.ReturnGuide(guide);
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor(string.Empty);
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor(null!);
        PathGuideFactory.ActiveAStarGuideCount.Should().Be(1);
    }

    [Fact]
    public void PathGuideFactory_ShouldReturnFalse_ForDisconnectedVolumeRequests()
    {
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(0, 0, 4), TraversalMedium.Gas, "GuideFactoryVolumeGap");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 4), TraversalMedium.Gas, "GuideFactoryVolumeGap");

        VolumePathRequest request = VolumePathRequest.Create(
            new Vector3d(0, 0, 4),
            new Vector3d(2, 0, 4),
            Fixed64.One,
            medium: TraversalMedium.Gas);
        request.Should().NotBeNull();

        PathGuideFactory.RequestGuide(request, out VolumeGuide guide).Should().BeFalse();
        guide.Should().BeNull();
        PathGuideFactory.ActiveVolumeGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldReleaseBorrowedFlowResults_WhenSharedFieldDoesNotContainTheNewStart()
    {
        RegisterSolidLine("GuideFactoryFlowFieldConnected", Vector3d.Zero, 3);
        PathTestFactory.RegisterSingleWalkablePoint("GuideFactoryFlowFieldDisconnected", new Vector3d(4, 0, 0));

        FlowFieldPathRequest cachedRequest = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);
        FlowFieldPathRequest disconnectedRequest = FlowFieldPathRequest.Create(
            new Vector3d(4, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One);

        cachedRequest.Should().NotBeNull();
        disconnectedRequest.Should().NotBeNull();

        PathGuideFactory.RequestGuide(cachedRequest, out FlowFieldGuide cachedGuide).Should().BeTrue();
        PathGuideFactory.ReturnGuide(cachedGuide);
        PathGuideFactory.ActiveFlowGuideCount.Should().Be(1);
        PathGuideFactory.AnyInUse.Should().BeFalse();

        PathGuideFactory.RequestGuide(disconnectedRequest, out FlowFieldGuide disconnectedGuide).Should().BeFalse();
        disconnectedGuide.Should().BeNull();
        PathGuideFactory.ActiveFlowGuideCount.Should().Be(1);
        PathGuideFactory.AnyInUse.Should().BeFalse();
    }

    [Fact]
    public void PathGuideFactory_ShouldReturnFalse_WhenFlowFallbackIsAllowedButNoTransitionRouteExists()
    {
        RegisterSolidLine("GuideFactoryFallbackStart", Vector3d.Zero, 2);
        RegisterSolidLine("GuideFactoryFallbackEnd", new Vector3d(4, 0, 0), 2);

        FlowFieldPathRequest request = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(5, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true);

        request.Should().NotBeNull();
        PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide).Should().BeFalse();
        guide.Should().BeNull();
        PathGuideFactory.ActiveFlowGuideCount.Should().Be(0);
    }

    private static void RegisterSolidLine(string chartName, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
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

    private sealed class UnknownRequest : IPathRequest
    {
        public Vector3d Origin => OriginValue;
        public Voxel StartNode => StartNodeValue;
        public Vector3d TargetPosition => TargetValue;
        public Voxel EndNode => EndNodeValue;
        public Fixed64 UnitSize => UnitSizeValue;
        public bool HasZeroDisplacement => StartNodeValue == EndNodeValue;
        public bool AllowUnwalkableEndpoints => false;
        public int MaxPathSearchRange { get => MaxPathSearchRangeValue; set => MaxPathSearchRangeValue = value; }
        public bool HasOrigin => StartNodeValue != null;
        public bool HasDestination => EndNodeValue != null;
        public bool HasValidEndpoints => HasOrigin && HasDestination;
        public bool IsValid => IsValidValue;
        public int RequestCacheKey => 1234;

        internal Vector3d OriginValue;
        internal Voxel StartNodeValue = null!;
        internal Vector3d TargetValue;
        internal Voxel EndNodeValue = null!;
        internal Fixed64 UnitSizeValue = Fixed64.One;
        internal int MaxPathSearchRangeValue;
        internal bool IsValidValue;

        public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

        public bool TrySetUnitSize(Fixed64 unitSize) => false;
    }
}
