//=======================================================================
// NavigationWorkMeter.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Tracks deterministic work consumed by one complete navigation query.</summary>
internal sealed class NavigationWorkMeter
{
    private NavigationWorkBudget _budget;
    private int _lookupReservationFloor;

    internal NavigationWorkMeter(NavigationWorkBudget budget) => Reset(budget);

    internal int LookupProbes { get; private set; }

    internal int EndpointCandidates { get; private set; }

    internal int ExpandedNodes { get; private set; }

    internal int EvaluatedEdges { get; private set; }

    internal int ConnectionLegs { get; private set; }

    internal int TransitionCandidates { get; private set; }

    internal int TransitionPairs { get; private set; }

    internal int TraceIntervals { get; private set; }

    internal int CoveredVoxelIntervals { get; private set; }

    internal int SimplificationRays { get; private set; }

    internal int RemainingLookupProbes =>
        _budget.MaxLookupProbes - LookupProbes - _lookupReservationFloor;

    internal int RemainingEndpointCandidates =>
        _budget.MaxEndpointCandidates - EndpointCandidates;

    internal int RemainingExpandedNodes => _budget.MaxExpandedNodes - ExpandedNodes;

    internal int RemainingEvaluatedEdges => _budget.MaxEvaluatedEdges - EvaluatedEdges;

    internal int RemainingConnectionLegs => _budget.MaxConnectionLegs - ConnectionLegs;

    internal int RemainingTransitionCandidates =>
        _budget.MaxTransitionCandidates - TransitionCandidates;

    internal int RemainingTransitionPairs => _budget.MaxTransitionPairs - TransitionPairs;

    internal int RemainingTraceIntervals => _budget.MaxTraceIntervals - TraceIntervals;

    internal int RemainingCoveredVoxelIntervals =>
        _budget.MaxCoveredVoxelIntervals - CoveredVoxelIntervals;

    internal int RemainingSimplificationRays =>
        _budget.MaxSimplificationRays - SimplificationRays;

    internal bool TryConsumeLookupProbes(int count)
    {
        if (count < 0 || count > RemainingLookupProbes)
            return false;
        LookupProbes += count;
        return true;
    }

    internal bool TrySetLookupReservationFloor(int count)
    {
        if (count < 0 || count > _budget.MaxLookupProbes - LookupProbes)
            return false;
        _lookupReservationFloor = count;
        return true;
    }

    internal void ReleaseLookupReservationFloor() => _lookupReservationFloor = 0;

    internal bool TryConsumeEndpointCandidates(int count)
    {
        if (count < 0 || count > RemainingEndpointCandidates)
            return false;
        EndpointCandidates += count;
        return true;
    }

    internal bool TryConsumeExpandedNodes(int count)
    {
        if (count < 0 || count > RemainingExpandedNodes)
            return false;
        ExpandedNodes += count;
        return true;
    }

    internal bool TryConsumeEvaluatedEdges(int count)
    {
        if (count < 0 || count > RemainingEvaluatedEdges)
            return false;
        EvaluatedEdges += count;
        return true;
    }

    internal bool TryConsumeConnectionLegs(int count)
    {
        if (count < 0 || count > RemainingConnectionLegs)
            return false;
        ConnectionLegs += count;
        return true;
    }

    internal bool TryConsumeTransitionCandidates(int count)
    {
        if (count < 0 || count > RemainingTransitionCandidates)
            return false;
        TransitionCandidates += count;
        return true;
    }

    internal bool TryConsumeTransitionPairs(int count)
    {
        if (count < 0 || count > RemainingTransitionPairs)
            return false;
        TransitionPairs += count;
        return true;
    }

    internal bool TryConsumeTraceIntervals(int count)
    {
        if (count < 0 || count > RemainingTraceIntervals)
            return false;
        TraceIntervals += count;
        return true;
    }

    internal bool TryConsumeCoveredVoxelIntervals(int count)
    {
        if (count < 0 || count > RemainingCoveredVoxelIntervals)
            return false;
        CoveredVoxelIntervals += count;
        return true;
    }

    internal bool TryConsumeSimplificationRays(int count)
    {
        if (count < 0 || count > RemainingSimplificationRays)
            return false;
        SimplificationRays += count;
        return true;
    }

    internal void Reset(NavigationWorkBudget budget)
    {
        _budget = budget;
        LookupProbes = 0;
        EndpointCandidates = 0;
        ExpandedNodes = 0;
        EvaluatedEdges = 0;
        ConnectionLegs = 0;
        TransitionCandidates = 0;
        TransitionPairs = 0;
        TraceIntervals = 0;
        CoveredVoxelIntervals = 0;
        SimplificationRays = 0;
        _lookupReservationFloor = 0;
    }
}
