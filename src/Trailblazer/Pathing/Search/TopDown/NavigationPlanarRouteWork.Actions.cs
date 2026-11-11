//=======================================================================
// NavigationPlanarRouteWork.Actions.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal sealed partial class NavigationPlanarRouteWork
{
    private void FillActionAt(NavigationAStarGuidePoint[] points, NavigationTransitionInstruction[] instructions,
        int point, int ordinal, int instructionIndex)
    {
        NavigationWorldGraph graph = _graphLease!.Graph;
        NavigationAStarGuidePoint source = points[point];
        NavigationAStarGuidePoint destination = points[point + 1];
        _planar!.TryGetNode(source.Address, out NavigationNodeRef node);
        NavigationTransitionPage page = graph.GetTransitionPage(node)!;
        TraversalTransitionDefinition definition = page.GetOutgoingAt(ordinal).Definition;
        instructions[instructionIndex] = new NavigationTransitionInstruction(
            NavigationTransitionIdentityKind.Definition, source.Address.MapId, definition.Id, definition.Type,
            source.Address, destination.Address, source.Medium, destination.Medium,
            source.Position, destination.Position, definition.LocomotionHints);
        points[point] = new NavigationAStarGuidePoint(source.Address, source.Position, source.Medium, instructionIndex);
    }

    private int GetActionOrdinal(int index)
    {
        if (_aStar != null)
        {
            _aStar.NodeTable.TryGetSlot(_aStar.PathNodes[index + 1], out int slot);
            ref NavigationAStarNodeRecord record = ref _aStar.NodeTable.GetRecord(slot);
            return record.ParentEdgeKind == NavigationTraversalEdgeKind.Transition
                ? record.ParentEdgeOrdinal - NavigationPlanarSearch.FirstActionOrdinal : -1;
        }

        ref NavigationFlowFieldSearchNode current = ref _flow!.GetRecord(_flow.SettledSlots[index]);
        return current.SelectedIsTransition
            ? current.SelectedEdge.CanonicalOutgoingOrdinal - NavigationPlanarSearch.FirstActionOrdinal : -1;
    }
}
