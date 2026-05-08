using FixedMathSharp;
using FluentAssertions;
using System;
using Trailblazer.Benchmarks.Navigation;
using Trailblazer.Benchmarks.Pathing;
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
            benchmarks.ReturnMixedActiveGuides();
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

            PathingScenarioSummary transitionChurn = benchmarks.MeasureTransitionRequestChurn();
            transitionChurn.RequestsCreated.Should().Be(PathingScenarioBenchmarks.TransitionChurnRequestCount);
            transitionChurn.CacheKeysRead.Should().Be(PathingScenarioBenchmarks.TransitionChurnRequestCount);

            benchmarks.MeasureFlowFieldFloodOpen32().FieldsVisited.Should().BeGreaterThan(0);
            benchmarks.MeasureFlowFieldFloodBlocker64Large().FieldsVisited.Should().BeGreaterThan(0);
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
}
