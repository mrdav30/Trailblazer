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

/// <summary>Reports progress through one bounded explicit-connection evaluation.</summary>
internal enum TraversalExplicitEdgeStatus : byte
{
    Pending = 0,
    Passable = 1,
    Impassable = 2,
    CostOverflow = 3
}

/// <summary>Retains scalar state while explicit corridor legs are evaluated.</summary>
internal struct TraversalExplicitEdgeWork
{
    internal NavigationGraphEdge Edge;
    internal NavigationNodeState SourceState;
    internal NavigationNodeState TargetState;
    internal NavigationNodeState PreviousState;
    internal NavigationAreaRule TargetRule;
    internal int WitnessIndex;
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

    internal NavigationAgentProfile Profile => _profile;

    internal NavigationAreaPolicy AreaPolicy => _areaPolicy;

    internal bool TryGetPassableNodeState(
        NavigationNodeRef node,
        out NavigationNodeState state) =>
        TryGetPassableNode(node, out state, out _);

    internal TraversalEvaluationStatus EvaluateEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out Fixed64 cost)
    {
        return edge.Kind switch
        {
            NavigationGraphEdgeKind.Explicit => EvaluateExplicitEdge(source, edge, out cost),
            NavigationGraphEdgeKind.Seam => EvaluateAutomaticSeam(source, edge, out cost),
            _ => EvaluateNative(source, edge, out cost)
        };
    }

    internal TraversalExplicitEdgeStatus BeginExplicitEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalExplicitEdgeWork work)
    {
        work = default;
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
            return TraversalExplicitEdgeStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        NavigationConnection connection = record.Definition;
        if (shape.Radius > connection.PortalRadiusClearance
            || shape.Height > connection.PortalHeightClearance)
        {
            return TraversalExplicitEdgeStatus.Impassable;
        }

        work = new TraversalExplicitEdgeWork
        {
            Edge = edge,
            SourceState = sourceState,
            TargetState = targetState,
            PreviousState = sourceState,
            TargetRule = targetRule
        };
        return TraversalExplicitEdgeStatus.Pending;
    }

    internal TraversalExplicitEdgeStatus AdvanceExplicitEdge(
        ref TraversalExplicitEdgeWork work,
        out NavigationNodeRef dependencyNode,
        out Fixed64 cost)
    {
        dependencyNode = default;
        cost = Fixed64.Zero;
        NavigationExplicitConnectionRecord record = work.Edge.ExplicitConnection;
        NavigationConnection connection = record.Definition;
        if (work.WitnessIndex < connection.Witnesses.Count)
        {
            NavigationCellAddress witnessAddress = connection.Witnesses[work.WitnessIndex++];
            if (!_graph.TryGetNodeRef(witnessAddress, out dependencyNode)
                || !TryGetPassableNode(
                    dependencyNode,
                    out NavigationNodeState witnessState,
                    out _))
            {
                return TraversalExplicitEdgeStatus.Impassable;
            }
            TraversalEvaluationStatus vertical = EvaluateVerticalDelta(
                work.PreviousState,
                witnessState);
            if (vertical != TraversalEvaluationStatus.Passable)
                return ToExplicitStatus(vertical);
            work.PreviousState = witnessState;
            return TraversalExplicitEdgeStatus.Pending;
        }

        dependencyNode = work.Edge.Target;
        TraversalEvaluationStatus destinationVertical = EvaluateVerticalDelta(
            work.PreviousState,
            work.TargetState);
        if (destinationVertical != TraversalEvaluationStatus.Passable)
            return ToExplicitStatus(destinationVertical);

        if (!Vector3d.TryGetDistance(
                work.SourceState.FootAnchor,
                connection.EntryAnchor,
                out Fixed64 sourceDistance)
            || !Vector3d.TryGetDistance(
                connection.ExitAnchor,
                work.TargetState.FootAnchor,
                out Fixed64 targetDistance)
            || !Fixed64.TryAdd(sourceDistance, record.CorridorCost, out Fixed64 total)
            || !Fixed64.TryAdd(total, targetDistance, out total)
            || !Fixed64.TryAdd(total, connection.AdditionalCost, out total)
            || !Fixed64.TryAdd(total, work.TargetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, work.TargetRule.AdditionalEnterCost, out total))
        {
            return TraversalExplicitEdgeStatus.CostOverflow;
        }

        cost = total;
        return TraversalExplicitEdgeStatus.Passable;
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
                out Vector3d targetPortalFoot))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }
        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(sourceState, targetState);
        if (vertical != TraversalEvaluationStatus.Passable)
            return vertical;

        if (!NavigationDistanceMath.TryCeiling(
                sourceState.FootAnchor,
                sourcePortalFoot,
                out Fixed64 sourceDistance)
            || !NavigationDistanceMath.TryCeiling(
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
        TraversalExplicitEdgeStatus status = BeginExplicitEdge(source, edge, out TraversalExplicitEdgeWork work);
        while (status == TraversalExplicitEdgeStatus.Pending)
            status = AdvanceExplicitEdge(ref work, out _, out cost);
        return status switch
        {
            TraversalExplicitEdgeStatus.Passable => TraversalEvaluationStatus.Passable,
            TraversalExplicitEdgeStatus.CostOverflow => TraversalEvaluationStatus.CostOverflow,
            _ => TraversalEvaluationStatus.Impassable
        };
    }

    private static TraversalExplicitEdgeStatus ToExplicitStatus(
        TraversalEvaluationStatus status) => status == TraversalEvaluationStatus.CostOverflow
            ? TraversalExplicitEdgeStatus.CostOverflow
            : TraversalExplicitEdgeStatus.Impassable;

    private TraversalEvaluationStatus EvaluateAutomaticSeam(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out Fixed64 cost)
    {
        cost = Fixed64.Zero;
        NavigationAutomaticSeamRef seam = edge.AutomaticSeam;
        if (edge.Kind != NavigationGraphEdgeKind.Seam
            || seam.Pair == null
            || !_graph.AutomaticSeams.IsActive(seam)
            || !_graph.TryGetNodeAddress(source, out NavigationCellAddress sourceAddress)
            || !sourceAddress.Equals(seam.Source)
            || !_graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress targetAddress)
            || !targetAddress.Equals(seam.Destination)
            || !TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(
                edge.Target,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return TraversalEvaluationStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        GridNavigationPortal portal = seam.Portal;
        if (!portal.IsValid
            || shape.Radius > portal.MaximumHorizontalRadius
            || shape.Height > portal.MaximumBodyHeight)
        {
            return TraversalEvaluationStatus.Impassable;
        }
        if (!portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d firstFoot,
                out Vector3d secondFoot))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }
        Vector3d sourcePortalFoot = seam.IsReverse ? secondFoot : firstFoot;
        Vector3d targetPortalFoot = seam.IsReverse ? firstFoot : secondFoot;
        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(sourceState, targetState);
        if (vertical != TraversalEvaluationStatus.Passable)
            return vertical;
        bool verticalPortal = portal.FaceKind == VoxelContactFaceKind.Vertical;
        if (!(verticalPortal
                ? NavigationDistanceMath.TryCeiling(
                    sourceState.FootAnchor,
                    sourcePortalFoot,
                    out Fixed64 sourceDistance)
                : Vector3d.TryGetDistance(
                    sourceState.FootAnchor,
                    sourcePortalFoot,
                    out sourceDistance))
            || !(verticalPortal
                ? NavigationDistanceMath.TryCeiling(
                    targetPortalFoot,
                    targetState.FootAnchor,
                    out Fixed64 targetDistance)
                : Vector3d.TryGetDistance(
                    targetPortalFoot,
                    targetState.FootAnchor,
                    out targetDistance))
            || !Fixed64.TryAdd(sourceDistance, targetDistance, out Fixed64 total)
            || !Fixed64.TryAdd(total, targetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, targetRule.AdditionalEnterCost, out total))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }

        cost = total;
        return TraversalEvaluationStatus.Passable;
    }

    private TraversalEvaluationStatus EvaluateVerticalDelta(
        NavigationNodeState source,
        NavigationNodeState target)
    {
        if (!Fixed64.TrySubtract(
                target.FootAnchor.Y,
                source.FootAnchor.Y,
                out Fixed64 verticalDelta))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }
        if (verticalDelta > Fixed64.Zero)
        {
            return verticalDelta <= _profile.MaxStepUp
                ? TraversalEvaluationStatus.Passable
                : TraversalEvaluationStatus.Impassable;
        }
        if (verticalDelta >= Fixed64.Zero)
            return TraversalEvaluationStatus.Passable;
        if (!Fixed64.TrySubtract(Fixed64.Zero, verticalDelta, out Fixed64 drop))
            return TraversalEvaluationStatus.CostOverflow;
        return drop <= _profile.MaxDropDown
            ? TraversalEvaluationStatus.Passable
            : TraversalEvaluationStatus.Impassable;
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
