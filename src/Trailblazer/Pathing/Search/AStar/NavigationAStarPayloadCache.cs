//=======================================================================
// NavigationAStarPayloadCache.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;

namespace Trailblazer.Pathing;

internal struct NavigationAStarPayloadReservation
{
    internal NavigationAStarPayloadReservation(
        NavigationAStarPayloadCache owner,
        NavigationAStarPayloadReservationSlot slot,
        ulong generation,
        long maximumBytes)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
        MaximumBytes = maximumBytes;
    }

    internal readonly NavigationAStarPayloadCache? Owner;
    internal readonly NavigationAStarPayloadReservationSlot? Slot;
    internal readonly ulong Generation;
    internal readonly long MaximumBytes;
    internal readonly bool HasLeaseSlot => Owner != null;
}

internal sealed class NavigationAStarPayloadReservationSlot
{
    internal ulong Generation;
    internal long MaximumBytes;
    internal bool IsReserved;
    internal NavigationAStarPayloadReservationSlot? NextPooled;
}

/// <summary>Stores a bounded concrete set of dependency-validated A* payloads.</summary>
internal sealed class NavigationAStarPayloadCache
{
    private readonly object _sync = new();
    private readonly GridWorld _world;
    private readonly NavigationAStarPayloadKey[] _keys;
    private readonly CacheEntry?[] _entries;
    private readonly byte[] _states;
    private readonly int[] _previous;
    private readonly int[] _next;
    private readonly int _mask;
    private readonly int _maximumEntries;
    private readonly long _maximumReusableBytes;
    private readonly long _maximumSinglePayloadBytes;
    private readonly long _maximumActivePayloadBytes;
    private readonly int _maximumActiveLeases;
    private int _leastRecent = -1;
    private int _mostRecent = -1;
    private int _count;
    private int _activeLeaseCount;
    private int _reservedLeaseCount;
    private long _cachedBytes;
    private long _leasedBytes;
    private long _detachedBytes;
    private long _reservedPayloadBytes;
    private NavigationAStarPayloadLease? _freeLeases;
    private NavigationAStarGuideLease? _freeGuides;
    private NavigationAStarPayloadReservationSlot? _freeReservationSlots;

    internal NavigationAStarPayloadCache(
        GridWorld world,
        int maxEntries,
        long maxReusableBytes = long.MaxValue,
        long maxSinglePayloadBytes = long.MaxValue,
        long maxActivePayloadBytes = long.MaxValue,
        int maxActiveLeases = int.MaxValue)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNegative(maxEntries, nameof(maxEntries));
        if (maxReusableBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxReusableBytes));
        if (maxSinglePayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSinglePayloadBytes));
        if (maxActivePayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxActivePayloadBytes));
        SwiftThrowHelper.ThrowIfNegative(maxActiveLeases, nameof(maxActiveLeases));
        SwiftThrowHelper.ThrowIfArgument(
            maxSinglePayloadBytes > maxActivePayloadBytes,
            nameof(maxSinglePayloadBytes),
            "A single payload cannot exceed the complete active-payload byte ceiling.");
        int tableSize = 1;
        int required = checked(Math.Max(1, maxEntries * 2));
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _world = world;
        _keys = new NavigationAStarPayloadKey[tableSize];
        _entries = new CacheEntry[tableSize];
        _states = new byte[tableSize];
        _previous = new int[tableSize];
        _next = new int[tableSize];
        Array.Fill(_previous, -1);
        Array.Fill(_next, -1);
        _mask = tableSize - 1;
        _maximumEntries = maxEntries;
        _maximumReusableBytes = maxReusableBytes;
        _maximumSinglePayloadBytes = maxSinglePayloadBytes;
        _maximumActivePayloadBytes = maxActivePayloadBytes;
        _maximumActiveLeases = maxActiveLeases;
    }

    internal long CachedBytes
    {
        get
        {
            lock (_sync)
                return _cachedBytes;
        }
    }

    internal long LeasedBytes
    {
        get
        {
            lock (_sync)
                return _leasedBytes;
        }
    }

    internal long DetachedBytes
    {
        get
        {
            lock (_sync)
                return _detachedBytes;
        }
    }

    internal int Count
    {
        get
        {
            lock (_sync)
                return _count;
        }
    }

    internal int ActiveLeaseCount
    {
        get
        {
            lock (_sync)
                return _activeLeaseCount;
        }
    }

    internal int ReservedLeaseCount
    {
        get
        {
            lock (_sync)
                return _reservedLeaseCount;
        }
    }

    internal long ReservedPayloadBytes
    {
        get
        {
            lock (_sync)
                return _reservedPayloadBytes;
        }
    }

    internal GridWorld World => _world;

    internal bool IsWorldCurrent(NavigationAStarPayload payload) =>
        payload.WorldChangeSequence is not ulong sequence
        || _world.ChangeSequence == sequence;

    internal NavigationAStarQueryStatus TryCreateGuide(
        NavigationWorldGraphStore store,
        NavigationAStarPayloadLease payloadLease,
        out NavigationAStarGuideLease? guide)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(payloadLease, nameof(payloadLease));
        NavigationAStarPayload payload = payloadLease.Payload;
        if (!payload.HasPath)
        {
            payloadLease.Dispose();
            guide = null;
            return NavigationAStarQueryStatus.NoPath;
        }
        NavigationWorldGraphLease? graphLease = store.TryAcquire();
        if (graphLease == null)
        {
            payloadLease.Dispose();
            guide = null;
            return NavigationAStarQueryStatus.CapacityExceeded;
        }
        using (graphLease)
        {
            if (!graphLease.Graph.IsDependencyCurrent(payload.Dependencies)
                || !store.Current.IsDependencyCurrent(payload.Dependencies)
                || !IsWorldCurrent(payload))
            {
                payloadLease.Dispose();
                guide = null;
                return NavigationAStarQueryStatus.Stale;
            }
        }
        lock (_sync)
        {
            guide = _freeGuides;
            if (guide == null)
                guide = new NavigationAStarGuideLease(this);
            else
            {
                _freeGuides = guide.NextPooled;
                guide.NextPooled = null;
            }
        }
        guide.Bind(store, payloadLease);
        return NavigationAStarQueryStatus.Success;
    }

    internal NavigationGuideStatus TryCreatePublicGuide(
        NavigationWorldGraphStore store,
        NavigationAStarPayloadLease payloadLease,
        out NavigationGuideLease? guide)
    {
        NavigationAStarQueryStatus status = TryCreateGuide(
            store,
            payloadLease,
            out NavigationAStarGuideLease? inner);
        if (status != NavigationAStarQueryStatus.Success)
        {
            guide = null;
            return NavigationGuideStatusMapper.ToPublic(status);
        }
        guide = new NavigationGuideLease(inner!);
        return NavigationGuideStatus.Success;
    }

    internal bool TryReservePayload(
        long maximumBytes,
        out NavigationAStarPayloadReservation reservation)
    {
        if (maximumBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        reservation = default;
        if (maximumBytes > _maximumSinglePayloadBytes)
            return false;
        lock (_sync)
        {
            if (_activeLeaseCount + _reservedLeaseCount >= _maximumActiveLeases
                || maximumBytes > _maximumActivePayloadBytes
                - _leasedBytes
                - _reservedPayloadBytes)
            {
                return false;
            }
            _reservedPayloadBytes = checked(_reservedPayloadBytes + maximumBytes);
            _reservedLeaseCount++;
            NavigationAStarPayloadReservationSlot? slot = _freeReservationSlots;
            if (slot == null)
            {
                slot = new NavigationAStarPayloadReservationSlot();
            }
            else
            {
                _freeReservationSlots = slot.NextPooled;
                slot.NextPooled = null;
            }
            slot.Generation = unchecked(slot.Generation + 1UL);
            slot.MaximumBytes = maximumBytes;
            slot.IsReserved = true;
            reservation = new NavigationAStarPayloadReservation(
                this,
                slot,
                slot.Generation,
                maximumBytes);
            return true;
        }
    }

    internal void ReleasePayloadReservation(
        ref NavigationAStarPayloadReservation reservation)
    {
        if (reservation.Owner == null)
        {
            reservation = default;
            return;
        }
        if (!ReferenceEquals(reservation.Owner, this))
            return;
        NavigationAStarPayloadReservation released = reservation;
        reservation = default;
        lock (_sync)
        {
            if (IsReservationCurrentUnderLock(released))
                ReleasePayloadReservationUnderLock(released);
        }
    }

    internal bool TryCheckoutReserved(
        NavigationAStarPayloadKey key,
        NavigationWorldGraph graph,
        ref NavigationAStarPayloadReservation reservation,
        out NavigationAStarPayloadLease lease)
    {
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        lease = null!;
        lock (_sync)
        {
            if (!IsReservationCurrentUnderLock(reservation))
                return false;
            FindSlot(key, out int slot, out bool found);
            if (!found)
                return false;
            CacheEntry current = _entries[slot]!;
            if (!graph.IsDependencyCurrent(current.Payload.Dependencies)
                || !IsWorldCurrent(current.Payload))
            {
                RemoveAt(slot);
                return false;
            }
            long otherReservations =
                _reservedPayloadBytes - reservation.MaximumBytes;
            if (current.Payload.RetainedBytes > reservation.MaximumBytes
                || (current.LeaseCount == 0
                    && current.Payload.RetainedBytes > _maximumActivePayloadBytes
                        - _leasedBytes
                        - otherReservations))
            {
                return false;
            }
            ReleasePayloadReservationUnderLock(reservation);
            reservation = default;
            lease = Checkout(current);
            Touch(slot);
            return true;
        }
    }

    internal bool TryPublish(
        NavigationAStarPayload payload,
        NavigationWorldGraphStore store,
        ref NavigationAStarPayloadReservation reservation,
        out NavigationAStarPayloadLease lease)
    {
        SwiftThrowHelper.ThrowIfNull(payload, nameof(payload));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        lease = null!;
        long retainedBytes = payload.RetainedBytes;
        if (!ReferenceEquals(reservation.Owner, this)
            || retainedBytes > reservation.MaximumBytes)
        {
            return false;
        }
        lock (_sync)
        {
            if (!IsReservationCurrentUnderLock(reservation))
                return false;
            NavigationWorldGraph graph = store.Current;
            if (!graph.IsDependencyCurrent(payload.Dependencies)
                || !IsWorldCurrent(payload))
                return false;
            long otherReservations =
                _reservedPayloadBytes - reservation.MaximumBytes;
            FindSlot(payload.Key, out int slot, out bool found);
            if (found)
            {
                CacheEntry current = _entries[slot]!;
                if (graph.IsDependencyCurrent(current.Payload.Dependencies)
                    && IsWorldCurrent(current.Payload))
                {
                    if (current.LeaseCount == 0
                            && current.Payload.RetainedBytes > _maximumActivePayloadBytes
                                - _leasedBytes
                                - otherReservations)
                    {
                        return false;
                    }
                    ReleasePayloadReservationUnderLock(reservation);
                    reservation = default;
                    lease = Checkout(current);
                    Touch(slot);
                    return true;
                }
                RemoveAt(slot);
            }

            if (_maximumEntries == 0 || retainedBytes > _maximumReusableBytes)
            {
                var detached = new CacheEntry(payload, isCached: false);
                _detachedBytes = checked(_detachedBytes + retainedBytes);
                ReleasePayloadReservationUnderLock(reservation);
                reservation = default;
                lease = Checkout(detached);
                return true;
            }

            while (_count >= _maximumEntries
                || retainedBytes > _maximumReusableBytes - _cachedBytes)
                RemoveAt(_leastRecent);

            FindSlot(payload.Key, out slot, out found);
            var entry = new CacheEntry(payload, isCached: true);
            _states[slot] = 1;
            _keys[slot] = payload.Key;
            _entries[slot] = entry;
            LinkMostRecent(slot);
            _count++;
            _cachedBytes = checked(_cachedBytes + retainedBytes);
            ReleasePayloadReservationUnderLock(reservation);
            reservation = default;
            lease = Checkout(entry);
            return true;
        }
    }

    internal void RemoveExact(NavigationAStarPayload payload)
    {
        SwiftThrowHelper.ThrowIfNull(payload, nameof(payload));
        lock (_sync)
        {
            FindSlot(payload.Key, out int slot, out bool found);
            if (found && ReferenceEquals(_entries[slot]!.Payload, payload))
                RemoveAt(slot);
        }
    }

    internal void Return(NavigationAStarPayloadLease lease)
    {
        CacheEntry? entry = lease.DetachEntry();
        if (entry == null)
            return;
        lock (_sync)
        {
            entry.LeaseCount--;
            _activeLeaseCount--;
            if (entry.LeaseCount == 0)
            {
                _leasedBytes = checked(_leasedBytes - entry.Payload.RetainedBytes);
                if (!entry.IsCached)
                    _detachedBytes = checked(_detachedBytes - entry.Payload.RetainedBytes);
            }
            RecycleLease(lease);
        }
    }

    internal void ReturnGuide(NavigationAStarGuideLease guide, long generation)
    {
        if (!guide.TryDetach(
                generation,
                out NavigationAStarPayloadLease payloadLease))
            return;
        payloadLease.Dispose();
        lock (_sync)
            RetainGuideIfReusable(generation, guide, ref _freeGuides);
    }

    internal static void RetainGuideIfReusable(
        long generation,
        NavigationAStarGuideLease guide,
        ref NavigationAStarGuideLease? freeGuides)
    {
        if (!NavigationGenerationCounter.CanAdvance(generation))
            return;
        guide.NextPooled = freeGuides;
        freeGuides = guide;
    }

    private NavigationAStarPayloadLease Checkout(CacheEntry entry)
    {
        if (entry.LeaseCount == 0)
            _leasedBytes = checked(_leasedBytes + entry.Payload.RetainedBytes);
        entry.LeaseCount++;
        _activeLeaseCount++;
        NavigationAStarPayloadLease? lease = _freeLeases;
        if (lease == null)
            lease = new NavigationAStarPayloadLease(this);
        else
        {
            _freeLeases = lease.NextPooled;
            lease.NextPooled = null;
        }
        lease.Bind(entry);
        return lease;
    }

    private void RecycleLease(NavigationAStarPayloadLease lease)
    {
        lease.NextPooled = _freeLeases;
        _freeLeases = lease;
    }

    private bool IsReservationCurrentUnderLock(
        NavigationAStarPayloadReservation reservation) =>
        ReferenceEquals(reservation.Owner, this)
        && reservation.Slot != null
        && reservation.Slot.Generation == reservation.Generation
        && reservation.Slot.IsReserved;

    private void ReleasePayloadReservationUnderLock(
        NavigationAStarPayloadReservation reservation)
    {
        NavigationAStarPayloadReservationSlot slot = reservation.Slot!;
        _reservedPayloadBytes -= slot.MaximumBytes;
        _reservedLeaseCount--;
        slot.MaximumBytes = 0;
        slot.IsReserved = false;
        slot.NextPooled = _freeReservationSlots;
        _freeReservationSlots = slot;
    }

    private void FindSlot(
        NavigationAStarPayloadKey key,
        out int slot,
        out bool found)
    {
        slot = key.GetHashCode() & _mask;
        int firstRemoved = -1;
        for (int probe = 0; probe < _states.Length; probe++)
        {
            byte state = _states[slot];
            if (state == 0)
            {
                found = false;
                if (firstRemoved >= 0)
                    slot = firstRemoved;
                return;
            }
            if (state == 1 && _keys[slot].Equals(key))
            {
                found = true;
                return;
            }
            if (state == 2 && firstRemoved < 0)
                firstRemoved = slot;
            slot = (slot + 1) & _mask;
        }
        found = false;
        slot = firstRemoved;
    }

    private void Touch(int slot)
    {
        if (_mostRecent == slot)
            return;
        Unlink(slot);
        LinkMostRecent(slot);
    }

    private void LinkMostRecent(int slot)
    {
        _previous[slot] = _mostRecent;
        _next[slot] = -1;
        if (_mostRecent >= 0)
            _next[_mostRecent] = slot;
        else
            _leastRecent = slot;
        _mostRecent = slot;
    }

    private void Unlink(int slot)
    {
        int previous = _previous[slot];
        int next = _next[slot];
        if (previous >= 0)
            _next[previous] = next;
        else
            _leastRecent = next;
        if (next >= 0)
            _previous[next] = previous;
        else
            _mostRecent = previous;
        _previous[slot] = -1;
        _next[slot] = -1;
    }

    private void RemoveAt(int slot)
    {
        CacheEntry entry = _entries[slot]!;
        _cachedBytes = checked(_cachedBytes - entry.Payload.RetainedBytes);
        entry.IsCached = false;
        if (entry.LeaseCount != 0)
            _detachedBytes = checked(_detachedBytes + entry.Payload.RetainedBytes);
        Unlink(slot);
        _keys[slot] = default;
        _entries[slot] = null;
        _states[slot] = 2;
        _count--;
    }

    internal sealed class CacheEntry
    {
        internal CacheEntry(NavigationAStarPayload payload, bool isCached)
        {
            Payload = payload;
            IsCached = isCached;
        }

        internal NavigationAStarPayload Payload { get; }

        internal int LeaseCount { get; set; }

        internal bool IsCached { get; set; }
    }
}
