//=======================================================================
// TraversalEvaluator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Reports the allocation-free result of one traversal evaluation.</summary>
internal enum TraversalEvaluationStatus : byte
{
    Passable = 0,
    Impassable = 1,
    CostOverflow = 2
}

/// <summary>Evaluates nodes and native edges against one resolved immutable query profile.</summary>
internal readonly struct TraversalEvaluator
{
    private readonly NavigationWorldGraph _graph;
    private readonly NavigationAgentProfile _profile;
    private readonly NavigationAreaPolicy _areaPolicy;
    private readonly TraversalMedia _medium;

    internal TraversalEvaluator(
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        TraversalMedium medium)
    {
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        profile.Validate(nameof(profile));
        TraversalMedia resolvedMedium = medium switch
        {
            TraversalMedium.Solid => TraversalMedia.Solid,
            TraversalMedium.Gas => TraversalMedia.Gas,
            TraversalMedium.Liquid => TraversalMedia.Liquid,
            _ => TraversalMedia.None
        };
        SwiftThrowHelper.ThrowIfArgument(
            resolvedMedium == TraversalMedia.None,
            nameof(medium),
            "Traversal medium must resolve to one exact authored medium.");

        _graph = graph;
        _profile = profile;
        _areaPolicy = areaPolicy;
        _medium = resolvedMedium;
    }

    internal bool IsNodePassable(NavigationNodeRef node) =>
        TryGetPassableNode(node, out _, out _);

    internal TraversalEvaluationStatus EvaluateNativeEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out Fixed64 cost) => EvaluateEdge(source, edge, out cost);

    internal TraversalEvaluationStatus EvaluateEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out Fixed64 cost)
    {
        return edge.Kind == NavigationGraphEdgeKind.Explicit
            ? EvaluateExplicitEdge(source, edge, out cost)
            : EvaluateNative(source, edge, out cost);
    }

    private TraversalEvaluationStatus EvaluateNative(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out Fixed64 cost)
    {
        cost = Fixed64.Zero;
        if (!TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(edge.Target, out NavigationNodeState targetState, out NavigationAreaRule targetRule)
            || edge.Kind != NavigationGraphEdgeKind.Native)
        {
            return TraversalEvaluationStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        GridNavigationPortal template = edge.NativePortal;
        if (!template.IsValid
            || shape.Radius > template.MaximumHorizontalRadius
            || shape.Height > template.MaximumBodyHeight)
        {
            return TraversalEvaluationStatus.Impassable;
        }

        if (!template.TryTranslate(sourceState.Center, out GridNavigationPortal portal)
            || !portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d sourcePortalFoot,
                out Vector3d targetPortalFoot)
            || !Fixed64.TrySubtract(
                targetState.FootAnchor.Y,
                sourceState.FootAnchor.Y,
                out Fixed64 verticalDelta))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }

        if (verticalDelta > Fixed64.Zero)
        {
            if (verticalDelta > _profile.MaxStepUp)
                return TraversalEvaluationStatus.Impassable;
        }
        else if (verticalDelta < Fixed64.Zero)
        {
            if (!Fixed64.TrySubtract(Fixed64.Zero, verticalDelta, out Fixed64 drop))
                return TraversalEvaluationStatus.CostOverflow;
            if (drop > _profile.MaxDropDown)
                return TraversalEvaluationStatus.Impassable;
        }

        if (!Vector3d.TryGetDistance(
                sourceState.FootAnchor,
                sourcePortalFoot,
                out Fixed64 sourceDistance)
            || !Vector3d.TryGetDistance(
                targetPortalFoot,
                targetState.FootAnchor,
                out Fixed64 targetDistance)
            || !Fixed64.TryAdd(sourceDistance, targetDistance, out Fixed64 total)
            || !Fixed64.TryAdd(total, targetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, targetRule.AdditionalEnterCost, out total))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }

        cost = total;
        return TraversalEvaluationStatus.Passable;
    }

    private TraversalEvaluationStatus EvaluateExplicitEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out Fixed64 cost)
    {
        cost = Fixed64.Zero;
        NavigationExplicitConnectionRecord record = edge.ExplicitConnection;
        if (record == null
            || !record.IsActive
            || edge.Kind != NavigationGraphEdgeKind.Explicit
            || !_graph.TryGetNodeAddress(source, out NavigationCellAddress sourceAddress)
            || !sourceAddress.Equals(record.Source)
            || !_graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress targetAddress)
            || !targetAddress.Equals(record.Destination)
            || !TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(
                edge.Target,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return TraversalEvaluationStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        NavigationConnection connection = record.Definition;
        if (shape.Radius > connection.PortalRadiusClearance
            || shape.Height > connection.PortalHeightClearance)
        {
            return TraversalEvaluationStatus.Impassable;
        }
        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            if (!_graph.TryGetNodeRef(connection.Witnesses[i], out NavigationNodeRef witness)
                || !TryGetPassableNode(witness, out _, out _))
            {
                return TraversalEvaluationStatus.Impassable;
            }
        }

        if (!Vector3d.TryGetDistance(
                sourceState.FootAnchor,
                connection.EntryAnchor,
                out Fixed64 sourceDistance)
            || !Vector3d.TryGetDistance(
                connection.ExitAnchor,
                targetState.FootAnchor,
                out Fixed64 targetDistance)
            || !Fixed64.TryAdd(sourceDistance, record.CorridorCost, out Fixed64 total)
            || !Fixed64.TryAdd(total, targetDistance, out total)
            || !Fixed64.TryAdd(total, connection.AdditionalCost, out total)
            || !Fixed64.TryAdd(total, targetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, targetRule.AdditionalEnterCost, out total))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }

        cost = total;
        return TraversalEvaluationStatus.Passable;
    }

    private bool TryGetPassableNode(
        NavigationNodeRef node,
        out NavigationNodeState state,
        out NavigationAreaRule areaRule)
    {
        areaRule = default;
        if (!_graph.TryGetNodeState(node, out state)
            || !state.IsPresent
            || state.ObstacleCount != 0
            || (_profile.AllowedMedia & _medium) != _medium
            || (state.Cell.Media & _medium) != _medium
            || (_profile.Capabilities & state.Cell.RequiredCapabilities)
                != state.Cell.RequiredCapabilities
            || !_areaPolicy.TryGetRule(state.Cell.Area, out areaRule)
            || !areaRule.IsAllowed
            || _profile.Shape.Radius > state.Cell.RadiusClearance
            || _profile.Shape.Height > state.Cell.HeightClearance)
        {
            state = default;
            areaRule = default;
            return false;
        }

        return true;
    }
}
