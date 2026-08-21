using System;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures cold deterministic reverse integration through the graph Flow provider.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase5", "Graph", "Flow", "Cold")]
public class NavigationFlowFieldBenchmarks
{
    private BenchmarkPathFixture _fixture;
    private NavigationFlowFieldWorkspace _workspace;
    private NavigationQueryAdmissionWork _admission;
    private PathQuery _query;
    private long _workspaceAllocatedBytes;
    private int _endpointCandidates;
    private int _settledNodes;
    private int _evaluatedEdges;
    private int _selectedEdges;
    private int _heapWork;
    private long _payloadBytes;

    /// <summary>Exact deterministic reverse-integration size.</summary>
    [Params(100, 1_000, 10_000, 100_000)]
    public int SettledNodeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(SettledNodeCount, 1);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(SettledNodeCount, 1));
        PathQuery published = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            $"cold-flow-{SettledNodeCount}",
            SettledNodeCount,
            1,
            NavigationGraphBenchmarkScenario.CreateBudget(SettledNodeCount));
        _query = NavigationGraphBenchmarkScenario.ToFlow(published);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _workspace = new NavigationFlowFieldWorkspace(
            1,
            NavigationGraphBenchmarkScenario.GetPageCapacity(SettledNodeCount),
            NavigationGraphBenchmarkScenario.GetPageCapacity(SettledNodeCount) + 2,
            SettledNodeCount,
            SettledNodeCount,
            SettledNodeCount);
        _workspaceAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _admission = new NavigationQueryAdmissionWork(
            _fixture.World,
            _fixture.Context.Pathing.NavigationGraphStore,
            _workspace.EndpointWorkspace,
            _workspace.RayWorkspace,
            PathAlgorithm.FlowField);

        long cost = ColdReverseIntegration();
        long expectedCost = checked((SettledNodeCount - 1L) * Fixed64.One.m_rawValue);
        int expectedEvaluatedEdges = checked((SettledNodeCount * 5) - 6);
        if (_endpointCandidates != 2
            || _settledNodes != SettledNodeCount
            || _evaluatedEdges != expectedEvaluatedEdges
            || _selectedEdges != SettledNodeCount - 1
            || _heapWork != checked(SettledNodeCount * 2)
            || _workspaceAllocatedBytes <= 0
            || _payloadBytes <= 0
            || cost != expectedCost)
        {
            throw new InvalidOperationException(
                $"Cold Flow preflight for {SettledNodeCount}: status=Success, "
                + $"endpoint_candidates={_endpointCandidates}/2, "
                + $"settled_nodes={_settledNodes}/{SettledNodeCount}, "
                + $"evaluated_edges={_evaluatedEdges}/{expectedEvaluatedEdges}, "
                + $"selected_edges={_selectedEdges}/{SettledNodeCount - 1}, "
                + $"heap_push_pop={_heapWork}/{SettledNodeCount * 2}, "
                + $"workspace_bytes={_workspaceAllocatedBytes}, payload_bytes={_payloadBytes}, "
                + $"origin_cost={cost}/{expectedCost}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine(
            $"PHASE5_FLOW_COLD settled_target={SettledNodeCount} endpoint_candidates={_endpointCandidates} "
            + $"settled_nodes={_settledNodes} evaluated_edges={_evaluatedEdges} "
            + $"selected_edges={_selectedEdges} heap_push_pop={_heapWork} "
            + $"workspace_bytes={_workspaceAllocatedBytes} "
            + $"payload_bytes={_payloadBytes} detached_bytes=0 component_nodes={_settledNodes} "
            + $"component_edges={_evaluatedEdges} cached_payload_bytes=0 reserved_bytes=0 "
            + "reserved_leases=0 active_leases=0 cache_entries=0");
        _admission?.Dispose();
        _fixture?.Teardown();
    }

    /// <summary>Resolves endpoints and builds one uncached immutable Flow payload.</summary>
    [Benchmark]
    public long ColdReverseIntegration()
    {
        NavigationWorldGraphLease lease = _fixture.Context.Pathing.TryAcquireNavigationGraph()
            ?? throw new InvalidOperationException("The cold Flow benchmark could not acquire its graph.");
        _admission.Begin(
            lease,
            _query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        try
        {
            while (_admission.Status == NavigationQueryAdmissionStatus.Pending)
                _admission.Advance(int.MaxValue, int.MaxValue);
            if (_admission.Status != NavigationQueryAdmissionStatus.Success)
                throw new InvalidOperationException($"Cold Flow admission failed with {_admission.Status}.");
            using var work = new NavigationFlowFieldWork(
                _fixture.World,
                _admission.Result,
                _workspace,
                _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.MaximumSinglePayloadBytes);
            while (work.Status == NavigationFlowFieldStatus.Pending)
                work.Advance(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            if (work.Status != NavigationFlowFieldStatus.Success || work.Result == null)
            {
                NavigationWorkMeter failedMeter = _admission.Meter;
                throw new InvalidOperationException(
                    $"Cold Flow search failed with {work.Status}: "
                    + $"lookup={failedMeter.LookupProbes}/{_query.Budget.MaxLookupProbes}, "
                    + $"expanded={failedMeter.ExpandedNodes}/{_query.Budget.MaxExpandedNodes}, "
                    + $"edges={failedMeter.EvaluatedEdges}/{_query.Budget.MaxEvaluatedEdges}, "
                    + $"connections={failedMeter.ConnectionLegs}/{_query.Budget.MaxConnectionLegs}.");
            }
            NavigationFlowFieldPayload payload = work.Result;
            _selectedEdges = ValidateReverseChain(payload);
            NavigationWorkMeter meter = _admission.Meter;
            _endpointCandidates = meter.EndpointCandidates;
            _settledNodes = meter.ExpandedNodes;
            _evaluatedEdges = meter.EvaluatedEdges;
            _heapWork = checked(_settledNodes + payload.Nodes.Length);
            _payloadBytes = payload.RetainedBytes;
            return payload.Nodes[payload.Nodes.Length - 1].IntegrationCost.m_rawValue;
        }
        finally
        {
            _admission.Dispose();
        }
    }

    private static int ValidateReverseChain(NavigationFlowFieldPayload payload)
    {
        for (int i = 1; i < payload.Nodes.Length; i++)
        {
            if (payload.Nodes[i].SelectedEdge.Target != payload.Nodes[i - 1].Address)
            {
                throw new InvalidOperationException(
                    $"Cold Flow selected edge {i} does not target the previous canonical node.");
            }
        }
        return Math.Max(0, payload.Nodes.Length - 1);
    }
}

/// <summary>Measures deterministic near-to-far publication and prefix promotion.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase5", "Graph", "Flow", "Promotion")]
public class NavigationFlowFieldPromotionBenchmarks
{
    private const int CorridorLength = 64;
    private BenchmarkPathFixture _fixture;
    private NavigationFlowAdmissionGate _gate;
    private PathQuery _nearQuery;
    private PathQuery _farQuery;
    private int _nearNodes;
    private int _farNodes;
    private long _payloadBytes;
    private long _detachedPeakBytes;

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(CorridorLength, 1);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                CorridorLength,
                concurrentQueries: 2));
        PathQuery published = NavigationGraphBenchmarkScenario.Publish(
            _fixture,
            configuration,
            "flow-promotion",
            CorridorLength,
            1,
            NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength));
        _farQuery = NavigationGraphBenchmarkScenario.ToFlow(published);
        _nearQuery = NavigationGraphBenchmarkScenario.WithStart(
            _farQuery,
            configuration,
            new VoxelIndex(CorridorLength - 8, 0, 0));
        _gate = _fixture.Context.Pathing.NavigationFlowAdmissionGate;
        if (NearToFarPromotion() <= 0)
            throw new InvalidOperationException("Near-to-far Flow promotion returned no signal.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
        Console.WriteLine(
            $"PHASE5_FLOW_PROMOTION near_nodes={_nearNodes} far_nodes={_farNodes} "
            + "evaluated_edges=n/a heap_push_pop=n/a workspace_bytes=n/a "
            + $"payload_bytes={_payloadBytes} detached_peak_bytes={_detachedPeakBytes} "
            + "component_nodes=n/a component_edges=n/a "
            + $"cached_payload_bytes={cache.CachedBytes} detached_bytes={cache.DetachedBytes} "
            + $"reserved_bytes={cache.ReservedPayloadBytes} reserved_leases={cache.ReservedLeaseCount} "
            + $"active_leases={cache.ActiveLeaseCount} cache_entries={cache.Count}");
        _fixture?.Teardown();
    }

    /// <summary>Builds a near prefix, promotes it with a far origin, then drains both leases.</summary>
    [Benchmark]
    public long NearToFarPromotion()
    {
        NavigationFlowQueryResult near = default;
        NavigationFlowQueryResult far = default;
        NavigationFlowFieldPayload farPayload = null;
        try
        {
            near = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _nearQuery);
            if (near.PayloadLease.TryGetPayload(out NavigationFlowFieldPayload nearPayload)
                != NavigationFlowFieldStatus.Success)
            {
                throw new InvalidOperationException("The near Flow payload was stale.");
            }
            far = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _farQuery);
            if (far.PayloadLease.TryGetPayload(out farPayload)
                != NavigationFlowFieldStatus.Success)
            {
                throw new InvalidOperationException("The far Flow payload was stale.");
            }
            NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
            _nearNodes = nearPayload.Nodes.Length;
            _farNodes = farPayload.Nodes.Length;
            _payloadBytes = farPayload.RetainedBytes;
            _detachedPeakBytes = Math.Max(_detachedPeakBytes, cache.DetachedBytes);
            if (!NavigationGraphBenchmarkScenario.IsStrictPrefix(nearPayload, farPayload)
                || cache.Count != 1
                || cache.ActiveLeaseCount != 2
                || cache.ReservedLeaseCount != 0
                || cache.ReservedPayloadBytes != 0)
            {
                throw new InvalidOperationException(
                    $"Near/far Flow preflight: prefix="
                    + $"{NavigationGraphBenchmarkScenario.IsStrictPrefix(nearPayload, farPayload)}, "
                    + $"near_nodes={_nearNodes}, far_nodes={_farNodes}, cache_entries={cache.Count}/1, "
                    + $"active_leases={cache.ActiveLeaseCount}/2, "
                    + $"reserved_leases={cache.ReservedLeaseCount}/0, "
                    + $"reserved_bytes={cache.ReservedPayloadBytes}/0.");
            }
            return _nearNodes + _farNodes + far.ResolvedOrigin.Index.x;
        }
        finally
        {
            near.Dispose();
            far.Dispose();
            NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
            cache.Reset();
            if (cache.ActiveLeaseCount != 0
                || cache.LeasedBytes != 0
                || cache.DetachedBytes != 0
                || cache.ReservedLeaseCount != 0
                || cache.ReservedPayloadBytes != 0
                || cache.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Near/far Flow drain: active_leases={cache.ActiveLeaseCount}/0, "
                    + $"leased_bytes={cache.LeasedBytes}/0, detached_bytes={cache.DetachedBytes}/0, "
                    + $"reserved_leases={cache.ReservedLeaseCount}/0, "
                    + $"reserved_bytes={cache.ReservedPayloadBytes}/0, cache_entries={cache.Count}/0.");
            }
        }
    }
}

/// <summary>Measures warm public Flow acquire, sample, and return across agent batches.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase5", "Graph", "Flow", "Agents")]
public class NavigationFlowFieldAgentBenchmarks
{
    private const int CorridorLength = 64;
    private static readonly GuideSampleWorkBudget SampleBudget = new(
        128,
        128,
        8,
        32,
        32,
        32,
        1);
    private BenchmarkPathFixture _fixture;
    private PathQuery[] _queries;
    private long _warmAllocatedBytes;
    private int _successes;
    private long _cachedPayloadBytes;

    /// <summary>Number of deterministic warm service operations in one batch.</summary>
    [Params(100, 500, 5_000)]
    public int AgentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration configuration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(CorridorLength, 1);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            configuration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                CorridorLength,
                concurrentQueries: 1));
        PathQuery published = NavigationGraphBenchmarkScenario.ToFlow(
            NavigationGraphBenchmarkScenario.Publish(
                _fixture,
                configuration,
                $"flow-agents-{AgentCount}",
                CorridorLength,
                1,
                NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength)));
        _queries = new PathQuery[AgentCount];
        for (int i = 0; i < AgentCount; i++)
        {
            _queries[i] = NavigationGraphBenchmarkScenario.WithStart(
                published,
                configuration,
                new VoxelIndex(i % (CorridorLength - 1), 0, 0));
        }

        WarmAgentBatch();
        WarmAgentBatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long signal = WarmAgentBatch();
        _warmAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        NavigationFlowFieldPayloadCache cache =
            _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        _cachedPayloadBytes = cache.CachedBytes;
        if (signal == 0
            || _successes != AgentCount
            || _warmAllocatedBytes != 0
            || cache.ActiveLeaseCount != 0
            || cache.LeasedBytes != 0
            || cache.DetachedBytes != 0
            || cache.ReservedLeaseCount != 0
            || cache.ReservedPayloadBytes != 0)
        {
            throw new InvalidOperationException(
                $"Warm Flow agents preflight for {AgentCount}: successes={_successes}/{AgentCount}, "
                + $"allocated_bytes={_warmAllocatedBytes}/0, active_leases={cache.ActiveLeaseCount}/0, "
                + $"leased_bytes={cache.LeasedBytes}/0, detached_bytes={cache.DetachedBytes}/0, "
                + $"reserved_leases={cache.ReservedLeaseCount}/0, "
                + $"reserved_bytes={cache.ReservedPayloadBytes}/0.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NavigationFlowFieldPayloadCache cache =
            _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        Console.WriteLine(
            $"PHASE5_FLOW_AGENTS agents={AgentCount} successes={_successes} "
            + $"warm_allocated_bytes={_warmAllocatedBytes} settled_nodes=n/a "
            + "evaluated_edges=n/a heap_push_pop=n/a workspace_bytes=n/a "
            + $"payload_bytes={_cachedPayloadBytes} detached_bytes={cache.DetachedBytes} "
            + "component_nodes=n/a component_edges=n/a "
            + $"cached_payload_bytes={cache.CachedBytes} reserved_bytes={cache.ReservedPayloadBytes} "
            + $"reserved_leases={cache.ReservedLeaseCount} active_leases={cache.ActiveLeaseCount} "
            + $"cache_entries={cache.Count}");
        _fixture?.Teardown();
    }

    /// <summary>Runs one allocation-free warm Flow service batch.</summary>
    [Benchmark]
    public long WarmAgentBatch()
    {
        int successes = 0;
        long signal = 0;
        for (int i = 0; i < _queries.Length; i++)
        {
            PathQuery query = _queries[i];
            NavigationGuideStatus status = _fixture.Context.Guides.RequestFlowField(
                query,
                out NavigationFlowFieldLease? result);
            if (status != NavigationGuideStatus.Success || !result.HasValue)
                throw new InvalidOperationException($"Warm Flow agent {i} failed with {status}.");
            NavigationFlowFieldLease guide = result.Value;
            try
            {
                NavigationGuideStatus sample = guide.TrySample(
                    query.Start.Position,
                    SampleBudget,
                    out NavigationFlowSample flowSample);
                if (sample != NavigationGuideStatus.Success || flowSample.Heading == Vector3d.Zero)
                {
                    throw new InvalidOperationException(
                        $"Warm Flow agent {i} sample failed: status={sample}, heading={flowSample.Heading}.");
                }
                successes++;
                signal ^= guide.OriginIntegrationCost.m_rawValue ^ flowSample.Heading.GetHashCode();
            }
            finally
            {
                guide.Dispose();
            }
        }
        _successes = successes;
        return signal ^ successes;
    }
}

/// <summary>Measures dependency-scoped Flow invalidation and unaffected reuse.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase5", "Graph", "Flow", "Mutation")]
public class NavigationFlowFieldMutationBenchmarks
{
    private const int CorridorLength = 64;
    private const string AffectedMap = "flow-mutation-affected";
    private const string UnaffectedMap = "flow-mutation-unaffected";
    private static readonly VoxelIndex MutationIndex = new(CorridorLength / 2, 0, 0);
    private static readonly NavigationCell MutatedCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.One,
        (Fixed64)4,
        (Fixed64)4);
    private BenchmarkPathFixture _fixture;
    private NavigationFlowAdmissionGate _gate;
    private PathQuery _affectedQuery;
    private PathQuery _unaffectedQuery;
    private long _nextOperationSequence;
    private long _affectedOriginalCost;
    private long _affectedRebuiltCost;
    private long _unaffectedCost;
    private int _affectedStaleResults;
    private int _unaffectedReuses;

    [GlobalSetup]
    public void Setup()
    {
        GridConfiguration affectedConfiguration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(CorridorLength, 1);
        GridConfiguration unaffectedConfiguration =
            NavigationGraphBenchmarkScenario.CreateConfiguration(
                CorridorLength,
                1,
                new Vector3d(0, 0, 2));
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            affectedConfiguration,
            NavigationGraphBenchmarkScenario.CreateSettings(
                CorridorLength,
                concurrentQueries: 4));
        if (!_fixture.World.TryAddGrid(unaffectedConfiguration, out _))
            throw new InvalidOperationException("The unaffected benchmark grid was not added.");
        _affectedQuery = NavigationGraphBenchmarkScenario.ToFlow(
            NavigationGraphBenchmarkScenario.Publish(
                _fixture,
                affectedConfiguration,
                AffectedMap,
                CorridorLength,
                1,
                NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength),
                operationSequence: 1));
        _unaffectedQuery = NavigationGraphBenchmarkScenario.ToFlow(
            NavigationGraphBenchmarkScenario.Publish(
                _fixture,
                unaffectedConfiguration,
                UnaffectedMap,
                CorridorLength,
                1,
                NavigationGraphBenchmarkScenario.CreateBudget(CorridorLength),
                operationSequence: 3));
        _nextOperationSequence = 5;
        _gate = _fixture.Context.Pathing.NavigationFlowAdmissionGate;
        if (AffectedAndUnaffectedMutation() <= 0)
            throw new InvalidOperationException("Flow mutation preflight returned no signal.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
        Console.WriteLine(
            $"PHASE5_FLOW_MUTATION affected_stale={_affectedStaleResults} "
            + $"unaffected_reuses={_unaffectedReuses} original_cost={_affectedOriginalCost} "
            + $"rebuilt_cost={_affectedRebuiltCost} unaffected_cost={_unaffectedCost} "
            + "settled_nodes=n/a evaluated_edges=n/a heap_push_pop=n/a workspace_bytes=n/a "
            + "payload_bytes=n/a component_nodes=n/a component_edges=n/a "
            + $"cached_payload_bytes={cache.CachedBytes} detached_bytes={cache.DetachedBytes} "
            + $"reserved_bytes={cache.ReservedPayloadBytes} reserved_leases={cache.ReservedLeaseCount} "
            + $"active_leases={cache.ActiveLeaseCount} cache_entries={cache.Count}");
        _fixture?.Teardown();
    }

    /// <summary>Invalidates one map dependency, rebuilds it, and proves the other map is reused.</summary>
    [Benchmark]
    public long AffectedAndUnaffectedMutation()
    {
        NavigationFlowQueryResult affectedBefore = default;
        NavigationFlowQueryResult unaffectedBefore = default;
        NavigationFlowQueryResult affectedAfter = default;
        NavigationFlowQueryResult unaffectedAfter = default;
        try
        {
            affectedBefore = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _affectedQuery);
            unaffectedBefore = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _unaffectedQuery);
            RequirePayload(affectedBefore, "affected-before", out NavigationFlowFieldPayload affectedOriginal);
            RequirePayload(unaffectedBefore, "unaffected-before", out NavigationFlowFieldPayload unaffectedOriginal);
            _affectedOriginalCost = affectedOriginal.Nodes[affectedOriginal.Nodes.Length - 1]
                .IntegrationCost.m_rawValue;
            _unaffectedCost = unaffectedOriginal.Nodes[unaffectedOriginal.Nodes.Length - 1]
                .IntegrationCost.m_rawValue;

            ApplyOverlay(NavigationCellOverlayOperation.Set(MutationIndex, MutatedCell));
            NavigationFlowQueryStatus stale = NavigationGraphBenchmarkScenario.ExecuteFlow(
                _gate,
                _affectedQuery,
                out NavigationFlowQueryResult staleResult);
            staleResult.Dispose();
            _affectedStaleResults = stale == NavigationFlowQueryStatus.Stale ? 1 : 0;
            if (_affectedStaleResults != 1
                || affectedBefore.PayloadLease.TryGetPayload(out _)
                    != NavigationFlowFieldStatus.Stale)
            {
                throw new InvalidOperationException(
                    $"Affected Flow mutation did not stale exactly once: status={stale}/Stale, "
                    + $"old_lease={affectedBefore.PayloadLease.TryGetPayload(out _)}/Stale.");
            }

            affectedAfter = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _affectedQuery);
            unaffectedAfter = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _unaffectedQuery);
            RequirePayload(affectedAfter, "affected-after", out NavigationFlowFieldPayload affectedRebuilt);
            RequirePayload(unaffectedAfter, "unaffected-after", out NavigationFlowFieldPayload unaffectedReused);
            _affectedRebuiltCost = affectedRebuilt.Nodes[affectedRebuilt.Nodes.Length - 1]
                .IntegrationCost.m_rawValue;
            _unaffectedReuses = ReferenceEquals(unaffectedOriginal, unaffectedReused) ? 1 : 0;
            if (_unaffectedReuses != 1
                || unaffectedBefore.PayloadLease.TryGetPayload(out _)
                    != NavigationFlowFieldStatus.Success
                || _affectedRebuiltCost != checked(_affectedOriginalCost + Fixed64.One.m_rawValue)
                || _unaffectedCost != _affectedOriginalCost)
            {
                throw new InvalidOperationException(
                    $"Flow mutation rebuild: unaffected_reuses={_unaffectedReuses}/1, "
                    + $"unaffected_lease={unaffectedBefore.PayloadLease.TryGetPayload(out _)}/Success, "
                    + $"rebuilt_cost={_affectedRebuiltCost}/{checked(_affectedOriginalCost + Fixed64.One.m_rawValue)}, "
                    + $"unaffected_cost={_unaffectedCost}/{_affectedOriginalCost}.");
            }
            return _affectedRebuiltCost ^ _unaffectedCost;
        }
        finally
        {
            affectedBefore.Dispose();
            unaffectedBefore.Dispose();
            affectedAfter.Dispose();
            unaffectedAfter.Dispose();
            ApplyOverlay(NavigationCellOverlayOperation.RevertToBake(MutationIndex));
            _gate.PayloadCache.Reset();
            NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
            if (cache.ActiveLeaseCount != 0
                || cache.LeasedBytes != 0
                || cache.DetachedBytes != 0
                || cache.ReservedLeaseCount != 0
                || cache.ReservedPayloadBytes != 0
                || cache.Count != 0)
            {
                throw new InvalidOperationException("Flow mutation cache state did not drain.");
            }
        }
    }

    private void ApplyOverlay(NavigationCellOverlayOperation cell)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(AffectedMap, new[] { cell })
            })),
            _nextOperationSequence++,
            effectiveFrame: _fixture.Context.FrameCount + 1);
        if (!_fixture.Context.Pathing.Admit(operation))
            throw new InvalidOperationException("The Flow mutation overlay was not admitted.");
        for (int frame = 0;
            frame < 4_096 && operation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            _fixture.Context.Simulate();
        }
        if (operation.Receipt.Status != NavigationOperationStatus.Applied)
        {
            throw new InvalidOperationException(
                $"The Flow mutation overlay failed with {operation.Receipt.Status}.");
        }
    }

    private static void RequirePayload(
        NavigationFlowQueryResult result,
        string name,
        out NavigationFlowFieldPayload payload)
    {
        if (result.PayloadLease.TryGetPayload(out payload) != NavigationFlowFieldStatus.Success)
            throw new InvalidOperationException($"The {name} Flow payload was stale.");
    }
}

/// <summary>Measures a single cold million-node articulation split and cross-cut rejection.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(Phase2GateConfig))]
[BenchmarkCategory("Phase5", "Graph", "Flow", "Articulation")]
public class NavigationFlowFieldArticulationBenchmarks
{
    private const int NodeCount = 1_000_000;
    private const string MapId = "flow-articulation-million";
    private static readonly VoxelIndex ArticulationIndex = new(NodeCount / 2, 0, 0);
    private BenchmarkPathFixture _fixture;
    private NavigationFlowAdmissionGate _gate;
    private PathQuery _query;
    private long _nextOperationSequence;
    private long _constructionElapsedMilliseconds;
    private long _splitElapsedMilliseconds;
    private long _constructionManagedBytes;
    private long _workingSetDeltaBytes;
    private long _peakWorkingSetBytes;
    private long _payloadBytes;
    private long _detachedPeakBytes;
    private int _beforeComponentCount;
    private int _afterComponentCount;
    private int _beforeComponentNodes;
    private int _afterComponentNodes;
    private long _splitComponentNodes;
    private long _splitComponentEdges;
    private int _materializationFrames;

    [GlobalSetup]
    public void Setup()
    {
        long started = Stopwatch.GetTimestamp();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(NodeCount - 1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var cells = new VoxelIndex[NodeCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new VoxelIndex(i, 0, 0);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(
            config: null,
            settings: NavigationGraphBenchmarkScenario.CreateArticulationSettings(NodeCount));
        if (!_fixture.World.TryAddGrid(configuration, cells, out _))
        {
            throw new InvalidOperationException(
                "The exact million-cell sparse GridForge line was not registered.");
        }
        _query = NavigationGraphBenchmarkScenario.ToFlow(
            NavigationGraphBenchmarkScenario.Publish(
                _fixture,
                configuration,
                MapId,
                NodeCount,
                1,
                NavigationGraphBenchmarkScenario.CreateBudget(NodeCount)));
        for (_materializationFrames = 0;
            _materializationFrames < 4_096;
            _materializationFrames++)
        {
            bool materialized;
            using (NavigationWorldGraphLease current =
                _fixture.Context.Pathing.TryAcquireNavigationGraph()
                    ?? throw new InvalidOperationException("The articulation graph was unavailable."))
            {
                materialized = current.Graph.TryGetCoveredAddressGeneration(MapId, out _);
            }
            if (materialized)
                break;
            _fixture.Context.Simulate();
        }
        if (_materializationFrames == 4_096)
            throw new InvalidOperationException("The million-cell map did not become materialized.");
        _nextOperationSequence = 3;
        _gate = _fixture.Context.Pathing.NavigationFlowAdmissionGate;
        using (NavigationWorldGraphLease graph =
            _fixture.Context.Pathing.TryAcquireNavigationGraph()
                ?? throw new InvalidOperationException("The articulation graph was not available."))
        {
            var left = new NavigationCellAddress(MapId, default);
            var right = new NavigationCellAddress(MapId, new VoxelIndex(NodeCount - 1, 0, 0));
            NavigationSurfaceComponent component = null;
            if (!graph.Graph.AreInSameSurfaceComponent(
                    left,
                    TraversalMedium.Solid,
                    right,
                    TraversalMedium.Solid)
                || !graph.Graph.SurfaceComponents.TryGet(
                    left,
                    TraversalMedium.Solid,
                    out component)
                || component.Members.Count != NodeCount)
            {
                throw new InvalidOperationException(
                    $"Million-node articulation baseline: same_component="
                    + $"{graph.Graph.AreInSameSurfaceComponent(
                        left,
                        TraversalMedium.Solid,
                        right,
                        TraversalMedium.Solid)}/True, "
                    + $"component_nodes={component?.Members.Count ?? 0}/{NodeCount}.");
            }
            _beforeComponentCount = 1;
            _beforeComponentNodes = component.Members.Count;
        }
        NavigationFlowQueryResult initial = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _query);
        try
        {
            if (initial.PayloadLease.TryGetPayload(out NavigationFlowFieldPayload payload)
                != NavigationFlowFieldStatus.Success
                || payload.Nodes.Length != NodeCount)
            {
                throw new InvalidOperationException("The million-node baseline Flow route was incomplete.");
            }
            _payloadBytes = payload.RetainedBytes;
        }
        finally
        {
            initial.Dispose();
        }
        _constructionElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _constructionManagedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Process process = Process.GetCurrentProcess();
        _workingSetDeltaBytes = Math.Max(0, process.WorkingSet64 - workingSetBefore);
        _peakWorkingSetBytes = process.PeakWorkingSet64;
        if (ArticulationSplit() <= 0)
            throw new InvalidOperationException("The million-node articulation preflight returned no signal.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
        Console.WriteLine(
            $"PHASE5_FLOW_ARTICULATION nodes={NodeCount} before_components={_beforeComponentCount} "
            + $"after_components={_afterComponentCount} before_component_nodes={_beforeComponentNodes} "
            + $"after_component_nodes={_afterComponentNodes} split_component_nodes={_splitComponentNodes} "
            + $"split_component_edges={_splitComponentEdges} construction_ms={_constructionElapsedMilliseconds} "
            + $"materialization_frames={_materializationFrames} "
            + $"split_ms={_splitElapsedMilliseconds} construction_managed_bytes={_constructionManagedBytes} "
            + $"working_set_delta_bytes={_workingSetDeltaBytes} peak_working_set_bytes={_peakWorkingSetBytes} "
            + "settled_nodes=1000000 evaluated_edges=n/a heap_push_pop=n/a workspace_bytes=n/a "
            + $"payload_bytes={_payloadBytes} detached_peak_bytes={_detachedPeakBytes} "
            + $"cached_payload_bytes={cache.CachedBytes} detached_bytes={cache.DetachedBytes} "
            + $"reserved_bytes={cache.ReservedPayloadBytes} reserved_leases={cache.ReservedLeaseCount} "
            + $"active_leases={cache.ActiveLeaseCount} cache_entries={cache.Count}");
        _fixture?.Teardown();
    }

    /// <summary>Suppresses the midpoint, validates two exact components and NoPath, then restores it.</summary>
    [Benchmark]
    public long ArticulationSplit()
    {
        NavigationFlowQueryResult before = default;
        long started = Stopwatch.GetTimestamp();
        _splitComponentNodes = 0;
        _splitComponentEdges = 0;
        try
        {
            before = NavigationGraphBenchmarkScenario.ExecuteFlow(_gate, _query);
            ApplyOverlay(
                NavigationCellOverlayOperation.Suppress(ArticulationIndex),
                recordComponentWork: true);
            NavigationFlowQueryStatus stale = NavigationGraphBenchmarkScenario.ExecuteFlow(
                _gate,
                _query,
                out NavigationFlowQueryResult staleResult);
            staleResult.Dispose();
            NavigationFlowQueryStatus crossCut = NavigationGraphBenchmarkScenario.ExecuteFlow(
                _gate,
                _query,
                out NavigationFlowQueryResult crossCutResult);
            crossCutResult.Dispose();
            var left = new NavigationCellAddress(MapId, default);
            var right = new NavigationCellAddress(MapId, new VoxelIndex(NodeCount - 1, 0, 0));
            using NavigationWorldGraphLease graph =
                _fixture.Context.Pathing.TryAcquireNavigationGraph()
                    ?? throw new InvalidOperationException("The split articulation graph was unavailable.");
            if (!graph.Graph.SurfaceComponents.TryGet(
                    left,
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponent leftComponent)
                || !graph.Graph.SurfaceComponents.TryGet(
                    right,
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponent rightComponent))
            {
                throw new InvalidOperationException("The split articulation components were unavailable.");
            }
            _afterComponentCount = leftComponent.Key == rightComponent.Key ? 1 : 2;
            _afterComponentNodes = checked(leftComponent.Members.Count + rightComponent.Members.Count);
            NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
            _detachedPeakBytes = Math.Max(_detachedPeakBytes, cache.DetachedBytes);
            if (stale != NavigationFlowQueryStatus.Stale
                || crossCut != NavigationFlowQueryStatus.NoPath
                || _afterComponentCount != 2
                || _afterComponentNodes != NodeCount - 1)
            {
                throw new InvalidOperationException(
                    $"Million-node articulation split: stale={stale}/Stale, "
                    + $"cross_cut={crossCut}/NoPath, "
                    + $"components={_afterComponentCount}/2, "
                    + $"component_nodes={_afterComponentNodes}/{NodeCount - 1}.");
            }
            _splitElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return _afterComponentNodes + _splitComponentEdges;
        }
        finally
        {
            before.Dispose();
            ApplyOverlay(
                NavigationCellOverlayOperation.RevertToBake(ArticulationIndex),
                recordComponentWork: false);
            _gate.PayloadCache.Reset();
            NavigationFlowFieldPayloadCache cache = _gate.PayloadCache;
            if (cache.ActiveLeaseCount != 0
                || cache.LeasedBytes != 0
                || cache.DetachedBytes != 0
                || cache.ReservedLeaseCount != 0
                || cache.ReservedPayloadBytes != 0
                || cache.Count != 0)
            {
                throw new InvalidOperationException("Million-node articulation cache state did not drain.");
            }
        }
    }

    private void ApplyOverlay(
        NavigationCellOverlayOperation cell,
        bool recordComponentWork)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(MapId, new[] { cell })
            })),
            _nextOperationSequence++,
            effectiveFrame: _fixture.Context.FrameCount + 1);
        if (!_fixture.Context.Pathing.Admit(operation))
            throw new InvalidOperationException("The articulation overlay was not admitted.");
        for (int frame = 0;
            frame < 4_096 && operation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            _fixture.Context.Simulate();
            if (recordComponentWork)
            {
                MaintenanceWorkMeter meter = _fixture.Context.Pathing.NavigationMaintenanceMeter;
                _splitComponentNodes += meter.ComponentNodes;
                _splitComponentEdges += meter.SurfaceComponentEdges;
            }
        }
        if (operation.Receipt.Status != NavigationOperationStatus.Applied)
        {
            throw new InvalidOperationException(
                $"The articulation overlay failed: status={operation.Receipt.Status}, "
                + $"rejection={operation.Receipt.Rejection}.");
        }
    }
}
