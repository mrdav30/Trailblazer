//=======================================================================
// NavigationFlowFieldPayloadCache.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;

namespace Trailblazer.Pathing;

internal readonly struct NavigationFlowFieldReservation
{
    internal NavigationFlowFieldReservation(
        NavigationFlowFieldPayloadCache owner,
        int slot,
        ulong generation)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
    }

    internal readonly NavigationFlowFieldPayloadCache? Owner;
    internal readonly int Slot;
    internal readonly ulong Generation;
}

/// <summary>Stores bounded destination-centric canonical flow-field prefixes.</summary>
internal sealed class NavigationFlowFieldPayloadCache : IDisposable
{
    private enum LeaseSlotState : byte
    {
        Free = 0,
        Reserved = 1,
        ActivePayload = 2,
        ActiveGuide = 3,
        Retired = 4
    }

    private enum PrefixRelation : byte
    {
        Equal = 0,
        CandidateLonger = 1,
        ExistingLonger = 2
    }

    private readonly object _sync = new();
    private readonly GridWorld _world;
    private readonly NavigationFlowFieldPayloadKey[] _keys;
    private readonly CacheEntry?[] _entries;
    private readonly byte[] _states;
    private readonly int[] _previous;
    private readonly int[] _next;
    private readonly LeaseSlot[] _leaseSlots;
    private readonly int[] _freeLeaseSlots;
    private readonly int _mask;
    private readonly int _maximumEntries;
    private readonly long _maximumReusableBytes;
    private readonly long _maximumSinglePayloadBytes;
    private readonly long _maximumActivePayloadBytes;
    private int _freeLeaseCount;
    private int _leastRecent = -1;
    private int _mostRecent = -1;
    private int _count;
    private int _activeLeaseCount;
    private int _reservedLeaseCount;
    private long _cachedBytes;
    private long _leasedBytes;
    private long _detachedBytes;
    private long _reservedPayloadBytes;
    private bool _disposed;
    private NavigationFlowFieldGuideLease?[] _freeGuides;
    private int _freeGuideCount;
    private readonly int _guideMapCapacity;
    private readonly NavigationImmediateRayWorkspace _immediateRayWorkspace;

    internal NavigationFlowFieldPayloadCache(
        GridWorld world,
        int maxEntries,
        long maxReusableBytes,
        long maxSinglePayloadBytes,
        long maxActivePayloadBytes,
        int maxActiveLeases,
        int guideMapCapacity,
        NavigationImmediateRayWorkspace immediateRayWorkspace)
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
        SwiftThrowHelper.ThrowIfNegative(guideMapCapacity, nameof(guideMapCapacity));
        SwiftThrowHelper.ThrowIfNull(immediateRayWorkspace, nameof(immediateRayWorkspace));
        SwiftThrowHelper.ThrowIfArgument(
            maxSinglePayloadBytes > maxActivePayloadBytes,
            nameof(maxSinglePayloadBytes),
            "A single payload cannot exceed the complete active-payload byte ceiling.");

        int tableSize = 1;
        int required = checked(Math.Max(1, maxEntries * 2));
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        _world = world;
        _keys = new NavigationFlowFieldPayloadKey[tableSize];
        _entries = new CacheEntry[tableSize];
        _states = new byte[tableSize];
        _previous = new int[tableSize];
        _next = new int[tableSize];
        Array.Fill(_previous, -1);
        Array.Fill(_next, -1);
        _leaseSlots = maxActiveLeases == 0
            ? Array.Empty<LeaseSlot>()
            : new LeaseSlot[maxActiveLeases];
        _freeLeaseSlots = maxActiveLeases == 0
            ? Array.Empty<int>()
            : new int[maxActiveLeases];
        for (int i = 0; i < _freeLeaseSlots.Length; i++)
            _freeLeaseSlots[i] = _freeLeaseSlots.Length - i - 1;
        _freeLeaseCount = _freeLeaseSlots.Length;
        _freeGuides = maxActiveLeases == 0
            ? Array.Empty<NavigationFlowFieldGuideLease?>()
            : new NavigationFlowFieldGuideLease[maxActiveLeases];
        for (int i = 0; i < _freeGuides.Length; i++)
            _freeGuides[i] = new NavigationFlowFieldGuideLease(guideMapCapacity);
        _freeGuideCount = _freeGuides.Length;
        _guideMapCapacity = guideMapCapacity;
        _immediateRayWorkspace = immediateRayWorkspace;
        _mask = tableSize - 1;
        _maximumEntries = maxEntries;
        _maximumReusableBytes = maxReusableBytes;
        _maximumSinglePayloadBytes = maxSinglePayloadBytes;
        _maximumActivePayloadBytes = maxActivePayloadBytes;
    }

    internal int Count
    {
        get { lock (_sync) return _count; }
    }

    internal NavigationImmediateRayWorkspace ImmediateRayWorkspace =>
        _immediateRayWorkspace;

    internal GridWorld World => _world;

    internal long CachedBytes
    {
        get { lock (_sync) return _cachedBytes; }
    }

    internal long LeasedBytes
    {
        get { lock (_sync) return _leasedBytes; }
    }

    internal long DetachedBytes
    {
        get { lock (_sync) return _detachedBytes; }
    }

    internal long ReservedPayloadBytes
    {
        get { lock (_sync) return _reservedPayloadBytes; }
    }

    internal int ActiveLeaseCount
    {
        get { lock (_sync) return _activeLeaseCount; }
    }

    internal int ReservedLeaseCount
    {
        get { lock (_sync) return _reservedLeaseCount; }
    }

    internal long MaximumSinglePayloadBytes => _maximumSinglePayloadBytes;

    internal NavigationGuideStatus TryCreateGuide(
        NavigationWorldGraphStore store,
        NavigationFlowQueryResult result,
        out NavigationFlowFieldLease guide)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        guide = default;
        NavigationFlowFieldPayloadLease payloadLease = result.PayloadLease;
        NavigationFlowFieldGuideLease? inner = null;
        NavigationFlowFieldPayload? payload = null;
        NavigationGuideStatus status = NavigationGuideStatus.Stale;
        int slot = -1;
        ulong generation = 0;
        FixedMathSharp.Fixed64 originIntegrationCost = FixedMathSharp.Fixed64.Zero;
        lock (_sync)
        {
            if (!_disposed
                && payloadLease.IsOwnedBy(this, out slot, out generation)
                && (uint)slot < (uint)_leaseSlots.Length)
            {
                ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
                if (leaseSlot.Generation == generation
                    && leaseSlot.State == LeaseSlotState.ActivePayload
                    && leaseSlot.Entry != null
                    && !leaseSlot.Entry.IsInvalidated)
                {
                    payload = leaseSlot.Entry.Payload;
                    NavigationWorldGraph current = store.Current;
                    if (current.MapCount > _guideMapCapacity)
                    {
                        status = NavigationGuideStatus.CapacityExceeded;
                    }
                    else if (!current.IsDependencyCurrent(payload.Dependencies)
                        || !IsWorldCurrent(payload))
                    {
                        leaseSlot.Entry.IsInvalidated = true;
                    }
                    else if (!payload.TryGetNode(
                            result.ResolvedOrigin,
                            payload.Key.StartMedium,
                            out NavigationFlowFieldNode origin))
                    {
                        status = NavigationGuideStatus.Stale;
                    }
                    else
                    {
                        inner = RentGuideUnderLock();
                        if (inner == null)
                        {
                            status = NavigationGuideStatus.CapacityExceeded;
                        }
                        else
                        {
                            leaseSlot.State = LeaseSlotState.ActiveGuide;
                            originIntegrationCost = origin.IntegrationCost;
                            status = NavigationGuideStatus.Success;
                        }
                    }
                }
            }
        }
        if (status != NavigationGuideStatus.Success || inner == null || payload == null)
        {
            payloadLease.Dispose();
            return status;
        }
        inner.Bind(
            this,
            store,
            slot,
            generation,
            result.ResolvedOrigin,
            payload.Key.StartMedium,
            originIntegrationCost);
        if (TryGetGuidePayload(slot, generation, out NavigationFlowFieldPayload attached)
                != NavigationFlowFieldStatus.Success
            || !ReferenceEquals(attached, payload)
            || !store.Current.IsDependencyCurrent(payload.Dependencies)
            || !IsWorldCurrent(payload))
        {
            inner.Dispose(inner.Generation);
            return NavigationGuideStatus.Stale;
        }
        guide = new NavigationFlowFieldLease(inner);
        return NavigationGuideStatus.Success;
    }

    internal bool TryReservePayload(
        long maximumBytes,
        out NavigationFlowFieldReservation reservation)
    {
        if (maximumBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        reservation = default;
        if (maximumBytes > _maximumSinglePayloadBytes)
            return false;
        lock (_sync)
        {
            if (_disposed
                || maximumBytes > _maximumActivePayloadBytes
                    - _leasedBytes
                    - _reservedPayloadBytes
                || !TryIssueLeaseSlot(out int slot, out ulong generation))
            {
                return false;
            }
            ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
            leaseSlot.State = LeaseSlotState.Reserved;
            leaseSlot.MaximumBytes = maximumBytes;
            _reservedLeaseCount++;
            _reservedPayloadBytes = checked(_reservedPayloadBytes + maximumBytes);
            reservation = new NavigationFlowFieldReservation(this, slot, generation);
            return true;
        }
    }

    internal void ReleasePayloadReservation(
        ref NavigationFlowFieldReservation reservation)
    {
        if (reservation.Owner == null)
        {
            reservation = default;
            return;
        }
        if (!ReferenceEquals(reservation.Owner, this))
            return;
        NavigationFlowFieldReservation released = reservation;
        reservation = default;
        lock (_sync)
        {
            if (TryGetReservationSlot(released, out int slot))
                ReleaseReservationSlot(slot);
        }
    }

    internal NavigationFlowFieldStatus TryCheckout(
        NavigationWorldGraphStore store,
        NavigationWorldGraph expectedGraph,
        NavigationFlowFieldPayloadKey key,
        NavigationCellAddress requiredOrigin,
        out NavigationFlowFieldPayloadLease lease,
        out NavigationFlowFieldPayload? proof)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(expectedGraph, nameof(expectedGraph));
        lease = default;
        proof = null;
        NavigationFlowFieldPayload? checkedPayload = null;
        NavigationFlowFieldStatus status;
        lock (_sync)
        {
            if (_disposed)
                return NavigationFlowFieldStatus.Stale;
            FindSlot(key, out int slot, out bool found);
            if (!found)
                return NavigationFlowFieldStatus.Pending;
            CacheEntry entry = _entries[slot]!;
            NavigationWorldGraph graph = store.Current;
            if (!graph.IsDependencyCurrent(entry.Payload.Dependencies)
                || !IsWorldCurrent(entry.Payload))
            {
                RemoveAt(slot, invalidate: true);
                return NavigationFlowFieldStatus.Stale;
            }
            if (!expectedGraph.IsDependencyCurrent(entry.Payload.Dependencies)
                || !IsWorldCurrent(entry.Payload))
                return NavigationFlowFieldStatus.Pending;
            checkedPayload = entry.Payload;
            status = ClassifyCoverage(checkedPayload, requiredOrigin);
            if (status == NavigationFlowFieldStatus.Success)
            {
                if (!TryCheckout(entry, out lease))
                    return NavigationFlowFieldStatus.CapacityExceeded;
                Touch(slot);
            }
            else if (status is NavigationFlowFieldStatus.NoPath
                or NavigationFlowFieldStatus.CostOverflow)
            {
                Touch(slot);
            }
        }
        if (checkedPayload != null
            && status != NavigationFlowFieldStatus.Pending
            && (!store.Current.IsDependencyCurrent(checkedPayload.Dependencies)
                || !IsWorldCurrent(checkedPayload)))
        {
            InvalidateExact(checkedPayload);
            lease.Dispose();
            lease = default;
            return NavigationFlowFieldStatus.Stale;
        }
        if (status != NavigationFlowFieldStatus.Pending)
            proof = checkedPayload;
        return status;
    }

    internal bool IsExactProofCurrent(
        NavigationWorldGraphStore store,
        NavigationFlowFieldPayload proof,
        NavigationCellAddress requiredOrigin,
        NavigationFlowFieldStatus expectedStatus)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(proof, nameof(proof));
        bool current;
        lock (_sync)
        {
            FindSlot(proof.Key, out int slot, out bool found);
            current = !_disposed
                && found
                && ReferenceEquals(_entries[slot]!.Payload, proof)
                && store.Current.IsDependencyCurrent(proof.Dependencies)
                && IsWorldCurrent(proof)
                && ClassifyCoverage(proof, requiredOrigin) == expectedStatus;
        }
        if (current
            && store.Current.IsDependencyCurrent(proof.Dependencies)
            && IsWorldCurrent(proof))
        {
            return true;
        }
        InvalidateExact(proof);
        return false;
    }

    internal NavigationFlowFieldStatus TryPublishOrPromote(
        NavigationWorldGraphStore store,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress requiredOrigin,
        ref NavigationFlowFieldReservation reservation,
        out NavigationFlowFieldPayloadLease lease)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(payload, nameof(payload));
        lease = default;
        if (payload.RetainedBytes > _maximumSinglePayloadBytes)
            return NavigationFlowFieldStatus.CapacityExceeded;
        NavigationFlowFieldPayload? canonical = null;
        NavigationFlowFieldStatus status;
        lock (_sync)
        {
            if (_disposed || !TryGetReservationSlot(reservation, out int reservationSlot))
                return NavigationFlowFieldStatus.Stale;
            ref LeaseSlot reserved = ref _leaseSlots[reservationSlot];
            if (payload.RetainedBytes > reserved.MaximumBytes)
                return NavigationFlowFieldStatus.CapacityExceeded;
            NavigationWorldGraph graph = store.Current;
            if (!graph.IsDependencyCurrent(payload.Dependencies)
                || !IsWorldCurrent(payload))
                return NavigationFlowFieldStatus.Stale;

            FindSlot(payload.Key, out int currentSlot, out bool found);
            if (found
                && (!graph.IsDependencyCurrent(_entries[currentSlot]!.Payload.Dependencies)
                    || !IsWorldCurrent(_entries[currentSlot]!.Payload)))
            {
                RemoveAt(currentSlot, invalidate: true);
                found = false;
            }

            status = ClassifyCoverage(payload, requiredOrigin);
            if (status == NavigationFlowFieldStatus.Pending)
            {
                throw new InvalidOperationException(
                    "A published flow payload must cover its requested origin or prove no path.");
            }

            CacheEntry? incumbent = found ? _entries[currentSlot] : null;
            if (incumbent != null)
            {
                PrefixRelation relation = ComparePrefixes(incumbent.Payload, payload);
                if (relation is PrefixRelation.Equal or PrefixRelation.ExistingLonger)
                {
                    canonical = incumbent.Payload;
                    status = ClassifyCoverage(canonical, requiredOrigin);
                    if (status == NavigationFlowFieldStatus.Pending)
                    {
                        throw new InvalidOperationException(
                            "A longer canonical flow prefix cannot cover less than its candidate.");
                    }
                    if (!TryFinishExisting(
                            currentSlot,
                            incumbent,
                            status,
                            reservationSlot,
                            ref reservation,
                            out lease))
                    {
                        return NavigationFlowFieldStatus.CapacityExceeded;
                    }
                }
                else
                {
                    canonical = payload;
                    if (!TryPublishCandidate(
                            payload,
                            status,
                            currentSlot,
                            incumbent,
                            reservationSlot,
                            ref reservation,
                            out lease))
                    {
                        return NavigationFlowFieldStatus.CapacityExceeded;
                    }
                }
            }
            else
            {
                canonical = payload;
                if (!TryPublishCandidate(
                        payload,
                        status,
                        currentSlot: -1,
                        incumbent: null,
                        reservationSlot,
                        ref reservation,
                        out lease))
                {
                    return NavigationFlowFieldStatus.CapacityExceeded;
                }
            }
        }

        if (canonical != null
            && (!store.Current.IsDependencyCurrent(canonical.Dependencies)
                || !IsWorldCurrent(canonical)))
        {
            InvalidateExact(canonical);
            lease.Dispose();
            lease = default;
            return NavigationFlowFieldStatus.Stale;
        }
        return status;
    }

    internal NavigationFlowFieldStatus TryGetPayload(
        int slot,
        ulong generation,
        out NavigationFlowFieldPayload payload)
    {
        lock (_sync)
        {
            if ((uint)slot < (uint)_leaseSlots.Length)
            {
                ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
                if (leaseSlot.Generation == generation
                    && leaseSlot.State == LeaseSlotState.ActivePayload
                    && leaseSlot.Entry != null
                    && !leaseSlot.Entry.IsInvalidated)
                {
                    payload = leaseSlot.Entry.Payload;
                    return NavigationFlowFieldStatus.Success;
                }
            }
        }
        payload = null!;
        return NavigationFlowFieldStatus.Stale;
    }

    internal NavigationFlowFieldStatus TryGetGuidePayload(
        int slot,
        ulong generation,
        out NavigationFlowFieldPayload payload)
    {
        lock (_sync)
        {
            if ((uint)slot < (uint)_leaseSlots.Length)
            {
                ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
                if (leaseSlot.Generation == generation
                    && leaseSlot.State == LeaseSlotState.ActiveGuide
                    && leaseSlot.Entry != null
                    && !leaseSlot.Entry.IsInvalidated)
                {
                    payload = leaseSlot.Entry.Payload;
                    return NavigationFlowFieldStatus.Success;
                }
            }
        }
        payload = null!;
        return NavigationFlowFieldStatus.Stale;
    }

    internal void Return(int slot, ulong generation)
    {
        lock (_sync)
        {
            if ((uint)slot >= (uint)_leaseSlots.Length)
                return;
            ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
            if (leaseSlot.Generation != generation
                || leaseSlot.State != LeaseSlotState.ActivePayload
                || leaseSlot.Entry == null)
            {
                return;
            }
            CacheEntry entry = leaseSlot.Entry;
            entry.LeaseCount--;
            _activeLeaseCount--;
            if (entry.LeaseCount == 0)
            {
                _leasedBytes = checked(_leasedBytes - entry.Payload.RetainedBytes);
                if (!entry.IsCached)
                    _detachedBytes = checked(_detachedBytes - entry.Payload.RetainedBytes);
            }
            RecycleLeaseSlot(slot);
        }
    }

    internal void ReturnGuide(NavigationFlowFieldGuideLease guide, ulong generation)
    {
        if (!guide.TryDetach(generation, out int slot, out ulong slotGeneration))
            return;
        bool canReuse = guide.CanReuse;
        lock (_sync)
        {
            if ((uint)slot < (uint)_leaseSlots.Length)
            {
                ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
                if (leaseSlot.Generation == slotGeneration
                    && leaseSlot.State == LeaseSlotState.ActiveGuide
                    && leaseSlot.Entry != null)
                {
                    CacheEntry entry = leaseSlot.Entry;
                    entry.LeaseCount--;
                    _activeLeaseCount--;
                    if (entry.LeaseCount == 0)
                    {
                        _leasedBytes = checked(_leasedBytes - entry.Payload.RetainedBytes);
                        if (!entry.IsCached)
                        {
                            _detachedBytes = checked(
                                _detachedBytes - entry.Payload.RetainedBytes);
                        }
                    }
                    RecycleLeaseSlot(slot);
                }
            }
            if (!_disposed
                && canReuse
                && _freeGuideCount < _freeGuides.Length)
            {
                _freeGuides[_freeGuideCount++] = guide;
            }
        }
    }

    internal void RemoveExact(NavigationFlowFieldPayload payload)
    {
        SwiftThrowHelper.ThrowIfNull(payload, nameof(payload));
        InvalidateExact(payload);
    }

    internal void Reset()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            ResetUnderLock();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            ResetUnderLock();
            _freeGuides = Array.Empty<NavigationFlowFieldGuideLease?>();
            _freeGuideCount = 0;
            _disposed = true;
        }
    }

    private bool TryFinishExisting(
        int currentSlot,
        CacheEntry incumbent,
        NavigationFlowFieldStatus status,
        int reservationSlot,
        ref NavigationFlowFieldReservation reservation,
        out NavigationFlowFieldPayloadLease lease)
    {
        lease = default;
        if (status == NavigationFlowFieldStatus.Success)
        {
            if (!CanActivate(incumbent, reservationSlot))
                return false;
            lease = ConvertReservation(
                incumbent,
                reservationSlot,
                ref reservation);
        }
        else
        {
            ReleaseReservationSlot(reservationSlot);
            reservation = default;
        }
        Touch(currentSlot);
        return true;
    }

    private bool TryPublishCandidate(
        NavigationFlowFieldPayload payload,
        NavigationFlowFieldStatus status,
        int currentSlot,
        CacheEntry? incumbent,
        int reservationSlot,
        ref NavigationFlowFieldReservation reservation,
        out NavigationFlowFieldPayloadLease lease)
    {
        lease = default;
        bool reusable = _maximumEntries > 0
            && payload.RetainedBytes <= _maximumReusableBytes;
        if (status == NavigationFlowFieldStatus.Success
            && !CanActivateNew(payload.RetainedBytes, reservationSlot))
        {
            return false;
        }
        if (!reusable && status != NavigationFlowFieldStatus.Success)
        {
            ReleaseReservationSlot(reservationSlot);
            reservation = default;
            return true;
        }

        CacheEntry candidate;
        if (reusable)
        {
            EvictFor(payload.RetainedBytes, currentSlot);
            if (incumbent != null)
                RemoveAt(currentSlot, invalidate: false);
            candidate = new CacheEntry(payload, isCached: true);
            Insert(candidate);
        }
        else
        {
            candidate = new CacheEntry(payload, isCached: false);
        }

        if (status == NavigationFlowFieldStatus.Success)
        {
            lease = ConvertReservation(candidate, reservationSlot, ref reservation);
        }
        else
        {
            ReleaseReservationSlot(reservationSlot);
            reservation = default;
        }
        return true;
    }

    private bool TryCheckout(
        CacheEntry entry,
        out NavigationFlowFieldPayloadLease lease)
    {
        lease = default;
        if (entry.LeaseCount == 0
            && entry.Payload.RetainedBytes > _maximumActivePayloadBytes
                - _leasedBytes
                - _reservedPayloadBytes)
        {
            return false;
        }
        if (!TryIssueLeaseSlot(out int slot, out ulong generation))
            return false;
        ActivateEntry(entry);
        ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
        leaseSlot.State = LeaseSlotState.ActivePayload;
        leaseSlot.Entry = entry;
        _activeLeaseCount++;
        lease = new NavigationFlowFieldPayloadLease(this, slot, generation);
        return true;
    }

    private NavigationFlowFieldPayloadLease ConvertReservation(
        CacheEntry entry,
        int reservationSlot,
        ref NavigationFlowFieldReservation reservation)
    {
        ref LeaseSlot leaseSlot = ref _leaseSlots[reservationSlot];
        _reservedPayloadBytes -= leaseSlot.MaximumBytes;
        _reservedLeaseCount--;
        leaseSlot.MaximumBytes = 0;
        leaseSlot.State = LeaseSlotState.ActivePayload;
        leaseSlot.Entry = entry;
        ActivateEntry(entry);
        _activeLeaseCount++;
        var lease = new NavigationFlowFieldPayloadLease(
            this,
            reservationSlot,
            leaseSlot.Generation);
        reservation = default;
        return lease;
    }

    private void ActivateEntry(CacheEntry entry)
    {
        if (entry.LeaseCount == 0)
        {
            _leasedBytes = checked(_leasedBytes + entry.Payload.RetainedBytes);
            if (!entry.IsCached)
                _detachedBytes = checked(_detachedBytes + entry.Payload.RetainedBytes);
        }
        entry.LeaseCount++;
    }

    private NavigationFlowFieldGuideLease? RentGuideUnderLock()
    {
        if (_freeGuideCount == 0)
            return null;
        int index = --_freeGuideCount;
        NavigationFlowFieldGuideLease guide = _freeGuides[index]!;
        _freeGuides[index] = null;
        return guide;
    }

    private bool CanActivate(CacheEntry entry, int reservationSlot) =>
        entry.LeaseCount != 0
        || CanActivateNew(entry.Payload.RetainedBytes, reservationSlot);

    private bool CanActivateNew(long retainedBytes, int reservationSlot)
    {
        long ownReservation = _leaseSlots[reservationSlot].MaximumBytes;
        long otherReservations = _reservedPayloadBytes - ownReservation;
        return retainedBytes <= _maximumActivePayloadBytes
            - _leasedBytes
            - otherReservations;
    }

    private static NavigationFlowFieldStatus ClassifyCoverage(
        NavigationFlowFieldPayload payload,
        NavigationCellAddress requiredOrigin)
    {
        if (!payload.TryGetNode(
                requiredOrigin,
                payload.Key.StartMedium,
                out NavigationFlowFieldNode origin))
        {
            return payload.IsComplete
                ? NavigationFlowFieldStatus.NoPath
                : NavigationFlowFieldStatus.Pending;
        }
        if (!FixedMathSharp.Fixed64.TryAdd(
                origin.IntegrationCost,
                payload.Key.FlowField.ExtraIntegrationCost,
                out FixedMathSharp.Fixed64 requiredCost))
        {
            return NavigationFlowFieldStatus.CostOverflow;
        }
        return payload.IsComplete || payload.LastSettledCost >= requiredCost
            ? NavigationFlowFieldStatus.Success
            : NavigationFlowFieldStatus.Pending;
    }

    private static PrefixRelation ComparePrefixes(
        NavigationFlowFieldPayload existing,
        NavigationFlowFieldPayload candidate)
    {
        if (existing.Key != candidate.Key)
            throw new InvalidOperationException("Flow payload keys are not compatible.");
        int common = Math.Min(existing.Nodes.Length, candidate.Nodes.Length);
        for (int i = 0; i < common; i++)
        {
            NavigationFlowFieldNode left = existing.Nodes[i];
            NavigationFlowFieldNode right = candidate.Nodes[i];
            if (left.Address != right.Address
                || left.Medium != right.Medium
                || left.IntegrationCost != right.IntegrationCost
                || left.SelectedEdge != right.SelectedEdge
                || left.TransitionInstructionOrdinal
                    != right.TransitionInstructionOrdinal
                || !NodeTransitionInstructionsAreEqual(
                    existing,
                    left,
                    candidate,
                    right))
            {
                throw new InvalidOperationException(
                    "Same-key flow payloads do not share one canonical node prefix.");
            }
        }

        if (existing.Nodes.Length == candidate.Nodes.Length)
        {
            if (existing.IsComplete != candidate.IsComplete
                || existing.WorldChangeSequence != candidate.WorldChangeSequence
                || existing.TransitionInstructions.Length
                    != candidate.TransitionInstructions.Length
                || !DependenciesAreEqual(existing.Dependencies, candidate.Dependencies))
            {
                throw new InvalidOperationException(
                    "Equal flow prefixes do not share exact completion dependencies.");
            }
            return PrefixRelation.Equal;
        }

        NavigationFlowFieldPayload shorter = existing.Nodes.Length < candidate.Nodes.Length
            ? existing
            : candidate;
        NavigationFlowFieldPayload longer = ReferenceEquals(shorter, existing)
            ? candidate
            : existing;
        if (shorter.IsComplete
            || !WorldDependencyIsSubset(shorter, longer)
            || longer.Nodes[shorter.Nodes.Length].IntegrationCost
                <= shorter.LastSettledCost
            || !DependenciesAreSubset(shorter.Dependencies, longer.Dependencies))
        {
            throw new InvalidOperationException(
                "A complete or dependency-incompatible flow payload cannot be a strict prefix.");
        }
        return ReferenceEquals(longer, candidate)
            ? PrefixRelation.CandidateLonger
            : PrefixRelation.ExistingLonger;
    }

    internal bool IsWorldCurrent(NavigationFlowFieldPayload payload) =>
        !payload.WorldChangeSequence.HasValue
        || _world.ChangeSequence == payload.WorldChangeSequence.GetValueOrDefault();

    private static bool DependenciesAreEqual(
        GraphDependencyStamp left,
        GraphDependencyStamp right) =>
        left.Components.Length == right.Components.Length
        && left.Pages.Length == right.Pages.Length
        && left.HasTransitionRuleDependency == right.HasTransitionRuleDependency
        && left.TransitionRuleVersion == right.TransitionRuleVersion
        && DependenciesAreSubset(left, right);

    private static bool DependenciesAreSubset(
        GraphDependencyStamp shorter,
        GraphDependencyStamp longer) =>
        shorter.AreaPolicy == longer.AreaPolicy
        && (!shorter.HasTransitionRuleDependency
            || (longer.HasTransitionRuleDependency
                && shorter.TransitionRuleVersion == longer.TransitionRuleVersion))
        && ComponentDependenciesAreSubset(shorter.Components, longer.Components)
        && PageDependenciesAreSubset(shorter.Pages, longer.Pages);

    private static bool WorldDependencyIsSubset(
        NavigationFlowFieldPayload shorter,
        NavigationFlowFieldPayload longer) =>
        !shorter.WorldChangeSequence.HasValue
        || longer.WorldChangeSequence == shorter.WorldChangeSequence;

    private static bool NodeTransitionInstructionsAreEqual(
        NavigationFlowFieldPayload leftPayload,
        NavigationFlowFieldNode left,
        NavigationFlowFieldPayload rightPayload,
        NavigationFlowFieldNode right)
    {
        int ordinal = left.TransitionInstructionOrdinal;
        if (ordinal < 0)
            return true;
        return (uint)ordinal < (uint)leftPayload.TransitionInstructions.Length
            && (uint)ordinal < (uint)rightPayload.TransitionInstructions.Length
            && TransitionInstructionsAreEqual(
                leftPayload.TransitionInstructions[ordinal],
                rightPayload.TransitionInstructions[ordinal]);
    }

    private static bool TransitionInstructionsAreEqual(
        in NavigationTransitionInstruction left,
        in NavigationTransitionInstruction right) =>
        left.IdentityKind == right.IdentityKind
        && string.Equals(left.OwnerMapId, right.OwnerMapId, StringComparison.Ordinal)
        && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && left.Type == right.Type
        && left.SourceAddress == right.SourceAddress
        && left.DestinationAddress == right.DestinationAddress
        && left.SourceMedium == right.SourceMedium
        && left.DestinationMedium == right.DestinationMedium
        && left.SourcePosition == right.SourcePosition
        && left.DestinationPosition == right.DestinationPosition
        && left.LocomotionHints == right.LocomotionHints;

    private static bool ComponentDependenciesAreSubset(
        GraphComponentDependency[] shorter,
        GraphComponentDependency[] longer)
    {
        int longerIndex = 0;
        for (int i = 0; i < shorter.Length; i++)
        {
            GraphComponentDependency expected = shorter[i];
            while (longerIndex < longer.Length
                && longer[longerIndex].Key.CompareTo(expected.Key) < 0)
            {
                longerIndex++;
            }
            if (longerIndex >= longer.Length
                || !longer[longerIndex].Equals(expected))
            {
                return false;
            }
            longerIndex++;
        }
        return true;
    }

    private static bool PageDependenciesAreSubset(
        GraphPageDependency[] shorter,
        GraphPageDependency[] longer)
    {
        int longerIndex = 0;
        for (int i = 0; i < shorter.Length; i++)
        {
            GraphPageDependency expected = shorter[i];
            while (longerIndex < longer.Length
                && ComparePageAddress(longer[longerIndex], expected) < 0)
            {
                longerIndex++;
            }
            if (longerIndex >= longer.Length
                || !longer[longerIndex].Equals(expected))
            {
                return false;
            }
            longerIndex++;
        }
        return true;
    }

    private static int ComparePageAddress(
        GraphPageDependency left,
        GraphPageDependency right)
    {
        int mapComparison = string.CompareOrdinal(left.MapId, right.MapId);
        return mapComparison != 0
            ? mapComparison
            : left.PageIndex.CompareTo(right.PageIndex);
    }

    private void EvictFor(long candidateBytes, int replacedSlot)
    {
        int prospectiveCount = _count + (replacedSlot >= 0 ? 0 : 1);
        long prospectiveBytes = checked(
            _cachedBytes
            - (replacedSlot >= 0 ? _entries[replacedSlot]!.Payload.RetainedBytes : 0)
            + candidateBytes);
        while (prospectiveCount > _maximumEntries
            || prospectiveBytes > _maximumReusableBytes)
        {
            int eviction = _leastRecent;
            if (eviction == replacedSlot)
                eviction = _next[eviction];
            if (eviction < 0)
                throw new InvalidOperationException("The flow payload LRU is inconsistent.");
            long bytes = _entries[eviction]!.Payload.RetainedBytes;
            RemoveAt(eviction, invalidate: false);
            prospectiveCount--;
            prospectiveBytes -= bytes;
        }
    }

    private void Insert(CacheEntry entry)
    {
        FindSlot(entry.Payload.Key, out int slot, out bool found);
        if (slot < 0 || found)
            throw new InvalidOperationException("The flow payload cache table is inconsistent.");
        _states[slot] = 1;
        _keys[slot] = entry.Payload.Key;
        _entries[slot] = entry;
        LinkMostRecent(slot);
        _count++;
        _cachedBytes = checked(_cachedBytes + entry.Payload.RetainedBytes);
    }

    private bool TryIssueLeaseSlot(out int slot, out ulong generation)
    {
        while (_freeLeaseCount > 0)
        {
            slot = _freeLeaseSlots[--_freeLeaseCount];
            ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
            if (leaseSlot.State != LeaseSlotState.Free)
                throw new InvalidOperationException("The flow lease-slot pool is inconsistent.");
            if (leaseSlot.Generation == ulong.MaxValue)
            {
                leaseSlot.State = LeaseSlotState.Retired;
                continue;
            }
            leaseSlot.Generation++;
            generation = leaseSlot.Generation;
            return true;
        }
        slot = -1;
        generation = 0;
        return false;
    }

    private bool TryGetReservationSlot(
        NavigationFlowFieldReservation reservation,
        out int slot)
    {
        slot = reservation.Slot;
        return ReferenceEquals(reservation.Owner, this)
            && (uint)slot < (uint)_leaseSlots.Length
            && _leaseSlots[slot].Generation == reservation.Generation
            && _leaseSlots[slot].State == LeaseSlotState.Reserved;
    }

    private void ReleaseReservationSlot(int slot)
    {
        ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
        _reservedPayloadBytes -= leaseSlot.MaximumBytes;
        _reservedLeaseCount--;
        RecycleLeaseSlot(slot);
    }

    private void RecycleLeaseSlot(int slot)
    {
        ref LeaseSlot leaseSlot = ref _leaseSlots[slot];
        leaseSlot.Entry = null;
        leaseSlot.MaximumBytes = 0;
        if (leaseSlot.Generation == ulong.MaxValue)
        {
            leaseSlot.State = LeaseSlotState.Retired;
            return;
        }
        leaseSlot.State = LeaseSlotState.Free;
        _freeLeaseSlots[_freeLeaseCount++] = slot;
    }

    private void InvalidateExact(NavigationFlowFieldPayload payload)
    {
        lock (_sync)
        {
            FindSlot(payload.Key, out int slot, out bool found);
            if (found && ReferenceEquals(_entries[slot]!.Payload, payload))
                RemoveAt(slot, invalidate: true);
            for (int i = 0; i < _leaseSlots.Length; i++)
            {
                ref LeaseSlot leaseSlot = ref _leaseSlots[i];
                if ((leaseSlot.State == LeaseSlotState.ActivePayload
                        || leaseSlot.State == LeaseSlotState.ActiveGuide)
                    && leaseSlot.Entry != null
                    && ReferenceEquals(leaseSlot.Entry.Payload, payload))
                {
                    leaseSlot.Entry.IsInvalidated = true;
                }
            }
        }
    }

    private void ResetUnderLock()
    {
        while (_leastRecent >= 0)
            RemoveAt(_leastRecent, invalidate: true);
        Array.Clear(_states, 0, _states.Length);
        for (int i = 0; i < _leaseSlots.Length; i++)
        {
            ref LeaseSlot leaseSlot = ref _leaseSlots[i];
            if (leaseSlot.State == LeaseSlotState.Reserved)
            {
                _reservedPayloadBytes -= leaseSlot.MaximumBytes;
                _reservedLeaseCount--;
                RecycleLeaseSlot(i);
            }
            else if (leaseSlot.State == LeaseSlotState.ActivePayload
                || leaseSlot.State == LeaseSlotState.ActiveGuide)
            {
                leaseSlot.Entry!.IsInvalidated = true;
            }
        }
    }

    private void FindSlot(
        NavigationFlowFieldPayloadKey key,
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
            if (state == 1 && _keys[slot] == key)
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

    private void RemoveAt(int slot, bool invalidate)
    {
        CacheEntry entry = _entries[slot]!;
        _cachedBytes = checked(_cachedBytes - entry.Payload.RetainedBytes);
        entry.IsCached = false;
        entry.IsInvalidated |= invalidate;
        if (entry.LeaseCount != 0)
            _detachedBytes = checked(_detachedBytes + entry.Payload.RetainedBytes);
        Unlink(slot);
        _keys[slot] = default;
        _entries[slot] = null;
        _states[slot] = 2;
        _count--;
    }

    private sealed class CacheEntry
    {
        internal CacheEntry(NavigationFlowFieldPayload payload, bool isCached)
        {
            Payload = payload;
            IsCached = isCached;
        }

        internal NavigationFlowFieldPayload Payload { get; }
        internal int LeaseCount { get; set; }
        internal bool IsCached { get; set; }
        internal bool IsInvalidated { get; set; }
    }

    private struct LeaseSlot
    {
        internal CacheEntry? Entry;
        internal long MaximumBytes;
        internal ulong Generation;
        internal LeaseSlotState State;
    }
}
