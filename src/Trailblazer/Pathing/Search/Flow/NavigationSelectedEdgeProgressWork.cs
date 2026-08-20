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
internal static class NavigationSelectedEdgeProgressWork
{
    private enum NodeLookupStatus : byte
    {
        Success,
        NotFound,
        BudgetExceeded
    }

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
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress sourceAddress,
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        GridCoveredAddressCursor coveredAddressCursor,
        GridCoveredAddressGeneration[] coveredAddressGenerations,
        GridCoveredAddress[] coveredAddressOutput,
        NavigationImmediateRayWorkspace immediateRayWorkspace,
        out NavigationCellAddress nextSourceAddress,
        out Vector3d heading)
    {
        nextSourceAddress = sourceAddress;
        heading = Vector3d.Zero;
        NavigationCellAddress currentSource = sourceAddress;
        bool allowRecovery = true;
        while (true)
        {
            NodeLookupStatus lookup = TryGetNode(
                payload,
                currentSource,
                ref meter,
                out NavigationFlowFieldNode node);
            if (lookup != NodeLookupStatus.Success)
            {
                return lookup == NodeLookupStatus.BudgetExceeded
                    ? NavigationGuideStatus.BudgetExceeded
                    : NavigationGuideStatus.Stale;
            }
            if (!graph.TryGetNodeRef(currentSource, out NavigationNodeRef sourceRef)
                || !graph.TryGetNodeState(sourceRef, out NavigationNodeState sourceState)
                || !graph.TryGetSeamPrism(currentSource, out GridCellPrism sourcePrism))
            {
                return NavigationGuideStatus.Stale;
            }
            if (!meter.TryConsumePrismChecks(1))
                return NavigationGuideStatus.BudgetExceeded;
            bool sourceContains = sourcePrism.Contains(actualFootPosition);
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
            if (!graph.TryGetNodeState(edge.Target, out NavigationNodeState targetState)
                || !graph.TryGetSeamPrism(
                    node.SelectedEdge.Target,
                    out GridCellPrism targetPrism))
            {
                return NavigationGuideStatus.Stale;
            }
            if (!meter.TryConsumePrismChecks(1))
                return NavigationGuideStatus.BudgetExceeded;
            bool targetContains = targetPrism.Contains(actualFootPosition);

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
            if (status == NavigationSurfaceEdgeAdvanceStatus.Blocked)
            {
                edge = default;
                return NavigationGuideStatus.BudgetExceeded;
            }
            if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (status == NavigationSurfaceEdgeAdvanceStatus.Complete)
                break;
            if (edges.CurrentOrdinal != selected.CanonicalOutgoingOrdinal)
                continue;
            edge = edges.Current;
            return graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress target)
                && target == selected.Target
                    ? NavigationGuideStatus.Success
                    : NavigationGuideStatus.Stale;
        }
        edge = default;
        return NavigationGuideStatus.Stale;
    }

    private static NodeLookupStatus TryGetNode(
        NavigationFlowFieldPayload payload,
        NavigationCellAddress address,
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
                return NodeLookupStatus.BudgetExceeded;
            }
            int middle = low + ((high - low) >> 1);
            NavigationFlowFieldNode candidate =
                payload.Nodes[payload.AddressLookupOrdinals[middle]];
            int comparison = candidate.Address.CompareTo(address);
            if (comparison == 0)
            {
                node = candidate;
                return NodeLookupStatus.Success;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        node = default;
        return NodeLookupStatus.NotFound;
    }

    private static NavigationGuideStatus TryAdvanceRecovery(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
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
        if (!graph.AreaCatalog.TryGet(
                payload.Key.AreaPolicy,
                out NavigationAreaPolicy? areaPolicy)
            || areaPolicy == null)
        {
            return NavigationGuideStatus.Stale;
        }
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
                    payload.Key.Traversal,
                    payload.Key.AllowTransitions,
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
                    return MapRayStatus(status);
                return TrySetHeadingUnchecked(
                    actualFootPosition,
                    target.Position,
                    out heading);
            }
        }
    }

    private static NavigationGuideStatus MapRayStatus(
        NavigationRayStatus status) => status switch
        {
            NavigationRayStatus.Blocked => NavigationGuideStatus.LocalRecoveryRequired,
            NavigationRayStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
            NavigationRayStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
            NavigationRayStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
            NavigationRayStatus.Stale => NavigationGuideStatus.Stale,
            _ => NavigationGuideStatus.Stale
        };

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
            if (!edge.NativePortal.TryTranslate(sourceState.Center, out resolvedPortal))
                return NavigationGuideStatus.CostOverflow;
        }
        else
        {
            NavigationAutomaticSeamRef seam = edge.AutomaticSeam;
            if (seam.Pair == null)
                return NavigationGuideStatus.Stale;
            resolvedPortal = seam.Portal;
            reverse = seam.IsReverse;
        }
        if (!resolvedPortal.IsValid
            || shape.Radius > resolvedPortal.MaximumHorizontalRadius
            || shape.Height > resolvedPortal.MaximumBodyHeight)
        {
            return NavigationGuideStatus.Stale;
        }
        if (!resolvedPortal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d first,
                out Vector3d second))
        {
            return NavigationGuideStatus.CostOverflow;
        }
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
        if (status != NavigationGuideStatus.Success)
        {
            heading = Vector3d.Zero;
            return status;
        }
        if (!passedSourcePortal)
        {
            return TrySetHeadingUnchecked(actualFootPosition, sourcePortal, out heading);
        }
        status = HasReachedOrPassed(
            sourcePortal,
            targetPortal,
            actualFootPosition,
            out bool passedTargetPortal);
        if (status != NavigationGuideStatus.Success)
        {
            heading = Vector3d.Zero;
            return status;
        }
        return TrySetHeadingUnchecked(
            actualFootPosition,
            passedTargetPortal ? targetFootAnchor : targetPortal,
            out heading);
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
        ref GuideSampleWorkMeter meter,
        out NavigationCellAddress nextSourceAddress,
        out Vector3d heading)
    {
        nextSourceAddress = sourceAddress;
        heading = Vector3d.Zero;
        NavigationConnection connection = record.Definition;
        int portalCount = connection.Witnesses.Count + 1;
        if (record.NavigationPortals.Count != portalCount)
        {
            return NavigationGuideStatus.Stale;
        }
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

        bool sourceBodyValid = sourceContains
            && GridCellGeometry.IsNavigationBodyAnchorValid(
                sourcePrism,
                actualFootPosition,
                shape.Radius,
                shape.Height,
                incomingPortal);
        if (sourceBodyValid)
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
            if (!graph.TryGetNodeRef(firstAddress, out NavigationNodeRef firstRef)
                || !graph.TryGetNodeState(firstRef, out NavigationNodeState firstState))
            {
                return NavigationGuideStatus.Stale;
            }
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
            if (!graph.TryGetSeamPrism(witnessAddress, out GridCellPrism witnessPrism))
                return NavigationGuideStatus.Stale;
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
            else if (!graph.TryGetNodeRef(nextAddress, out NavigationNodeRef nextRef)
                || !graph.TryGetNodeState(nextRef, out nextState))
            {
                return NavigationGuideStatus.Stale;
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

        bool targetBodyValid = targetContains
            && GridCellGeometry.IsNavigationBodyAnchorValid(
                targetPrism,
                actualFootPosition,
                shape.Radius,
                shape.Height,
                incomingPortal);
        if (targetBodyValid)
        {
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
        if (!portals.MoveNext())
            return NavigationGuideStatus.Stale;
        portal = portals.Current;
        if (!portal.IsValid
            || shape.Radius > portal.MaximumHorizontalRadius
            || shape.Height > portal.MaximumBodyHeight)
        {
            return NavigationGuideStatus.Stale;
        }
        return portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out sourcePortalAnchor,
                out targetPortalAnchor)
            ? NavigationGuideStatus.Success
            : NavigationGuideStatus.CostOverflow;
    }

    private static NavigationGuideStatus TrySetHeading(
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

    private static NavigationGuideStatus HasReachedOrPassed(
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
        if (graph.MapCount > generations.Length)
            return NavigationGuideStatus.CapacityExceeded;
        for (int i = 0; i < graph.MapCount; i++)
        {
            if (!meter.TryConsumeCurrentNodeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            if (!graph.TryGetCoveredAddressGeneration(
                    i,
                    out _,
                    out generations[i]))
            {
                return NavigationGuideStatus.Stale;
            }
        }
        if (!world.TryBeginCoveredAddresses(
                cursor,
                actualFootPosition,
                actualFootPosition,
                graph.MapCount))
        {
            return NavigationGuideStatus.CapacityExceeded;
        }

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
                if (!meter.TryConsumeCurrentNodeLookupProbes(
                        checked(lookupProbes + addressProbes + outputCount)))
                {
                    return NavigationGuideStatus.BudgetExceeded;
                }
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
            if (!meter.TryConsumeCurrentNodeLookupProbes(
                    checked(flushLookups + flushAddresses + flushed)))
            {
                return NavigationGuideStatus.BudgetExceeded;
            }
            if (flushStatus == GridCoveredAddressCursorStatus.Stale)
                return NavigationGuideStatus.Stale;
            if (flushed != 0)
            {
                NavigationGuideStatus candidateStatus = ConsiderCandidate(
                    graph,
                    payload,
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
            if (flushStatus == GridCoveredAddressCursorStatus.Complete)
            {
                if (!hasCandidate)
                    return NavigationGuideStatus.LocalRecoveryRequired;
                rebased = best;
                return NavigationGuideStatus.Success;
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
            if (!meter.TryConsumeCurrentNodeLookupProbes(
                    checked(addressLookups + enumeratedAddresses + addressOutputs)))
            {
                return NavigationGuideStatus.BudgetExceeded;
            }
            if (addressStatus == GridCoveredAddressCursorStatus.Stale)
                return NavigationGuideStatus.Stale;
            if (addressStatus == GridCoveredAddressCursorStatus.Complete)
            {
                if (!hasCandidate)
                    return NavigationGuideStatus.LocalRecoveryRequired;
                rebased = best;
                return NavigationGuideStatus.Success;
            }
        }
    }

    private static NavigationGuideStatus ConsiderCandidate(
        NavigationWorldGraph graph,
        NavigationFlowFieldPayload payload,
        Vector3d actualFootPosition,
        GridCoveredAddress candidate,
        ref GuideSampleWorkMeter meter,
        ref bool hasCandidate,
        ref Fixed64 bestDistance,
        ref NavigationCellAddress best)
    {
        if (!graph.TryGetMapId(candidate.ConfigurationKey, out string mapId))
            return NavigationGuideStatus.Success;
        var address = new NavigationCellAddress(mapId, candidate.VoxelIndex);
        if (!meter.TryConsumePrismChecks(1))
            return NavigationGuideStatus.BudgetExceeded;
        if (!graph.TryGetSeamPrism(address, out GridCellPrism prism)
            || !prism.Contains(actualFootPosition))
        {
            return NavigationGuideStatus.Success;
        }
        if (!graph.TryGetNodeRef(address, out NavigationNodeRef node)
            || !graph.TryGetNodeState(node, out NavigationNodeState state)
            || !state.IsPresent)
        {
            return NavigationGuideStatus.Success;
        }
        NodeLookupStatus lookup = TryGetNode(payload, address, ref meter, out _);
        if (lookup != NodeLookupStatus.Success)
        {
            return lookup == NodeLookupStatus.BudgetExceeded
                ? NavigationGuideStatus.BudgetExceeded
                : NavigationGuideStatus.Success;
        }
        if (!Vector3d.TryGetDistance(
                actualFootPosition,
                state.FootAnchor,
                out Fixed64 distance))
        {
            return NavigationGuideStatus.CostOverflow;
        }
        if (!hasCandidate
            || distance < bestDistance
            || (distance == bestDistance && address.CompareTo(best) < 0))
        {
            hasCandidate = true;
            bestDistance = distance;
            best = address;
        }
        return NavigationGuideStatus.Success;
    }
}
