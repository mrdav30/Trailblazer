using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Caches deterministic directed transition snapshots for one transition registry state.
/// </summary>
internal sealed class TraversalTransitionQueryCache
{
    internal object CacheLock { get; } = new();

    internal int CachedRegistryVersion = -1;

    internal TraversalTransition[] AllDirectedTransitions = Array.Empty<TraversalTransition>();

    internal bool HasAllDirectedTransitions;

    internal SwiftDictionary<TraversalTransitionType, TraversalTransition[]> DirectedTransitionsByType { get; } = new();

    internal SwiftDictionary<int, TraversalTransition[]> DirectedTransitionsByMediumPair { get; } = new();

    internal SwiftDictionary<int, TraversalTransition[]> DirectedTransitionsFromSourceGrid { get; } = new();

    internal SwiftDictionary<long, TraversalTransition[]> DirectedTransitionsFromSourceGridByType { get; } = new();

    internal SwiftDictionary<long, TraversalTransition[]> DirectedTransitionsFromSourceGridByMediumPair { get; } = new();

    internal SwiftDictionary<int, TraversalTransition[]> DirectedTransitionsToDestinationGrid { get; } = new();

    internal SwiftDictionary<long, TraversalTransition[]> DirectedTransitionsToDestinationGridByMediumPair { get; } = new();

    internal SwiftDictionary<TraversalTransitionType, int[]> SourceGridIndicesByType { get; } = new();
}
