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
    // Keep the goal flag in the medium slot to preserve the retained node size.
    private const int GoalFlag = int.MinValue;
    private readonly int _mediumAndGoal;

    internal NavigationFlowFieldNode(
        NavigationCellAddress address,
        TraversalMedium medium,
        Fixed64 integrationCost,
        NavigationSelectedEdgeRef selectedEdge,
        int transitionInstructionOrdinal,
        bool selectedTargetIsGoal = false)
    {
        Address = address;
        _mediumAndGoal = (int)medium | (selectedTargetIsGoal ? GoalFlag : 0);
        IntegrationCost = integrationCost;
        SelectedEdge = selectedEdge;
        TransitionInstructionOrdinal = transitionInstructionOrdinal;
    }

    internal NavigationCellAddress Address { get; }

    internal TraversalMedium Medium => (TraversalMedium)(_mediumAndGoal & int.MaxValue);

    internal Fixed64 IntegrationCost { get; }

    internal NavigationSelectedEdgeRef SelectedEdge { get; }

    internal int TransitionInstructionOrdinal { get; }

    internal bool SelectedTargetIsGoal => (_mediumAndGoal & GoalFlag) != 0;
}
