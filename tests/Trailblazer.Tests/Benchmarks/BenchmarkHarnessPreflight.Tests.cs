using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Benchmarks.Navigation;
using Trailblazer.Benchmarks.Pathing;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Benchmarks;

[Collection("PathingCollection")]
public sealed class BenchmarkHarnessPreflightTests
{
    [Fact]
    public void AStarPathRequestBenchmarks_ShouldKeepAllConfiguredRequestsValid_AfterGlobalSetup()
    {
        var benchmarks = new AStarPathRequestBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.RawSurvey_OpenPlane32().HasPath.Should().BeTrue();
            benchmarks.ColdGuide_OpenPlane32().Should().BeTrue();
            benchmarks.ColdGuide_Corridor64().Should().BeTrue();
            benchmarks.ColdGuide_Corridor256().Should().BeTrue();
            benchmarks.ColdGuide_Corridor1024().Should().BeTrue();
            benchmarks.ColdGuide_BlockerField64().Should().BeTrue();
            benchmarks.ColdGuide_Heuristic_Manhattan().Should().BeTrue();
            benchmarks.ColdGuide_Heuristic_Octile().Should().BeTrue();
            benchmarks.ColdGuide_Heuristic_Euclidean().Should().BeTrue();
            benchmarks.FailedRoute_ChokeUnitSize2().Should().BeFalse();
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void AStarHeuristicBenchmarks_ShouldKeepEquivalentRouteShapeWithoutOpenPlaneChurn()
    {
        try
        {
            TrailblazerWorldManager.Setup();
            TrailblazerWorldManager.TryAddGrid(
                new GridConfiguration(new Vector3d(44, -4, -4), new Vector3d(120, 8, 72)),
                out _).Should().BeTrue();

            Vector3d origin = new(48, 0, 0);
            Vector3d destination = RegisterOpenPlane("PreflightAStarHeuristic64", 64, origin);

            AStarHeuristicSummary manhattan = MeasureAStarHeuristic(
                origin,
                destination,
                HeuristicMethod.Manhattan);
            AStarHeuristicSummary octile = MeasureAStarHeuristic(
                origin,
                destination,
                HeuristicMethod.Octile);
            AStarHeuristicSummary euclidean = MeasureAStarHeuristic(
                origin,
                destination,
                HeuristicMethod.Euclidean);

            octile.RouteCost.Should().BeLessThanOrEqualTo(manhattan.RouteCost);
            euclidean.RouteCost.Should().BeLessThanOrEqualTo(manhattan.RouteCost);
            octile.WaypointCount.Should().BeGreaterThan(0);
            euclidean.WaypointCount.Should().BeGreaterThan(0);

            int acceptedClosedNodeBudget = manhattan.ClosedNodes + 2;
            string summary = $"{manhattan}; {octile}; {euclidean}";
            octile.ClosedNodes.Should().BeLessThanOrEqualTo(acceptedClosedNodeBudget, summary);
            euclidean.ClosedNodes.Should().BeLessThanOrEqualTo(acceptedClosedNodeBudget, summary);
        }
        finally
        {
            PathManager.Reset();
            TrailblazerWorldManager.Reset();
            TrailblazerManager.Reset();
        }
    }

    [Fact]
    public void VolumePathRequestBenchmarks_ShouldKeepAllConfiguredRequestsValid_AfterGlobalSetup()
    {
        var benchmarks = new VolumePathRequestBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.RawSurvey_DirectGasCorridor().Should().BeTrue();
            benchmarks.ColdGuide_DirectGasCorridor().Should().BeTrue();
            benchmarks.ColdGuide_LShapeGasPath().Should().BeTrue();
            benchmarks.WarmGuide_DirectGasCorridor().Should().BeTrue();
            benchmarks.WarmGuide_LShapeGasPath().Should().BeTrue();
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void NavSteeringBenchmarks_ShouldKeepGuidedScenariosValid_AfterGlobalSetup()
    {
        var benchmarks = new NavSteeringBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.FirstFrame_DirectLOS().Should().NotBe(default);
            benchmarks.FirstFrame_GuidedAStar().Should().NotBe(default);
            benchmarks.SteadyState_GuidedAStar().Should().NotBe(default);
            benchmarks.SteadyState_GuidedFlowField().Should().NotBe(default);
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void GuideCacheBenchmarks_ShouldSeedMixedCachePressureScenarios_AfterGlobalSetup()
    {
        var benchmarks = new GuideCacheBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.SeedMixedCacheForInvalidation();
            CacheInvalidationCardinality noMatch = benchmarks.MeasureInvalidateMixedCacheFor_NoMatchingChart();
            noMatch.EntriesScanned.Should().Be(0);
            noMatch.EntriesMatched.Should().Be(0);
            noMatch.EntriesRemoved.Should().Be(0);

            long noMatchAllocated = MeasureAllocatedBytes(() => benchmarks.MeasureInvalidateMixedCacheFor_NoMatchingChart());
            noMatchAllocated.Should().BeLessThan(128);

            benchmarks.SeedMixedCacheForInvalidation();
            CacheInvalidationCardinality solid = benchmarks.MeasureInvalidateMixedCacheFor_MatchingSolidChart();
            solid.EntriesMatched.Should().Be(GuideCacheBenchmarks.MixedCacheEntriesPerFamily * 2);
            solid.EntriesRemoved.Should().Be(solid.EntriesMatched);

            benchmarks.SeedMixedCacheForInvalidation();
            CacheInvalidationCardinality volume = benchmarks.MeasureInvalidateMixedCacheFor_MatchingVolumeChart();
            volume.EntriesMatched.Should().Be(GuideCacheBenchmarks.MixedCacheEntriesPerFamily);
            volume.EntriesRemoved.Should().Be(volume.EntriesMatched);

            benchmarks.SeedMixedCacheForInvalidation();
            CacheInvalidationCardinality hybrid = benchmarks.MeasureInvalidateMixedCacheFor_MatchingHybridChart();
            hybrid.EntriesMatched.Should().Be(GuideCacheBenchmarks.MixedCacheEntriesPerFamily);
            hybrid.EntriesRemoved.Should().Be(hybrid.EntriesMatched);

            benchmarks.SeedMixedCacheForCull();
            CacheCullCardinality freshCull = benchmarks.MeasureCullMixedCache_NoStale();
            freshCull.EntriesRemoved.Should().Be(0);

            benchmarks.SeedMixedCacheForCullWithActiveQuarter();
            CacheCullCardinality staleCull = benchmarks.MeasureCullMixedCache_StaleWithActiveQuarter();
            staleCull.EntriesRemoved.Should().BeGreaterThan(0);
            staleCull.ActiveEntriesRemaining.Should().Be(GuideCacheBenchmarks.MixedActiveEntriesPerFamily * 3);
        }
        finally
        {
            benchmarks.ClearMixedCachePressure();
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void NavigationScenarioBenchmarks_ShouldKeepMixedAgentFramesValid_AfterGlobalSetup()
    {
        var benchmarks = new NavigationScenarioBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.PrepareFirstFrameMixedSteering();
            NavigationScenarioSummary firstFrame100 = benchmarks.MeasureFirstFrameMixedSteering100();
            firstFrame100.AgentsProcessed.Should().Be(NavigationScenarioBenchmarks.MixedAgentCount100);
            firstFrame100.DirectLosAgents.Should().BeGreaterThan(0);
            firstFrame100.AStarAgents.Should().BeGreaterThan(0);
            firstFrame100.FlowFieldAgents.Should().BeGreaterThan(0);
            firstFrame100.CombinedSteeringAgents.Should().BeGreaterThan(0);
            firstFrame100.GuideBackedAgents.Should().BeGreaterThan(0);

            NavigationScenarioSummary steady500 = benchmarks.MeasureFixedStepMixedSteering500();
            steady500.AgentsProcessed.Should().Be(NavigationScenarioBenchmarks.MixedAgentCount500);
            steady500.NonZeroHeadings.Should().BeGreaterThan(0);
            steady500.CombinedSteeringAgents.Should().BeGreaterThan(0);
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    [Fact]
    public void PathingScenarioBenchmarks_ShouldKeepScenarioRoutesValid_AfterGlobalSetup()
    {
        var benchmarks = new PathingScenarioBenchmarks();

        try
        {
            benchmarks.GlobalSetup();

            benchmarks.PrepareDynamicObstacleRepathWave();
            PathingScenarioSummary dynamicWave = benchmarks.MeasureDynamicObstacleUpdateRepathWave();
            dynamicWave.ChartUpdates.Should().Be(1);
            dynamicWave.GuidesResolved.Should().Be(PathingScenarioBenchmarks.DynamicRepathWaveCount);

            benchmarks.PrepareFlowFieldSharing();
            PathingScenarioSummary flowSharing = benchmarks.MeasureFlowFieldSharing500();
            flowSharing.GuidesResolved.Should().Be(PathingScenarioBenchmarks.FlowSharingCount500);

            benchmarks.PrepareReachabilityFirstHit();
            PathingScenarioSummary reachability = benchmarks.MeasureReachabilityFirstHitClearanceCombos();
            reachability.RequestsAttempted.Should().Be(PathingScenarioBenchmarks.ReachabilityComboCount);
            reachability.FailedRoutes.Should().Be(PathingScenarioBenchmarks.ReachabilityComboCount);
            reachability.ReachabilitySnapshotBuilds.Should().Be(PathingScenarioBenchmarks.ReachabilityComboCount);
            reachability.ReachabilitySnapshotsRetained.Should().Be(1);

            benchmarks.PrepareReachabilityFirstHit();
            PathingScenarioSummary reachabilityWorkload = benchmarks.MeasureReachabilityFirstHitWorkloadCombos();
            reachabilityWorkload.RequestsAttempted.Should().Be(PathingScenarioBenchmarks.ReachabilityWorkloadComboCount);
            reachabilityWorkload.FailedRoutes.Should().Be(PathingScenarioBenchmarks.ReachabilityWorkloadComboCount);
            reachabilityWorkload.ReachabilitySnapshotBuilds.Should().Be(PathingScenarioBenchmarks.ReachabilityWorkloadComboCount);
            reachabilityWorkload.ReachabilitySnapshotsRetained.Should().Be(1);
            reachabilityWorkload.ReachabilityScratchCapacity.Should().BeGreaterThan(0);

            benchmarks.PrepareReachabilityFirstHit();
            benchmarks.MeasureReachabilityFirstHitWorkloadCombos();
            benchmarks.PrepareReachabilityFirstHit();
            long reachabilityWorkloadAllocated =
                MeasureAllocatedBytes(() => benchmarks.MeasureReachabilityFirstHitWorkloadCombos());
            reachabilityWorkloadAllocated.Should().BeLessThan(2_000_000);

            benchmarks.PrepareReachabilitySteadyHit();
            PathingScenarioSummary reachabilitySteady = benchmarks.MeasureReachabilitySteadyHitActiveCombo();
            reachabilitySteady.RequestsAttempted.Should().Be(PathingScenarioBenchmarks.ReachabilitySteadyHitOperations);
            reachabilitySteady.FailedRoutes.Should().Be(PathingScenarioBenchmarks.ReachabilitySteadyHitOperations);
            reachabilitySteady.ReachabilitySnapshotBuilds.Should().Be(0);
            reachabilitySteady.ReachabilitySnapshotsRetained.Should().Be(1);
            reachabilitySteady.ReachabilityScratchCapacity.Should().BeGreaterThan(0);

            benchmarks.PrepareReachabilityInvalidation();
            PathingScenarioSummary reachabilityInvalidation = benchmarks.MeasureReachabilityInvalidation();
            reachabilityInvalidation.ChartUpdates.Should().Be(2);
            reachabilityInvalidation.ReachabilitySnapshotsRetained.Should().Be(0);
            reachabilityInvalidation.ReachabilityScratchCapacity.Should().BeGreaterThan(0);

            PathingScenarioSummary transitionChurn = benchmarks.MeasureTransitionRequestChurn();
            transitionChurn.RequestsCreated.Should().Be(PathingScenarioBenchmarks.TransitionChurnRequestCount);
            transitionChurn.CacheKeysRead.Should().Be(PathingScenarioBenchmarks.TransitionChurnRequestCount);

            PathingScenarioSummary floodOpen32 = benchmarks.MeasureFlowFieldFloodOpen32();
            PathingScenarioSummary floodOpen64 = benchmarks.MeasureFlowFieldFloodOpen64();
            PathingScenarioSummary floodOpen128 = benchmarks.MeasureFlowFieldFloodOpen128();
            PathingScenarioSummary floodBlocker64Default = benchmarks.MeasureFlowFieldFloodBlocker64Default();
            PathingScenarioSummary floodBlocker64Large = benchmarks.MeasureFlowFieldFloodBlocker64Large();

            floodOpen32.GuidesResolved.Should().BeGreaterThan(0);
            floodOpen64.GuidesResolved.Should().BeGreaterThan(0);
            floodOpen128.GuidesResolved.Should().BeGreaterThan(0);
            int open32FieldsPerSurvey = floodOpen32.FieldsVisited / floodOpen32.GuidesResolved;
            int open64FieldsPerSurvey = floodOpen64.FieldsVisited / floodOpen64.GuidesResolved;
            int open128FieldsPerSurvey = floodOpen128.FieldsVisited / floodOpen128.GuidesResolved;

            floodOpen32.FieldsVisited.Should().BeGreaterThan(0);
            open64FieldsPerSurvey.Should().BeGreaterThan(open32FieldsPerSurvey);
            open128FieldsPerSurvey.Should().BeGreaterThan(open64FieldsPerSurvey);
            floodOpen32.MaxPathSearchRange.Should().BeGreaterThan(0);
            floodOpen64.MaxPathSearchRange.Should().Be(floodOpen32.MaxPathSearchRange);
            floodOpen128.MaxPathSearchRange.Should().Be(floodOpen64.MaxPathSearchRange);
            floodOpen32.ExtraFloodRange.Should().Be(FlowFieldPathRequest.DefaultExtraFloodRange);
            floodBlocker64Default.ExtraFloodRange.Should().Be(FlowFieldPathRequest.DefaultExtraFloodRange);
            floodBlocker64Large.ExtraFloodRange.Should().Be(FlowFieldPathRequest.DefaultExtraFloodRange * 4);
            floodBlocker64Large.FieldsVisited.Should().BeGreaterThanOrEqualTo(floodBlocker64Default.FieldsVisited);
        }
        finally
        {
            benchmarks.GlobalCleanup();
        }
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Vector3d RegisterOpenPlane(string chartName, int size, Vector3d minBounds)
    {
        bool[,,] data = new bool[1, size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                data[0, x, z] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
        return new Vector3d(
            minBounds.x + (Fixed64)(size - 1),
            minBounds.y,
            minBounds.z + (Fixed64)(size - 1));
    }

    private static AStarHeuristicSummary MeasureAStarHeuristic(
        Vector3d origin,
        Vector3d destination,
        HeuristicMethod heuristic)
    {
        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            origin,
            destination,
            Fixed64.One,
            heuristic));
        AStarSurveyResult result = AStarSurveyor.Shared.FindPath(request);
        result.HasPath.Should().BeTrue();

        int closedNodes = CountClosedNodes(request);
        return new AStarHeuristicSummary(
            heuristic,
            closedNodes,
            result.Waypoints[^1].PathCost,
            result.Waypoints.Length);
    }

    private static int CountClosedNodes(AStarPathRequest request)
    {
        var surveyor = new AStarSurveyor();
        ReflectionUtility.SetPrivateField(surveyor, "_request", request);

        PathHeap<SolidChartPartition> heap =
            ReflectionUtility.GetPrivateField<PathHeap<SolidChartPartition>>(surveyor, "_heap");
        SwiftCollections.SwiftDictionary<Voxel, AStarVoxelMeta> meta =
            ReflectionUtility.GetPrivateField<SwiftCollections.SwiftDictionary<Voxel, AStarVoxelMeta>>(
                surveyor,
                "_meta");

        request.StartNode!.TryGetPartition(out SolidChartPartition? startPartition).Should().BeTrue();
        meta[request.StartNode] = new AStarVoxelMeta { PathCost = 0 };
        heap.Add(TestRequire.NotNull(startPartition), pathCost: 0);

        ReflectionUtility.InvokePrivate<bool>(surveyor, "TracePath").Should().BeTrue();

        int closedNodes = 0;
        foreach (SolidChartPartition _ in heap.EnumerateClosed())
            closedNodes++;

        return closedNodes;
    }

    private readonly record struct AStarHeuristicSummary(
        HeuristicMethod Heuristic,
        int ClosedNodes,
        int RouteCost,
        int WaypointCount);
}
