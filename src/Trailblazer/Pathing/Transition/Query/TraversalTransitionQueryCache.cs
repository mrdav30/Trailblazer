//=======================================================================
// TraversalTransitionQueryCache.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
