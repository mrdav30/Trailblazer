//=======================================================================
// NavigationSurfaceAStarBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures production endpoint admission plus one uncached graph A* corridor search.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase34", "Graph", "AStar", "Cold")]
public class NavigationSurfaceAStarBenchmarks
{
    private BenchmarkPathFixture _fixture;
    private NavigationAStarWorkspace _workspace;
    private NavigationQueryAdmissionWork _admission;
    private PathQuery _query;
    private long _workspaceAllocatedBytes;
    private int _lookupProbes;
    private int _endpointCandidates;
    private int _expandedNodes;
    private int _evaluatedEdges;
    private int _heapWork;
    private long _resultBytes;

    /// <summary>Representative deterministic expansion/work-budget size.</summary>
    [Params(100, 1_000, 10_000, 100_000)]
    public int ExpansionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(ExpansionCount, length: 1);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                ExpansionCount,
                concurrentQueries: 1));
        _query = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            $"cold-astar-{ExpansionCount}",
            ExpansionCount,
            length: 1,
            NavigationGraphBenchmarkScenario.CreateBudget(ExpansionCount));

        long before = GC.GetAllocatedBytesForCurrentThread();
        _workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: NavigationGraphBenchmarkScenario.GetPageCapacity(ExpansionCount),
            componentCapacity: checked(
                NavigationGraphBenchmarkScenario.GetPageCapacity(ExpansionCount) + 2),
            nodeCapacity: ExpansionCount,
            rayCoveredAddressCapacity: ExpansionCount,
            rayTraceIntervalCapacity: ExpansionCount,
            guidePointCapacity: ExpansionCount);
        _workspaceAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _admission = new NavigationQueryAdmissionWork(
            _fixture.World,
            _workspace.EndpointWorkspace,
            PathAlgorithm.AStar);

        long cost = ResolveEndpointsAndColdAStar();
        if (_expandedNodes != ExpansionCount
            || _endpointCandidates != 2
            || _heapWork != checked(ExpansionCount * 2)
            || cost != checked((ExpansionCount - 1L) * Fixed64.One.m_rawValue))
        {
            throw new InvalidOperationException(
                $"Cold graph preflight failed for {ExpansionCount}: candidates={_endpointCandidates}, "
                + $"expanded={_expandedNodes}, heap_work={_heapWork}, cost={cost}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine(
            $"PHASE34_GRAPH_ASTAR expansion_target={ExpansionCount} "
            + $"lookup_probes={_lookupProbes} endpoint_candidates={_endpointCandidates} "
            + $"expanded_nodes={_expandedNodes} evaluated_edges={_evaluatedEdges} "
            + $"heap_push_pop={_heapWork} workspace_allocated_bytes={_workspaceAllocatedBytes} "
            + $"result_bytes={_resultBytes}");
        _admission?.Dispose();
        _fixture?.Teardown();
    }

    /// <summary>Resolves both endpoints and runs one cold fixed-point graph A* search.</summary>
    [Benchmark]
    public long ResolveEndpointsAndColdAStar()
    {
        NavigationWorldGraphLease lease = _fixture.Context.Pathing.TryAcquireNavigationGraph()
            ?? throw new InvalidOperationException("The cold graph benchmark could not acquire its snapshot.");
        _admission.Begin(lease, _query);
        try
        {
            while (_admission.Status == NavigationQueryAdmissionStatus.Pending)
                _admission.Advance(int.MaxValue, int.MaxValue);
            if (_admission.Status != NavigationQueryAdmissionStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Cold graph endpoint admission failed with {_admission.Status}.");
            }

            using var search = new NavigationSurfaceAStarWork(
                _admission.Result,
                _workspace);
            while (search.Status == NavigationSurfaceAStarStatus.Pending)
                search.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            if (search.Status != NavigationSurfaceAStarStatus.Success)
                throw new InvalidOperationException($"Cold graph A* failed with {search.Status}.");

            NavigationWorkMeter meter = _admission.Meter;
            NavigationAStarPayload payload = search.Result;
            _lookupProbes = meter.LookupProbes;
            _endpointCandidates = meter.EndpointCandidates;
            _expandedNodes = meter.ExpandedNodes;
            _evaluatedEdges = meter.EvaluatedEdges;
            _heapWork = checked(meter.ExpandedNodes + payload.Nodes.Length);
            _resultBytes = payload.RetainedBytes;
            return payload.Cost.m_rawValue;
        }
        finally
        {
            _admission.Dispose();
        }
    }
}
