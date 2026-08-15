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

    internal static PathQuery Publish(
        BenchmarkPathFixture fixture,
        GridConfiguration configuration,
        string mapId,
        int width,
        int length,
        NavigationWorkBudget budget)
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
            operationSequence: 1,
            effectiveFrame: 1);
        var policyKey = new NavigationAreaPolicyKey(mapId, revision: 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 2,
            effectiveFrame: 1);
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
                + $"policy={policyOperation.Receipt.Status}.");
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
        long maximumPayloadBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            nodeCapacity,
            componentCount: 1,
            pageCapacity);
        NavigationQueryLimits queryLimits = new(
            maxBatchItems: concurrentQueries,
            maxBatchDescriptorBytes: 65_536,
            maxConcurrentAStarQueries: concurrentQueries,
            aStarWorkspaceMapCapacity: 1,
            aStarWorkspaceEndpointPageCapacity: pageCapacity,
            aStarWorkspaceNodeCapacity: nodeCapacity,
            maxAStarCacheEntries: cacheEntries,
            maxAStarReusablePayloadBytes: Math.Max(16_777_216L, maximumPayloadBytes * cacheEntries),
            maxAStarSinglePayloadBytes: maximumPayloadBytes,
            maxAStarActivePayloadBytes: checked(maximumPayloadBytes * concurrentQueries),
            maxAStarActivePayloadLeases: concurrentQueries);
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
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

    internal static NavigationWorkBudget CreateBudget(int nodeCount, int edgeSlack = 0) => new(
        maxLookupProbes: Math.Max(1_024, checked(nodeCount * 4)),
        maxEndpointCandidates: 2,
        maxExpandedNodes: nodeCount,
        maxEvaluatedEdges: checked((nodeCount * 4) + edgeSlack),
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

    private static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        if (!binding.TryGetCellPrism(index, out GridCellPrism prism))
            throw new InvalidOperationException($"The graph benchmark could not resolve cell {index}.");
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }
}
