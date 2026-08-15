//=======================================================================
// NavigationFlowFieldNode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one stable node in a destination-centric flow field.</summary>
internal readonly struct NavigationFlowFieldNode
{
    internal NavigationFlowFieldNode(
        NavigationCellAddress address,
        Fixed64 integrationCost,
        NavigationSelectedEdgeRef selectedEdge)
    {
        Address = address;
        IntegrationCost = integrationCost;
        SelectedEdge = selectedEdge;
    }

    internal NavigationCellAddress Address { get; }

    internal Fixed64 IntegrationCost { get; }

    internal NavigationSelectedEdgeRef SelectedEdge { get; }
}
