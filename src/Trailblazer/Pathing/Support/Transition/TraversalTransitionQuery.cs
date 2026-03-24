using System;
using FixedMathSharp;
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

    private static readonly object _cacheLock = new();

    private static int _cachedRegistryVersion = -1;

    private static TraversalTransition[] _allDirectedTransitions = Array.Empty<TraversalTransition>();

    private static bool _hasAllDirectedTransitions;

    private static readonly SwiftDictionary<int, TraversalTransition[]> _directedTransitionsFromSourceGrid = new();

    private static readonly SwiftDictionary<int, TraversalTransition[]> _directedTransitionsToDestinationGrid = new();

    public static TraversalTransition[] GetDirectedTransitions()
    {
        lock (_cacheLock)
        {
            EnsureCacheVersion();
            if (_hasAllDirectedTransitions)
                return _allDirectedTransitions;

            _allDirectedTransitions = BuildAllDirectedTransitions(TraversalTransitionRegistry.GetRegisteredTransitions());
            _hasAllDirectedTransitions = true;
            return _allDirectedTransitions;
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
                TraversalTransitionRegistry.GetRegisteredTransitionsTouchingGrid(sourceGridIndex),
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
                TraversalTransitionRegistry.GetRegisteredTransitionsTouchingGrid(destinationGridIndex),
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
        _directedTransitionsFromSourceGrid.Clear();
        _directedTransitionsToDestinationGrid.Clear();
        _cachedRegistryVersion = registryVersion;
    }

    private static TraversalTransition[] BuildAllDirectedTransitions(
        RegisteredTraversalTransition[] transitions)
    {
        SwiftList<TraversalTransition> directed = new(transitions.Length * 2);
        for (int i = 0; i < transitions.Length; i++)
            AddAllDirectedTransitions(directed, transitions[i]);

        return SortTransitions(directed);
    }

    private static TraversalTransition[] BuildDirectedTransitionsForGrid(
        RegisteredTraversalTransition[] transitions,
        int gridIndex,
        GridMatchAxis axis)
    {
        SwiftList<TraversalTransition> directed = new(transitions.Length * 2);

        for (int i = 0; i < transitions.Length; i++)
        {
            RegisteredTraversalTransition registered = transitions[i];
            if (MatchesGrid(registered, gridIndex, axis))
                directed.Add(registered.Transition);

            if (registered.Transition.IsBidirectional
                && MatchesGrid(registered, gridIndex, GetOppositeAxis(axis)))
            {
                directed.Add(CreateReversedTransition(registered.Transition));
            }
        }

        return SortTransitions(directed);
    }

    private static bool MatchesGrid(
        RegisteredTraversalTransition registered,
        int gridIndex,
        GridMatchAxis axis)
    {
        return axis == GridMatchAxis.Source
            ? registered.SourceVoxelIndex.GridIndex == gridIndex
            : registered.DestinationVoxelIndex.GridIndex == gridIndex;
    }

    private static void AddAllDirectedTransitions(
        SwiftList<TraversalTransition> directed,
        RegisteredTraversalTransition registered)
    {
        directed.Add(registered.Transition);

        if (registered.Transition.IsBidirectional)
            directed.Add(CreateReversedTransition(registered.Transition));
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
        Array.Sort(ordered, CompareTransitions);
        return ordered;
    }

    private static int CompareTransitions(TraversalTransition left, TraversalTransition right)
    {
        int idComparison = string.CompareOrdinal(left.Id, right.Id);
        if (idComparison != 0)
            return idComparison;

        int typeComparison = left.Type.CompareTo(right.Type);
        if (typeComparison != 0)
            return typeComparison;

        int sourceComparison = CompareAnchors(left.Source, right.Source);
        if (sourceComparison != 0)
            return sourceComparison;

        int destinationComparison = CompareAnchors(left.Destination, right.Destination);
        if (destinationComparison != 0)
            return destinationComparison;

        int costComparison = left.PathCostModifier.CompareTo(right.PathCostModifier);
        if (costComparison != 0)
            return costComparison;

        return left.IsBidirectional.CompareTo(right.IsBidirectional);
    }

    private static int CompareAnchors(TraversalTransitionAnchor left, TraversalTransitionAnchor right)
    {
        int spaceComparison = left.Space.CompareTo(right.Space);
        if (spaceComparison != 0)
            return spaceComparison;

        int voxelComparison = ComparePositions(left.VoxelPosition, right.VoxelPosition);
        if (voxelComparison != 0)
            return voxelComparison;

        int overrideComparison = left.HasPointOverride.CompareTo(right.HasPointOverride);
        if (overrideComparison != 0)
            return overrideComparison;

        return ComparePositions(left.Position, right.Position);
    }

    private static int ComparePositions(Vector3d left, Vector3d right)
    {
        int xComparison = left.x.CompareTo(right.x);
        if (xComparison != 0)
            return xComparison;

        int yComparison = left.y.CompareTo(right.y);
        if (yComparison != 0)
            return yComparison;

        return left.z.CompareTo(right.z);
    }
}
