//=======================================================================
// NavigationAStarGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns one immutable A* payload and a guide-local waypoint cursor.</summary>
internal sealed class NavigationAStarGuideLease
{
    private readonly NavigationAStarPayloadCache _owner;
    private readonly object _sync = new();
    private NavigationWorldGraphStore? _store;
    private NavigationAStarPayloadLease? _payloadLease;
    private NavigationAStarQueryStatus _status;
    private int _currentWaypointOrdinal;
    private long _generation;

    internal NavigationAStarGuideLease(NavigationAStarPayloadCache owner)
    {
        _owner = owner;
    }

    internal long Generation
    {
        get { lock (_sync) return _generation; }
    }

    internal bool CanReuse
    {
        get { lock (_sync) return _generation < long.MaxValue; }
    }

    internal NavigationAStarGuideLease? NextPooled { get; set; }

    internal void Bind(
        NavigationWorldGraphStore store,
        NavigationAStarPayloadLease payloadLease)
    {
        lock (_sync)
        {
            if (_store != null || _payloadLease != null)
                throw new InvalidOperationException("The A* guide lease is already active.");
            if (_generation == long.MaxValue)
                throw new InvalidOperationException("The A* guide lease generation is exhausted.");
            _generation++;
            _store = store;
            _payloadLease = payloadLease;
            _currentWaypointOrdinal = 0;
            _status = NavigationAStarQueryStatus.Success;
        }
    }

    internal NavigationAStarQueryStatus GetStatus(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? _status
                : NavigationAStarQueryStatus.Stale;
        }
    }

    internal int GetCurrentWaypointOrdinal(long generation)
    {
        lock (_sync)
            return IsGenerationActiveUnderLock(generation) ? _currentWaypointOrdinal : -1;
    }

    internal int GetWaypointCount(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? _payloadLease!.Payload.GuidePoints.Length
                : 0;
        }
    }

    internal Fixed64 GetTotalCost(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? _payloadLease!.Payload.Cost
                : Fixed64.Zero;
        }
    }

    internal NavigationAStarQueryStatus TryGetCurrentWaypoint(
        long generation,
        out NavigationCellAddress address,
        out Vector3d footPosition)
    {
        lock (_sync)
        {
            if (!IsGenerationActiveUnderLock(generation))
            {
                address = default;
                footPosition = default;
                return NavigationAStarQueryStatus.Stale;
            }
            return TryGetCurrentWaypointUnderLock(out address, out footPosition);
        }
    }

    internal NavigationAStarQueryStatus TryAdvanceWaypoint(long generation)
    {
        lock (_sync)
        {
            return IsGenerationActiveUnderLock(generation)
                ? TryAdvanceWaypointUnderLock()
                : NavigationAStarQueryStatus.Stale;
        }
    }

    internal bool TryDetach(
        long generation,
        out NavigationAStarPayloadLease? payloadLease)
    {
        lock (_sync)
        {
            payloadLease = null;
            if (!IsGenerationActiveUnderLock(generation))
                return false;
            _store = null;
            payloadLease = _payloadLease;
            _payloadLease = null;
            _currentWaypointOrdinal = 0;
            _status = NavigationAStarQueryStatus.Pending;
            return true;
        }
    }

    internal void Dispose(long generation) => _owner.ReturnGuide(this, generation);

    private bool IsGenerationActiveUnderLock(long generation) =>
        generation > 0
        && generation == _generation
        && _store != null
        && _payloadLease != null;

    private NavigationAStarQueryStatus TryGetCurrentWaypointUnderLock(
        out NavigationCellAddress address,
        out Vector3d footPosition)
    {
        address = default;
        footPosition = default;
        if (_status != NavigationAStarQueryStatus.Success)
            return _status;
        NavigationWorldGraphStore? store = _store;
        NavigationAStarPayloadLease? payloadLease = _payloadLease;
        if (store == null || payloadLease == null)
            return MarkStaleUnderLock();
        NavigationWorldGraphLease? graphLease = store.TryAcquire();
        if (graphLease == null)
            return NavigationAStarQueryStatus.CapacityExceeded;
        using (graphLease)
        {
            NavigationAStarPayload payload = payloadLease.Payload;
            NavigationWorldGraph graph = graphLease.Graph;
            if (!graph.IsDependencyCurrent(payload.Dependencies)
                || (uint)_currentWaypointOrdinal >= (uint)payload.GuidePoints.Length)
            {
                return MarkStaleUnderLock();
            }
            NavigationAStarGuidePoint current =
                payload.GuidePoints[_currentWaypointOrdinal];
            if (!store.Current.IsDependencyCurrent(payload.Dependencies))
            {
                return MarkStaleUnderLock();
            }
            address = current.Address;
            footPosition = current.Position;
            return NavigationAStarQueryStatus.Success;
        }
    }

    private NavigationAStarQueryStatus TryAdvanceWaypointUnderLock()
    {
        if (_status != NavigationAStarQueryStatus.Success)
            return _status;
        NavigationWorldGraphStore? store = _store;
        NavigationAStarPayloadLease? payloadLease = _payloadLease;
        if (store == null || payloadLease == null)
            return MarkStaleUnderLock();
        NavigationWorldGraphLease? graphLease = store.TryAcquire();
        if (graphLease == null)
            return NavigationAStarQueryStatus.CapacityExceeded;
        using (graphLease)
        {
            NavigationAStarPayload payload = payloadLease.Payload;
            if (!graphLease.Graph.IsDependencyCurrent(payload.Dependencies)
                || !store.Current.IsDependencyCurrent(payload.Dependencies))
            {
                return MarkStaleUnderLock();
            }
            if (_currentWaypointOrdinal + 1 < payload.GuidePoints.Length)
                _currentWaypointOrdinal++;
            return NavigationAStarQueryStatus.Success;
        }
    }

    private NavigationAStarQueryStatus MarkStaleUnderLock()
    {
        _status = NavigationAStarQueryStatus.Stale;
        return _status;
    }
}
