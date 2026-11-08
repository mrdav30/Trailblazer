//=======================================================================
// NavigationRayChainRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one visited cell and its predecessor during a navigation ray chain search.</summary>
internal struct NavigationRayChainRecord
{
    internal NavigationNodeRef Node;
    internal int PredecessorOrdinal;
    internal int RootOrdinal;
    internal Fixed64 ArrivalParameter;
    internal Fixed64 TraversalCost;
    internal NavigationExplicitConnectionRecord IncomingExplicitConnection;
    internal NavigationRayChainRecordState State;
    internal bool IsSemanticCostNeutral;
}
