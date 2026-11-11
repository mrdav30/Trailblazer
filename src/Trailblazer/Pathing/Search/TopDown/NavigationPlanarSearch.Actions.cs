//=======================================================================
// NavigationPlanarSearch.Actions.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

internal partial class NavigationPlanarSearch
{
    // Ordinary native and seam ordinals sort before every action.
    internal const int FirstActionOrdinal = 16;
    private NavigationTransitionPage? _actionPage;
    private NavigationCellAddress _actionEndpoint;
    private int _actionCursor;

    private void BeginTransitionEdges(NavigationMediumStateRef current)
    {
        _actionCursor = 0;
        _actionPage = null;
        if (!_request.AllowTransitions)
            return;
        _actionPage = Graph.GetTransitionPage(current.Node);
        _actionEndpoint = new NavigationCellAddress(_currentMap.MapId, _current);
    }

    private NavigationTraversalEdgeAdvanceStatus AdvanceTransitionEdge(out NavigationMediumStateRef neighbor,
        out Fixed64 cost, out int ordinal)
    {
        neighbor = default;
        cost = default;
        ordinal = default;
        NavigationTransitionPage? page = _actionPage;
        if (page == null || _actionCursor == (_incoming ? page.IncomingCount : page.OutgoingCount))
            return NavigationTraversalEdgeAdvanceStatus.Complete;
        if (!_meter.TryConsumeTransitionCandidates(1) || !_meter.TryConsumeLookupProbes(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

        int slot = _actionCursor++;
        if (_incoming)
        {
            NavigationIncomingTransitionRef incoming = page.GetIncomingAt(slot);
            if (incoming.SourceMedium != _request.Medium)
                return NavigationTraversalEdgeAdvanceStatus.Pending;
            page = Graph.GetTransitionPage(incoming.SourcePage)!;
            slot = page.FindIncomingSourceOrdinal(incoming, _actionEndpoint, _meter, out bool budgetExhausted);
            if (slot < 0)
                return budgetExhausted ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
                    : NavigationTraversalEdgeAdvanceStatus.Pending;
        }

        NavigationPublishedTransition transition = page.GetOutgoingAt(slot);
        TraversalTransitionDefinition definition = transition.Definition;
        if ((!_incoming && (transition.SourceAddress != _actionEndpoint || definition.SourceMedium != _request.Medium))
            || (_request.Capabilities & definition.RequiredCapabilities) != definition.RequiredCapabilities
            || !TryGetNode(_incoming ? transition.SourceAddress : definition.Destination, out var adjacentMap, out var adjacentNode))
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        if (!_meter.TryConsumeEvaluatedEdges(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;

        NavigationMapInstance sourceMap = _incoming ? adjacentMap : _currentMap;
        NavigationMapInstance targetMap = _incoming ? _currentMap : adjacentMap;
        NavigationGuideStatus status = TraceActionAnchor(sourceMap, transition.SourceAddress);
        if (status == NavigationGuideStatus.Success)
            status = TraceActionAnchor(targetMap, definition.Destination);
        if (status != NavigationGuideStatus.Success)
            return MapTraceFailure(status);
        targetMap.TryGetSlot(definition.Destination.Index, out int targetSlot);
        targetMap.TryGetEffectiveCell(targetSlot, out NavigationCell cell);
        if (!NavigationTransitionEdgeEvaluator.TryAddEntryCosts(definition.ActionCost, cell.EnterCost,
                _policy.GetRequiredRule(cell.Area).AdditionalEnterCost, out cost))
            return NavigationTraversalEdgeAdvanceStatus.CostOverflow;

        ordinal = FirstActionOrdinal + slot;
        neighbor = new NavigationMediumStateRef(adjacentNode, _request.Medium);
        return NavigationTraversalEdgeAdvanceStatus.Edge;
    }

    private NavigationGuideStatus TraceActionAnchor(NavigationMapInstance map, NavigationCellAddress address)
    {
        map.Map.GridBinding.TryGetCellPrism(address.Index, out var prism);
        Vector2d center = prism.Center.ToVector2d();
        return Trace(map, address.Index, map, address.Index, center, center, NavigationGuideStatus.NoPath);
    }
}
