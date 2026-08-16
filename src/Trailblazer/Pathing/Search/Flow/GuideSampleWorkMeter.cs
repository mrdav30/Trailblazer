//=======================================================================
// GuideSampleWorkMeter.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Tracks exact bounded work shared by one or more guide samples.</summary>
internal struct GuideSampleWorkMeter
{
    private int _remainingCurrentNodeLookupProbes;
    private int _remainingCursorLegScans;
    private int _remainingCursorRebases;
    private int _remainingPortalChecks;
    private int _remainingPrismChecks;
    private int _remainingTraceIntervals;
    private int _remainingLocalRecoveryAttempts;

    internal GuideSampleWorkMeter(GuideSampleWorkBudget budget)
    {
        _remainingCurrentNodeLookupProbes = budget.MaxCurrentNodeLookupProbes;
        _remainingCursorLegScans = budget.MaxCursorLegScans;
        _remainingCursorRebases = budget.MaxCursorRebases;
        _remainingPortalChecks = budget.MaxPortalChecks;
        _remainingPrismChecks = budget.MaxPrismChecks;
        _remainingTraceIntervals = budget.MaxTraceIntervals;
        _remainingLocalRecoveryAttempts = budget.MaxLocalRecoveryAttempts;
    }

    internal bool TryConsumeCurrentNodeLookupProbes(int count) =>
        TryConsume(ref _remainingCurrentNodeLookupProbes, count);

    internal bool TryConsumeCursorLegScans(int count) =>
        TryConsume(ref _remainingCursorLegScans, count);

    internal bool TryConsumeCursorRebases(int count) =>
        TryConsume(ref _remainingCursorRebases, count);

    internal bool TryConsumePortalChecks(int count) =>
        TryConsume(ref _remainingPortalChecks, count);

    internal bool TryConsumePrismChecks(int count) =>
        TryConsume(ref _remainingPrismChecks, count);

    internal bool TryConsumeTraceIntervals(int count) =>
        TryConsume(ref _remainingTraceIntervals, count);

    internal bool TryConsumeLocalRecoveryAttempts(int count) =>
        TryConsume(ref _remainingLocalRecoveryAttempts, count);

    internal readonly int GetCurrentNodeLookupAllowance() =>
        _remainingCurrentNodeLookupProbes;

    private static bool TryConsume(ref int remaining, int count)
    {
        if (count < 0 || count > remaining)
            return false;
        remaining -= count;
        return true;
    }
}
