//=======================================================================
// TraversalTransitionQuery.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides deterministic directed transition snapshots for planners.
/// </summary>
/// <remarks>
/// The query layer favors grid-scoped snapshots first, while still allowing callers to fall back
/// to a full directed snapshot when they intentionally need world-wide transition discovery.
/// </remarks>
internal static class TraversalTransitionQuery
{
    private enum GridMatchAxis
    {
        Source,
        Destination
    }

    private static TraversalTransitionQueryCache Cache => PathManager.ActiveState.TransitionQueryCache;

    private static object _cacheLock => Cache.CacheLock;

    private static int _cachedRegistryVersion
    {
        get => Cache.CachedRegistryVersion;
        set => Cache.CachedRegistryVersion = value;
    }

    private static TraversalTransition[] _allDirectedTransitions
    {
        get => Cache.AllDirectedTransitions;
        set => Cache.AllDirectedTransitions = value;
    }

    private static bool _hasAllDirectedTransitions
    {
        get => Cache.HasAllDirectedTransitions;
        set => Cache.HasAllDirectedTransitions = value;
    }

    private static SwiftDictionary<TraversalTransitionType, TraversalTransition[]> _directedTransitionsByType =>
        Cache.DirectedTransitionsByType;

    private static SwiftDictionary<int, TraversalTransition[]> _directedTransitionsByMediumPair =>
        Cache.DirectedTransitionsByMediumPair;

    private static SwiftDictionary<int, TraversalTransition[]> _directedTransitionsFromSourceGrid =>
        Cache.DirectedTransitionsFromSourceGrid;

    private static SwiftDictionary<long, TraversalTransition[]> _directedTransitionsFromSourceGridByType =>
        Cache.DirectedTransitionsFromSourceGridByType;

    private static SwiftDictionary<long, TraversalTransition[]> _directedTransitionsFromSourceGridByMediumPair =>
        Cache.DirectedTransitionsFromSourceGridByMediumPair;

    private static SwiftDictionary<int, TraversalTransition[]> _directedTransitionsToDestinationGrid =>
        Cache.DirectedTransitionsToDestinationGrid;

    private static SwiftDictionary<long, TraversalTransition[]> _directedTransitionsToDestinationGridByMediumPair =>
        Cache.DirectedTransitionsToDestinationGridByMediumPair;

    private static SwiftDictionary<TraversalTransitionType, int[]> _sourceGridIndicesByType =>
        Cache.SourceGridIndicesByType;

    public static TraversalTransition[] GetDirectedTransitions()
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            return GetOrBuildAllDirectedTransitions_NoLock();
        }
    }

    public static TraversalTransition[] GetDirectedTransitions(TraversalTransitionType type)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_directedTransitionsByType.TryGetValue(type, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] filtered = FilterDirectedTransitionsByType(
                GetOrBuildAllDirectedTransitions_NoLock(),
                type);
            _directedTransitionsByType[type] = filtered;
            return filtered;
        }
    }

    public static TraversalTransition[] GetDirectedTransitions(
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            int key = MakeMediumPairKey(sourceMedium, destinationMedium);
            if (_directedTransitionsByMediumPair.TryGetValue(key, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] filtered = FilterDirectedTransitionsByMediumPair(
                GetOrBuildAllDirectedTransitions_NoLock(),
                sourceMedium,
                destinationMedium);
            _directedTransitionsByMediumPair[key] = filtered;
            return filtered;
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsFromSourceGrid(int sourceGridIndex)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            return GetOrBuildDirectedTransitionsFromSourceGrid_NoLock(sourceGridIndex);
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsFromSourceGrid(
        int sourceGridIndex,
        TraversalTransitionType type)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            long key = MakeGridTypeKey(sourceGridIndex, type);
            if (_directedTransitionsFromSourceGridByType.TryGetValue(key, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] filtered = FilterDirectedTransitionsByType(
                GetOrBuildDirectedTransitionsFromSourceGrid_NoLock(sourceGridIndex),
                type);
            _directedTransitionsFromSourceGridByType[key] = filtered;
            return filtered;
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsFromSourceGrid(
        int sourceGridIndex,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            long key = MakeGridMediumPairKey(sourceGridIndex, sourceMedium, destinationMedium);
            if (_directedTransitionsFromSourceGridByMediumPair.TryGetValue(key, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] filtered = FilterDirectedTransitionsByMediumPair(
                GetOrBuildDirectedTransitionsFromSourceGrid_NoLock(sourceGridIndex),
                sourceMedium,
                destinationMedium);
            _directedTransitionsFromSourceGridByMediumPair[key] = filtered;
            return filtered;
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsToDestinationGrid(int destinationGridIndex)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            return GetOrBuildDirectedTransitionsToDestinationGrid_NoLock(destinationGridIndex);
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsToDestinationGrid(
        int destinationGridIndex,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            long key = MakeGridMediumPairKey(destinationGridIndex, sourceMedium, destinationMedium);
            if (_directedTransitionsToDestinationGridByMediumPair.TryGetValue(key, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] filtered = FilterDirectedTransitionsByMediumPair(
                GetOrBuildDirectedTransitionsToDestinationGrid_NoLock(destinationGridIndex),
                sourceMedium,
                destinationMedium);
            _directedTransitionsToDestinationGridByMediumPair[key] = filtered;
            return filtered;
        }
    }

    internal static int[] GetSourceGridIndices(TraversalTransitionType type)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_sourceGridIndicesByType.TryGetValue(type, out int[] cached))
                return cached;

            int[] sourceGridIndices = BuildSourceGridIndices(GetDirectedTransitions(type));
            _sourceGridIndicesByType[type] = sourceGridIndices;
            return sourceGridIndices;
        }
    }

    private static void EnsureCacheVersion()
    {
        int registryVersion = TraversalTransitionRegistry.RegistryVersion;
        if (_cachedRegistryVersion == registryVersion)
            return;

        _allDirectedTransitions = Array.Empty<TraversalTransition>();
        _hasAllDirectedTransitions = false;
        _directedTransitionsByType.Clear();
        _directedTransitionsByMediumPair.Clear();
        _directedTransitionsFromSourceGrid.Clear();
        _directedTransitionsFromSourceGridByType.Clear();
        _directedTransitionsFromSourceGridByMediumPair.Clear();
        _directedTransitionsToDestinationGrid.Clear();
        _directedTransitionsToDestinationGridByMediumPair.Clear();
        _sourceGridIndicesByType.Clear();
        _cachedRegistryVersion = registryVersion;
    }

    private static TraversalTransition[] FilterDirectedTransitionsByType(
        TraversalTransition[] transitions,
        TraversalTransitionType type)
    {
        if (transitions.Length == 0)
            return Array.Empty<TraversalTransition>();

        SwiftList<TraversalTransition> filtered = new();
        for (int i = 0; i < transitions.Length; i++)
        {
            if (transitions[i].Type == type)
                filtered.Add(transitions[i]);
        }

        return filtered.Count == 0
            ? Array.Empty<TraversalTransition>()
            : filtered.ToArray();
    }

    private static TraversalTransition[] FilterDirectedTransitionsByMediumPair(
        TraversalTransition[] transitions,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        if (transitions.Length == 0)
            return Array.Empty<TraversalTransition>();

        SwiftList<TraversalTransition> filtered = new();
        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            if (transition.Source.Medium == sourceMedium
                && transition.Destination.Medium == destinationMedium)
            {
                filtered.Add(transition);
            }
        }

        return filtered.Count == 0
            ? Array.Empty<TraversalTransition>()
            : filtered.ToArray();
    }

    private static TraversalTransition[] GetOrBuildAllDirectedTransitions_NoLock()
    {
        if (_hasAllDirectedTransitions)
            return _allDirectedTransitions;

        _allDirectedTransitions = BuildAllDirectedTransitions(TraversalTransitionRegistry.GetActiveTransitions());
        _hasAllDirectedTransitions = true;
        return _allDirectedTransitions;
    }

    private static TraversalTransition[] GetOrBuildDirectedTransitionsFromSourceGrid_NoLock(int sourceGridIndex)
    {
        if (_directedTransitionsFromSourceGrid.TryGetValue(sourceGridIndex, out TraversalTransition[] cached))
            return cached;

        TraversalTransition[] directed = BuildDirectedTransitionsForGrid(
            TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(sourceGridIndex),
            sourceGridIndex,
            GridMatchAxis.Source);
        _directedTransitionsFromSourceGrid[sourceGridIndex] = directed;
        return directed;
    }

    private static TraversalTransition[] GetOrBuildDirectedTransitionsToDestinationGrid_NoLock(int destinationGridIndex)
    {
        if (_directedTransitionsToDestinationGrid.TryGetValue(destinationGridIndex, out TraversalTransition[] cached))
            return cached;

        TraversalTransition[] directed = BuildDirectedTransitionsForGrid(
            TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(destinationGridIndex),
            destinationGridIndex,
            GridMatchAxis.Destination);
        _directedTransitionsToDestinationGrid[destinationGridIndex] = directed;
        return directed;
    }

    private static TraversalTransition[] BuildAllDirectedTransitions(
        TraversalTransition[] transitions)
    {
        SwiftList<TraversalTransition> directed = new(transitions.Length * 2);
        for (int i = 0; i < transitions.Length; i++)
            AddAllDirectedTransitions(directed, transitions[i]);

        return SortTransitions(directed);
    }

    private static TraversalTransition[] BuildDirectedTransitionsForGrid(
        TraversalTransition[] transitions,
        int gridIndex,
        GridMatchAxis axis)
    {
        SwiftList<TraversalTransition> directed = new(transitions.Length * 2);

        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransition transition = transitions[i];
            if (MatchesGrid(transition, gridIndex, axis))
                directed.Add(transition);

            if (transition.IsBidirectional
                && MatchesGrid(transition, gridIndex, GetOppositeAxis(axis)))
            {
                directed.Add(CreateReversedTransition(transition));
            }
        }

        return SortTransitions(directed);
    }

    private static int[] BuildSourceGridIndices(TraversalTransition[] transitions)
    {
        if (transitions.Length == 0)
            return Array.Empty<int>();

        SwiftHashSet<int> uniqueGridIndices = new();
        SwiftList<int> orderedGridIndices = new();
        for (int i = 0; i < transitions.Length; i++)
        {
            int gridIndex = transitions[i].Source.VoxelIndex.GridIndex;
            if (uniqueGridIndices.Add(gridIndex))
                orderedGridIndices.Add(gridIndex);
        }

        int[] result = orderedGridIndices.ToArray();
        Array.Sort(result);
        return result;
    }

    private static bool MatchesGrid(
        TraversalTransition transition,
        int gridIndex,
        GridMatchAxis axis)
    {
        return axis == GridMatchAxis.Source
            ? transition.Source.VoxelIndex.GridIndex == gridIndex
            : transition.Destination.VoxelIndex.GridIndex == gridIndex;
    }

    private static void AddAllDirectedTransitions(
        SwiftList<TraversalTransition> directed,
        TraversalTransition transition)
    {
        directed.Add(transition);

        if (transition.IsBidirectional)
            directed.Add(CreateReversedTransition(transition));
    }

    private static GridMatchAxis GetOppositeAxis(GridMatchAxis axis)
    {
        return axis == GridMatchAxis.Source
            ? GridMatchAxis.Destination
            : GridMatchAxis.Source;
    }

    private static TraversalTransition CreateReversedTransition(TraversalTransition transition)
    {
        return new TraversalTransition(
            transition.Id,
            transition.Type,
            transition.Destination,
            transition.Source,
            transition.PathCostModifier,
            transition.IsBidirectional);
    }

    private static TraversalTransition[] SortTransitions(SwiftList<TraversalTransition> directed)
    {
        TraversalTransition[] ordered = directed.ToArray();
        TraversalTransitionOrdering.Sort(ordered);
        return ordered;
    }

    private static int MakeMediumPairKey(
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        return ((int)sourceMedium << 16) | (int)destinationMedium;
    }

    private static long MakeGridTypeKey(int gridIndex, TraversalTransitionType type)
    {
        return ((long)(uint)gridIndex << 32) | (uint)type;
    }

    private static long MakeGridMediumPairKey(
        int gridIndex,
        TraversalMedium sourceMedium,
        TraversalMedium destinationMedium)
    {
        return ((long)(uint)gridIndex << 32) | (uint)MakeMediumPairKey(sourceMedium, destinationMedium);
    }
}
