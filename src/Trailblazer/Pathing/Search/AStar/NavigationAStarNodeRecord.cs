//=======================================================================
// NavigationAStarNodeRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores search state and predecessor evidence for one A* node.</summary>
internal struct NavigationAStarNodeRecord
{
    internal Fixed64 Cost;
    internal Fixed64 Heuristic;
    // The search-only f-score adds eight bytes per workspace node, never to cached payloads.
    internal Fixed64 EstimatedTotalCost;
    internal NavigationMediumStateRef Parent;
    internal NavigationTraversalEdgeKind ParentEdgeKind;
    internal int HeapIndex;
    internal int ParentEdgeOrdinal;
    internal bool HasParent;
    internal bool Closed;
}
