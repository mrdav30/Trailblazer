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
        if (source.Medium != target.Medium)
            return NavigationVolumeEdgeStatus.Stale;
        bool foundSourceAddress = graph.TryGetNodeAddress(
            source.Node,
            out NavigationCellAddress sourceAddress);
        bool foundTargetAddress = graph.TryGetNodeAddress(
            target.Node,
            out NavigationCellAddress targetAddress);
        bool foundSourcePrism = graph.TryGetSeamPrism(
            sourceAddress,
            out GridCellPrism sourcePrism);
        bool foundTargetPrism = graph.TryGetSeamPrism(
            targetAddress,
            out GridCellPrism targetPrism);
        System.Diagnostics.Debug.Assert(
            foundSourceAddress && foundTargetAddress && foundSourcePrism && foundTargetPrism,
            "Volume edge endpoints retain addresses and prisms in their immutable graph.");
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
        System.Diagnostics.Debug.Assert(
            !hasSeam
            || (seam.Pair != null
                && graph.AutomaticSeams.IsActive(seam)
                && sourceAddress.Equals(seam.Source)
                && targetAddress.Equals(seam.Destination)
                && seam.Portal.IsValid));
        System.Diagnostics.Debug.Assert(
            !hasSeam
            || GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out _));
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
        if (!TryGetCost(
                sourceAnchor,
                targetAnchor,
                targetState.Cell.EnterCost,
                targetRule.AdditionalEnterCost,
                out Fixed64 total))
        {
            return NavigationVolumeEdgeStatus.CostOverflow;
        }

        cost = total;
        return NavigationVolumeEdgeStatus.Passable;
    }

    internal static bool TryGetCost(
        Vector3d sourceAnchor,
        Vector3d targetAnchor,
        Fixed64 targetEnterCost,
        Fixed64 additionalEnterCost,
        out Fixed64 total) => NavigationDistanceMath.TryCeiling(
            sourceAnchor,
            targetAnchor,
            out total)
        && Fixed64.TryAdd(total, targetEnterCost, out total)
        && Fixed64.TryAdd(total, additionalEnterCost, out total);

    internal NavigationVolumeEdgeStatus CertifyRaySegment(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        Vector3d sourceFoot,
        Vector3d targetFoot,
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies)
    {
        NavigationWorldGraph graph = _nodes.Graph;
        if (source.Medium != target.Medium)
            return NavigationVolumeEdgeStatus.Stale;
        bool foundSourceAddress = graph.TryGetNodeAddress(
            source.Node,
            out NavigationCellAddress sourceAddress);
        bool foundTargetAddress = graph.TryGetNodeAddress(
            target.Node,
            out NavigationCellAddress targetAddress);
        bool foundSourcePrism = graph.TryGetSeamPrism(
            sourceAddress,
            out GridCellPrism sourcePrism);
        bool foundTargetPrism = graph.TryGetSeamPrism(
            targetAddress,
            out GridCellPrism targetPrism);
        System.Diagnostics.Debug.Assert(
            foundSourceAddress && foundTargetAddress && foundSourcePrism && foundTargetPrism,
            "Certified volume-ray endpoints retain addresses and prisms in their immutable graph.");
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
        if (source.Node.Equals(target.Node))
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
