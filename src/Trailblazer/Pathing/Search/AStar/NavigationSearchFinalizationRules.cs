//=======================================================================
// NavigationSearchFinalizationRules.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

internal static class NavigationSearchFinalizationRules
{
    internal static bool IsEpochCurrent(
        bool epochRequired,
        ulong expectedEpoch,
        ulong currentEpoch) =>
        !epochRequired || currentEpoch == expectedEpoch;

    internal static NavigationEndpointResolutionStatus ResolveEndpointStatus(
        NavigationEndpointResolutionStatus status,
        bool dependenciesWereRead,
        bool dependenciesAreCurrent,
        bool epochIsCurrent) =>
        !epochIsCurrent || (dependenciesWereRead && !dependenciesAreCurrent)
            ? NavigationEndpointResolutionStatus.Stale
            : status;

    internal static NavigationEndpointResolutionStatus ResolveEndpointCursorStatus(
        bool hasResult) =>
        hasResult
            ? NavigationEndpointResolutionStatus.Success
            : NavigationEndpointResolutionStatus.InvalidEndpoint;

    internal static bool TryResolveTraversalTerminalStatus(
        NavigationTraversalEdgeAdvanceStatus traversalStatus,
        out NavigationSurfaceAStarStatus terminalStatus)
    {
        switch (traversalStatus)
        {
            case NavigationTraversalEdgeAdvanceStatus.BudgetExceeded:
                terminalStatus = NavigationSurfaceAStarStatus.BudgetExceeded;
                return true;
            case NavigationTraversalEdgeAdvanceStatus.CapacityExceeded:
                terminalStatus = NavigationSurfaceAStarStatus.CapacityExceeded;
                return true;
            case NavigationTraversalEdgeAdvanceStatus.CostOverflow:
                terminalStatus = NavigationSurfaceAStarStatus.CostOverflow;
                return true;
            case NavigationTraversalEdgeAdvanceStatus.Stale:
                terminalStatus = NavigationSurfaceAStarStatus.Stale;
                return true;
            default:
                terminalStatus = NavigationSurfaceAStarStatus.Pending;
                return false;
        }
    }

    internal static NavigationTraversalEdgeAdvanceStatus ResolveTraversalEpochStatus(
        NavigationTraversalEdgeAdvanceStatus traversalStatus,
        bool epochIsCurrent) =>
        epochIsCurrent
            ? traversalStatus
            : NavigationTraversalEdgeAdvanceStatus.Stale;

    internal static NavigationSurfaceAStarStatus ResolveAStarEpochStatus(
        NavigationSurfaceAStarStatus status,
        bool epochIsCurrent) =>
        epochIsCurrent
            ? status
            : NavigationSurfaceAStarStatus.Stale;

    internal static NavigationSurfaceAStarStatus ResolveBlockedTraversalStatus(
        bool requiresConnectionProgress,
        int remainingConnectionLegs,
        int remainingEvaluatedEdges) =>
        (requiresConnectionProgress
            ? remainingConnectionLegs
            : remainingEvaluatedEdges) == 0
                ? NavigationSurfaceAStarStatus.BudgetExceeded
                : NavigationSurfaceAStarStatus.Pending;

    internal static NavigationSurfaceAStarStatus ResolveIncompleteLookupStatus(
        int remainingLookupProbes) =>
        remainingLookupProbes == 0
            ? NavigationSurfaceAStarStatus.BudgetExceeded
            : NavigationSurfaceAStarStatus.Pending;

    internal static bool ShouldConsumeTraversalSurfacePoint(
        bool hasSurfacePoint,
        NavigationTraversalEdgeAdvanceStatus traversalStatus) =>
        hasSurfacePoint
        && traversalStatus != NavigationTraversalEdgeAdvanceStatus.Stale;

    internal static bool TryGetEuclideanHeuristic(
        bool hasFootAnchor,
        Vector3d footAnchor,
        Vector3d targetFootAnchor,
        out Fixed64 heuristic)
    {
        if (hasFootAnchor
            && NavigationDistanceMath.TryFloor(
                footAnchor,
                targetFootAnchor,
                out heuristic))
        {
            return true;
        }
        heuristic = Fixed64.Zero;
        return false;
    }

    internal static bool ShouldAcceptSimplificationRay(
        NavigationCellAddress actualEnd,
        NavigationCellAddress candidateEnd,
        Fixed64 traversalCost,
        Fixed64 rawCost) =>
        actualEnd == candidateEnd
        && traversalCost <= rawCost;

    internal static bool TryAdmitSimplification(
        int pathNodeCount,
        int remainingSimplificationRays,
        int componentCount,
        int pageCount,
        NavigationWorkMeter meter,
        out int lookupReservation)
    {
        lookupReservation = 0;
        if (pathNodeCount < 2)
            return false;
        if (remainingSimplificationRays == 0)
            return false;
        return TrySetFinalizationLookupReservation(
            componentCount,
            pageCount,
            meter,
            out lookupReservation);
    }

    internal static bool TryPrepareDependencyMerge(
        bool epochIsCurrent,
        NavigationDependencyWorkspace target,
        NavigationDependencyWorkspace source,
        NavigationWorkMeter meter,
        int priorLookupReservation,
        out int enlargedLookupReservation)
    {
        enlargedLookupReservation = 0;
        if (!epochIsCurrent)
            return false;
        if (!target.TryCountMissing(
                source,
                meter,
                out int missingComponents,
                out int missingPages))
        {
            return false;
        }
        if (!target.CanFit(missingComponents, missingPages))
            return false;
        if (!TrySetFinalizationLookupReservation(
                target.ComponentCount + missingComponents,
                target.PageCount + missingPages,
                meter,
                out enlargedLookupReservation))
        {
            return false;
        }
        int appendProbeCount = source.ComponentCount + source.PageCount;
        if (meter.TryConsumeLookupProbes(appendProbeCount))
            return true;
        meter.TrySetLookupReservationFloor(priorLookupReservation);
        return false;
    }

    internal static bool TryCombineLookupReservation(
        int comparisonCount,
        int componentCount,
        int pageCount,
        out int reservation)
    {
        try
        {
            reservation = checked(comparisonCount + componentCount + pageCount);
            return true;
        }
        catch (OverflowException)
        {
            reservation = 0;
            return false;
        }
    }

    internal static bool TryGetFinalizationLookupReservation(
        int componentCount,
        int pageCount,
        out int reservation)
    {
        try
        {
            int comparisonCount = NavigationDependencySortWork.GetMaximumComparisonCount(
                componentCount,
                pageCount);
            return TryCombineLookupReservation(
                comparisonCount,
                componentCount,
                pageCount,
                out reservation);
        }
        catch (OverflowException)
        {
            reservation = 0;
            return false;
        }
    }

    private static bool TrySetFinalizationLookupReservation(
        int componentCount,
        int pageCount,
        NavigationWorkMeter meter,
        out int reservation) =>
        TryGetFinalizationLookupReservation(
            componentCount,
            pageCount,
            out reservation)
        && meter.TrySetLookupReservationFloor(reservation);
}
