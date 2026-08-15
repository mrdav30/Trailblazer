//=======================================================================
// NavigationGuideServiceBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using BenchmarkDotNet.Attributes;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures zero-allocation warm public guide acquisition and cursor use.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase34", "Graph", "Guide", "Warm")]
public class NavigationGuideServiceBenchmarks
{
    private BenchmarkPathFixture _fixture;
    private PathQuery _query;
    private int _expectedWaypointCount;

    /// <summary>Legacy-comparable graph route shape.</summary>
    [Params("OpenPlane32", "Corridor1024")]
    public string Scenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        bool openPlane = string.Equals(Scenario, "OpenPlane32", StringComparison.Ordinal);
        int width = openPlane ? 32 : 1_024;
        int length = openPlane ? 32 : 1;
        int nodeCapacity = checked(width * length);
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(width, length);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                nodeCapacity,
                concurrentQueries: 1));
        _query = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            $"warm-guide-{Scenario}",
            width,
            length,
            // Keep the documented expansion budget exact; facade chunking is separate work.
            NavigationGraphBenchmarkScenario.CreateBudget(nodeCapacity));
        _expectedWaypointCount = openPlane ? 63 : 1_024;

        RunWarmGuideRoundTrip();
        RunWarmGuideRoundTrip();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long preflight = RunWarmGuideRoundTrip();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0 || preflight == 0)
        {
            throw new InvalidOperationException(
                $"Warm guide preflight for {Scenario} allocated {allocated} bytes or returned no signal.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture?.Teardown();

    /// <summary>Acquires, samples, advances, and disposes one cached public graph guide.</summary>
    [Benchmark]
    public long WarmGuideAcquireSampleAdvanceDispose() => RunWarmGuideRoundTrip();

    private long RunWarmGuideRoundTrip()
    {
        NavigationGuideStatus request = _fixture.Context.Guides.RequestGuide(
            _query,
            out NavigationGuideLease? result);
        if (request != NavigationGuideStatus.Success || !result.HasValue)
            throw new InvalidOperationException($"Warm guide request failed with {request}.");

        NavigationGuideLease guide = result.Value;
        try
        {
            if (guide.WaypointCount != _expectedWaypointCount)
            {
                throw new InvalidOperationException(
                    $"Warm guide route changed: expected {_expectedWaypointCount}, got {guide.WaypointCount}.");
            }
            NavigationGuideStatus sample = guide.TryGetCurrentWaypoint(out _, out _);
            NavigationGuideStatus advance = guide.TryAdvanceWaypoint();
            if (sample != NavigationGuideStatus.Success
                || advance != NavigationGuideStatus.Success
                || guide.CurrentWaypointIndex != 1)
            {
                throw new InvalidOperationException(
                    $"Warm guide cursor failed: sample={sample}, advance={advance}, "
                    + $"cursor={guide.CurrentWaypointIndex}.");
            }
            return ((long)guide.WaypointCount << 32)
                | (uint)(guide.CurrentWaypointIndex + 1);
        }
        finally
        {
            guide.Dispose();
        }
    }
}
