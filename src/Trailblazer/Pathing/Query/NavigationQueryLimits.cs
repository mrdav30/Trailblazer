//=======================================================================
// NavigationQueryLimits.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Defines finite context-owned navigation query admission and retention ceilings.</summary>
public readonly struct NavigationQueryLimits
{
    /// <summary>The hard ceiling for one deterministic query batch.</summary>
    public const int MaximumBatchItems = 256;

    /// <summary>Gets the recommended finite navigation query limits.</summary>
    public static NavigationQueryLimits Default { get; } = new(
        maxBatchItems: 8,
        maxBatchDescriptorBytes: 65_536,
        maxConcurrentNavigationQueries: 8,
        aStarWorkspaceMapCapacity: 16,
        aStarWorkspaceEndpointPageCapacity: 512,
        aStarWorkspaceComponentCapacity: 512,
        aStarWorkspaceNodeCapacity: 4_096,
        maxAStarCacheEntries: 128,
        maxAStarReusablePayloadBytes: 16_777_216,
        maxAStarSinglePayloadBytes: 1_159_480,
        maxAStarActivePayloadBytes: 4_194_304,
        maxAStarActivePayloadLeases: 8,
        flowWorkspaceMapCapacity: 16,
        flowWorkspaceEndpointPageCapacity: 512,
        flowWorkspaceComponentCapacity: 512,
        flowWorkspaceNodeCapacity: 4_096,
        rayWorkspaceCoveredAddressCapacity: 4_096,
        rayWorkspaceTraceIntervalCapacity: 4_096,
        aStarWorkspaceGuidePointCapacity: 8_191,
        maxFlowCacheEntries: 128,
        maxFlowReusablePayloadBytes: 33_554_432,
        maxFlowSinglePayloadBytes: 1_012_024,
        maxFlowActivePayloadBytes: 4_194_304,
        maxFlowActivePayloadLeases: 8);

    /// <summary>Initializes explicit finite navigation query limits.</summary>
    public NavigationQueryLimits(
        int maxBatchItems,
        long maxBatchDescriptorBytes,
        int maxConcurrentNavigationQueries,
        int aStarWorkspaceMapCapacity,
        int aStarWorkspaceEndpointPageCapacity,
        int aStarWorkspaceComponentCapacity,
        int aStarWorkspaceNodeCapacity,
        int maxAStarCacheEntries,
        long maxAStarReusablePayloadBytes,
        long maxAStarSinglePayloadBytes,
        long maxAStarActivePayloadBytes,
        int maxAStarActivePayloadLeases,
        int flowWorkspaceMapCapacity,
        int flowWorkspaceEndpointPageCapacity,
        int flowWorkspaceComponentCapacity,
        int flowWorkspaceNodeCapacity,
        int rayWorkspaceCoveredAddressCapacity,
        int rayWorkspaceTraceIntervalCapacity,
        int aStarWorkspaceGuidePointCapacity,
        int maxFlowCacheEntries,
        long maxFlowReusablePayloadBytes,
        long maxFlowSinglePayloadBytes,
        long maxFlowActivePayloadBytes,
        int maxFlowActivePayloadLeases)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxBatchItems <= 0 || maxBatchItems > MaximumBatchItems,
            maxBatchItems,
            nameof(maxBatchItems));
        ThrowIfNonPositive(maxBatchDescriptorBytes, nameof(maxBatchDescriptorBytes));
        ThrowIfNonPositive(
            maxConcurrentNavigationQueries,
            nameof(maxConcurrentNavigationQueries));
        ValidateWorkspace(
            aStarWorkspaceMapCapacity,
            aStarWorkspaceEndpointPageCapacity,
            aStarWorkspaceComponentCapacity,
            aStarWorkspaceNodeCapacity,
            nameof(aStarWorkspaceMapCapacity),
            nameof(aStarWorkspaceEndpointPageCapacity),
            nameof(aStarWorkspaceComponentCapacity),
            nameof(aStarWorkspaceNodeCapacity));
        ValidateCache(
            maxAStarCacheEntries,
            maxAStarReusablePayloadBytes,
            maxAStarSinglePayloadBytes,
            maxAStarActivePayloadBytes,
            maxAStarActivePayloadLeases,
            nameof(maxAStarCacheEntries),
            nameof(maxAStarReusablePayloadBytes),
            nameof(maxAStarSinglePayloadBytes),
            nameof(maxAStarActivePayloadBytes),
            nameof(maxAStarActivePayloadLeases));
        ValidateWorkspace(
            flowWorkspaceMapCapacity,
            flowWorkspaceEndpointPageCapacity,
            flowWorkspaceComponentCapacity,
            flowWorkspaceNodeCapacity,
            nameof(flowWorkspaceMapCapacity),
            nameof(flowWorkspaceEndpointPageCapacity),
            nameof(flowWorkspaceComponentCapacity),
            nameof(flowWorkspaceNodeCapacity));
        ThrowIfNonPositive(
            rayWorkspaceCoveredAddressCapacity,
            nameof(rayWorkspaceCoveredAddressCapacity));
        ThrowIfNonPositive(
            rayWorkspaceTraceIntervalCapacity,
            nameof(rayWorkspaceTraceIntervalCapacity));
        SwiftThrowHelper.ThrowIfArgument(
            rayWorkspaceTraceIntervalCapacity > rayWorkspaceCoveredAddressCapacity,
            nameof(rayWorkspaceTraceIntervalCapacity),
            "Trace-interval capacity cannot exceed covered-address capacity.");
        ThrowIfNonPositive(
            aStarWorkspaceGuidePointCapacity,
            nameof(aStarWorkspaceGuidePointCapacity));
        SwiftThrowHelper.ThrowIfArgument(
            aStarWorkspaceGuidePointCapacity < aStarWorkspaceNodeCapacity,
            nameof(aStarWorkspaceGuidePointCapacity),
            "A* guide-point capacity must cover every admitted search node.");
        ValidateCache(
            maxFlowCacheEntries,
            maxFlowReusablePayloadBytes,
            maxFlowSinglePayloadBytes,
            maxFlowActivePayloadBytes,
            maxFlowActivePayloadLeases,
            nameof(maxFlowCacheEntries),
            nameof(maxFlowReusablePayloadBytes),
            nameof(maxFlowSinglePayloadBytes),
            nameof(maxFlowActivePayloadBytes),
            nameof(maxFlowActivePayloadLeases));

        MaxBatchItems = maxBatchItems;
        MaxBatchDescriptorBytes = maxBatchDescriptorBytes;
        MaxConcurrentNavigationQueries = maxConcurrentNavigationQueries;
        AStarWorkspaceMapCapacity = aStarWorkspaceMapCapacity;
        AStarWorkspaceEndpointPageCapacity = aStarWorkspaceEndpointPageCapacity;
        AStarWorkspaceComponentCapacity = aStarWorkspaceComponentCapacity;
        AStarWorkspaceNodeCapacity = aStarWorkspaceNodeCapacity;
        MaxAStarCacheEntries = maxAStarCacheEntries;
        MaxAStarReusablePayloadBytes = maxAStarReusablePayloadBytes;
        MaxAStarSinglePayloadBytes = maxAStarSinglePayloadBytes;
        MaxAStarActivePayloadBytes = maxAStarActivePayloadBytes;
        MaxAStarActivePayloadLeases = maxAStarActivePayloadLeases;
        FlowWorkspaceMapCapacity = flowWorkspaceMapCapacity;
        FlowWorkspaceEndpointPageCapacity = flowWorkspaceEndpointPageCapacity;
        FlowWorkspaceComponentCapacity = flowWorkspaceComponentCapacity;
        FlowWorkspaceNodeCapacity = flowWorkspaceNodeCapacity;
        RayWorkspaceCoveredAddressCapacity = rayWorkspaceCoveredAddressCapacity;
        RayWorkspaceTraceIntervalCapacity = rayWorkspaceTraceIntervalCapacity;
        AStarWorkspaceGuidePointCapacity = aStarWorkspaceGuidePointCapacity;
        MaxFlowCacheEntries = maxFlowCacheEntries;
        MaxFlowReusablePayloadBytes = maxFlowReusablePayloadBytes;
        MaxFlowSinglePayloadBytes = maxFlowSinglePayloadBytes;
        MaxFlowActivePayloadBytes = maxFlowActivePayloadBytes;
        MaxFlowActivePayloadLeases = maxFlowActivePayloadLeases;
    }

    /// <summary>Gets the maximum query descriptors accepted by one batch.</summary>
    public int MaxBatchItems { get; }

    /// <summary>Gets the maximum logical bytes accepted for submitted batch descriptors.</summary>
    public long MaxBatchDescriptorBytes { get; }

    /// <summary>Gets the aggregate maximum concurrently admitted A* and Flow queries.</summary>
    public int MaxConcurrentNavigationQueries { get; }

    /// <summary>Gets the map capacity of each exclusive A* workspace.</summary>
    public int AStarWorkspaceMapCapacity { get; }

    /// <summary>Gets the endpoint-page capacity of each exclusive A* workspace.</summary>
    public int AStarWorkspaceEndpointPageCapacity { get; }

    /// <summary>Gets the component-dependency capacity of each exclusive A* workspace.</summary>
    public int AStarWorkspaceComponentCapacity { get; }

    /// <summary>Gets the node capacity of each exclusive A* workspace.</summary>
    public int AStarWorkspaceNodeCapacity { get; }

    /// <summary>Gets the maximum reusable A* payload count.</summary>
    public int MaxAStarCacheEntries { get; }

    /// <summary>Gets the maximum reusable A* payload bytes.</summary>
    public long MaxAStarReusablePayloadBytes { get; }

    /// <summary>Gets the maximum bytes retained by one A* payload.</summary>
    public long MaxAStarSinglePayloadBytes { get; }

    /// <summary>Gets the maximum bytes retained by active A* payloads.</summary>
    public long MaxAStarActivePayloadBytes { get; }

    /// <summary>Gets the maximum active and reserved A* payload lease count.</summary>
    public int MaxAStarActivePayloadLeases { get; }

    /// <summary>Gets the map capacity of each exclusive Flow workspace.</summary>
    public int FlowWorkspaceMapCapacity { get; }

    /// <summary>Gets the endpoint and dependency-page capacity of each exclusive Flow workspace.</summary>
    public int FlowWorkspaceEndpointPageCapacity { get; }

    /// <summary>Gets the component-dependency capacity of each exclusive Flow workspace.</summary>
    public int FlowWorkspaceComponentCapacity { get; }

    /// <summary>Gets the node capacity of each exclusive Flow workspace.</summary>
    public int FlowWorkspaceNodeCapacity { get; }

    /// <summary>Gets the covered-address capacity of each navigation-ray workspace.</summary>
    public int RayWorkspaceCoveredAddressCapacity { get; }

    /// <summary>Gets the ordered trace-interval capacity of each navigation-ray workspace.</summary>
    public int RayWorkspaceTraceIntervalCapacity { get; }

    /// <summary>Gets the raw and simplified guide-point capacity of each A* workspace.</summary>
    public int AStarWorkspaceGuidePointCapacity { get; }

    /// <summary>Gets the maximum reusable Flow payload count.</summary>
    public int MaxFlowCacheEntries { get; }

    /// <summary>Gets the maximum reusable Flow payload bytes.</summary>
    public long MaxFlowReusablePayloadBytes { get; }

    /// <summary>Gets the maximum bytes retained by one Flow payload.</summary>
    public long MaxFlowSinglePayloadBytes { get; }

    /// <summary>Gets the maximum bytes retained by unique active Flow payloads.</summary>
    public long MaxFlowActivePayloadBytes { get; }

    /// <summary>Gets the maximum active and reserved Flow payload lease count.</summary>
    public int MaxFlowActivePayloadLeases { get; }

    private static void ValidateWorkspace(
        int mapCapacity,
        int pageCapacity,
        int componentCapacity,
        int nodeCapacity,
        string mapName,
        string pageName,
        string componentName,
        string nodeName)
    {
        SwiftThrowHelper.ThrowIfNegative(mapCapacity, mapName);
        SwiftThrowHelper.ThrowIfNegative(pageCapacity, pageName);
        SwiftThrowHelper.ThrowIfNegative(componentCapacity, componentName);
        SwiftThrowHelper.ThrowIfNegative(nodeCapacity, nodeName);
    }

    private static void ValidateCache(
        int entries,
        long reusableBytes,
        long singleBytes,
        long activeBytes,
        int activeLeases,
        string entriesName,
        string reusableName,
        string singleName,
        string activeName,
        string leaseName)
    {
        SwiftThrowHelper.ThrowIfNegative(entries, entriesName);
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            reusableBytes < 0,
            null,
            reusableName);
        ThrowIfNonPositive(singleBytes, singleName);
        ThrowIfNonPositive(activeBytes, activeName);
        SwiftThrowHelper.ThrowIfArgument(
            singleBytes > activeBytes,
            singleName,
            "A single payload cannot exceed the complete active-payload byte ceiling.");
        ThrowIfNonPositive(activeLeases, leaseName);
    }

    private static void ThrowIfNonPositive(int value, string parameterName) =>
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(value <= 0, value, parameterName);

    private static void ThrowIfNonPositive(long value, string parameterName) =>
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(value <= 0, null, parameterName);
}
