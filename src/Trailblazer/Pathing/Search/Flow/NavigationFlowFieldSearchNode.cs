//=======================================================================
// NavigationFlowFieldSearchNode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores cost and selected-edge state for one node during flow-field search.</summary>
internal struct NavigationFlowFieldSearchNode
{
    internal NavigationCellAddress Address;
    internal Fixed64 IntegrationCost;
    internal NavigationSelectedEdgeRef SelectedEdge;
    internal int HeapIndex;
    internal bool SelectedIsTransition;
    internal bool Closed;
}
