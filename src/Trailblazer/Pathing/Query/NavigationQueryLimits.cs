//=======================================================================
// NavigationQueryLimits.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Defines finite context-owned A* query admission and retention ceilings.</summary>
public readonly struct NavigationQueryLimits
{
    /// <summary>The hard ceiling for one deterministic query batch.</summary>
    public const int MaximumBatchItems = 256;

    /// <summary>Gets the recommended finite A* query limits.</summary>
    public static NavigationQueryLimits Default { get; } = new(
        maxBatchItems: 8,
        maxBatchDescriptorBytes: 65_536,
        maxConcurrentAStarQueries: 8,
        aStarWorkspaceMapCapacity: 16,
        aStarWorkspaceEndpointPageCapacity: 512,
        aStarWorkspaceNodeCapacity: 4_096,
        maxAStarCacheEntries: 128,
        maxAStarReusablePayloadBytes: 16_777_216,
        maxAStarSinglePayloadBytes: 262_144,
        maxAStarActivePayloadBytes: 2_097_152,
        maxAStarActivePayloadLeases: 8,
        aStarWorkspaceComponentCapacity: 512);

    /// <summary>Initializes explicit finite A* query limits.</summary>
    public NavigationQueryLimits(
        int maxBatchItems,
        long maxBatchDescriptorBytes,
        int maxConcurrentAStarQueries,
        int aStarWorkspaceMapCapacity,
        int aStarWorkspaceEndpointPageCapacity,
        int aStarWorkspaceNodeCapacity,
        int maxAStarCacheEntries,
        long maxAStarReusablePayloadBytes,
        long maxAStarSinglePayloadBytes,
        long maxAStarActivePayloadBytes,
        int maxAStarActivePayloadLeases,
        int aStarWorkspaceComponentCapacity)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxBatchItems <= 0 || maxBatchItems > MaximumBatchItems,
            maxBatchItems,
            nameof(maxBatchItems));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxBatchDescriptorBytes <= 0,
            null,
            nameof(maxBatchDescriptorBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxConcurrentAStarQueries <= 0,
            maxConcurrentAStarQueries,
            nameof(maxConcurrentAStarQueries));
        SwiftThrowHelper.ThrowIfNegative(
            aStarWorkspaceMapCapacity,
            nameof(aStarWorkspaceMapCapacity));
        SwiftThrowHelper.ThrowIfNegative(
            aStarWorkspaceEndpointPageCapacity,
            nameof(aStarWorkspaceEndpointPageCapacity));
        SwiftThrowHelper.ThrowIfNegative(
            aStarWorkspaceComponentCapacity,
            nameof(aStarWorkspaceComponentCapacity));
        SwiftThrowHelper.ThrowIfNegative(
            aStarWorkspaceNodeCapacity,
            nameof(aStarWorkspaceNodeCapacity));
        SwiftThrowHelper.ThrowIfNegative(maxAStarCacheEntries, nameof(maxAStarCacheEntries));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxAStarReusablePayloadBytes < 0,
            null,
            nameof(maxAStarReusablePayloadBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxAStarSinglePayloadBytes <= 0,
            null,
            nameof(maxAStarSinglePayloadBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxAStarActivePayloadBytes <= 0,
            null,
            nameof(maxAStarActivePayloadBytes));
        SwiftThrowHelper.ThrowIfArgument(
            maxAStarSinglePayloadBytes > maxAStarActivePayloadBytes,
            nameof(maxAStarSinglePayloadBytes),
            "A single payload cannot exceed the complete active-payload byte ceiling.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxAStarActivePayloadLeases <= 0,
            maxAStarActivePayloadLeases,
            nameof(maxAStarActivePayloadLeases));
        MaxBatchItems = maxBatchItems;
        MaxBatchDescriptorBytes = maxBatchDescriptorBytes;
        MaxConcurrentAStarQueries = maxConcurrentAStarQueries;
        AStarWorkspaceMapCapacity = aStarWorkspaceMapCapacity;
        AStarWorkspaceEndpointPageCapacity = aStarWorkspaceEndpointPageCapacity;
        AStarWorkspaceComponentCapacity = aStarWorkspaceComponentCapacity;
        AStarWorkspaceNodeCapacity = aStarWorkspaceNodeCapacity;
        MaxAStarCacheEntries = maxAStarCacheEntries;
        MaxAStarReusablePayloadBytes = maxAStarReusablePayloadBytes;
        MaxAStarSinglePayloadBytes = maxAStarSinglePayloadBytes;
        MaxAStarActivePayloadBytes = maxAStarActivePayloadBytes;
        MaxAStarActivePayloadLeases = maxAStarActivePayloadLeases;
    }

    /// <summary>Gets the maximum query descriptors accepted by one batch.</summary>
    public int MaxBatchItems { get; }

    /// <summary>Gets the maximum logical bytes accepted for submitted batch descriptors.</summary>
    public long MaxBatchDescriptorBytes { get; }

    /// <summary>Gets the maximum concurrently admitted queries.</summary>
    public int MaxConcurrentAStarQueries { get; }

    /// <summary>Gets the map capacity of each exclusive A* workspace.</summary>
    public int AStarWorkspaceMapCapacity { get; }

    /// <summary>Gets the endpoint-page capacity of each exclusive A* workspace.</summary>
    public int AStarWorkspaceEndpointPageCapacity { get; }

    /// <summary>Gets the exact component-dependency capacity of each exclusive A* workspace.</summary>
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
}
