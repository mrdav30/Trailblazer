//=======================================================================
// NavigationQueryAdmissionCoordinator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Identifies one generation-safe aggregate query-capacity reservation.</summary>
internal readonly struct NavigationQueryCapacityReservation
{
    internal NavigationQueryCapacityReservation(
        NavigationQueryAdmissionCoordinator owner,
        int lane,
        ulong generation,
        int count)
    {
        Owner = owner;
        Lane = lane;
        Generation = generation;
        Count = count;
    }

    internal NavigationQueryAdmissionCoordinator? Owner { get; }
    internal int Lane { get; }
    internal ulong Generation { get; }
    internal int Count { get; }
}

/// <summary>Bounds aggregate A* and Flow admission without sharing their workers.</summary>
internal sealed class NavigationQueryAdmissionCoordinator
{
    private readonly object _sync = new();
    private readonly LaneState[] _lanes = new LaneState[2];
    private readonly int _maximumCount;
    private int _activeCount;

    internal NavigationQueryAdmissionCoordinator(int maximumCount)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maximumCount <= 0,
            maximumCount,
            nameof(maximumCount));
        _maximumCount = maximumCount;
    }

    internal int ActiveCount
    {
        get
        {
            lock (_sync)
                return _activeCount;
        }
    }

    internal int TryReservePrefix(
        PathAlgorithm algorithm,
        int requestedCount,
        out NavigationQueryCapacityReservation reservation)
    {
        SwiftThrowHelper.ThrowIfNegative(requestedCount, nameof(requestedCount));
        int lane = GetLane(algorithm);
        lock (_sync)
        {
            ref LaneState state = ref _lanes[lane];
            int count = Math.Min(requestedCount, _maximumCount - _activeCount);
            if (requestedCount == 0
                || state.Active
                || count == 0)
            {
                reservation = default;
                return 0;
            }
            state.Generation = unchecked(state.Generation + 1UL);
            state.Count = count;
            state.Active = true;
            _activeCount += count;
            reservation = new NavigationQueryCapacityReservation(
                this,
                lane,
                state.Generation,
                count);
            return count;
        }
    }

    internal NavigationQueryCapacityReservation Trim(
        NavigationQueryCapacityReservation reservation,
        int retainedCount)
    {
        lock (_sync)
        {
            if (!TryGetActiveLane(reservation, out int lane))
                return default;
            ref LaneState state = ref _lanes[lane];
            _activeCount -= state.Count - retainedCount;
            state.Count = retainedCount;
            if (retainedCount == 0)
            {
                state.Active = false;
                return default;
            }
            return new NavigationQueryCapacityReservation(
                this,
                lane,
                state.Generation,
                retainedCount);
        }
    }

    internal void Release(NavigationQueryCapacityReservation reservation)
    {
        lock (_sync)
        {
            if (!TryGetActiveLane(reservation, out int lane))
                return;
            ref LaneState state = ref _lanes[lane];
            _activeCount -= state.Count;
            state.Count = 0;
            state.Active = false;
        }
    }

    private bool TryGetActiveLane(
        NavigationQueryCapacityReservation reservation,
        out int lane)
    {
        lane = reservation.Lane;
        return ReferenceEquals(reservation.Owner, this)
            && (uint)lane < (uint)_lanes.Length
            && _lanes[lane].Active
            && _lanes[lane].Generation == reservation.Generation
            && _lanes[lane].Count == reservation.Count;
    }

    private static int GetLane(PathAlgorithm algorithm) =>
        algorithm == PathAlgorithm.AStar ? 0 : 1;

    private struct LaneState
    {
        internal ulong Generation;
        internal int Count;
        internal bool Active;
    }
}
