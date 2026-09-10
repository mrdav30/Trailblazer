//=======================================================================
// ColdGuideBenchmarkScenario.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>Owns prepublished geometry and matched public/internal cold acquisition controls.</summary>
internal sealed class ColdGuideBenchmarkScenario : IDisposable
{
    private readonly NavigationFlowFieldWorkspace _workspace;
    private readonly NavigationQueryAdmissionWork _admission;
    private readonly NavigationCellAddress _start;
    private readonly NavigationCellAddress _end;
    private readonly NavigationAStarPayloadKey _aStarKey;
    private readonly long _maximumAStarBytes;
    private readonly bool _ladder;
    private long _expectedCost;

    internal ColdGuideBenchmarkScenario(string geometry, int nodeCount, int concurrentQueries = 1,
        NavigationSchedulingSettings schedulingSettings = null)
    {
        SwiftThrowHelper.ThrowIfTrue(geometry != "Plane" && geometry != "Ladder", "Unknown cold geometry.");
        _ladder = geometry == "Ladder";
        NodeCount = nodeCount;
        Geometry = geometry;
        Console.WriteLine($"NAVIGATION_COLD_SETUP geometry={Geometry} nodes={NodeCount} phase=begin");
        var phaseClock = Stopwatch.StartNew();
        long phaseAllocated = GC.GetAllocatedBytesForCurrentThread();
        int width = _ladder ? nodeCount / 2 : nodeCount == 100_000 ? 400 : nodeCount == 10_000 ? 100 : 10;
        int length = _ladder ? 3 : nodeCount / width;
        GridConfiguration configuration = _ladder
            ? new GridConfiguration(Vector3d.Zero, new Vector3d(width - 1, 2, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One), storageKind: GridStorageKind.Dense)
            : NavigationGraphBenchmarkScenario.CreateConfiguration(width, length);
        TrailblazerWorldContextSettings settings = CreateSettings(nodeCount, concurrentQueries, schedulingSettings);
        _maximumAStarBytes = settings.QueryLimits.MaxAStarSinglePayloadBytes;
        Fixture = new BenchmarkPathFixture();
        Fixture.Setup(configuration, settings);
        string mapId = $"cold-public-{geometry}-{nodeCount}";
        _start = new NavigationCellAddress(mapId, default);
        _end = new NavigationCellAddress(mapId, _ladder
            ? new VoxelIndex(0, 2, 0) : new VoxelIndex(width - 1, 0, length - 1));
        // Stacked-floor boundary discovery also charges physically covered cells rejected by the map.
        var budget = new NavigationWorkBudget(
            checked(nodeCount * 512), _ladder ? 8 : 2, nodeCount, checked(nodeCount * 32),
            checked(nodeCount * 4), checked(nodeCount * 4), checked(nodeCount * 4),
            checked(nodeCount * 4), checked(nodeCount * 4), checked(nodeCount * 4), 0);
        if (_ladder)
            AStarQuery = PublishLadder(configuration, mapId, width, budget);
        else
            AStarQuery = NavigationGraphBenchmarkScenario.Publish(Fixture, configuration, mapId, width, length, budget);
        WriteSetupPhase("world-publication", phaseClock, ref phaseAllocated);
        FlowQuery = CopyAlgorithm(AStarQuery, PathAlgorithm.FlowField);
        _aStarKey = new NavigationAStarPayloadKey(AStarQuery, _start, _end, TraversalMedium.Solid, TraversalMedia.Solid);
        int pages = NavigationGraphBenchmarkScenario.GetPageCapacity(nodeCount);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _workspace = new NavigationFlowFieldWorkspace(1, pages, 2, nodeCount, nodeCount, nodeCount);
        _admission = new NavigationQueryAdmissionWork(Fixture.World, Fixture.Context.Pathing.NavigationGraphStore,
            _workspace.EndpointWorkspace, _workspace.RayWorkspace, PathAlgorithm.FlowField);
        WorkspaceAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        WriteSetupPhase("internal-workspace", phaseClock, ref phaseAllocated);
        _expectedCost = InternalFlow();
        SwiftThrowHelper.ThrowIfTrue(_expectedCost <= 0, "Cold benchmark requires a nonempty route.");
        WriteSetupPhase("first-internal-cold-flow", phaseClock, ref phaseAllocated);
        ClearCaches();
        RequestFlow();
        ValidateFlowPayload(FlowQuery);
        WriteSetupPhase("first-public-cold-flow", phaseClock, ref phaseAllocated);
        RequestAStar();
        WriteSetupPhase("first-public-cold-astar", phaseClock, ref phaseAllocated);
        // The first successful cold calls already warmed both workspace and lease pools.
        // Keep their payloads for the warm-allocation check; clear only after it finishes.
        for (int i = 0; i < 256; i++)
            RequestFlow();
        before = GC.GetAllocatedBytesForCurrentThread();
        RequestFlow();
        WarmAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        SwiftThrowHelper.ThrowIfTrue(WarmAllocatedBytes != 0, $"Warm acquisition allocated {WarmAllocatedBytes} bytes.");
        ClearCaches();
        WriteSetupPhase("warm-257-and-drain", phaseClock, ref phaseAllocated);
    }

    internal BenchmarkPathFixture Fixture { get; }
    internal PathQuery FlowQuery { get; }
    internal PathQuery AStarQuery { get; }
    internal string Geometry { get; }
    internal int NodeCount { get; }
    internal int SettledNodes { get; private set; }
    internal int EndpointCandidates { get; private set; }
    internal int EvaluatedEdges { get; private set; }
    internal int LookupProbes { get; private set; }
    internal int TransitionCandidates { get; private set; }
    internal int TransitionPairs { get; private set; }
    internal long PayloadBytes { get; private set; }
    internal long AStarPayloadBytes { get; private set; }
    internal int AStarStepCount { get; private set; }
    internal long WorkspaceAllocatedBytes { get; }
    internal long WarmAllocatedBytes { get; }
    internal long ExpectedCost => _expectedCost;

    /// <summary>Removes one completed scheduled A* payload after its lease has been released.</summary>
    internal void RemoveAStarPayload(PathQuery query)
    {
        NavigationAStarPayloadCache cache = Fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var key = new NavigationAStarPayloadKey(query, _start, _end, TraversalMedium.Solid, TraversalMedia.Solid);
        SwiftThrowHelper.ThrowIfTrue(!cache.TryReservePayload(_maximumAStarBytes, out var reservation),
            "Scheduled A* eviction reservation failed.");
        try
        {
            SwiftThrowHelper.ThrowIfTrue(!cache.TryCheckoutReserved(key,
                Fixture.Context.Pathing.NavigationGraphStore.Current, ref reservation, out var lease),
                "Scheduled A* eviction expected the exact completed payload.");
            using (lease)
                cache.RemoveExact(lease.Payload);
        }
        finally
        {
            cache.ReleasePayloadReservation(ref reservation);
        }
    }

    private void WriteSetupPhase(string phase, Stopwatch clock, ref long allocatedBefore)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine($"NAVIGATION_COLD_SETUP geometry={Geometry} nodes={NodeCount} phase={phase} "
            + $"elapsed_ms={clock.Elapsed.TotalMilliseconds:F3} thread_allocated_bytes={allocated} "
            + $"settled={SettledNodes} evaluated_edges={EvaluatedEdges} lookup_probes={LookupProbes}");
        clock.Restart();
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    }

    internal long RequestFlow() => RequestFlow(FlowQuery);

    internal long RequestFlow(PathQuery query)
    {
        NavigationGuideStatus status = Fixture.Context.Guides.RequestFlowField(query, out NavigationFlowFieldLease? result);
        SwiftThrowHelper.ThrowIfTrue(status != NavigationGuideStatus.Success || !result.HasValue,
            $"Cold public Flow failed: {status}.");
        using NavigationFlowFieldLease lease = result.Value;
        long cost = lease.OriginIntegrationCost.m_rawValue;
        SwiftThrowHelper.ThrowIfTrue(cost != _expectedCost, $"Flow cost changed: {cost}/{_expectedCost}.");
        return cost;
    }

    internal long RequestAStar()
    {
        NavigationGuideStatus status = Fixture.Context.Guides.RequestGuide(AStarQuery, out NavigationGuideLease? result);
        SwiftThrowHelper.ThrowIfTrue(status != NavigationGuideStatus.Success || !result.HasValue,
            $"Cold public A* failed: {status}.");
        using NavigationGuideLease lease = result.Value;
        long cost = lease.TotalCost.m_rawValue;
        SwiftThrowHelper.ThrowIfTrue(cost != _expectedCost || lease.StepCount < 2,
            $"A* route changed: cost={cost}/{_expectedCost}, steps={lease.StepCount}.");
        AStarStepCount = lease.StepCount;
        return cost;
    }

    internal long InternalFlow()
    {
        NavigationWorldGraphLease graph = Fixture.Context.Pathing.TryAcquireNavigationGraph();
        SwiftThrowHelper.ThrowIfTrue(graph == null, "Cold graph lease unavailable.");
        _admission.Begin(graph, FlowQuery, TraversalMedium.Solid, TraversalMedia.Solid);
        try
        {
            while (_admission.Status == NavigationQueryAdmissionStatus.Pending)
                _admission.Advance(FlowQuery.Budget.MaxLookupProbes, FlowQuery.Budget.MaxEndpointCandidates);
            SwiftThrowHelper.ThrowIfTrue(_admission.Status != NavigationQueryAdmissionStatus.Success,
                $"Cold internal admission failed: {_admission.Status}; endpoint_candidates={_admission.Meter.EndpointCandidates}/{FlowQuery.Budget.MaxEndpointCandidates}, lookup_probes={_admission.Meter.LookupProbes}/{FlowQuery.Budget.MaxLookupProbes}.");
            using var work = new NavigationFlowFieldWork(Fixture.World, _admission.Result, _workspace,
                Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.MaximumSinglePayloadBytes);
            while (work.Status == NavigationFlowFieldStatus.Pending)
                work.Advance(FlowQuery.Budget.MaxLookupProbes, NodeCount, FlowQuery.Budget.MaxEvaluatedEdges,
                    FlowQuery.Budget.MaxConnectionLegs);
            SwiftThrowHelper.ThrowIfTrue(work.Status != NavigationFlowFieldStatus.Success || work.Result == null,
                $"Cold internal Flow failed: {work.Status}.");
            NavigationWorkMeter meter = _admission.Meter;
            SettledNodes = meter.ExpandedNodes;
            EndpointCandidates = meter.EndpointCandidates;
            EvaluatedEdges = meter.EvaluatedEdges;
            LookupProbes = meter.LookupProbes;
            TransitionCandidates = meter.TransitionCandidates;
            TransitionPairs = meter.TransitionPairs;
            ValidatePayload(work.Result);
            SwiftThrowHelper.ThrowIfTrue(SettledNodes != NodeCount, $"Settled nodes={SettledNodes}/{NodeCount}.");
            work.Result.TryGetNode(_start, TraversalMedium.Solid, out NavigationFlowFieldNode origin);
            return origin.IntegrationCost.m_rawValue;
        }
        finally
        {
            _admission.Dispose();
        }
    }

    /// <summary>Untimed exact-key inspection; a miss fails instead of silently rebuilding.</summary>
    internal void ValidateFlowPayload(PathQuery query)
    {
        NavigationFlowFieldPayloadCache cache = Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        NavigationWorldGraphStore store = Fixture.Context.Pathing.NavigationGraphStore;
        var key = new NavigationFlowFieldPayloadKey(query, _end, TraversalMedium.Solid, TraversalMedia.Solid);
        NavigationFlowFieldStatus status = cache.TryCheckout(store, store.Current, key, _start, out var lease, out _);
        SwiftThrowHelper.ThrowIfTrue(status != NavigationFlowFieldStatus.Success, $"Expected cached Flow payload: {status}.");
        using (lease)
        {
            SwiftThrowHelper.ThrowIfTrue(lease.TryGetPayload(out NavigationFlowFieldPayload payload)
                != NavigationFlowFieldStatus.Success, "Cached Flow payload unavailable.");
            ValidatePayload(payload);
        }
    }

    private void ValidatePayload(NavigationFlowFieldPayload payload)
    {
        SwiftThrowHelper.ThrowIfTrue(payload.Nodes.Length != NodeCount
            || payload.TransitionInstructions.Length != (_ladder ? 1 : 0),
            $"Payload nodes={payload.Nodes.Length}/{NodeCount}, actions={payload.TransitionInstructions.Length}.");
        PayloadBytes = payload.RetainedBytes;
    }

    /// <summary>Untimed cache removal preserves the prepublished graph and warmed workspace pools.</summary>
    internal void ClearCaches()
    {
        ValidateDrained();
        Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.Reset();
        NavigationAStarPayloadCache cache = Fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        if (cache.Count != 0)
        {
            SwiftThrowHelper.ThrowIfTrue(!cache.TryReservePayload(_maximumAStarBytes, out var reservation),
                "A* eviction reservation failed.");
            try
            {
                SwiftThrowHelper.ThrowIfTrue(!cache.TryCheckoutReserved(_aStarKey,
                    Fixture.Context.Pathing.NavigationGraphStore.Current, ref reservation, out var lease),
                    "A* eviction expected the exact cached request.");
                using (lease)
                {
                    AStarPayloadBytes = lease.Payload.RetainedBytes;
                    cache.RemoveExact(lease.Payload);
                }
            }
            finally
            {
                cache.ReleasePayloadReservation(ref reservation);
            }
        }
        SwiftThrowHelper.ThrowIfTrue(cache.Count != 0 || cache.CachedBytes != 0
            || Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.Count != 0
            || Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.CachedBytes != 0,
            "Cold setup retained a reusable payload.");
        ValidateDrained();
    }

    internal void ValidateDrained()
    {
        NavigationFlowFieldPayloadCache flow = Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        NavigationAStarPayloadCache astar = Fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        SwiftThrowHelper.ThrowIfTrue(flow.ActiveLeaseCount != 0 || flow.ReservedLeaseCount != 0
            || flow.ReservedPayloadBytes != 0 || flow.LeasedBytes != 0 || flow.DetachedBytes != 0
            || astar.ActiveLeaseCount != 0 || astar.ReservedLeaseCount != 0
            || astar.ReservedPayloadBytes != 0 || astar.LeasedBytes != 0 || astar.DetachedBytes != 0,
            "Acquisition leaked an active, detached, or reserved payload.");
    }

    internal void WriteCounters()
    {
        ValidateDrained();
        Console.WriteLine($"NAVIGATION_COLD_PUBLIC geometry={Geometry} nodes={NodeCount} status=Success "
            + $"matched_internal_settled={SettledNodes} matched_internal_edges={EvaluatedEdges} "
            + $"matched_internal_endpoint_candidates={EndpointCandidates} "
            + $"matched_internal_lookup={LookupProbes} transition_candidates={TransitionCandidates} transition_pairs={TransitionPairs} "
            + $"flow_payload_bytes={PayloadBytes} astar_payload_bytes={AStarPayloadBytes} astar_steps={AStarStepCount} "
            + $"internal_workspace_allocated_bytes={WorkspaceAllocatedBytes} "
            + $"warm_public_allocated_bytes={WarmAllocatedBytes} "
            + $"flow_cached_bytes={Fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.CachedBytes} "
            + "active_leases=0 reserved_leases=0 detached_bytes=0");
    }

    public void Dispose()
    {
        _admission.Dispose();
        Fixture.Teardown();
    }

    private PathQuery PublishLadder(GridConfiguration configuration, string mapId, int width, NavigationWorkBudget budget)
    {
        SwiftThrowHelper.ThrowIfTrue(!configuration.TryNormalize(out var binding), "Invalid ladder geometry.");
        var builder = new NavigationMapBuilder(mapId, binding);
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, (Fixed64)4, (Fixed64)4);
        for (int x = 0; x < width; x++)
        {
            builder.AddCell(new VoxelIndex(x, 0, 0), cell);
            builder.AddCell(new VoxelIndex(x, 2, 0), cell);
        }
        builder.AddTransition(new TraversalTransitionDefinition("ladder", TraversalTransitionType.Climb,
            new VoxelIndex(width - 1, 0, 0), TraversalMedium.Solid,
            new NavigationCellAddress(mapId, new VoxelIndex(width - 1, 2, 0)), TraversalMedium.Solid,
            TraversalCapability.Climb, Fixed64.One, TraversalTransitionLocomotionHints.RequestClimb));
        var map = new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear, 1, Fixture.Context.FrameCount + 1);
        var policyKey = new NavigationAreaPolicyKey(mapId, 1);
        var policy = new NavigationAreaPolicyCommitOperation(new NavigationAreaPolicy(policyKey,
            new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 2, Fixture.Context.FrameCount + 1);
        SwiftThrowHelper.ThrowIfTrue(!Fixture.Context.Pathing.Admit(map) || !Fixture.Context.Pathing.Admit(policy),
            "Ladder publication was not admitted.");
        for (int i = 0; i < 4096 && (map.Receipt.Status == NavigationOperationStatus.Pending
            || policy.Receipt.Status == NavigationOperationStatus.Pending); i++)
            Fixture.Context.Simulate();
        SwiftThrowHelper.ThrowIfTrue(map.Receipt.Status != NavigationOperationStatus.Applied
            || policy.Receipt.Status != NavigationOperationStatus.Applied, "Ladder publication failed.");
        var agent = new NavigationAgentProfile(new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.Climb);
        return new PathQuery(new NavigationEndpoint(NavigationGraphBenchmarkScenario.GetFoot(binding, _start.Index), mapId),
            new NavigationEndpoint(NavigationGraphBenchmarkScenario.GetFoot(binding, _end.Index), mapId), agent,
            policyKey, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), PathAlgorithm.AStar, budget, true);
    }

    private static PathQuery CopyAlgorithm(PathQuery query, PathAlgorithm algorithm) => new(query.Start, query.End,
        query.Agent, query.AreaPolicy, query.Traversal, algorithm, query.Budget, query.AllowTransitions,
        new FlowFieldQueryOptions(Fixed64.Zero));

    private static TrailblazerWorldContextSettings CreateSettings(int nodes, int activeLeases,
        NavigationSchedulingSettings schedulingSettings)
    {
        TrailblazerWorldContextSettings defaults = NavigationGraphBenchmarkScenario.CreateSettings(nodes, 1, 8);
        int pages = NavigationGraphBenchmarkScenario.GetPageCapacity(nodes);
        int points = checked(nodes * 2 - 1);
        long astar = NavigationAStarPayload.GetMaximumRetainedBytes(points, nodes - 1, 2, pages);
        long flow = NavigationFlowFieldPayload.GetMaximumRetainedBytes(nodes, nodes - 1, 2, pages);
        var limits = new NavigationQueryLimits(maxBatchItems: 1, maxBatchDescriptorBytes: 65_536,
            maxConcurrentNavigationQueries: 1, aStarWorkspaceMapCapacity: 1,
            aStarWorkspaceEndpointPageCapacity: pages, aStarWorkspaceComponentCapacity: 2,
            aStarWorkspaceNodeCapacity: nodes, maxAStarCacheEntries: 8,
            maxAStarReusablePayloadBytes: astar * 8, maxAStarSinglePayloadBytes: astar,
            maxAStarActivePayloadBytes: astar * activeLeases, maxAStarActivePayloadLeases: activeLeases,
            flowWorkspaceMapCapacity: 1, flowWorkspaceEndpointPageCapacity: pages,
            flowWorkspaceComponentCapacity: 2, flowWorkspaceNodeCapacity: nodes,
            maxFlowCacheEntries: 8, maxFlowReusablePayloadBytes: flow * 8,
            maxFlowSinglePayloadBytes: flow, maxFlowActivePayloadBytes: flow * activeLeases,
            maxFlowActivePayloadLeases: activeLeases, rayWorkspaceCoveredAddressCapacity: nodes,
            rayWorkspaceTraceIntervalCapacity: nodes, aStarWorkspaceGuidePointCapacity: points);
        return new TrailblazerWorldContextSettings(defaults.OperationLimits, defaults.MaintenanceBudget,
            defaults.GuideSampleBudget, defaults.MovementGroupPadding, defaults.MaxIngressEntries,
            defaults.MaxIngressBytes, defaults.MaxActiveSnapshots, defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots, defaults.MaxRetiredSnapshotBytes, defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap, defaults.MaxDynamicCellSlots, defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies, defaults.MaxAreaRulesPerPolicy, defaults.MaxAreaRules,
            Math.Max(defaults.MaxConcurrentSnapshotLeases, activeLeases), limits, schedulingSettings);
    }
}
