//=======================================================================
// NavigationWorkMeter.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Tracks deterministic work consumed by one complete navigation query.</summary>
internal sealed class NavigationWorkMeter
{
    private NavigationWorkBudget _budget;
    private int _lookupReservationFloor;
    private int _guideLookupAndCoveredLimit;
    private int _guideEdgeAndConnectionLimit;
    private int _guidePortalLimit;
    private int _guidePrismLimit;

    internal NavigationWorkMeter(NavigationWorkBudget budget) => Reset(budget);

    internal int LookupProbes { get; private set; }

    internal int EndpointCandidates { get; private set; }

    internal int ExpandedNodes { get; private set; }

    internal int EvaluatedEdges { get; private set; }

    internal int ConnectionLegs { get; private set; }

    internal int TransitionCandidates { get; private set; }

    internal int TransitionPairs { get; private set; }

    internal int PrimaryVolumeCandidates { get; private set; }

    internal int ShortcutVolumeCandidates { get; private set; }

    internal long VolumeUnionChecks { get; private set; }

    internal int SuccessfulDependencyMerges { get; private set; }

    internal int TraceIntervals { get; private set; }

    internal int CoveredVoxelIntervals { get; private set; }

    internal int SimplificationRays { get; private set; }

    internal int RemainingLookupProbes => IsGuideSampleBridge
        ? Math.Min(
            _budget.MaxLookupProbes - LookupProbes - _lookupReservationFloor,
            _guideLookupAndCoveredLimit - LookupProbes - CoveredVoxelIntervals)
        : _budget.MaxLookupProbes - LookupProbes - _lookupReservationFloor;

    internal int RemainingEndpointCandidates =>
        _budget.MaxEndpointCandidates - EndpointCandidates;

    internal int RemainingExpandedNodes => _budget.MaxExpandedNodes - ExpandedNodes;

    internal int RemainingEvaluatedEdges => IsGuideSampleBridge
        ? Math.Min(
            _budget.MaxEvaluatedEdges - EvaluatedEdges,
            _guideEdgeAndConnectionLimit - EvaluatedEdges - ConnectionLegs)
        : _budget.MaxEvaluatedEdges - EvaluatedEdges;

    internal int RemainingConnectionLegs => IsGuideSampleBridge
        ? Math.Min(
            _budget.MaxConnectionLegs - ConnectionLegs,
            _guideEdgeAndConnectionLimit - EvaluatedEdges - ConnectionLegs)
        : _budget.MaxConnectionLegs - ConnectionLegs;

    internal int RemainingTransitionCandidates =>
        _budget.MaxTransitionCandidates - TransitionCandidates;

    internal int RemainingTransitionPairs => _budget.MaxTransitionPairs - TransitionPairs;

    internal int RemainingTraceIntervals => _budget.MaxTraceIntervals - TraceIntervals;

    internal int RemainingCoveredVoxelIntervals => IsGuideSampleBridge
        ? Math.Min(
            _budget.MaxCoveredVoxelIntervals - CoveredVoxelIntervals,
            _guideLookupAndCoveredLimit - LookupProbes - CoveredVoxelIntervals)
        : _budget.MaxCoveredVoxelIntervals - CoveredVoxelIntervals;

    internal long RemainingGridCandidateWork => IsGuideSampleBridge
        ? (long)_guideLookupAndCoveredLimit - LookupProbes - CoveredVoxelIntervals
        : checked((long)RemainingLookupProbes + RemainingCoveredVoxelIntervals);

    internal bool IsGuideSampleBridge => _guideLookupAndCoveredLimit >= 0;

    internal int GuidePortalChecks { get; private set; }

    internal int GuidePrismChecks { get; private set; }

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

    internal void RecordVolumeCandidate(bool isPrimary)
    {
        if (isPrimary)
            PrimaryVolumeCandidates++;
        else
            ShortcutVolumeCandidates++;
    }

    internal void RecordVolumeUnionCheck() => VolumeUnionChecks++;

    internal void RecordSuccessfulDependencyMerge() =>
        SuccessfulDependencyMerges++;

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

    internal bool TryConsumeGuidePortalChecks(int count)
    {
        if (!IsGuideSampleBridge
            || count < 0
            || count > _guidePortalLimit - GuidePortalChecks)
        {
            return false;
        }
        GuidePortalChecks += count;
        return true;
    }

    internal bool TryConsumeGuidePrismChecks(int count)
    {
        if (!IsGuideSampleBridge
            || count < 0
            || count > _guidePrismLimit - GuidePrismChecks)
        {
            return false;
        }
        GuidePrismChecks += count;
        return true;
    }

    internal void ResetForGuideSample(
        int lookupAndCoveredLimit,
        int edgeAndConnectionLimit,
        int portalLimit,
        int prismLimit,
        int traceIntervalLimit)
    {
        var budget = new NavigationWorkBudget(
            lookupAndCoveredLimit,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            edgeAndConnectionLimit,
            edgeAndConnectionLimit,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            traceIntervalLimit,
            lookupAndCoveredLimit,
            maxSimplificationRays: 0);
        Reset(budget);
        _guideLookupAndCoveredLimit = lookupAndCoveredLimit;
        _guideEdgeAndConnectionLimit = edgeAndConnectionLimit;
        _guidePortalLimit = portalLimit;
        _guidePrismLimit = prismLimit;
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
        PrimaryVolumeCandidates = 0;
        ShortcutVolumeCandidates = 0;
        VolumeUnionChecks = 0;
        SuccessfulDependencyMerges = 0;
        TraceIntervals = 0;
        CoveredVoxelIntervals = 0;
        SimplificationRays = 0;
        GuidePortalChecks = 0;
        GuidePrismChecks = 0;
        _lookupReservationFloor = 0;
        _guideLookupAndCoveredLimit = -1;
        _guideEdgeAndConnectionLimit = -1;
        _guidePortalLimit = 0;
        _guidePrismLimit = 0;
    }
}
