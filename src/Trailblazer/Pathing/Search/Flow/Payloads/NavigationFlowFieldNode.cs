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
        TraversalMedium medium,
        Fixed64 integrationCost,
        NavigationSelectedEdgeRef selectedEdge,
        int transitionInstructionOrdinal)
    {
        Address = address;
        Medium = medium;
        IntegrationCost = integrationCost;
        SelectedEdge = selectedEdge;
        TransitionInstructionOrdinal = transitionInstructionOrdinal;
    }

    internal NavigationCellAddress Address { get; }

    internal TraversalMedium Medium { get; }

    internal Fixed64 IntegrationCost { get; }

    internal NavigationSelectedEdgeRef SelectedEdge { get; }

    internal int TransitionInstructionOrdinal { get; }
}
