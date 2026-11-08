//=======================================================================
// TraversalExplicitEdgeWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Retains scalar state while explicit corridor legs are evaluated.</summary>
internal struct TraversalExplicitEdgeWork
{
    internal NavigationExplicitConnectionRecord Record;
    internal NavigationNodeRef Target;
    internal NavigationPagedSequence<GridNavigationPortal>.Enumerator Portals;
    internal NavigationCellAddress PreviousAddress;
    internal GridCellPrism PreviousPrism;
    internal Vector3d SourceFootAnchor;
    internal Vector3d TargetFootAnchor;
    internal Vector3d PreviousFootAnchor;
    internal Fixed64 TargetEnterCost;
    internal Fixed64 TargetAreaEnterCost;
    internal int WitnessIndex;
}
