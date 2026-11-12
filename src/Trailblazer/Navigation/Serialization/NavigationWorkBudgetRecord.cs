//=======================================================================
// NavigationWorkBudgetRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>Records budget fields inline and lets the caller choose when to validate them.</summary>
internal struct NavigationWorkBudgetRecord
{
    private int _maxLookupProbes;
    private int _maxEndpointCandidates;
    private int _maxExpandedNodes;
    private int _maxEvaluatedEdges;
    private int _maxConnectionLegs;
    private int _maxTransitionCandidates;
    private int _maxTransitionPairs;
    private int _maxStagedLegAttempts;
    private int _maxTraceIntervals;
    private int _maxCoveredVoxelIntervals;
    private int _maxSimplificationRays;

    internal NavigationWorkBudgetRecord(NavigationWorkBudget budget)
    {
        _maxLookupProbes = budget.MaxLookupProbes;
        _maxEndpointCandidates = budget.MaxEndpointCandidates;
        _maxExpandedNodes = budget.MaxExpandedNodes;
        _maxEvaluatedEdges = budget.MaxEvaluatedEdges;
        _maxConnectionLegs = budget.MaxConnectionLegs;
        _maxTransitionCandidates = budget.MaxTransitionCandidates;
        _maxTransitionPairs = budget.MaxTransitionPairs;
        _maxStagedLegAttempts = budget.MaxStagedLegAttempts;
        _maxTraceIntervals = budget.MaxTraceIntervals;
        _maxCoveredVoxelIntervals = budget.MaxCoveredVoxelIntervals;
        _maxSimplificationRays = budget.MaxSimplificationRays;
    }

    internal readonly NavigationWorkBudget CreateBudget() =>
        new(_maxLookupProbes, _maxEndpointCandidates, _maxExpandedNodes, _maxEvaluatedEdges,
            _maxConnectionLegs, _maxTransitionCandidates, _maxTransitionPairs, _maxStagedLegAttempts,
            _maxTraceIntervals, _maxCoveredVoxelIntervals, _maxSimplificationRays);

    internal void Look(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _maxLookupProbes, "MaxLookupProbes", 0);
        RecordValues.Look(chronicler, ref _maxEndpointCandidates, "MaxEndpointCandidates", 0);
        RecordValues.Look(chronicler, ref _maxExpandedNodes, "MaxExpandedNodes", 0);
        RecordValues.Look(chronicler, ref _maxEvaluatedEdges, "MaxEvaluatedEdges", 0);
        RecordValues.Look(chronicler, ref _maxConnectionLegs, "MaxConnectionLegs", 0);
        RecordValues.Look(chronicler, ref _maxTransitionCandidates, "MaxTransitionCandidates", 0);
        RecordValues.Look(chronicler, ref _maxTransitionPairs, "MaxTransitionPairs", 0);
        RecordValues.Look(chronicler, ref _maxStagedLegAttempts, "MaxStagedLegAttempts", 0);
        RecordValues.Look(chronicler, ref _maxTraceIntervals, "MaxTraceIntervals", 0);
        RecordValues.Look(chronicler, ref _maxCoveredVoxelIntervals, "MaxCoveredVoxelIntervals", 0);
        RecordValues.Look(chronicler, ref _maxSimplificationRays, "MaxSimplificationRays", 0);
    }
}
