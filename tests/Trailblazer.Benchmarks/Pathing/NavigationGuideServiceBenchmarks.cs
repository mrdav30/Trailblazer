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

/// <summary>Measures zero-allocation warm public Flow acquire, sample, and return.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase5", "Graph", "Flow", "Warm")]
public class NavigationGuideServiceBenchmarks
{
    private BenchmarkPathFixture _fixture;
    private PathQuery _query;
    private static readonly GuideSampleWorkBudget SampleBudget = new(
        128,
        128,
        8,
        32,
        32,
        32,
        1);
    private long _warmAllocatedBytes;
    private long _cachedPayloadBytes;

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
        PathQuery published = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            $"warm-guide-{Scenario}",
            width,
            length,
            // Keep the documented expansion budget exact; facade chunking is separate work.
            NavigationGraphBenchmarkScenario.CreateBudget(
                nodeCapacity,
                edgeSlack: checked(nodeCapacity * 16)));
        _query = new PathQuery(
            published.Start,
            published.End,
            published.Agent,
            published.AreaPolicy,
            published.Traversal,
            PathAlgorithm.FlowField,
            published.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(FixedMathSharp.Fixed64.Zero));

        RunWarmFlowRoundTrip();
        RunWarmFlowRoundTrip();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long preflight = RunWarmFlowRoundTrip();
        _warmAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        NavigationFlowFieldPayloadCache cache =
            _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        _cachedPayloadBytes = cache.CachedBytes;
        if (_warmAllocatedBytes != 0
            || preflight == 0
            || cache.ActiveLeaseCount != 0
            || cache.LeasedBytes != 0
            || cache.DetachedBytes != 0)
        {
            throw new InvalidOperationException(
                $"Warm Flow preflight for {Scenario}: allocated_bytes={_warmAllocatedBytes}, "
                + $"signal={preflight}, active_leases={cache.ActiveLeaseCount}, "
                + $"leased_bytes={cache.LeasedBytes}, detached_bytes={cache.DetachedBytes}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine(
            $"PHASE5_FLOW_SERVICE scenario={Scenario} warm_allocated_bytes={_warmAllocatedBytes} "
            + $"cached_payload_bytes={_cachedPayloadBytes} active_leases=0 leased_bytes=0 "
            + "detached_bytes=0 reserved_leases=0 reserved_bytes=0");
        _fixture?.Teardown();
    }

    /// <summary>Acquires, samples, and returns one cached public graph Flow field.</summary>
    [Benchmark]
    public long WarmFlowAcquireSampleDispose() => RunWarmFlowRoundTrip();

    private long RunWarmFlowRoundTrip()
    {
        NavigationGuideStatus request = _fixture.Context.Guides.RequestFlowField(
            _query,
            out NavigationFlowFieldLease? result);
        if (request != NavigationGuideStatus.Success || !result.HasValue)
            throw new InvalidOperationException($"Warm Flow request failed with {request}.");

        NavigationFlowFieldLease guide = result.Value;
        try
        {
            NavigationGuideStatus sample = guide.TrySample(
                _query.Start.Position,
                SampleBudget,
                out FixedMathSharp.Vector3d heading);
            if (sample != NavigationGuideStatus.Success || heading == FixedMathSharp.Vector3d.Zero)
            {
                throw new InvalidOperationException(
                    $"Warm Flow sample failed: sample={sample}, heading={heading}.");
            }
            return guide.OriginIntegrationCost.m_rawValue ^ heading.GetHashCode();
        }
        finally
        {
            guide.Dispose();
        }
    }

}
