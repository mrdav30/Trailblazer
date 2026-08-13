using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class PathGuideFactoryCoverageTests : IDisposable
{
    public PathGuideFactoryCoverageTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RequestGuide_ShouldRejectNullInvalidAndUnknownRequests()
    {
        PathGuideFactory.RequestGuide(null!, out IGuide? nullGuide).Should().BeFalse();
        nullGuide.Should().BeNull();

        var invalidRequest = new TestPathRequest { IsValid = false };
        PathGuideFactory.RequestGuide(invalidRequest, out IGuide? invalidGuide).Should().BeFalse();
        invalidGuide.Should().BeNull();

        using TrailblazerWorldContext otherContext = PathTestFactory.CreateContextWithGrid();
        var foreignContextRequest = new TestPathRequest(otherContext) { IsValid = true };
        PathGuideFactory.RequestGuide(foreignContextRequest, out IGuide? foreignGuide).Should().BeFalse();
        foreignGuide.Should().BeNull();

        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryUnknown", Vector3d.Zero, 2);
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

        var unknownRequest = new TestPathRequest
        {
            Origin = Vector3d.Zero,
            StartNode = start,
            TargetPosition = new Vector3d(1, 0, 0),
            EndNode = end,
            UnitSize = Fixed64.One,
            MaxPathSearchRange = 1,
            IsValid = true
        };

        PathGuideFactory.RequestGuide(unknownRequest, out IGuide? unknownGuide).Should().BeFalse();
        unknownGuide.Should().BeNull();
    }

    [Fact]
    public void RequestAStar_ShouldFastFailReachabilityBlockedRequests()
    {
        bool[,,] data = PathTestFactory.BuildSingleVoxelChoke();
        PathTestFactory.RegisterFromData(TestWorld.Context, "GuideFactoryChokeFastFail", data, Vector3d.Zero);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(6, 0, 2),
            Fixed64.Two));

        SolidPartitionReachability.IsProvablyUnreachable(request).Should().BeTrue();

        PathGuideFactory.RequestAStar(request).Should().BeNull();
        PathGuideFactory.TotalAStarGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldBuildHybridGuide_FromTransitionAwareRoute()
    {
        GuidedPathTestScene.RegisterTransitionFallbackScene(TestWorld.Context);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowTraversalTransitions: true));

        HybridPathRequest hybridRequest = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(request));
        HybridGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(hybridRequest, out HybridGuide? createdGuide),
            createdGuide);
        guide.ActiveWaypoints.Should().NotBeEmpty();
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void RequestGuideTyped_ShouldReturnFalseAndReleaseGuide_WhenGuideTypeDoesNotMatch()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryTypedMismatch", Vector3d.Zero, 3);
        AStarPathRequest request = TestRequire.NotNull(
            AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        PathGuideFactory.RequestGuide<FlowFieldGuide>(request, out FlowFieldGuide? mismatchedGuide).Should().BeFalse();

        mismatchedGuide.Should().BeNull();
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);
        PathGuideFactory.InUseAStarGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldRespectFlushForceAndCullBehavior()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryFlush", Vector3d.Zero, 3);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        PathGuideFactory.RequestGuide(request, out _).Should().BeTrue();
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);
        PathGuideFactory.AnyInUse.Should().BeTrue();

        PathGuideFactory.FlushCache();
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);

        PathGuideFactory.FlushCache(force: true);
        PathGuideFactory.TotalAStarGuideCount.Should().Be(0);
        PathGuideFactory.AnyInUse.Should().BeFalse();

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
        PathGuideFactory.ReturnGuide(guide);
        PathGuideFactory.IsPooling.Should().BeTrue();

        PathGuideFactory.CullExpiredGuides(currentFrame: 601);
        PathGuideFactory.TotalAStarGuideCount.Should().Be(0);
        PathGuideFactory.IsPooling.Should().BeFalse();
    }

    [Fact]
    public void PathGuideFactory_ShouldInvalidateChartAndVolumeCaches()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryInvalidateSolid", Vector3d.Zero, 3);
        AStarPathRequest aStarRequest = TestRequire.NotNull(
            AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));
        AStarGuide aStarGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(aStarRequest, out AStarGuide? createdAStarGuide),
            createdAStarGuide);
        PathGuideFactory.ReturnGuide(aStarGuide);
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor("GuideFactoryInvalidateSolid");
        PathGuideFactory.TotalAStarGuideCount.Should().Be(0);

        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 2), TraversalMedium.Gas, 3, "GuideFactoryInvalidateVolume");
        VolumePathRequest volumeRequest = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide volumeGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(volumeRequest, out VolumeGuide? createdVolumeGuide),
            createdVolumeGuide);
        PathGuideFactory.ReturnGuide(volumeGuide);
        PathGuideFactory.TotalVolumeGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateVolumeCache();
        PathGuideFactory.TotalVolumeGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldIgnoreEmptyChartInvalidations()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryEmptyInvalidate", Vector3d.Zero, 3);

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
        PathGuideFactory.ReturnGuide(guide);
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor(string.Empty);
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor(null!);
        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);
    }

    [Fact]
    public void PathGuideFactory_ShouldReturnFalse_ForDisconnectedVolumeRequests()
    {
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(0, 0, 4), TraversalMedium.Gas, "GuideFactoryVolumeGap");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(2, 0, 4), TraversalMedium.Gas, "GuideFactoryVolumeGap");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 4),
            new Vector3d(2, 0, 4),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        PathGuideFactory.RequestGuide(request, out VolumeGuide? guide).Should().BeFalse();
        guide.Should().BeNull();
        PathGuideFactory.TotalVolumeGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldReleaseBorrowedFlowResults_WhenSharedFieldDoesNotContainTheNewStart()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryFlowFieldConnected", Vector3d.Zero, 3);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "GuideFactoryFlowFieldDisconnected", new Vector3d(4, 0, 0));

        FlowFieldPathRequest cachedRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        FlowFieldPathRequest disconnectedRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(4, 0, 0),
            new Vector3d(2, 0, 0),
            Fixed64.One));
        disconnectedRequest.RequestCacheKey.Should().Be(cachedRequest.RequestCacheKey);

        FlowFieldGuide cachedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(cachedRequest, out FlowFieldGuide? createdCachedGuide),
            createdCachedGuide);
        PathGuideFactory.AnyInUse.Should().BeTrue();

        PathGuideFactory.ReturnGuide(cachedGuide);

        PathGuideFactory.TotalFlowGuideCount.Should().Be(1);
        PathGuideFactory.InUseFlowGuideCount.Should().Be(0);

        PathGuideFactory.RequestGuide(disconnectedRequest, out FlowFieldGuide? disconnectedGuide).Should().BeFalse();
        disconnectedGuide.Should().BeNull();
        PathGuideFactory.InUseFlowGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldRecomputeDestinationCentricFlowField_ForFartherOrigin()
    {
        PathTestFactory.RegisterSolidLine(
            TestWorld.Context,
            "GuideFactoryFlowFieldExpansion",
            Vector3d.Zero,
            8);
        FlowFieldPathRequest nearRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            new Vector3d(6, 0, 0),
            new Vector3d(7, 0, 0),
            Fixed64.One));
        nearRequest.ExtraFloodRange = 0;
        FlowFieldPathRequest farRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(7, 0, 0),
            Fixed64.One));
        farRequest.ExtraFloodRange = 0;
        farRequest.RequestCacheKey.Should().Be(nearRequest.RequestCacheKey);

        FlowFieldGuide nearGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(nearRequest, out FlowFieldGuide? createdNearGuide),
            createdNearGuide);

        FlowFieldGuide farGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(farRequest, out FlowFieldGuide? createdFarGuide),
            createdFarGuide);
        farGuide.TryGetMovementDirection(Vector3d.Zero, out Vector3d direction).Should().BeTrue();
        direction.X.Should().BeGreaterThan(Fixed64.Zero);
        nearGuide.TryGetMovementDirection(new Vector3d(6, 0, 0), out Vector3d nearDirection).Should().BeTrue();
        nearDirection.X.Should().BeGreaterThan(Fixed64.Zero,
            "the original active guide must retain its shared partial field");
        FlowFieldSurveyResult promotedResult = TestRequire.NotNull(farGuide.FlowMap);

        FlowFieldGuide repeatedFarGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(farRequest, out FlowFieldGuide? createdRepeatedFarGuide),
            createdRepeatedFarGuide);
        repeatedFarGuide.FlowMap.Should().BeSameAs(promotedResult,
            "the larger covering field should replace the partial destination entry for subsequent agents");

        PathGuideFactory.InvalidateCacheFor("GuideFactoryFlowFieldExpansion");

        nearGuide.TryGetMovementDirection(new Vector3d(6, 0, 0), out _).Should().BeFalse();
        farGuide.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse(
            "the active promoted field must not survive invalidation of its utilized chart");
        PathGuideFactory.InUseFlowGuideCount.Should().Be(0);

        PathGuideFactory.ReturnGuide(nearGuide);
        PathGuideFactory.ReturnGuide(farGuide);
        PathGuideFactory.ReturnGuide(repeatedFarGuide);
        PathGuideFactory.InUseFlowGuideCount.Should().Be(0);
    }

    [Fact]
    public void PathGuideFactory_ShouldReturnFalse_WhenFlowFallbackIsAllowedButNoTransitionRouteExists()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryFallbackStart", Vector3d.Zero, 2);
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryFallbackEnd", new Vector3d(4, 0, 0), 2);

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(5, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));
        PathGuideFactory.RequestGuide(request, out FlowFieldGuide? guide).Should().BeFalse();
        guide.Should().BeNull();
        PathGuideFactory.TotalFlowGuideCount.Should().Be(0);
    }

    [Fact]
    public void FlowTransitionFallbackPlan_ShouldInvalidateForIntermediateSegmentChart_WhenBoundaryAnchorsAreUsed()
    {
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryBoundaryStart", Vector3d.Zero);
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryBoundaryBridge", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryBoundaryEntry", new Vector3d(2, 0, 0));
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryBoundaryExit", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryBoundaryEnd", new Vector3d(5, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guide-factory-boundary-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(5, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();
        PathGuideFactory.ReturnGuide(guide);

        PathGuideFactory.TotalHybridRoutePlanCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor("GuideFactoryBoundaryBridge");

        PathGuideFactory.TotalHybridRoutePlanCount.Should().Be(0);
    }

    [Fact]
    public void AStarTransitionFallbackResult_ShouldInvalidateForVolumeSegmentChart()
    {
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryAStarFallbackStart", new Vector3d(-1, 0, 0));
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryAStarFallbackEnd", new Vector3d(3, 0, 0));
        RegisterVolumePoint("GuideFactoryAStarFallbackWaterEntry", new Vector3d(0, 0, 0), TraversalMedia.Liquid);
        RegisterVolumePoint("GuideFactoryAStarFallbackWaterMiddle", new Vector3d(1, 0, 0), TraversalMedia.Liquid);
        RegisterVolumePoint("GuideFactoryAStarFallbackWaterExit", new Vector3d(2, 0, 0), TraversalMedia.Liquid);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guide-factory-water-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(new Vector3d(-1, 0, 0)),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(0, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "guide-factory-water-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(-1, 0, 0),
            new Vector3d(3, 0, 0),
            Fixed64.One,
            HeuristicMethod.Manhattan,
            allowTraversalTransitions: true));

        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
        PathGuideFactory.ReturnGuide(guide);

        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);

        PathGuideFactory.InvalidateCacheFor("GuideFactoryAStarFallbackWaterMiddle");

        PathGuideFactory.TotalAStarGuideCount.Should().Be(0);
    }

    [Fact]
    public void FlowFieldCacheHit_ShouldAllocateWithinFiftyBytesOfAStarHit()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryWarmHitAllocation", Vector3d.Zero, 4);

        AStarPathRequest aStarRequest = TestRequire.NotNull(
            AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(
            FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));

        const int iterations = 256;
        long aStarAllocated = MeasureWarmHitAllocation<AStarGuide>(aStarRequest, iterations);
        long flowFieldAllocated = MeasureWarmHitAllocation<FlowFieldGuide>(flowFieldRequest, iterations);

        flowFieldAllocated.Should().BeLessThanOrEqualTo(aStarAllocated + (50L * iterations));
    }

    [Fact]
    public void WarmGuideHits_ShouldAllocateNearZero_WhenReturnedGuidesCanBeReused()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryWarmReuseSolid", Vector3d.Zero, 4);
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 4), TraversalMedium.Gas, 4, "GuideFactoryWarmReuseVolume");

        AStarPathRequest aStarRequest = TestRequire.NotNull(
            AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(
            FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        VolumePathRequest volumeRequest = TestRequire.NotNull(
            VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 4), new Vector3d(3, 0, 4), Fixed64.One, medium: TraversalMedium.Gas));

        const int iterations = 256;

        long aStarAllocated = MeasureWarmHitAllocation<AStarGuide>(aStarRequest, iterations);
        long flowFieldAllocated = MeasureWarmHitAllocation<FlowFieldGuide>(flowFieldRequest, iterations);
        long volumeAllocated = MeasureWarmHitAllocation<VolumeGuide>(volumeRequest, iterations);

        string allocationSummary = $"A*={aStarAllocated}, FlowField={flowFieldAllocated}, Volume={volumeAllocated} bytes.";
        aStarAllocated.Should().BeLessThan(1_024, allocationSummary);
        flowFieldAllocated.Should().BeLessThan(1_024, allocationSummary);
        volumeAllocated.Should().BeLessThan(1_024, allocationSummary);
    }

    [Fact]
    public void RequestCacheKeys_ShouldNotAllocateSteadyState()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "GuideFactoryRequestKeySolid", Vector3d.Zero, 4);
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 4), TraversalMedium.Gas, 4, "GuideFactoryRequestKeyVolume");

        AStarPathRequest aStarRequest = TestRequire.NotNull(
            AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(
            FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero, new Vector3d(3, 0, 0), Fixed64.One));
        VolumePathRequest volumeRequest = TestRequire.NotNull(
            VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 4), new Vector3d(3, 0, 4), Fixed64.One, medium: TraversalMedium.Gas));

        const int iterations = 1_024;

        long aggregate = 0;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                aggregate += aStarRequest.RequestCacheKey.GetHashCode();
                aggregate += flowFieldRequest.RequestCacheKey.GetHashCode();
                aggregate += volumeRequest.RequestCacheKey.GetHashCode();
            }
        });

        aggregate.Should().NotBe(0);
        allocated.Should().BeLessThan(128);
    }

    [Fact]
    public void ReusableSurveyResultCacheWarmCheckout_ShouldNotAllocateSteadyState()
    {
        var request = new TestPathRequest { IsValid = true };
        var cache = new ReusableSurveyResultCache<FakeSurveyResult>();

        cache.TryGetOrCreate(request, () => FakeSurveyResult.Create(request.RequestCacheKey), out FakeSurveyResult result).Should().BeTrue();
        cache.Return(result, dispose: false);

        const int iterations = 256;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                if (!cache.TryCheckout(request, out FakeSurveyResult warmResult))
                    throw new InvalidOperationException("Expected cache warm hit.");

                cache.Return(warmResult, dispose: false);
            }
        });

        allocated.Should().BeLessThan(1_024);
    }

    [Fact]
    public void GuidePoolRentRelease_ShouldNotAllocateSteadyState()
    {
        var pool = new GuidePool<FakeGuide>(() => new FakeGuide(), static guide => guide.Reset());
        FakeGuide warmGuide = pool.Rent();
        pool.Release(warmGuide);

        const int iterations = 256;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                FakeGuide guide = pool.Rent();
                pool.Release(guide);
            }
        });

        allocated.Should().BeLessThan(128);
    }

    [Fact]
    public void RoutePlanChartKeyCollection_ShouldFallBackToEndpointChartOwners_WhenSegmentKeysAreMissing()
    {
        PathTestFactory.RegisterSolidPoint(TestWorld.Context, "GuideFactoryEndpointSolid", Vector3d.Zero);
        RegisterVolumePoint("GuideFactoryEndpointGas", new Vector3d(1, 0, 0), TraversalMedia.Gas);
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        var request = new TestPathRequest
        {
            Origin = Vector3d.Zero,
            TargetPosition = new Vector3d(1, 0, 0),
            StartNode = start,
            EndNode = end,
            MaxPathSearchRange = 1,
            IsValid = true
        };
        var plan = new HybridRoutePlan(
            new[] { HybridRouteStep.Segment(request) },
            Array.Empty<TraversalTransition>(),
            totalPathCost: 0);

        string[] chartKeys = ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathGuideFactory),
            "CollectRoutePlanChartKeys",
            plan);
        string[] nullPlanKeys = ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathGuideFactory),
            "CollectRoutePlanChartKeys",
            (object)null!);
        var waypointOnlyPlan = new HybridRoutePlan(
            new[] { HybridRouteStep.Waypoint(TestWorld.Context, Vector3d.Zero) },
            Array.Empty<TraversalTransition>(),
            totalPathCost: 0);
        string[] waypointOnlyKeys = ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathGuideFactory),
            "CollectRoutePlanChartKeys",
            waypointOnlyPlan);
        var nullSafeKeys = new SwiftHashSet<string>();
        ReflectionUtility.InvokePrivateStatic<object?>(
            typeof(PathGuideFactory),
            "AddChartKeys",
            nullSafeKeys,
            null!);
        ReflectionUtility.InvokePrivateStatic<object?>(
            typeof(PathGuideFactory),
            "AddRequestEndpointChartOwners",
            nullSafeKeys,
            null!);
        ReflectionUtility.InvokePrivateStatic<object?>(
            typeof(PathGuideFactory),
            "AddVoxelChartOwners",
            nullSafeKeys,
            null!);

        chartKeys.Should().Contain("GuideFactoryEndpointSolid");
        chartKeys.Should().Contain("GuideFactoryEndpointGas");
        nullPlanKeys.Should().BeEmpty();
        waypointOnlyKeys.Should().BeEmpty();
        nullSafeKeys.Should().BeEmpty();
    }

    private static void RegisterVolumePoint(string chartName, Vector3d position, TraversalMedia media)
    {
        var data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = new NavigationChartCell(media);

        PathManager.Register(NavigationChart.From3D(chartName, data, position, Fixed64.One));
    }

    private static long MeasureWarmHitAllocation<T>(IPathRequest request, int iterations)
        where T : class, IGuide
    {
        T warmGuide = TestRequire.Created(PathGuideFactory.RequestGuide(request, out T? createdWarmGuide), createdWarmGuide);
        PathGuideFactory.ReturnGuide(warmGuide);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            if (!PathGuideFactory.RequestGuide(request, out T? guide) || guide == null)
                throw new InvalidOperationException($"Expected warm {typeof(T).Name} cache hit.");

            PathGuideFactory.ReturnGuide(guide);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

}
