//=======================================================================
// NavigationGraphBenchmarkScenario.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>Builds one production graph-backed benchmark route without benchmark-only runtime hooks.</summary>
internal static class NavigationGraphBenchmarkScenario
{
    internal static GridConfiguration CreateConfiguration(int width, int length) => new(
        Vector3d.Zero,
        new Vector3d(width - 1, 0, length - 1),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: GridStorageKind.Dense);

    internal static GridConfiguration CreateConfiguration(
        int width,
        int length,
        Vector3d origin) => new(
        origin,
        origin + new Vector3d(width - 1, 0, length - 1),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: GridStorageKind.Dense);

    internal static PathQuery Publish(
        BenchmarkPathFixture fixture,
        GridConfiguration configuration,
        string mapId,
        int width,
        int length,
        NavigationWorkBudget budget,
        long operationSequence = 1)
    {
        if (!configuration.TryNormalize(out NormalizedGridConfiguration binding))
            throw new InvalidOperationException("The graph benchmark configuration is invalid.");

        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < length; z++)
                builder.AddCell(new VoxelIndex(x, 0, z), cell);
        }

        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence,
            effectiveFrame: fixture.Context.FrameCount + 1);
        var policyKey = new NavigationAreaPolicyKey(mapId, revision: 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: checked(operationSequence + 1),
            effectiveFrame: fixture.Context.FrameCount + 1);
        if (!fixture.Context.Pathing.Admit(mapOperation)
            || !fixture.Context.Pathing.Admit(policyOperation))
        {
            throw new InvalidOperationException("The graph benchmark map or area policy was not admitted.");
        }
        for (int frame = 0;
            frame < 4_096
            && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
                || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
            frame++)
        {
            fixture.Context.Simulate();
        }
        if (mapOperation.Receipt.Status != NavigationOperationStatus.Applied
            || policyOperation.Receipt.Status != NavigationOperationStatus.Applied)
        {
            throw new InvalidOperationException(
                $"The graph benchmark publication failed: map={mapOperation.Receipt.Status}, "
                + $"map_rejection={mapOperation.Receipt.Rejection}, "
                + $"policy={policyOperation.Receipt.Status}, "
                + $"policy_rejection={policyOperation.Receipt.Rejection}.");
        }

        var startIndex = default(VoxelIndex);
        var endIndex = new VoxelIndex(width - 1, 0, length - 1);
        Vector3d start = GetFoot(binding, startIndex);
        Vector3d end = GetFoot(binding, endIndex);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        return new PathQuery(
            new NavigationEndpoint(start, mapId),
            new NavigationEndpoint(end, mapId),
            profile,
            policyKey,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            budget,
            allowTransitions: false);
    }

    internal static TrailblazerWorldContextSettings CreateSettings(
        int nodeCapacity,
        int concurrentQueries,
        int cacheEntries = 128)
    {
        int pageCapacity = checked(((nodeCapacity + NavigationSemanticPage.SlotCount - 1)
            / NavigationSemanticPage.SlotCount) + 2);
        int guidePointCapacity = checked((nodeCapacity * 2) - 1);
        long maximumAStarPayloadBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            guidePointCapacity,
            componentCount: 1,
            pageCapacity);
        long maximumFlowPayloadBytes = NavigationFlowFieldPayload.GetMaximumRetainedBytes(
            nodeCapacity,
            componentCount: 1,
            pageCapacity);
        NavigationQueryLimits queryLimits = new(
            maxBatchItems: concurrentQueries,
            maxBatchDescriptorBytes: 65_536,
            maxConcurrentNavigationQueries: concurrentQueries,
            aStarWorkspaceMapCapacity: 1,
            aStarWorkspaceEndpointPageCapacity: pageCapacity,
            aStarWorkspaceNodeCapacity: nodeCapacity,
            maxAStarCacheEntries: cacheEntries,
            maxAStarReusablePayloadBytes: Math.Max(16_777_216L, maximumAStarPayloadBytes * cacheEntries),
            maxAStarSinglePayloadBytes: maximumAStarPayloadBytes,
            maxAStarActivePayloadBytes: checked(maximumAStarPayloadBytes * concurrentQueries),
            maxAStarActivePayloadLeases: concurrentQueries,
            aStarWorkspaceComponentCapacity: 1,
            flowWorkspaceMapCapacity: 1,
            flowWorkspaceEndpointPageCapacity: pageCapacity,
            flowWorkspaceComponentCapacity: 1,
            flowWorkspaceNodeCapacity: nodeCapacity,
            maxFlowCacheEntries: cacheEntries,
            maxFlowReusablePayloadBytes: Math.Max(33_554_432L, maximumFlowPayloadBytes * cacheEntries),
            maxFlowSinglePayloadBytes: maximumFlowPayloadBytes,
            maxFlowActivePayloadBytes: checked(maximumFlowPayloadBytes * concurrentQueries),
            maxFlowActivePayloadLeases: concurrentQueries,
            rayWorkspaceCoveredAddressCapacity: nodeCapacity,
            rayWorkspaceTraceIntervalCapacity: nodeCapacity,
            aStarWorkspaceGuidePointCapacity: guidePointCapacity);
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            Math.Max(defaults.MaxActiveSnapshotBytes, 268_435_456L),
            defaults.MaxRetiredSnapshots,
            Math.Max(defaults.MaxRetiredSnapshotBytes, 268_435_456L),
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            Math.Max(defaults.MaxConcurrentSnapshotLeases, concurrentQueries),
            queryLimits);
    }

    internal static TrailblazerWorldContextSettings CreateArticulationSettings(
        int nodeCapacity)
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            nodeCapacity,
            concurrentQueries: 2,
            cacheEntries: 1);
        NavigationOperationLimits limits = settings.OperationLimits;
        NavigationQueryLimits query = settings.QueryLimits;
        long maximumFlowPayloadBytes = NavigationFlowFieldPayload.GetMaximumRetainedBytes(
            nodeCapacity,
            componentCount: 2,
            query.FlowWorkspaceEndpointPageCapacity);
        var articulationQueryLimits = new NavigationQueryLimits(
            query.MaxBatchItems,
            query.MaxBatchDescriptorBytes,
            query.MaxConcurrentNavigationQueries,
            query.AStarWorkspaceMapCapacity,
            query.AStarWorkspaceEndpointPageCapacity,
            query.AStarWorkspaceComponentCapacity,
            query.AStarWorkspaceNodeCapacity,
            query.MaxAStarCacheEntries,
            query.MaxAStarReusablePayloadBytes,
            query.MaxAStarSinglePayloadBytes,
            query.MaxAStarActivePayloadBytes,
            query.MaxAStarActivePayloadLeases,
            query.FlowWorkspaceMapCapacity,
            query.FlowWorkspaceEndpointPageCapacity,
            flowWorkspaceComponentCapacity: 2,
            query.FlowWorkspaceNodeCapacity,
            query.RayWorkspaceCoveredAddressCapacity,
            query.RayWorkspaceTraceIntervalCapacity,
            query.AStarWorkspaceGuidePointCapacity,
            query.MaxFlowCacheEntries,
            maxFlowReusablePayloadBytes: maximumFlowPayloadBytes,
            maxFlowSinglePayloadBytes: maximumFlowPayloadBytes,
            maxFlowActivePayloadBytes: checked(maximumFlowPayloadBytes * 2),
            query.MaxFlowActivePayloadLeases);
        var largeLimits = new NavigationOperationLimits(
            limits.MaxPendingOperations,
            limits.MaxPendingDescriptorBytes,
            maxPreparedMapBytes: 536_870_912L,
            limits.MaxBatchItems,
            limits.MaxBatchDescriptorBytes,
            limits.MaxBatchSortScratchBytes,
            limits.MaxCorridorCells,
            limits.MaxMaps,
            limits.MaxRetainedMapIdentities,
            limits.MaxOverlayCellsPerMap,
            limits.MaxOverlayConnectionsPerMap,
            limits.MaxOverlayTransitionsPerMap,
            limits.MaxOverlayCells,
            limits.MaxOverlayConnections,
            limits.MaxOverlayTransitions);
        return new TrailblazerWorldContextSettings(
            largeLimits,
            settings.MaintenanceBudget,
            settings.GuideSampleBudget,
            settings.MaxIngressEntries,
            settings.MaxIngressBytes,
            settings.MaxActiveSnapshots,
            maxActiveSnapshotBytes: 4_294_967_296L,
            settings.MaxRetiredSnapshots,
            maxRetiredSnapshotBytes: 4_294_967_296L,
            maxPersistentGraphPages: 8_388_608,
            settings.MaxDynamicCellSlotsPerMap,
            settings.MaxDynamicCellSlots,
            settings.NavigationAreaCount,
            settings.MaxAreaPolicies,
            settings.MaxAreaRulesPerPolicy,
            settings.MaxAreaRules,
            settings.MaxConcurrentSnapshotLeases,
            articulationQueryLimits);
    }

    internal static NavigationWorkBudget CreateBudget(int nodeCount, int edgeSlack = 0) => new(
        maxLookupProbes: Math.Max(1_024, checked(nodeCount * 128)),
        maxEndpointCandidates: 2,
        maxExpandedNodes: nodeCount,
        maxEvaluatedEdges: checked((nodeCount * 8) + edgeSlack),
        maxConnectionLegs: 0,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 0,
        maxSimplificationRays: 0);

    internal static int GetPageCapacity(int nodeCapacity) => checked(
        ((nodeCapacity + NavigationSemanticPage.SlotCount - 1)
        / NavigationSemanticPage.SlotCount) + 2);

    internal static PathQuery ToFlow(PathQuery query) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        PathAlgorithm.FlowField,
        query.Budget,
        allowTransitions: false,
        new FlowFieldQueryOptions(Fixed64.Zero));

    internal static PathQuery WithStart(
        PathQuery query,
        GridConfiguration configuration,
        VoxelIndex index)
    {
        if (!configuration.TryNormalize(out NormalizedGridConfiguration binding))
            throw new InvalidOperationException("The graph benchmark configuration is invalid.");
        return query.WithStartPosition(GetFoot(binding, index));
    }

    internal static NavigationFlowQueryResult ExecuteFlow(
        NavigationFlowAdmissionGate gate,
        PathQuery query)
    {
        NavigationFlowQueryStatus status = ExecuteFlow(gate, query, out NavigationFlowQueryResult result);
        if (status != NavigationFlowQueryStatus.Success)
            throw new InvalidOperationException($"The Flow benchmark query failed with {status}.");
        return result;
    }

    internal static NavigationFlowQueryStatus ExecuteFlow(
        NavigationFlowAdmissionGate gate,
        PathQuery query,
        out NavigationFlowQueryResult result)
    {
        result = default;
        if (gate.Begin(query, out NavigationFlowBatchWork work)
            != NavigationFlowQueryStatus.Pending)
        {
            throw new InvalidOperationException("The Flow benchmark query was not admitted.");
        }
        try
        {
            while (!work.IsAdmissionComplete)
                work.AdvanceAdmission(int.MaxValue, int.MaxValue);
            while (!work.IsReadyToPublish(0))
                work.AdvanceSearch(0, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            if (work.PublishReadyPrefix(1) != 1)
                throw new InvalidOperationException("The Flow benchmark query did not publish.");
            NavigationFlowQueryStatus status = work.GetStatus(0);
            if (status == NavigationFlowQueryStatus.Success)
                result = work.TakeResult(0);
            return status;
        }
        finally
        {
            work.Dispose();
        }
    }

    internal static bool IsStrictPrefix(
        NavigationFlowFieldPayload prefix,
        NavigationFlowFieldPayload longer)
    {
        if (prefix.Nodes.Length >= longer.Nodes.Length
            || prefix.Dependencies.Components.Length > longer.Dependencies.Components.Length
            || prefix.Dependencies.Pages.Length > longer.Dependencies.Pages.Length)
        {
            return false;
        }
        for (int i = 0; i < prefix.Nodes.Length; i++)
        {
            if (!prefix.Nodes[i].Equals(longer.Nodes[i]))
                return false;
        }
        for (int i = 0; i < prefix.Dependencies.Components.Length; i++)
        {
            if (!prefix.Dependencies.Components[i].Equals(longer.Dependencies.Components[i]))
                return false;
        }
        for (int i = 0; i < prefix.Dependencies.Pages.Length; i++)
        {
            if (!prefix.Dependencies.Pages[i].Equals(longer.Dependencies.Pages[i]))
                return false;
        }
        return true;
    }

    internal static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        if (!binding.TryGetCellPrism(index, out GridCellPrism prism))
            throw new InvalidOperationException($"The graph benchmark could not resolve cell {index}.");
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }
}
