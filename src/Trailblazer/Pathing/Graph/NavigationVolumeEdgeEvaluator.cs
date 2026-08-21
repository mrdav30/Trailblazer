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
        if (hasSeam
            && (!GridCellGeometry.TryCreateNavigationPortal(
                    sourcePrism,
                    targetPrism,
                    out GridNavigationPortal seamPortal)
                || !TraversalEvaluator.IsSamePortal(
                    seam.Portal,
                    seamPortal,
                    seam.IsReverse)))
        {
            return NavigationVolumeEdgeStatus.Stale;
        }
        NavigationVolumeEdgeStatus segmentStatus = CertifyResolvedSegment(
            source,
            target,
            sourcePrism,
            targetPrism,
            sourceAnchor,
            targetAnchor,
            isPrimary,
            meter,
            dependencies);
        if (segmentStatus != NavigationVolumeEdgeStatus.Passable)
            return segmentStatus;
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

    internal NavigationVolumeEdgeStatus CertifyRaySegment(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        Vector3d sourceFoot,
        Vector3d targetFoot,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies)
    {
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
        if (!_nodes.TryGetPassableNode(source.Node, out _, out _)
            || !_nodes.TryGetPassableNode(target.Node, out _, out _))
        {
            return NavigationVolumeEdgeStatus.Impassable;
        }

        return CertifyResolvedSegment(
            source,
            target,
            sourcePrism,
            targetPrism,
            sourceFoot,
            targetFoot,
            allowPortalFastPath: true,
            meter,
            dependencies);
    }

    private NavigationVolumeEdgeStatus CertifyResolvedSegment(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        GridCellPrism sourcePrism,
        GridCellPrism targetPrism,
        Vector3d sourceFoot,
        Vector3d targetFoot,
        bool allowPortalFastPath,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies)
    {
        KinematicBodyShape shape = _nodes.Profile.Shape;
        bool fastPath;
        if (source.Node == target.Node)
        {
            fastPath = GridCellGeometry.IsNavigationBodySegmentValid(
                sourcePrism,
                sourceFoot,
                targetFoot,
                shape.Radius,
                shape.Height,
                default,
                default,
                GridNavigationBodySegmentEndpointAllowance.None);
        }
        else
        {
            fastPath = allowPortalFastPath
                && GridCellGeometry.TryCreateNavigationPortal(
                    sourcePrism,
                    targetPrism,
                    out GridNavigationPortal portal)
                && GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                    sourcePrism,
                    targetPrism,
                    portal,
                    sourceFoot,
                    targetFoot,
                    shape.Radius,
                    shape.Height,
                    out Fixed64 sourceParameter,
                    out Fixed64 targetParameter)
                && GridCellGeometry.IsNavigationBodySegmentValid(
                    sourcePrism,
                    sourceFoot,
                    Vector3d.Lerp(sourceFoot, targetFoot, sourceParameter),
                    shape.Radius,
                    shape.Height,
                    default,
                    portal,
                    GridNavigationBodySegmentEndpointAllowance.None)
                && GridCellGeometry.IsNavigationBodySegmentValid(
                    targetPrism,
                    Vector3d.Lerp(sourceFoot, targetFoot, targetParameter),
                    targetFoot,
                    shape.Radius,
                    shape.Height,
                    portal,
                    default,
                    GridNavigationBodySegmentEndpointAllowance.None);
        }
        if (fastPath)
            return NavigationVolumeEdgeStatus.Passable;

        var union = new NavigationVolumeAnchorEvaluator(
            _world,
            _nodes.Graph,
            _nodes.Profile,
            _nodes.AreaPolicy,
            _workspace);
        NavigationVolumeAnchorStatus status = union.EvaluateSegment(
            source,
            target,
            sourceFoot,
            targetFoot,
            meter,
            dependencies);
        return status switch
        {
            NavigationVolumeAnchorStatus.Success => NavigationVolumeEdgeStatus.Passable,
            NavigationVolumeAnchorStatus.BudgetExceeded =>
                NavigationVolumeEdgeStatus.BudgetExceeded,
            NavigationVolumeAnchorStatus.CostOverflow =>
                NavigationVolumeEdgeStatus.CostOverflow,
            NavigationVolumeAnchorStatus.CapacityExceeded =>
                NavigationVolumeEdgeStatus.CapacityExceeded,
            NavigationVolumeAnchorStatus.Stale => NavigationVolumeEdgeStatus.Stale,
            _ => NavigationVolumeEdgeStatus.Impassable
        };
    }
}
