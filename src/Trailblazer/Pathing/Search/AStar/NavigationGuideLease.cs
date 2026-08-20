//=======================================================================
// NavigationGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns one graph-backed A* payload reference and its guide-local waypoint cursor.</summary>
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

    /// <summary>Gets the zero-based guide-local waypoint cursor.</summary>
    public int CurrentWaypointIndex => _inner?.GetCurrentWaypointOrdinal(_generation) ?? -1;

    /// <summary>Gets the immutable number of waypoints in the leased route.</summary>
    public int WaypointCount => _inner?.GetWaypointCount(_generation) ?? 0;

    /// <summary>Gets the immutable exact fixed-point cost of the complete route.</summary>
    public Fixed64 TotalCost => _inner?.GetTotalCost(_generation) ?? Fixed64.Zero;

    /// <summary>Gets the current waypoint's stable address and foot position.</summary>
    public NavigationGuideStatus TryGetCurrentWaypoint(
        out NavigationCellAddress address,
        out Vector3d footPosition)
    {
        NavigationAStarGuideLease? inner = _inner;
        if (inner == null)
        {
            address = default;
            footPosition = default;
            return NavigationGuideStatus.Stale;
        }

        return NavigationGuideStatusMapper.ToPublic(
            inner.TryGetCurrentWaypoint(
                _generation,
                out address,
                out footPosition));
    }

    /// <summary>Advances the guide-local cursor by one waypoint when dependencies remain current.</summary>
    public NavigationGuideStatus TryAdvanceWaypoint()
    {
        return _inner == null
            ? NavigationGuideStatus.Stale
            : NavigationGuideStatusMapper.ToPublic(
                _inner.TryAdvanceWaypoint(_generation));
    }

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
}
