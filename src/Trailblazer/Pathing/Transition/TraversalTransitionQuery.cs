using SwiftCollections;
using System;

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

    private static readonly object _cacheLock = new();

    private static int _cachedRegistryVersion = -1;

    private static TraversalTransition[] _allDirectedTransitions = Array.Empty<TraversalTransition>();

    private static bool _hasAllDirectedTransitions;

    private static readonly SwiftDictionary<TraversalTransitionType, TraversalTransition[]> _directedTransitionsByType = new();

    private static readonly SwiftDictionary<int, TraversalTransition[]> _directedTransitionsFromSourceGrid = new();

    private static readonly SwiftDictionary<int, TraversalTransition[]> _directedTransitionsToDestinationGrid = new();

    public static TraversalTransition[] GetDirectedTransitions()
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_hasAllDirectedTransitions)
                return _allDirectedTransitions;

            _allDirectedTransitions = BuildAllDirectedTransitions(TraversalTransitionRegistry.GetActiveTransitions());
            _hasAllDirectedTransitions = true;
            return _allDirectedTransitions;
        }
    }

    public static TraversalTransition[] GetDirectedTransitions(TraversalTransitionType type)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_directedTransitionsByType.TryGetValue(type, out TraversalTransition[] cached))
                return cached;

            if (!_hasAllDirectedTransitions)
            {
                _allDirectedTransitions = BuildAllDirectedTransitions(TraversalTransitionRegistry.GetActiveTransitions());
                _hasAllDirectedTransitions = true;
            }

            TraversalTransition[] filtered = FilterDirectedTransitionsByType(_allDirectedTransitions, type);
            _directedTransitionsByType[type] = filtered;
            return filtered;
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsFromSourceGrid(int sourceGridIndex)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_directedTransitionsFromSourceGrid.TryGetValue(sourceGridIndex, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] directed = BuildDirectedTransitionsForGrid(
                TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(sourceGridIndex),
                sourceGridIndex,
                GridMatchAxis.Source);
            _directedTransitionsFromSourceGrid[sourceGridIndex] = directed;
            return directed;
        }
    }

    public static TraversalTransition[] GetDirectedTransitionsToDestinationGrid(int destinationGridIndex)
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_directedTransitionsToDestinationGrid.TryGetValue(destinationGridIndex, out TraversalTransition[] cached))
                return cached;

            TraversalTransition[] directed = BuildDirectedTransitionsForGrid(
                TraversalTransitionRegistry.GetActiveTransitionsTouchingGrid(destinationGridIndex),
                destinationGridIndex,
                GridMatchAxis.Destination);
            _directedTransitionsToDestinationGrid[destinationGridIndex] = directed;
            return directed;
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
        _directedTransitionsFromSourceGrid.Clear();
        _directedTransitionsToDestinationGrid.Clear();
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
}
