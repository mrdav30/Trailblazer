//=======================================================================
// NavigationGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns one graph-backed A* payload reference and its guide-local step cursor.</summary>
public readonly struct NavigationGuideLease : IDisposable
{
    private readonly NavigationAStarGuideLease? _inner;
    private readonly long _generation;

    internal NavigationGuideLease(NavigationAStarGuideLease inner)
    {
        _inner = inner;
        _generation = inner.Generation;
    }

    /// <summary>Gets the lease's current dependency-validated status.</summary>
    public NavigationGuideStatus Status => NavigationGuideStatusMapper.ToPublic(
        _inner?.GetStatus(_generation) ?? NavigationAStarQueryStatus.Stale);

    /// <summary>Gets the zero-based guide-local step cursor.</summary>
    public int CurrentStepIndex => _inner?.GetCurrentWaypointOrdinal(_generation) ?? -1;

    /// <summary>Gets the immutable number of movement and action steps in the leased route.</summary>
    public int StepCount => _inner?.GetWaypointCount(_generation) ?? 0;

    /// <summary>Gets the immutable exact fixed-point cost of the complete route.</summary>
    public Fixed64 TotalCost => _inner?.GetTotalCost(_generation) ?? Fixed64.Zero;

    /// <summary>Gets the current dependency-validated movement or action step.</summary>
    public NavigationGuideStatus TryGetCurrentStep(out NavigationGuideStep step)
    {
        NavigationAStarGuideLease? inner = _inner;
        if (inner == null)
        {
            step = default;
            return NavigationGuideStatus.Stale;
        }

        return NavigationGuideStatusMapper.ToPublic(
            inner.TryGetCurrentStep(
                _generation,
                out step));
    }

    /// <summary>Advances one ordinary movement step; a pending action cannot be crossed.</summary>
    public NavigationGuideStatus TryAdvanceStep()
    {
        return _inner == null
            ? NavigationGuideStatus.Stale
            : NavigationGuideStatusMapper.ToPublic(
                _inner.TryAdvanceWaypoint(_generation));
    }

    /// <summary>Completes the exact current pending semantic action.</summary>
    public NavigationGuideStatus CompletePendingTransition(
        in NavigationTransitionInstruction instruction) =>
        _inner == null
            ? NavigationGuideStatus.Stale
            : NavigationGuideStatusMapper.ToPublic(
                _inner.CompletePendingTransition(_generation, instruction));

    /// <inheritdoc />
    public void Dispose() => _inner?.Dispose(_generation);
}

internal static class NavigationGuideStatusMapper
{
    internal static NavigationGuideStatus ToPublic(NavigationAStarQueryStatus status) => status switch
    {
        NavigationAStarQueryStatus.Success => NavigationGuideStatus.Success,
        NavigationAStarQueryStatus.Unsupported => NavigationGuideStatus.Unsupported,
        NavigationAStarQueryStatus.NoMap => NavigationGuideStatus.NoMap,
        NavigationAStarQueryStatus.InvalidProfile => NavigationGuideStatus.InvalidProfile,
        NavigationAStarQueryStatus.InvalidStart => NavigationGuideStatus.InvalidStart,
        NavigationAStarQueryStatus.InvalidEnd => NavigationGuideStatus.InvalidEnd,
        NavigationAStarQueryStatus.NoPath => NavigationGuideStatus.NoPath,
        NavigationAStarQueryStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
        NavigationAStarQueryStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
        NavigationAStarQueryStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
        NavigationAStarQueryStatus.Stale => NavigationGuideStatus.Stale,
        _ => NavigationGuideStatus.Stale
    };

    internal static NavigationGuideStatus ToPublic(NavigationFlowQueryStatus status) => status switch
    {
        NavigationFlowQueryStatus.Success => NavigationGuideStatus.Success,
        NavigationFlowQueryStatus.Unsupported => NavigationGuideStatus.Unsupported,
        NavigationFlowQueryStatus.NoMap => NavigationGuideStatus.NoMap,
        NavigationFlowQueryStatus.InvalidProfile => NavigationGuideStatus.InvalidProfile,
        NavigationFlowQueryStatus.InvalidStart => NavigationGuideStatus.InvalidStart,
        NavigationFlowQueryStatus.InvalidEnd => NavigationGuideStatus.InvalidEnd,
        NavigationFlowQueryStatus.NoPath => NavigationGuideStatus.NoPath,
        NavigationFlowQueryStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
        NavigationFlowQueryStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
        NavigationFlowQueryStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
        NavigationFlowQueryStatus.Stale => NavigationGuideStatus.Stale,
        _ => NavigationGuideStatus.Stale
    };

    internal static NavigationGuideStatus ToPublic(NavigationRayStatus status)
    {
        System.Diagnostics.Debug.Assert(status is
            NavigationRayStatus.Blocked or
            NavigationRayStatus.BudgetExceeded or
            NavigationRayStatus.CostOverflow or
            NavigationRayStatus.CapacityExceeded or
            NavigationRayStatus.Stale,
            "Ray recovery maps only terminal statuses not handled by its caller.");
        return status switch
        {
            NavigationRayStatus.Blocked => NavigationGuideStatus.LocalRecoveryRequired,
            NavigationRayStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
            NavigationRayStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
            NavigationRayStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
            _ => NavigationGuideStatus.Stale
        };
    }

    internal static NavigationFlowFieldStatus ToFlowField(
        NavigationTraversalEdgeAdvanceStatus status)
    {
        System.Diagnostics.Debug.Assert(status is
            NavigationTraversalEdgeAdvanceStatus.Complete or
            NavigationTraversalEdgeAdvanceStatus.BudgetExceeded or
            NavigationTraversalEdgeAdvanceStatus.CostOverflow or
            NavigationTraversalEdgeAdvanceStatus.CapacityExceeded or
            NavigationTraversalEdgeAdvanceStatus.Stale,
            "Flow traversal maps only terminal statuses not handled by its caller.");
        return status switch
        {
            NavigationTraversalEdgeAdvanceStatus.BudgetExceeded =>
                NavigationFlowFieldStatus.BudgetExceeded,
            NavigationTraversalEdgeAdvanceStatus.CostOverflow =>
                NavigationFlowFieldStatus.CostOverflow,
            NavigationTraversalEdgeAdvanceStatus.CapacityExceeded =>
                NavigationFlowFieldStatus.CapacityExceeded,
            _ => NavigationFlowFieldStatus.Stale
        };
    }
}
