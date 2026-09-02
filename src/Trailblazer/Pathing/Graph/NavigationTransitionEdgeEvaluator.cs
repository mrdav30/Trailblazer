//=======================================================================
// NavigationTransitionEdgeEvaluator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using NavigationTransitionEdgeStatus = Trailblazer.Pathing.NavigationTraversalEvaluationStatus;

namespace Trailblazer.Pathing;

/// <summary>Preserves the exact cost and action endpoints selected for a transition.</summary>
internal readonly struct NavigationTransitionEdgeEvidence
{
    internal NavigationTransitionEdgeEvidence(
        Fixed64 cost,
        Vector3d sourceAction,
        Vector3d destinationAction)
    {
        Cost = cost;
        SourceAction = sourceAction;
        DestinationAction = destinationAction;
    }

    internal Fixed64 Cost { get; }

    internal Vector3d SourceAction { get; }

    internal Vector3d DestinationAction { get; }
}

/// <summary>Evaluates authored transition action legs and costs without endpoint travel.</summary>
internal readonly struct NavigationTransitionEdgeEvaluator
{
    private readonly NavigationWorldGraph _graph;
    private readonly NavigationAgentProfile _profile;
    private readonly NavigationAreaPolicy _areaPolicy;
    private readonly GridWorld _world;
    private readonly NavigationRayWorkspace _workspace;

    internal NavigationTransitionEdgeEvaluator(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        NavigationRayWorkspace workspace)
    {
        _graph = graph;
        _profile = profile;
        _areaPolicy = areaPolicy;
        _world = world;
        _workspace = workspace;
    }

    internal NavigationTransitionEdgeStatus EvaluateDefinition(
        NavigationMediumStateRef source,
        NavigationPublishedTransition transition,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        out NavigationMediumStateRef target,
        out NavigationTransitionEdgeEvidence evidence)
    {
        target = default;
        evidence = default;
        TraversalTransitionDefinition definition = transition.Definition;
        bool found = _graph.TryGetNodeAddress(
            source.Node,
            out NavigationCellAddress sourceAddress);
        System.Diagnostics.Debug.Assert(found,
            "Transition candidates are evaluated only from published graph nodes.");
        if (!sourceAddress.Equals(transition.SourceAddress)
            || source.Medium != definition.SourceMedium)
        {
            return NavigationTransitionEdgeStatus.Stale;
        }
        if (!_graph.TryGetNodeRef(
                definition.Destination,
                out NavigationNodeRef targetNode))
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }
        target = new NavigationMediumStateRef(
            targetNode,
            definition.DestinationMedium);
        if (!dependencies.TryRecordPage(
                sourceAddress.MapId,
                source.Node.CellSlot / NavigationSemanticPage.SlotCount)
            || !dependencies.TryRecordPage(
                definition.Destination.MapId,
                target.Node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return NavigationTransitionEdgeStatus.CapacityExceeded;
        }
        if ((_profile.Capabilities & definition.RequiredCapabilities)
                != definition.RequiredCapabilities)
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }

        var sourceNodes = new TraversalEvaluator(
            _graph,
            _profile,
            _areaPolicy,
            source.Medium);
        var targetNodes = new TraversalEvaluator(
            _graph,
            _profile,
            _areaPolicy,
            target.Medium);
        if (!sourceNodes.TryGetPassableNode(
                source.Node,
                out NavigationNodeState sourceState,
                out _)
            || !targetNodes.TryGetPassableNode(
                target.Node,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }
        if (!TryGetAnchors(
                sourceState,
                source.Medium,
                targetState,
                target.Medium,
                out Vector3d sourceAnchor,
                out Vector3d targetAnchor))
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }

        Vector3d sourceAction = definition.HasSourcePointOverride
            ? definition.SourcePointOverride
            : sourceAnchor;
        Vector3d targetAction = definition.HasDestinationPointOverride
            ? definition.DestinationPointOverride
            : targetAnchor;
        NavigationTransitionEdgeStatus sourceLeg = EvaluateLeg(
            source,
            sourceAnchor,
            sourceAction,
            meter,
            dependencies);
        if (sourceLeg != NavigationTransitionEdgeStatus.Passable)
            return sourceLeg;
        NavigationTransitionEdgeStatus targetLeg = EvaluateLeg(
            target,
            targetAction,
            targetAnchor,
            meter,
            dependencies);
        if (targetLeg != NavigationTransitionEdgeStatus.Passable)
            return targetLeg;

        if (!TryGetCost(
                sourceAnchor,
                sourceAction,
                definition.ActionCost,
                targetAction,
                targetAnchor,
                targetState.Cell.EnterCost,
                targetRule.AdditionalEnterCost,
                out Fixed64 total))
        {
            return NavigationTransitionEdgeStatus.CostOverflow;
        }

        evidence = new NavigationTransitionEdgeEvidence(
            total,
            sourceAction,
            targetAction);
        return NavigationTransitionEdgeStatus.Passable;
    }

    internal NavigationTransitionEdgeStatus EvaluateRule(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        TraversalTransitionRule rule,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        out NavigationTransitionEdgeEvidence evidence)
    {
        evidence = default;
        bool foundSource = _graph.TryGetNodeAddress(
            source.Node,
            out NavigationCellAddress address);
        bool foundTarget = _graph.TryGetNodeAddress(
            target.Node,
            out NavigationCellAddress targetAddress);
        System.Diagnostics.Debug.Assert(foundSource && foundTarget,
            "Transition rule pairs contain only published graph nodes.");
        if ((rule.Scope == TraversalTransitionRuleScope.SameCell
                && !source.Node.Equals(target.Node))
            || source.Medium != rule.SourceMedium
            || target.Medium != rule.DestinationMedium
            || (_profile.Capabilities & rule.RequiredCapabilities)
                != rule.RequiredCapabilities)
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }
        if (!dependencies.TryRecordPage(
                address.MapId,
                source.Node.CellSlot / NavigationSemanticPage.SlotCount)
            || !dependencies.TryRecordPage(
                targetAddress.MapId,
                target.Node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return NavigationTransitionEdgeStatus.CapacityExceeded;
        }

        var sourceNodes = new TraversalEvaluator(
            _graph,
            _profile,
            _areaPolicy,
            source.Medium);
        var targetNodes = new TraversalEvaluator(
            _graph,
            _profile,
            _areaPolicy,
            target.Medium);
        if (!sourceNodes.TryGetPassableNodeState(
                source.Node,
                out NavigationNodeState sourceState)
            || !targetNodes.TryGetPassableNode(
                target.Node,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }
        if (!TryGetAnchors(
                sourceState,
                source.Medium,
                targetState,
                target.Medium,
                out Vector3d sourceAnchor,
                out Vector3d targetAnchor))
        {
            return NavigationTransitionEdgeStatus.Impassable;
        }

        Vector3d sourceAction = sourceAnchor;
        Vector3d targetAction = targetAnchor;
        GridNavigationPortal portal = default;
        if (rule.Scope == TraversalTransitionRuleScope.PositiveFaceContact)
        {
            bool foundSourcePrism = _graph.TryGetSeamPrism(
                address,
                out GridCellPrism sourcePrism);
            bool foundTargetPrism = _graph.TryGetSeamPrism(
                targetAddress,
                out GridCellPrism targetPrism);
            System.Diagnostics.Debug.Assert(foundSourcePrism && foundTargetPrism,
                "Published transition endpoints belong to their bound grid prisms.");
            if (!GridCellGeometry.TryCreateNavigationPortal(
                    sourcePrism,
                    targetPrism,
                    out portal))
            {
                return NavigationTransitionEdgeStatus.Impassable;
            }
            if (!GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                targetPrism,
                portal,
                sourceAnchor,
                targetAnchor,
                _profile.Shape.Radius,
                _profile.Shape.Height,
                out Fixed64 sourceParameter,
                out Fixed64 targetParameter))
            {
                return NavigationTransitionEdgeStatus.Impassable;
            }
            sourceAction = Vector3d.Lerp(sourceAnchor, targetAnchor, sourceParameter);
            targetAction = Vector3d.Lerp(sourceAnchor, targetAnchor, targetParameter);
        }
        NavigationTransitionEdgeStatus sourceLeg = EvaluateLeg(
            source,
            sourceAnchor,
            sourceAction,
            meter,
            dependencies,
            outgoingPortal: portal);
        if (sourceLeg != NavigationTransitionEdgeStatus.Passable)
            return sourceLeg;
        NavigationTransitionEdgeStatus targetLeg = EvaluateLeg(
            target,
            targetAction,
            targetAnchor,
            meter,
            dependencies,
            incomingPortal: portal);
        if (targetLeg != NavigationTransitionEdgeStatus.Passable)
            return targetLeg;
        if (!TryGetCost(
                sourceAnchor,
                sourceAction,
                rule.ActionCost,
                targetAction,
                targetAnchor,
                targetState.Cell.EnterCost,
                targetRule.AdditionalEnterCost,
                out Fixed64 total))
        {
            return NavigationTransitionEdgeStatus.CostOverflow;
        }

        evidence = new NavigationTransitionEdgeEvidence(
            total,
            sourceAction,
            targetAction);
        return NavigationTransitionEdgeStatus.Passable;
    }

    internal static bool TryGetCost(
        Vector3d sourceAnchor,
        Vector3d sourceAction,
        Fixed64 actionCost,
        Vector3d targetAction,
        Vector3d targetAnchor,
        Fixed64 targetEnterCost,
        Fixed64 additionalEnterCost,
        out Fixed64 total) => NavigationDistanceMath.TryCeiling(
            sourceAnchor,
            sourceAction,
            out total)
        && Fixed64.TryAdd(total, actionCost, out total)
        && NavigationDistanceMath.TryCeiling(
            targetAction,
            targetAnchor,
            out Fixed64 targetDistance)
        && Fixed64.TryAdd(total, targetDistance, out total)
        && Fixed64.TryAdd(total, targetEnterCost, out total)
        && Fixed64.TryAdd(total, additionalEnterCost, out total);

    private bool TryGetAnchors(
        NavigationNodeState source,
        TraversalMedium sourceMedium,
        NavigationNodeState target,
        TraversalMedium targetMedium,
        out Vector3d sourceAnchor,
        out Vector3d targetAnchor)
    {
        if (!TryGetAnchor(source, sourceMedium, out sourceAnchor))
        {
            targetAnchor = default;
            return false;
        }
        return TryGetAnchor(target, targetMedium, out targetAnchor);
    }

    private bool TryGetAnchor(
        NavigationNodeState state,
        TraversalMedium medium,
        out Vector3d anchor)
    {
        if (medium == TraversalMedium.Solid)
        {
            anchor = state.FootAnchor;
            return true;
        }
        return state.TryGetCenteredVolumeFootAnchor(_profile.Shape.Height, out anchor);
    }

    private NavigationTransitionEdgeStatus EvaluateLeg(
        NavigationMediumStateRef state,
        Vector3d start,
        Vector3d end,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        GridNavigationPortal incomingPortal = default,
        GridNavigationPortal outgoingPortal = default)
    {
        if (start == end && state.Medium == TraversalMedium.Solid)
            return NavigationTransitionEdgeStatus.Passable;
        if (state.Medium == TraversalMedium.Solid)
        {
            _graph.TryGetNodeAddress(state.Node, out NavigationCellAddress address);
            _graph.TryGetSeamPrism(address, out GridCellPrism prism);
            System.Diagnostics.Debug.Assert(
                address.MapId != null && prism.FootprintVertexCount > 0);
            bool valid = GridCellGeometry.IsNavigationBodySegmentValid(
                prism,
                start,
                end,
                _profile.Shape.Radius,
                _profile.Shape.Height,
                incomingPortal,
                outgoingPortal,
                GridNavigationBodySegmentEndpointAllowance.None);
            System.Diagnostics.Debug.Assert(valid,
                "Published transition actions and computed portals are valid in-cell segments.");
            return NavigationTransitionEdgeStatus.Passable;
        }

        if ((incomingPortal.IsValid || outgoingPortal.IsValid)
            && _graph.TryGetNodeAddress(
                state.Node,
                out NavigationCellAddress volumeAddress)
            && _graph.TryGetSeamPrism(volumeAddress, out GridCellPrism volumePrism)
            && GridCellGeometry.IsNavigationBodySegmentValid(
                volumePrism,
                start,
                end,
                _profile.Shape.Radius,
                _profile.Shape.Height,
                incomingPortal,
                outgoingPortal,
                GridNavigationBodySegmentEndpointAllowance.None))
        {
            return NavigationTransitionEdgeStatus.Passable;
        }

        var volume = new NavigationVolumeAnchorEvaluator(
            _world,
            _graph,
            _profile,
            _areaPolicy,
            _workspace);
        NavigationVolumeAnchorStatus status = volume.EvaluateSegment(
            state,
            state,
            start,
            end,
            meter,
            dependencies);
        return status switch
        {
            NavigationVolumeAnchorStatus.Success =>
                NavigationTransitionEdgeStatus.Passable,
            NavigationVolumeAnchorStatus.BudgetExceeded =>
                NavigationTransitionEdgeStatus.BudgetExceeded,
            NavigationVolumeAnchorStatus.CostOverflow =>
                NavigationTransitionEdgeStatus.CostOverflow,
            NavigationVolumeAnchorStatus.CapacityExceeded =>
                NavigationTransitionEdgeStatus.CapacityExceeded,
            NavigationVolumeAnchorStatus.Stale =>
                NavigationTransitionEdgeStatus.Stale,
            _ => NavigationTransitionEdgeStatus.Impassable
        };
    }
}
