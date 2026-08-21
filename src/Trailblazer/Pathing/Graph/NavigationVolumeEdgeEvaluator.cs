//=======================================================================
// NavigationVolumeEdgeEvaluator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using NavigationVolumeEdgeStatus = Trailblazer.Pathing.NavigationTraversalEvaluationStatus;

namespace Trailblazer.Pathing;

internal enum NavigationTraversalEvaluationStatus : byte
{
    Passable = 0,
    Impassable = 1,
    BudgetExceeded = 2,
    CostOverflow = 3,
    CapacityExceeded = 4,
    Stale = 5
}

/// <summary>Evaluates one centered volume-body movement without surface constraints.</summary>
internal readonly struct NavigationVolumeEdgeEvaluator
{
    private readonly TraversalEvaluator _nodes;
    private readonly GridWorld _world;
    private readonly NavigationRayWorkspace _workspace;

    internal NavigationVolumeEdgeEvaluator(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        TraversalMedium medium,
        NavigationRayWorkspace workspace)
    {
        _nodes = new TraversalEvaluator(graph, profile, areaPolicy, medium);
        _world = world;
        _workspace = workspace;
    }

    internal NavigationVolumeEdgeStatus Evaluate(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        bool isPrimary,
        NavigationAutomaticSeamRef seam,
        bool hasSeam,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        out Fixed64 cost)
    {
        cost = default;
        NavigationWorldGraph graph = _nodes.Graph;
        if (source.Medium != target.Medium
            || !graph.TryGetNodeAddress(source.Node, out NavigationCellAddress sourceAddress)
            || !graph.TryGetNodeAddress(target.Node, out NavigationCellAddress targetAddress)
            || !graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            || !graph.TryGetSeamPrism(targetAddress, out GridCellPrism targetPrism))
        {
            return NavigationVolumeEdgeStatus.Stale;
        }
        if (!dependencies.TryRecordPage(
                sourceAddress.MapId,
                source.Node.CellSlot / NavigationSemanticPage.SlotCount)
            || !dependencies.TryRecordPage(
                targetAddress.MapId,
                target.Node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return NavigationVolumeEdgeStatus.CapacityExceeded;
        }
        if (!_nodes.TryGetPassableNode(
                source.Node,
                out NavigationNodeState sourceState,
                out _)
            || !_nodes.TryGetPassableNode(
                target.Node,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return NavigationVolumeEdgeStatus.Impassable;
        }

        KinematicBodyShape shape = _nodes.Profile.Shape;
        if (!sourceState.TryGetCenteredVolumeFootAnchor(
                shape.Height,
                out Vector3d sourceAnchor)
            || !targetState.TryGetCenteredVolumeFootAnchor(
                shape.Height,
                out Vector3d targetAnchor))
        {
            return NavigationVolumeEdgeStatus.CostOverflow;
        }
        if (hasSeam
            && (seam.Pair == null
                || !graph.AutomaticSeams.IsActive(seam)
                || !sourceAddress.Equals(seam.Source)
                || !targetAddress.Equals(seam.Destination)
                || !seam.Portal.IsValid))
        {
            return NavigationVolumeEdgeStatus.Stale;
        }
        GridNavigationPortal portal = default;
        Fixed64 sourceParameter = default;
        Fixed64 targetParameter = default;
        bool fastPath = isPrimary
            && GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out portal)
            && (!hasSeam
                || TraversalEvaluator.IsSamePortal(
                    seam.Portal,
                    portal,
                    seam.IsReverse))
            && GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                targetPrism,
                portal,
                sourceAnchor,
                targetAnchor,
                shape.Radius,
                shape.Height,
                out sourceParameter,
                out targetParameter);
        if (hasSeam && (!portal.IsValid
            || !TraversalEvaluator.IsSamePortal(
                seam.Portal,
                portal,
                seam.IsReverse)))
        {
            return NavigationVolumeEdgeStatus.Stale;
        }
        if (fastPath)
        {
            Vector3d sourcePoint = Vector3d.Lerp(
                sourceAnchor,
                targetAnchor,
                sourceParameter);
            Vector3d targetPoint = Vector3d.Lerp(
                sourceAnchor,
                targetAnchor,
                targetParameter);
            fastPath = GridCellGeometry.IsNavigationBodySegmentValid(
                    sourcePrism,
                    sourceAnchor,
                    sourcePoint,
                    shape.Radius,
                    shape.Height,
                    default,
                    portal,
                    GridNavigationBodySegmentEndpointAllowance.None)
                && GridCellGeometry.IsNavigationBodySegmentValid(
                    targetPrism,
                    targetPoint,
                    targetAnchor,
                    shape.Radius,
                    shape.Height,
                    portal,
                    default,
                    GridNavigationBodySegmentEndpointAllowance.None);
        }
        if (!fastPath)
        {
            var union = new NavigationVolumeAnchorEvaluator(
                _world,
                graph,
                _nodes.Profile,
                _nodes.AreaPolicy,
                _workspace);
            NavigationVolumeAnchorStatus unionStatus = union.EvaluateSegment(
                source,
                target,
                sourceAnchor,
                targetAnchor,
                meter,
                dependencies);
            if (unionStatus == NavigationVolumeAnchorStatus.BudgetExceeded)
                return NavigationVolumeEdgeStatus.BudgetExceeded;
            if (unionStatus == NavigationVolumeAnchorStatus.CapacityExceeded)
                return NavigationVolumeEdgeStatus.CapacityExceeded;
            if (unionStatus == NavigationVolumeAnchorStatus.CostOverflow)
                return NavigationVolumeEdgeStatus.CostOverflow;
            if (unionStatus == NavigationVolumeAnchorStatus.Stale)
                return NavigationVolumeEdgeStatus.Stale;
            if (unionStatus != NavigationVolumeAnchorStatus.Success)
                return NavigationVolumeEdgeStatus.Impassable;
        }
        if (!NavigationDistanceMath.TryCeiling(
                sourceAnchor,
                targetAnchor,
                out Fixed64 total)
            || !Fixed64.TryAdd(total, targetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, targetRule.AdditionalEnterCost, out total))
        {
            return NavigationVolumeEdgeStatus.CostOverflow;
        }

        cost = total;
        return NavigationVolumeEdgeStatus.Passable;
    }
}
