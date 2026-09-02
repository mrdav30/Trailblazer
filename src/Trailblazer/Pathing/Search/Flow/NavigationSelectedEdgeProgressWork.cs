//=======================================================================
// NavigationSelectedEdgeProgressWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

internal readonly struct NavigationFlowRejoinTarget
{
    internal NavigationFlowRejoinTarget(
        Vector3d position,
        NavigationRayChainConstraint constraint)
    {
        Position = position;
        Constraint = constraint;
    }

    internal Vector3d Position { get; }

    internal NavigationRayChainConstraint Constraint { get; }
}

/// <summary>Samples only the canonical selected edge stored by one flow node.</summary>
internal enum NavigationFlowNodeLookupStatus : byte
{
    Success,
    NotFound,
    BudgetExceeded
}

internal static class NavigationSelectedEdgeProgressWork
{
    internal static NavigationGuideStatus MapNodeLookupStatus(
        NavigationFlowNodeLookupStatus status,
        bool required) => status switch
    {
        NavigationFlowNodeLookupStatus.Success => NavigationGuideStatus.Success,
        NavigationFlowNodeLookupStatus.BudgetExceeded =>
            NavigationGuideStatus.BudgetExceeded,
        _ => required ? NavigationGuideStatus.Stale : NavigationGuideStatus.Success
    };

    internal static NavigationGuideStatus MapStructuralEdgeStatus(
        NavigationSurfaceEdgeAdvanceStatus status) => status switch
    {
        NavigationSurfaceEdgeAdvanceStatus.Pending => NavigationGuideStatus.Success,
        NavigationSurfaceEdgeAdvanceStatus.Edge => NavigationGuideStatus.Success,
        NavigationSurfaceEdgeAdvanceStatus.Blocked => NavigationGuideStatus.BudgetExceeded,
        _ => NavigationGuideStatus.Stale
    };

    internal static bool TryGetRejoinTarget(
        NavigationCellAddress sourceAddress,
        NavigationNodeState sourceState,
        NavigationNodeState targetState,
        NavigationSelectedEdgeRef selected,
        Vector3d selectedExitTarget,
        int targetOrdinal,
        out NavigationFlowRejoinTarget target)
    {
        SwiftThrowHelper.ThrowIfNegative(targetOrdinal, nameof(targetOrdinal));
        target = default;
        if (targetOrdinal == 0)
        {
            target = new NavigationFlowRejoinTarget(
                sourceState.FootAnchor,
                NavigationRayChainConstraint.SourceOnly(sourceAddress));
            return true;
        }

        NavigationRayChainConstraint constraint =
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                selected.Target,
                selected.CanonicalOutgoingOrdinal);
        if (targetOrdinal == 1)
        {
            target = new NavigationFlowRejoinTarget(selectedExitTarget, constraint);
            return true;
        }
        if (targetOrdinal == 2)
        {
            target = new NavigationFlowRejoinTarget(targetState.FootAnchor, constraint);
            return true;
        }
        return false;
    }

    internal static NavigationGuideStatus TrySample(
        bool dependencyCurrent,
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress sourceAddress,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        GridCoveredAddressCursor coveredAddressCursor,
        GridCoveredAddressGeneration[] coveredAddressGenerations,
        GridCoveredAddress[] coveredAddressOutput,
        NavigationImmediateRayWorkspace immediateRayWorkspace,
        out NavigationFlowFieldNode currentNode,
        out NavigationCellAddress nextSourceAddress,
        out Vector3d target,
        out Vector3d heading)
    {
        currentNode = default;
        nextSourceAddress = sourceAddress;
        target = default;
        heading = Vector3d.Zero;
        if (!dependencyCurrent)
            return NavigationGuideStatus.Stale;
        if (medium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return TrySampleVolume(
                world,
                store,
                graph,
                payload,
                sourceAddress,
                medium,
                actualFootPosition,
                ref meter,
                immediateRayWorkspace,
                out currentNode,
                out nextSourceAddress,
                out target,
                out heading);
        }
        NavigationCellAddress currentSource = sourceAddress;
        bool allowRecovery = true;
        while (true)
        {
            NavigationFlowNodeLookupStatus lookup = TryGetNode(
                payload,
                currentSource,
                medium,
                ref meter,
                out NavigationFlowFieldNode node);
            if (lookup != NavigationFlowNodeLookupStatus.Success)
            {
                return MapNodeLookupStatus(lookup, required: true);
            }
            currentNode = node;
            if (node.TransitionInstructionOrdinal >= 0)
            {
                nextSourceAddress = currentSource;
                return NavigationGuideStatus.Success;
            }
            graph.TryGetNodeRef(currentSource, out NavigationNodeRef sourceRef);
            graph.TryGetNodeState(
                sourceRef,
                medium,
                out NavigationNodeState sourceState);
            graph.TryGetSeamPrism(currentSource, out GridCellPrism sourcePrism);
            if (!meter.TryConsumePrismChecks(1))
                return NavigationGuideStatus.BudgetExceeded;
            bool sourceContains = sourcePrism.Contains(actualFootPosition);
            target = sourceState.FootAnchor;
            if (currentSource == payload.Key.DestinationAddress)
            {
                if (sourceContains
                    && GridCellGeometry.IsNavigationBodyAnchorValid(
                        sourcePrism,
                        actualFootPosition,
                        payload.Key.Agent.Shape.Radius,
                        payload.Key.Agent.Shape.Height,
                        default))
                {
                    nextSourceAddress = currentSource;
                    if (IsWithinArrivalRadius(
                            actualFootPosition,
                            sourceState.FootAnchor,
                            payload.Key.Agent.ArrivalRadius))
                    {
                        heading = Vector3d.Zero;
                        return NavigationGuideStatus.Success;
                    }
                    return TrySetHeading(
                        actualFootPosition,
                        sourceState.FootAnchor,
                        ref meter,
                        out heading);
                }
                NavigationGuideStatus destinationRecovery = TryAdvanceRecovery(
                    world,
                    store,
                    graph,
                    payload,
                    medium,
                    actualFootPosition,
                    ref meter,
                    coveredAddressCursor,
                    coveredAddressGenerations,
                    coveredAddressOutput,
                    immediateRayWorkspace,
                    currentSource,
                    sourceState,
                    default,
                    Vector3d.Zero,
                    default,
                    hasSelectedEdge: false,
                    allowRecovery,
                    out currentSource,
                    out Vector3d destinationRejoinHeading,
                    out bool destinationRejoined);
                if (destinationRecovery != NavigationGuideStatus.Success)
                    return destinationRecovery;
                if (destinationRejoined)
                {
                    nextSourceAddress = sourceAddress;
                    heading = destinationRejoinHeading;
                    return NavigationGuideStatus.Success;
                }
                allowRecovery = false;
                continue;
            }

            NavigationGuideStatus edgeStatus = TryResolveSelectedEdge(
                graph,
                sourceRef,
                node.SelectedEdge,
                ref meter,
                out NavigationGraphEdge edge);
            if (edgeStatus != NavigationGuideStatus.Success)
                return edgeStatus;
            graph.TryGetNodeState(
                edge.Target,
                medium,
                out NavigationNodeState targetState);
            graph.TryGetSeamPrism(
                node.SelectedEdge.Target,
                out GridCellPrism targetPrism);
            if (!meter.TryConsumePrismChecks(1))
                return NavigationGuideStatus.BudgetExceeded;
            bool targetContains = targetPrism.Contains(actualFootPosition);
            target = targetState.FootAnchor;

            if (edge.Kind == NavigationGraphEdgeKind.Explicit)
            {
                NavigationGuideStatus explicitStatus = TrySampleExplicit(
                    graph,
                    edge.ExplicitConnection,
                    currentSource,
                    sourceState,
                    sourcePrism,
                    targetState,
                    targetPrism,
                    sourceContains,
                    targetContains,
                    actualFootPosition,
                    payload.Key.Agent.Shape,
                    node.SelectedEdge.Target == payload.Key.DestinationAddress,
                    payload.Key.Agent.ArrivalRadius,
                    ref meter,
                    out NavigationCellAddress explicitSource,
                    out heading);
                if (explicitStatus == NavigationGuideStatus.LocalRecoveryRequired)
                {
                    NavigationGuideStatus recovery = TryAdvanceRecovery(
                        world,
                        store,
                        graph,
                        payload,
                        medium,
                        actualFootPosition,
                        ref meter,
                        coveredAddressCursor,
                        coveredAddressGenerations,
                        coveredAddressOutput,
                        immediateRayWorkspace,
                        currentSource,
                        sourceState,
                        node.SelectedEdge,
                        edge.ExplicitConnection.Definition.ExitAnchor,
                        targetState,
                        hasSelectedEdge: true,
                        allowRecovery,
                        out currentSource,
                        out Vector3d rejoinHeading,
                        out bool rejoined);
                    if (recovery != NavigationGuideStatus.Success)
                        return recovery;
                    if (rejoined)
                    {
                        nextSourceAddress = sourceAddress;
                        heading = rejoinHeading;
                        return NavigationGuideStatus.Success;
                    }
                    allowRecovery = false;
                    continue;
                }
                if (explicitStatus != NavigationGuideStatus.Success
                    || explicitSource == currentSource)
                {
                    nextSourceAddress = currentSource;
                    return explicitStatus;
                }
                currentSource = explicitSource;
                continue;
            }

            NavigationGuideStatus portalStatus = TryResolvePortal(
                edge,
                sourceState,
                payload.Key.Agent.Shape,
                ref meter,
                out GridNavigationPortal selectedPortal,
                out Vector3d sourcePortal,
                out Vector3d targetPortal);
            if (portalStatus != NavigationGuideStatus.Success)
                return portalStatus;
            bool sourceBodyValid = sourceContains
                && GridCellGeometry.IsNavigationBodyAnchorValid(
                    sourcePrism,
                    actualFootPosition,
                    payload.Key.Agent.Shape.Radius,
                    payload.Key.Agent.Shape.Height,
                    selectedPortal);
            bool targetBodyValid = targetContains
                && GridCellGeometry.IsNavigationBodyAnchorValid(
                    targetPrism,
                    actualFootPosition,
                    payload.Key.Agent.Shape.Radius,
                    payload.Key.Agent.Shape.Height,
                    selectedPortal);
            if (!sourceBodyValid && !targetBodyValid)
            {
                NavigationGuideStatus recovery = TryAdvanceRecovery(
                    world,
                    store,
                    graph,
                    payload,
                    medium,
                    actualFootPosition,
                    ref meter,
                    coveredAddressCursor,
                    coveredAddressGenerations,
                    coveredAddressOutput,
                    immediateRayWorkspace,
                    currentSource,
                    sourceState,
                    node.SelectedEdge,
                    targetPortal,
                    targetState,
                    hasSelectedEdge: true,
                    allowRecovery,
                    out currentSource,
                    out Vector3d rejoinHeading,
                    out bool rejoined);
                if (recovery != NavigationGuideStatus.Success)
                    return recovery;
                if (rejoined)
                {
                    nextSourceAddress = sourceAddress;
                    heading = rejoinHeading;
                    return NavigationGuideStatus.Success;
                }
                allowRecovery = false;
                continue;
            }
            if (targetBodyValid
                && node.SelectedEdge.Target == payload.Key.DestinationAddress
                && IsWithinArrivalRadius(
                    actualFootPosition,
                    targetState.FootAnchor,
                    payload.Key.Agent.ArrivalRadius))
            {
                nextSourceAddress = currentSource;
                heading = Vector3d.Zero;
                return NavigationGuideStatus.Success;
            }
            if (sourceBodyValid)
            {
                nextSourceAddress = currentSource;
                return TrySampleDirectedLeg(
                    sourceState.FootAnchor,
                    sourcePortal,
                    targetPortal,
                    targetState.FootAnchor,
                    actualFootPosition,
                    ref meter,
                    out heading);
            }

            NavigationGuideStatus targetProgress = HasReachedOrPassed(
                targetPortal,
                targetState.FootAnchor,
                actualFootPosition,
                out bool passedTarget);
            if (targetProgress != NavigationGuideStatus.Success)
                return targetProgress;
            if (!passedTarget)
            {
                nextSourceAddress = currentSource;
                return TrySetHeading(
                    actualFootPosition,
                    targetState.FootAnchor,
                    ref meter,
                    out heading);
            }
            if (!meter.TryConsumeCursorRebases(1))
                return NavigationGuideStatus.BudgetExceeded;
            currentSource = node.SelectedEdge.Target;
        }
    }

    private static NavigationGuideStatus TryResolveSelectedEdge(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        NavigationSelectedEdgeRef selected,
        ref GuideSampleWorkMeter meter,
        out NavigationGraphEdge edge)
    {
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateStructuralSurfaceEdges(source);
        int edgeStepRemaining = int.MaxValue;
        while (true)
        {
            NavigationSurfaceEdgeAdvanceStatus status = edges.AdvanceOne(
                ref meter,
                ref edgeStepRemaining);
            NavigationGuideStatus mappedStatus = MapStructuralEdgeStatus(status);
            if (mappedStatus != NavigationGuideStatus.Success)
            {
                edge = default;
                return mappedStatus;
            }
            if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (edges.CurrentOrdinal != selected.CanonicalOutgoingOrdinal)
                continue;
            edge = edges.Current;
            return NavigationGuideStatus.Success;
        }
    }

    private static NavigationGuideStatus TrySampleVolume(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress sourceAddress,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        NavigationImmediateRayWorkspace immediateRayWorkspace,
        out NavigationFlowFieldNode currentNode,
        out NavigationCellAddress nextSourceAddress,
        out Vector3d target,
        out Vector3d heading)
    {
        currentNode = default;
        nextSourceAddress = sourceAddress;
        target = default;
        heading = Vector3d.Zero;
        NavigationCellAddress currentSource = sourceAddress;
        graph.AreaCatalog.TryGet(
            payload.Key.AreaPolicy,
            out NavigationAreaPolicy areaPolicy);
        while (true)
        {
            NavigationFlowNodeLookupStatus lookup = TryGetNode(
                payload,
                currentSource,
                medium,
                ref meter,
                out NavigationFlowFieldNode node);
            if (lookup != NavigationFlowNodeLookupStatus.Success)
            {
                return MapNodeLookupStatus(lookup, required: true);
            }
            currentNode = node;
            if (node.TransitionInstructionOrdinal >= 0)
            {
                nextSourceAddress = currentSource;
                return NavigationGuideStatus.Success;
            }
            graph.TryGetNodeRef(currentSource, out NavigationNodeRef sourceRef);
            graph.TryGetNodeState(
                sourceRef,
                medium,
                out NavigationNodeState sourceState);
            sourceState.TryGetCenteredVolumeFootAnchor(
                payload.Key.Agent.Shape.Height,
                out Vector3d sourceAnchor);
            target = sourceAnchor;
            if (!node.SelectedEdge.IsValid)
            {
                nextSourceAddress = currentSource;
                NavigationGuideStatus destination = RunRay(
                    world,
                    store,
                    graph,
                    payload,
                    areaPolicy,
                    medium,
                    actualFootPosition,
                    sourceAnchor,
                    NavigationRayChainConstraint.SourceOnly(currentSource),
                    ref meter,
                    immediateRayWorkspace);
                if (destination != NavigationGuideStatus.Success)
                    return destination;
                if (IsWithinArrivalRadius(
                        actualFootPosition,
                        sourceAnchor,
                        payload.Key.Agent.ArrivalRadius))
                {
                    heading = Vector3d.Zero;
                    return NavigationGuideStatus.Success;
                }
                return TrySetHeadingUnchecked(actualFootPosition, sourceAnchor, out heading);
            }
            graph.TryGetNodeRef(
                node.SelectedEdge.Target,
                out NavigationNodeRef targetRef);
            graph.TryGetNodeState(
                targetRef,
                medium,
                out NavigationNodeState targetState);
            targetState.TryGetCenteredVolumeFootAnchor(
                payload.Key.Agent.Shape.Height,
                out Vector3d targetAnchor);
            target = targetAnchor;
            if (actualFootPosition == targetAnchor)
            {
                NavigationGuideStatus arrived = RunRay(
                    world,
                    store,
                    graph,
                    payload,
                    areaPolicy,
                    medium,
                    actualFootPosition,
                    actualFootPosition,
                    NavigationRayChainConstraint.SourceOnly(node.SelectedEdge.Target),
                    ref meter,
                    immediateRayWorkspace);
                if (arrived != NavigationGuideStatus.Success)
                    return arrived;
                if (!meter.TryConsumeCursorRebases(1))
                    return NavigationGuideStatus.BudgetExceeded;
                currentSource = node.SelectedEdge.Target;
                nextSourceAddress = currentSource;
                continue;
            }

            NavigationGuideStatus status = RunRay(
                world,
                store,
                graph,
                payload,
                areaPolicy,
                medium,
                actualFootPosition,
                targetAnchor,
                NavigationRayChainConstraint.SelectedEdge(
                    currentSource,
                    node.SelectedEdge.Target,
                    node.SelectedEdge.CanonicalOutgoingOrdinal),
                ref meter,
                immediateRayWorkspace);
            if (status != NavigationGuideStatus.Success)
                return status;
            nextSourceAddress = currentSource;
            if (node.SelectedEdge.Target == payload.Key.DestinationAddress
                && IsWithinArrivalRadius(
                    actualFootPosition,
                    targetAnchor,
                    payload.Key.Agent.ArrivalRadius))
            {
                heading = Vector3d.Zero;
                return NavigationGuideStatus.Success;
            }
            return TrySetHeadingUnchecked(actualFootPosition, targetAnchor, out heading);
        }
    }

    private static bool IsWithinArrivalRadius(
        Vector3d actualFootPosition,
        Vector3d target,
        Fixed64 arrivalRadius) =>
        Vector3d.CompareDistanceSquared(
            actualFootPosition,
            target,
            Vector3d.Zero,
            new Vector3d(arrivalRadius, Fixed64.Zero, Fixed64.Zero)) <= 0;

    internal static NavigationGuideStatus TrySampleTransitionApproach(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress source,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        Vector3d actionPosition,
        ref GuideSampleWorkMeter meter,
        NavigationImmediateRayWorkspace immediateRayWorkspace,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        bool alreadyAtAction = actualFootPosition == actionPosition;
        graph.AreaCatalog.TryGet(
            payload.Key.AreaPolicy,
            out NavigationAreaPolicy areaPolicy);
        NavigationGuideStatus status = RunRay(
            world,
            store,
            graph,
            payload,
            areaPolicy,
            medium,
            actualFootPosition,
            actionPosition,
            NavigationRayChainConstraint.SourceOnly(source),
            ref meter,
            immediateRayWorkspace);
        if (alreadyAtAction && status == NavigationGuideStatus.LocalRecoveryRequired)
            return NavigationGuideStatus.Success;
        return status == NavigationGuideStatus.Success
            ? TrySetHeadingUnchecked(actualFootPosition, actionPosition, out heading)
            : status;
    }

    private static NavigationGuideStatus RunRay(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        NavigationAreaPolicy areaPolicy,
        TraversalMedium medium,
        Vector3d start,
        Vector3d end,
        NavigationRayChainConstraint constraint,
        ref GuideSampleWorkMeter meter,
        NavigationImmediateRayWorkspace immediateRayWorkspace)
    {
        lock (immediateRayWorkspace.SyncRoot)
        {
            NavigationWorkMeter rayMeter = immediateRayWorkspace.WorkMeter;
            rayMeter.ResetForGuideSample(
                meter.GetCurrentNodeLookupAllowance(),
                meter.GetCursorLegScanAllowance(),
                meter.GetPortalCheckAllowance(),
                meter.GetPrismCheckAllowance(),
                meter.GetTraceIntervalAllowance());
            NavigationRayWork ray = immediateRayWorkspace.RayWork;
            ray.Begin(new NavigationRayRequest(
                world,
                store,
                graph,
                payload.Key.Agent,
                areaPolicy,
                medium,
                start,
                end,
                NavigationRayEndpointAllowance.StartPrefix,
                constraint));
            NavigationRayStatus status;
            try
            {
                do
                {
                    status = ray.Advance(rayMeter);
                }
                while (status == NavigationRayStatus.Pending);
            }
            finally
            {
                ray.Reset();
            }
            meter.TryConsumeCurrentNodeLookupProbes(
                checked(rayMeter.LookupProbes + rayMeter.CoveredVoxelIntervals));
            meter.TryConsumeCursorLegScans(
                checked(rayMeter.EvaluatedEdges + rayMeter.ConnectionLegs));
            meter.TryConsumePortalChecks(rayMeter.GuidePortalChecks);
            meter.TryConsumePrismChecks(rayMeter.GuidePrismChecks);
            meter.TryConsumeTraceIntervals(rayMeter.TraceIntervals);
            return status == NavigationRayStatus.Success
                ? NavigationGuideStatus.Success
                : NavigationGuideStatusMapper.ToPublic(status);
        }
    }

    private static NavigationFlowNodeLookupStatus TryGetNode(
        NavigationFlowFieldPayload payload,
        NavigationCellAddress address,
        TraversalMedium medium,
        ref GuideSampleWorkMeter meter,
        out NavigationFlowFieldNode node)
    {
        int low = 0;
        int high = payload.AddressLookupOrdinals.Length - 1;
        while (low <= high)
        {
            if (!meter.TryConsumeCurrentNodeLookupProbes(1))
            {
                node = default;
                return NavigationFlowNodeLookupStatus.BudgetExceeded;
            }
            int middle = low + ((high - low) >> 1);
            NavigationFlowFieldNode candidate =
                payload.Nodes[payload.AddressLookupOrdinals[middle]];
            int comparison = candidate.Address.CompareTo(address);
            if (comparison == 0)
                comparison = ((int)candidate.Medium).CompareTo((int)medium);
            if (comparison == 0)
            {
                node = candidate;
                return NavigationFlowNodeLookupStatus.Success;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        node = default;
        return NavigationFlowNodeLookupStatus.NotFound;
    }

    private static NavigationGuideStatus TryAdvanceRecovery(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        GridCoveredAddressCursor coveredAddressCursor,
        GridCoveredAddressGeneration[] coveredAddressGenerations,
        GridCoveredAddress[] coveredAddressOutput,
        NavigationImmediateRayWorkspace immediateRayWorkspace,
        NavigationCellAddress sourceAddress,
        NavigationNodeState sourceState,
        NavigationSelectedEdgeRef selected,
        Vector3d selectedExitTarget,
        NavigationNodeState targetState,
        bool hasSelectedEdge,
        bool allowRecovery,
        out NavigationCellAddress rebased,
        out Vector3d heading,
        out bool rejoined)
    {
        rebased = default;
        heading = Vector3d.Zero;
        rejoined = false;
        if (!allowRecovery)
            return NavigationGuideStatus.LocalRecoveryRequired;
        NavigationGuideStatus rebase = TryRebase(
            world,
            graph,
            payload,
            medium,
            actualFootPosition,
            ref meter,
            coveredAddressCursor,
            coveredAddressGenerations,
            coveredAddressOutput,
            out rebased);
        if (rebase != NavigationGuideStatus.LocalRecoveryRequired)
            return rebase;
        NavigationGuideStatus status = TryRejoin(
            world,
            store,
            graph,
            payload,
            medium,
            sourceAddress,
            sourceState,
            selected,
            selectedExitTarget,
            targetState,
            hasSelectedEdge,
            actualFootPosition,
            ref meter,
            immediateRayWorkspace,
            out heading);
        rejoined = status == NavigationGuideStatus.Success;
        return status;
    }

    private static NavigationGuideStatus TryRejoin(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        TraversalMedium medium,
        NavigationCellAddress sourceAddress,
        NavigationNodeState sourceState,
        NavigationSelectedEdgeRef selected,
        Vector3d selectedExitTarget,
        NavigationNodeState targetState,
        bool hasSelectedEdge,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        NavigationImmediateRayWorkspace immediateRayWorkspace,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        graph.AreaCatalog.TryGet(
            payload.Key.AreaPolicy,
            out NavigationAreaPolicy areaPolicy);
        lock (immediateRayWorkspace.SyncRoot)
        {
            NavigationRayWork ray = immediateRayWorkspace.RayWork;
            for (int targetOrdinal = 0; ; targetOrdinal++)
            {
                if (targetOrdinal > 0 && !hasSelectedEdge)
                    return NavigationGuideStatus.LocalRecoveryRequired;
                if (!TryGetRejoinTarget(
                    sourceAddress,
                    sourceState,
                    targetState,
                    selected,
                    selectedExitTarget,
                    targetOrdinal,
                    out NavigationFlowRejoinTarget target))
                {
                    return NavigationGuideStatus.LocalRecoveryRequired;
                }

                ray.Begin(new NavigationRayRequest(
                    world,
                    store,
                    graph,
                    payload.Key.Agent,
                    areaPolicy,
                    medium,
                    actualFootPosition,
                    target.Position,
                    NavigationRayEndpointAllowance.StartPrefix,
                    target.Constraint));
                NavigationRayStatus status;
                NavigationRayResult result;
                try
                {
                    do
                    {
                        status = ray.Advance(ref meter);
                    }
                    while (status == NavigationRayStatus.Pending);
                    result = ray.Result;
                }
                finally
                {
                    ray.Reset();
                }
                if (status == NavigationRayStatus.Blocked
                    || (status == NavigationRayStatus.Success
                        && !result.IsSemanticCostNeutral
                        && target.Constraint.Kind
                            != NavigationRayChainConstraintKind.SelectedEdge))
                {
                    continue;
                }
                if (status != NavigationRayStatus.Success)
                    return NavigationGuideStatusMapper.ToPublic(status);
                return TrySetHeadingUnchecked(
                    actualFootPosition,
                    target.Position,
                    out heading);
            }
        }
    }

    private static NavigationGuideStatus TryResolvePortal(
        NavigationGraphEdge edge,
        NavigationNodeState sourceState,
        KinematicBodyShape shape,
        ref GuideSampleWorkMeter meter,
        out GridNavigationPortal resolvedPortal,
        out Vector3d sourcePortal,
        out Vector3d targetPortal)
    {
        resolvedPortal = default;
        sourcePortal = default;
        targetPortal = default;
        if (!meter.TryConsumePortalChecks(1))
            return NavigationGuideStatus.BudgetExceeded;
        bool reverse = false;
        if (edge.Kind == NavigationGraphEdgeKind.Native)
        {
            edge.NativePortal.TryTranslate(sourceState.Center, out resolvedPortal);
        }
        else
        {
            NavigationAutomaticSeamRef seam = edge.AutomaticSeam;
            resolvedPortal = seam.Portal;
            reverse = seam.IsReverse;
        }
        resolvedPortal.TryResolveProfile(
            shape.Radius,
            shape.Height,
            out Vector3d first,
            out Vector3d second);
        sourcePortal = reverse ? second : first;
        targetPortal = reverse ? first : second;
        return NavigationGuideStatus.Success;
    }

    private static NavigationGuideStatus TrySampleDirectedLeg(
        Vector3d sourceFootAnchor,
        Vector3d sourcePortal,
        Vector3d targetPortal,
        Vector3d targetFootAnchor,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        out Vector3d heading)
    {
        int traceIntervals = sourcePortal == targetPortal ? 2 : 3;
        if (!meter.TryConsumeCursorLegScans(1)
            || !meter.TryConsumeTraceIntervals(traceIntervals))
        {
            heading = Vector3d.Zero;
            return NavigationGuideStatus.BudgetExceeded;
        }
        return TrySampleDirectedLegUnchecked(
            sourceFootAnchor,
            sourcePortal,
            targetPortal,
            targetFootAnchor,
            actualFootPosition,
            out heading);
    }

    private static NavigationGuideStatus TrySampleDirectedLegUnchecked(
        Vector3d sourceFootAnchor,
        Vector3d sourcePortal,
        Vector3d targetPortal,
        Vector3d targetFootAnchor,
        Vector3d actualFootPosition,
        out Vector3d heading)
    {
        NavigationGuideStatus status = HasReachedOrPassed(
            sourceFootAnchor,
            sourcePortal,
            actualFootPosition,
            out bool passedSourcePortal);
        bool passedTargetPortal = false;
        if (status == NavigationGuideStatus.Success && passedSourcePortal)
        {
            status = HasReachedOrPassed(
                sourcePortal,
                targetPortal,
                actualFootPosition,
                out passedTargetPortal);
        }
        if (status != NavigationGuideStatus.Success)
            return FailDirectedLeg(status, out heading);
        if (!passedSourcePortal)
        {
            return TrySetHeadingUnchecked(actualFootPosition, sourcePortal, out heading);
        }
        return TrySetHeadingUnchecked(
            actualFootPosition,
            passedTargetPortal ? targetFootAnchor : targetPortal,
            out heading);
    }

    private static NavigationGuideStatus FailDirectedLeg(
        NavigationGuideStatus status,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        return status;
    }

    private static NavigationGuideStatus TrySampleExplicit(
        NavigationWorldGraph graph,
        NavigationExplicitConnectionRecord record,
        NavigationCellAddress sourceAddress,
        NavigationNodeState sourceState,
        GridCellPrism sourcePrism,
        NavigationNodeState targetState,
        GridCellPrism targetPrism,
        bool sourceContains,
        bool targetContains,
        Vector3d actualFootPosition,
        KinematicBodyShape shape,
        bool targetIsDestination,
        Fixed64 arrivalRadius,
        ref GuideSampleWorkMeter meter,
        out NavigationCellAddress nextSourceAddress,
        out Vector3d heading)
    {
        nextSourceAddress = sourceAddress;
        heading = Vector3d.Zero;
        NavigationConnection connection = record.Definition;
        NavigationPagedSequence<GridNavigationPortal>.Enumerator portals =
            record.NavigationPortals.GetEnumerator();
        NavigationGuideStatus firstPortalStatus = TryReadExplicitPortal(
            ref portals,
            shape,
            ref meter,
            out GridNavigationPortal incomingPortal,
            out Vector3d incomingSourceAnchor,
            out Vector3d incomingTargetAnchor);
        if (firstPortalStatus != NavigationGuideStatus.Success)
            return firstPortalStatus;

        bool zeroWitnessTargetBodyValid = connection.Witnesses.Count == 0
            && targetContains
            && GridCellGeometry.IsNavigationBodyAnchorValid(
                targetPrism,
                actualFootPosition,
                shape.Radius,
                shape.Height,
                incomingPortal);
        if (targetIsDestination
            && zeroWitnessTargetBodyValid
            && IsWithinArrivalRadius(actualFootPosition, targetState.FootAnchor, arrivalRadius))
        {
            return NavigationGuideStatus.Success;
        }

        bool sourceBodyValid = sourceContains
            && GridCellGeometry.IsNavigationBodyAnchorValid(
                sourcePrism,
                actualFootPosition,
                shape.Radius,
                shape.Height,
                incomingPortal);
        if (sourceBodyValid && !zeroWitnessTargetBodyValid)
        {
            NavigationGuideStatus progressStatus = HasReachedOrPassed(
                sourceState.FootAnchor,
                connection.EntryAnchor,
                actualFootPosition,
                out bool passedEntry);
            if (progressStatus != NavigationGuideStatus.Success)
                return progressStatus;
            if (!passedEntry)
            {
                return TrySetHeading(
                    actualFootPosition,
                    connection.EntryAnchor,
                    ref meter,
                    out heading);
            }
            if (connection.Witnesses.Count == 0)
            {
                return TrySampleDirectedLeg(
                    connection.EntryAnchor,
                    incomingSourceAnchor,
                    incomingTargetAnchor,
                    connection.ExitAnchor,
                    actualFootPosition,
                    ref meter,
                    out heading);
            }
            NavigationCellAddress firstAddress = connection.Witnesses[0];
            graph.TryGetNodeRef(firstAddress, out NavigationNodeRef firstRef);
            graph.TryGetNodeState(firstRef, out NavigationNodeState firstState);
            return TrySampleDirectedLeg(
                connection.EntryAnchor,
                incomingSourceAnchor,
                incomingTargetAnchor,
                firstState.FootAnchor,
                actualFootPosition,
                ref meter,
                out heading);
        }

        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            NavigationGuideStatus outgoingPortalStatus = TryReadExplicitPortal(
                ref portals,
                shape,
                ref meter,
                out GridNavigationPortal outgoingPortal,
                out Vector3d outgoingSourceAnchor,
                out Vector3d outgoingTargetAnchor);
            if (outgoingPortalStatus != NavigationGuideStatus.Success)
                return outgoingPortalStatus;
            NavigationCellAddress witnessAddress = connection.Witnesses[i];
            graph.TryGetSeamPrism(witnessAddress, out GridCellPrism witnessPrism);
            if (!meter.TryConsumePrismChecks(1))
                return NavigationGuideStatus.BudgetExceeded;
            if (!witnessPrism.Contains(actualFootPosition))
            {
                incomingPortal = outgoingPortal;
                incomingTargetAnchor = outgoingTargetAnchor;
                continue;
            }
            bool witnessBodyValid = GridCellGeometry.IsNavigationBodyAnchorValid(
                    witnessPrism,
                    actualFootPosition,
                    shape.Radius,
                    shape.Height,
                    outgoingPortal)
                || GridCellGeometry.IsNavigationBodyAnchorValid(
                    witnessPrism,
                    actualFootPosition,
                    shape.Radius,
                    shape.Height,
                    incomingPortal);
            if (!witnessBodyValid)
            {
                incomingPortal = outgoingPortal;
                incomingTargetAnchor = outgoingTargetAnchor;
                continue;
            }
            NavigationCellAddress nextAddress = i + 1 < connection.Witnesses.Count
                ? connection.Witnesses[i + 1]
                : record.Destination;
            NavigationNodeState nextState;
            if (nextAddress == record.Destination)
            {
                nextState = targetState;
            }
            else
            {
                graph.TryGetNodeRef(nextAddress, out NavigationNodeRef nextRef);
                graph.TryGetNodeState(nextRef, out nextState);
            }
            return TrySampleDirectedLeg(
                incomingTargetAnchor,
                outgoingSourceAnchor,
                outgoingTargetAnchor,
                nextAddress == record.Destination
                    ? connection.ExitAnchor
                    : nextState.FootAnchor,
                actualFootPosition,
                ref meter,
                out heading);
        }

        bool targetBodyValid = zeroWitnessTargetBodyValid
            || (connection.Witnesses.Count > 0
                && targetContains
                && GridCellGeometry.IsNavigationBodyAnchorValid(
                    targetPrism,
                    actualFootPosition,
                    shape.Radius,
                    shape.Height,
                    incomingPortal));
        if (targetBodyValid)
        {
            if (targetIsDestination
                && IsWithinArrivalRadius(actualFootPosition, targetState.FootAnchor, arrivalRadius))
            {
                return NavigationGuideStatus.Success;
            }
            NavigationGuideStatus progressStatus = HasReachedOrPassed(
                incomingTargetAnchor,
                connection.ExitAnchor,
                actualFootPosition,
                out bool passedExit);
            if (progressStatus != NavigationGuideStatus.Success)
                return progressStatus;
            if (!passedExit)
            {
                return TrySetHeading(
                    actualFootPosition,
                    connection.ExitAnchor,
                    ref meter,
                    out heading);
            }
            progressStatus = HasReachedOrPassed(
                connection.ExitAnchor,
                targetState.FootAnchor,
                actualFootPosition,
                out bool passedTarget);
            if (progressStatus != NavigationGuideStatus.Success)
                return progressStatus;
            if (!passedTarget)
            {
                return TrySetHeading(
                    actualFootPosition,
                    targetState.FootAnchor,
                    ref meter,
                    out heading);
            }
            if (!meter.TryConsumeCursorRebases(1))
                return NavigationGuideStatus.BudgetExceeded;
            nextSourceAddress = record.Destination;
            return NavigationGuideStatus.Success;
        }
        return NavigationGuideStatus.LocalRecoveryRequired;
    }

    private static NavigationGuideStatus TryReadExplicitPortal(
        ref NavigationPagedSequence<GridNavigationPortal>.Enumerator portals,
        KinematicBodyShape shape,
        ref GuideSampleWorkMeter meter,
        out GridNavigationPortal portal,
        out Vector3d sourcePortalAnchor,
        out Vector3d targetPortalAnchor)
    {
        portal = default;
        sourcePortalAnchor = default;
        targetPortalAnchor = default;
        if (!meter.TryConsumePortalChecks(1))
            return NavigationGuideStatus.BudgetExceeded;
        portals.MoveNext();
        portal = portals.Current;
        portal.TryResolveProfile(
            shape.Radius,
            shape.Height,
            out sourcePortalAnchor,
            out targetPortalAnchor);
        return NavigationGuideStatus.Success;
    }

    internal static NavigationGuideStatus TrySetHeading(
        Vector3d actualFootPosition,
        Vector3d target,
        ref GuideSampleWorkMeter meter,
        out Vector3d heading)
    {
        if (!meter.TryConsumeCursorLegScans(1)
            || !meter.TryConsumeTraceIntervals(1))
        {
            heading = Vector3d.Zero;
            return NavigationGuideStatus.BudgetExceeded;
        }
        return TrySetHeadingUnchecked(actualFootPosition, target, out heading);
    }

    private static NavigationGuideStatus TrySetHeadingUnchecked(
        Vector3d actualFootPosition,
        Vector3d target,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        if (!Vector3d.TrySubtract(target, actualFootPosition, out Vector3d delta))
            return NavigationGuideStatus.CostOverflow;
        if (delta != Vector3d.Zero)
            heading = delta.Normalized;
        return NavigationGuideStatus.Success;
    }

    internal static NavigationGuideStatus HasReachedOrPassed(
        Vector3d start,
        Vector3d end,
        Vector3d actual,
        out bool passed)
    {
        passed = false;
        if (start == end)
        {
            passed = true;
            return NavigationGuideStatus.Success;
        }
        if (!Vector3d.TrySubtract(end, start, out Vector3d direction)
            || !Vector3d.TrySubtract(actual, end, out Vector3d beyond)
            || !Vector3d.TryDot(beyond, direction, out Fixed64 projection))
        {
            return NavigationGuideStatus.CostOverflow;
        }
        passed = projection >= Fixed64.Zero;
        return NavigationGuideStatus.Success;
    }

    private static NavigationGuideStatus TryRebase(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        GridCoveredAddressCursor cursor,
        GridCoveredAddressGeneration[] generations,
        GridCoveredAddress[] output,
        out NavigationCellAddress rebased)
    {
        rebased = default;
        if (!meter.TryConsumeLocalRecoveryAttempts(1)
            || !meter.TryConsumeCursorRebases(1))
        {
            return NavigationGuideStatus.BudgetExceeded;
        }
        for (int i = 0; i < graph.MapCount; i++)
        {
            if (!meter.TryConsumeCurrentNodeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            bool foundGeneration = graph.TryGetCoveredAddressGeneration(
                i,
                out _,
                out generations[i]);
            System.Diagnostics.Debug.Assert(
                foundGeneration,
                "published graph instances are materialized and indexed one-to-one");
        }
        bool began = world.TryBeginCoveredAddresses(
            cursor,
            actualFootPosition,
            actualFootPosition,
            graph.MapCount);
        System.Diagnostics.Debug.Assert(began);

        int inputOrdinal = 0;
        bool hasCandidate = false;
        Fixed64 bestDistance = Fixed64.Zero;
        NavigationCellAddress best = default;
        while (true)
        {
            if (inputOrdinal < graph.MapCount)
            {
                int available = meter.GetCurrentNodeLookupAllowance();
                if (available == 0)
                    return NavigationGuideStatus.BudgetExceeded;
                GridCoveredAddressCursorStatus bindStatus = world.AdvanceCoveredAddresses(
                    cursor,
                    generations.AsSpan(inputOrdinal, graph.MapCount - inputOrdinal),
                    output,
                    lookupProbeLimit: available,
                    addressProbeLimit: 0,
                    outputLimit: 0,
                    out int lookupProbes,
                    out int addressProbes,
                    out int inputsConsumed,
                    out int outputCount);
                meter.TryConsumeCurrentNodeLookupProbes(
                    checked(lookupProbes + addressProbes + outputCount));
                inputOrdinal += inputsConsumed;
                if (bindStatus == GridCoveredAddressCursorStatus.Stale)
                    return NavigationGuideStatus.Stale;
                continue;
            }

            int outputBudget = meter.GetCurrentNodeLookupAllowance() > 0 ? 1 : 0;
            GridCoveredAddressCursorStatus flushStatus = world.AdvanceCoveredAddresses(
                cursor,
                ReadOnlySpan<GridCoveredAddressGeneration>.Empty,
                output,
                lookupProbeLimit: 0,
                addressProbeLimit: 0,
                outputLimit: outputBudget,
                out int flushLookups,
                out int flushAddresses,
                out _,
                out int flushed);
            meter.TryConsumeCurrentNodeLookupProbes(
                checked(flushLookups + flushAddresses + flushed));
            if (flushed != 0)
            {
                System.Diagnostics.Debug.Assert(
                    flushStatus != GridCoveredAddressCursorStatus.Stale,
                    "a stale covered-address cursor cannot publish output");
                NavigationGuideStatus candidateStatus = ConsiderCandidate(
                    graph,
                    payload,
                    medium,
                    actualFootPosition,
                    output[0],
                    ref meter,
                    ref hasCandidate,
                    ref bestDistance,
                    ref best);
                if (candidateStatus != NavigationGuideStatus.Success)
                    return candidateStatus;
                continue;
            }
            if (TryResolveRebaseCursorStatus(
                    flushStatus,
                    hasCandidate,
                    best,
                    out NavigationGuideStatus flushResult,
                    out rebased))
            {
                return flushResult;
            }

            int addressBudget = meter.GetCurrentNodeLookupAllowance();
            if (addressBudget == 0)
                return NavigationGuideStatus.BudgetExceeded;
            GridCoveredAddressCursorStatus addressStatus = world.AdvanceCoveredAddresses(
                cursor,
                ReadOnlySpan<GridCoveredAddressGeneration>.Empty,
                output,
                lookupProbeLimit: 0,
                addressProbeLimit: addressBudget,
                outputLimit: 0,
                out int addressLookups,
                out int enumeratedAddresses,
                out _,
                out int addressOutputs);
            meter.TryConsumeCurrentNodeLookupProbes(
                checked(addressLookups + enumeratedAddresses + addressOutputs));
            if (TryResolveRebaseCursorStatus(
                    addressStatus,
                    hasCandidate,
                    best,
                    out NavigationGuideStatus addressResult,
                    out rebased))
            {
                return addressResult;
            }
        }
    }

    internal static bool TryResolveRebaseCursorStatus(
        GridCoveredAddressCursorStatus cursorStatus,
        bool hasCandidate,
        NavigationCellAddress best,
        out NavigationGuideStatus status,
        out NavigationCellAddress rebased)
    {
        if (cursorStatus == GridCoveredAddressCursorStatus.Stale)
        {
            status = NavigationGuideStatus.Stale;
            rebased = default;
            return true;
        }
        if (cursorStatus == GridCoveredAddressCursorStatus.Complete)
        {
            status = hasCandidate
                ? NavigationGuideStatus.Success
                : NavigationGuideStatus.LocalRecoveryRequired;
            rebased = best;
            return true;
        }
        status = NavigationGuideStatus.Success;
        rebased = default;
        return false;
    }

    private static NavigationGuideStatus ConsiderCandidate(
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        GridCoveredAddress candidate,
        ref GuideSampleWorkMeter meter,
        ref bool hasCandidate,
        ref Fixed64 bestDistance,
        ref NavigationCellAddress best)
    {
        // Covered generations originate from this graph's configuration index.
        graph.TryGetMapId(candidate.ConfigurationKey, out string mapId);
        var address = new NavigationCellAddress(mapId, candidate.VoxelIndex);
        return ConsiderCandidateAddress(
            graph,
            payload,
            medium,
            actualFootPosition,
            address,
            ref meter,
            ref hasCandidate,
            ref bestDistance,
            ref best);
    }

    internal static NavigationGuideStatus ConsiderCandidateAddress(
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        TraversalMedium medium,
        Vector3d actualFootPosition,
        NavigationCellAddress address,
        ref GuideSampleWorkMeter meter,
        ref bool hasCandidate,
        ref Fixed64 bestDistance,
        ref NavigationCellAddress best)
    {
        if (!meter.TryConsumePrismChecks(1))
            return NavigationGuideStatus.BudgetExceeded;
        bool foundPrism = graph.TryGetSeamPrism(address, out GridCellPrism prism);
        System.Diagnostics.Debug.Assert(foundPrism,
            "covered-address candidates retain their graph-owned configuration prism");
        if (!prism.Contains(actualFootPosition)
            || !graph.TryGetNodeRef(address, out NavigationNodeRef node)
            || !graph.TryGetNodeState(node, medium, out NavigationNodeState state)
            || !state.IsPresent)
        {
            return NavigationGuideStatus.Success;
        }
        NavigationFlowNodeLookupStatus lookup = TryGetNode(
            payload,
            address,
            medium,
            ref meter,
            out _);
        if (lookup != NavigationFlowNodeLookupStatus.Success)
        {
            return MapNodeLookupStatus(lookup, required: false);
        }
        if (!Vector3d.TryGetDistance(
                actualFootPosition,
                state.FootAnchor,
                out Fixed64 distance))
        {
            return NavigationGuideStatus.CostOverflow;
        }
        SelectNearestCandidate(
            distance,
            address,
            ref hasCandidate,
            ref bestDistance,
            ref best);
        return NavigationGuideStatus.Success;
    }

    internal static void SelectNearestCandidate(
        Fixed64 candidateDistance,
        NavigationCellAddress candidate,
        ref bool hasCandidate,
        ref Fixed64 bestDistance,
        ref NavigationCellAddress best)
    {
        if (!hasCandidate
            || candidateDistance < bestDistance
            || (candidateDistance == bestDistance && candidate.CompareTo(best) < 0))
        {
            hasCandidate = true;
            bestDistance = candidateDistance;
            best = candidate;
        }
    }
}
