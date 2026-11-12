using Trailblazer.Pathing;

namespace Trailblazer.Tests;

internal static class PlanarSchedulingTestSettings
{
    internal static TrailblazerWorldContextSettings Create(NavigationSchedulingSettings? scheduling = null,
        long? maxBatchDescriptorBytes = null, GuideSampleWorkBudget? guideSampleBudget = null)
    {
        var defaults = TrailblazerWorldContextSettings.Default;
        var d = defaults.QueryLimits;
        var limits = maxBatchDescriptorBytes is long bytes
            ? new NavigationQueryLimits(d.MaxBatchItems, bytes, d.MaxConcurrentNavigationQueries,
                d.AStarWorkspaceMapCapacity, d.AStarWorkspaceEndpointPageCapacity, d.AStarWorkspaceComponentCapacity,
                d.AStarWorkspaceNodeCapacity, d.MaxAStarCacheEntries, d.MaxAStarReusablePayloadBytes,
                d.MaxAStarSinglePayloadBytes, d.MaxAStarActivePayloadBytes, d.MaxAStarActivePayloadLeases,
                d.FlowWorkspaceMapCapacity, d.FlowWorkspaceEndpointPageCapacity, d.FlowWorkspaceComponentCapacity,
                d.FlowWorkspaceNodeCapacity, d.RayWorkspaceCoveredAddressCapacity, d.RayWorkspaceTraceIntervalCapacity,
                d.AStarWorkspaceGuidePointCapacity, d.MaxFlowCacheEntries, d.MaxFlowReusablePayloadBytes,
                d.MaxFlowSinglePayloadBytes, d.MaxFlowActivePayloadBytes, d.MaxFlowActivePayloadLeases)
            : d;
        scheduling ??= new NavigationSchedulingSettings(lookupStepLimit: 1, endpointCandidateStepLimit: 1,
            nodeStepLimit: 1, edgeStepLimit: 1, connectionStepLimit: 1);
        return new TrailblazerWorldContextSettings(defaults.OperationLimits, defaults.MaintenanceBudget,
            guideSampleBudget ?? defaults.GuideSampleBudget, defaults.MovementGroupPadding, defaults.MaxIngressEntries,
            defaults.MaxIngressBytes, defaults.MaxActiveSnapshots, defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots, defaults.MaxRetiredSnapshotBytes, defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap, defaults.MaxDynamicCellSlots, defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies, defaults.MaxAreaRulesPerPolicy, defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases, limits, scheduling);
    }
}
