//=======================================================================
// NavigationOperationLimits.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Defines finite admission and candidate-overlay limits for map operations.</summary>
public readonly struct NavigationOperationLimits
{
    /// <summary>
    /// The hard ceiling that bounds conservative same-frame receipt coalescing work.
    /// Larger admitted queues carry over through multiple deterministic batches.
    /// </summary>
    public const int MaximumBatchItems = 256;

    /// <summary>Initializes explicit finite operation and overlay limits.</summary>
    public NavigationOperationLimits(
        int maxPendingOperations,
        long maxPendingDescriptorBytes,
        long maxPreparedMapBytes,
        int maxBatchItems,
        long maxBatchDescriptorBytes,
        long maxBatchSortScratchBytes,
        int maxCorridorCells,
        int maxMaps,
        int maxRetainedMapIdentities,
        int maxOverlayCellsPerMap,
        int maxOverlayConnectionsPerMap,
        int maxOverlayTransitionsPerMap,
        int maxOverlayCells,
        int maxOverlayConnections,
        int maxOverlayTransitions)
    {
        SwiftThrowHelper.ThrowIfArgument(maxPendingOperations <= 0, nameof(maxPendingOperations));
        SwiftThrowHelper.ThrowIfArgument(maxPendingDescriptorBytes <= 0, nameof(maxPendingDescriptorBytes));
        SwiftThrowHelper.ThrowIfArgument(maxPreparedMapBytes <= 0, nameof(maxPreparedMapBytes));
        SwiftThrowHelper.ThrowIfArgument(
            maxBatchItems <= 0 || maxBatchItems > MaximumBatchItems,
            nameof(maxBatchItems));
        SwiftThrowHelper.ThrowIfArgument(maxBatchDescriptorBytes <= 0, nameof(maxBatchDescriptorBytes));
        SwiftThrowHelper.ThrowIfArgument(maxBatchSortScratchBytes <= 0, nameof(maxBatchSortScratchBytes));
        SwiftThrowHelper.ThrowIfArgument(maxCorridorCells < 2, nameof(maxCorridorCells));
        SwiftThrowHelper.ThrowIfArgument(maxCorridorCells > int.MaxValue / 2, nameof(maxCorridorCells));
        SwiftThrowHelper.ThrowIfArgument(maxMaps <= 0, nameof(maxMaps));
        SwiftThrowHelper.ThrowIfArgument(maxRetainedMapIdentities < maxMaps, nameof(maxRetainedMapIdentities));
        SwiftThrowHelper.ThrowIfArgument(maxOverlayCellsPerMap < 0, nameof(maxOverlayCellsPerMap));
        SwiftThrowHelper.ThrowIfArgument(maxOverlayConnectionsPerMap < 0, nameof(maxOverlayConnectionsPerMap));
        SwiftThrowHelper.ThrowIfArgument(maxOverlayTransitionsPerMap < 0, nameof(maxOverlayTransitionsPerMap));
        SwiftThrowHelper.ThrowIfArgument(maxOverlayCells < maxOverlayCellsPerMap, nameof(maxOverlayCells));
        SwiftThrowHelper.ThrowIfArgument(maxOverlayConnections < maxOverlayConnectionsPerMap, nameof(maxOverlayConnections));
        SwiftThrowHelper.ThrowIfArgument(maxOverlayTransitions < maxOverlayTransitionsPerMap, nameof(maxOverlayTransitions));

        MaxPendingOperations = maxPendingOperations;
        MaxPendingDescriptorBytes = maxPendingDescriptorBytes;
        MaxPreparedMapBytes = maxPreparedMapBytes;
        MaxBatchItems = maxBatchItems;
        MaxBatchDescriptorBytes = maxBatchDescriptorBytes;
        MaxBatchSortScratchBytes = maxBatchSortScratchBytes;
        MaxCorridorCells = maxCorridorCells;
        MaxMaps = maxMaps;
        MaxRetainedMapIdentities = maxRetainedMapIdentities;
        MaxOverlayCellsPerMap = maxOverlayCellsPerMap;
        MaxOverlayConnectionsPerMap = maxOverlayConnectionsPerMap;
        MaxOverlayTransitionsPerMap = maxOverlayTransitionsPerMap;
        MaxOverlayCells = maxOverlayCells;
        MaxOverlayConnections = maxOverlayConnections;
        MaxOverlayTransitions = maxOverlayTransitions;
        MaxTransitionRulesPerMap = maxOverlayTransitionsPerMap > 0
            ? maxOverlayTransitionsPerMap
            : 1;
        MaxTransitionRules = maxOverlayTransitions > MaxTransitionRulesPerMap
            ? maxOverlayTransitions
            : MaxTransitionRulesPerMap;
    }

    /// <summary>Gets the maximum number of admitted operations.</summary>
    public int MaxPendingOperations { get; }

    /// <summary>Gets the maximum submitted descriptor bytes retained by the queue.</summary>
    public long MaxPendingDescriptorBytes { get; }

    /// <summary>Gets the maximum retained bytes across prepared maps.</summary>
    public long MaxPreparedMapBytes { get; }

    /// <summary>Gets the maximum operation count admitted into one deterministic fold batch.</summary>
    public int MaxBatchItems { get; }

    /// <summary>Gets the maximum descriptor bytes admitted into one fold batch.</summary>
    public long MaxBatchDescriptorBytes { get; }

    /// <summary>Gets the maximum deterministic sorting-scratch bytes admitted for one fold batch.</summary>
    public long MaxBatchSortScratchBytes { get; }

    /// <summary>Gets the maximum number of prisms in one connection corridor validation chain.</summary>
    public int MaxCorridorCells { get; }

    /// <summary>Gets the maximum map count in a candidate registry.</summary>
    public int MaxMaps { get; }

    /// <summary>
    /// Gets the maximum number of map IDs whose bake-version high-water marks remain retained.
    /// Removed IDs remain counted so stale checkpoint identities cannot be reused.
    /// </summary>
    public int MaxRetainedMapIdentities { get; }

    /// <summary>Gets the maximum cell overlay entries retained for one map.</summary>
    public int MaxOverlayCellsPerMap { get; }

    /// <summary>Gets the maximum connection overlay entries retained for one map.</summary>
    public int MaxOverlayConnectionsPerMap { get; }

    /// <summary>Gets the maximum transition overlay entries retained for one map.</summary>
    public int MaxOverlayTransitionsPerMap { get; }

    /// <summary>Gets the maximum cell overlay entries retained across the context candidate.</summary>
    public int MaxOverlayCells { get; }

    /// <summary>Gets the maximum connection overlay entries retained across the context candidate.</summary>
    public int MaxOverlayConnections { get; }

    /// <summary>Gets the maximum transition overlay entries retained across the context candidate.</summary>
    public int MaxOverlayTransitions { get; }

    /// <summary>Gets the temporary internal per-map rule ceiling derived from transition limits.</summary>
    internal int MaxTransitionRulesPerMap { get; }

    /// <summary>Gets the temporary internal total rule ceiling derived from transition limits.</summary>
    internal int MaxTransitionRules { get; }
}
