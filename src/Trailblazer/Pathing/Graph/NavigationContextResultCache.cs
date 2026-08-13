//=======================================================================
// NavigationContextResultCache.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Owns dependency-validated immutable result entries under one context cache gate.
/// </summary>
internal sealed class NavigationContextResultCache<TPayload> : IDisposable
    where TPayload : class
{
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationContextCacheGate _gate;
    private readonly int _maxEntries;
    private readonly int _maxActiveLeases;
    private readonly SwiftDictionary<PathRequestCacheKey, NavigationResultCacheEntry<TPayload>> _entries = new();
    private readonly SwiftList<NavigationResultEntryLease<TPayload>> _leasePool = new();
    private long _cachedBytes;
    private long _detachedBytes;
    private long _discardedDuplicateBytes;
    private int _activeLeaseCount;
    private bool _disposed;

    internal NavigationContextResultCache(
        NavigationWorldGraphStore store,
        TrailblazerWorldContextSettings settings)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(settings, nameof(settings));
        _store = store;
        _gate = store.CacheGate;
        _maxEntries = settings.MaxConcurrentPathQueries;
        _maxActiveLeases = settings.MaxConcurrentPathQueries;
    }

    internal int EntryCount
    {
        get { lock (_gate.SyncRoot) return _entries.Count; }
    }

    internal long CachedBytes
    {
        get { lock (_gate.SyncRoot) return _cachedBytes; }
    }

    internal long DetachedBytes
    {
        get { lock (_gate.SyncRoot) return _detachedBytes; }
    }

    internal long DiscardedDuplicateBytes
    {
        get { lock (_gate.SyncRoot) return _discardedDuplicateBytes; }
    }

    internal bool TryCheckout(
        PathRequestCacheKey key,
        out NavigationResultEntryLease<TPayload>? lease)
    {
        if (!key.IsInitialized)
        {
            lease = null;
            return false;
        }

        NavigationWorldGraphLease? graphLease = _store.TryAcquire();
        if (graphLease == null)
        {
            lease = null;
            return false;
        }

        try
        {
            lock (_gate.SyncRoot)
            {
                if (_disposed
                    || !_entries.TryGetValue(key, out NavigationResultCacheEntry<TPayload> entry))
                {
                    lease = null;
                    return false;
                }

                NavigationWorldGraph current = _store.Current;
                if (!_gate.IsSafetyEpochCurrent(entry.SafetyEpoch)
                    || !current.IsDependencyCurrent(entry.Dependency))
                {
                    RemoveCachedEntry(entry);
                    lease = null;
                    return false;
                }
                if (_activeLeaseCount >= _maxActiveLeases)
                {
                    lease = null;
                    return false;
                }
                if (!graphLease.Graph.IsDependencyCurrent(entry.Dependency))
                {
                    lease = null;
                    return false;
                }

                entry.ActiveCheckoutCount++;
                _activeLeaseCount++;
                lease = RentLease(entry);
                return true;
            }
        }
        finally
        {
            graphLease.Dispose();
        }
    }

    internal NavigationResultCacheStatus TryCreateDetached(
        NavigationQueryAdmissionLease query,
        PathRequestCacheKey key,
        TPayload payload,
        GraphDependencyStamp dependency,
        long payloadBytes,
        out NavigationResultEntryLease<TPayload>? lease)
    {
        SwiftThrowHelper.ThrowIfNull(query, nameof(query));
        SwiftThrowHelper.ThrowIfNull(payload, nameof(payload));
        SwiftThrowHelper.ThrowIfNull(dependency, nameof(dependency));
        SwiftThrowHelper.ThrowIfArgument(!key.IsInitialized, nameof(key), "Result key must be initialized.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(payloadBytes <= 0, null, nameof(payloadBytes));

        if (!query.Graph.IsDependencyCurrent(dependency))
        {
            lease = null;
            return NavigationResultCacheStatus.Stale;
        }

        lock (_gate.SyncRoot)
        {
            if (_disposed)
            {
                lease = null;
                return NavigationResultCacheStatus.Disposed;
            }
            NavigationWorldGraph current = _store.Current;
            if (!_gate.IsSafetyEpochCurrent(query.SafetyEpoch)
                || !current.IsDependencyCurrent(dependency))
            {
                lease = null;
                return NavigationResultCacheStatus.Stale;
            }
            if (_activeLeaseCount >= _maxActiveLeases
                || payloadBytes > query.ResultBytes)
            {
                lease = null;
                return NavigationResultCacheStatus.CapacityExceeded;
            }

            var entry = new NavigationResultCacheEntry<TPayload>(
                key,
                payload,
                dependency,
                payloadBytes,
                query.SafetyEpoch);
            entry.ActiveCheckoutCount = 1;
            _gate.TransferReservedResultToPayloadUnderGate(payloadBytes);
            query.TransferReservationToPayload(payloadBytes);
            _detachedBytes += payloadBytes;
            _activeLeaseCount++;
            lease = RentLease(entry);
            return NavigationResultCacheStatus.Detached;
        }
    }

    internal NavigationResultCacheStatus TryPromote(
        NavigationQueryAdmissionLease query,
        NavigationResultEntryLease<TPayload> candidate)
    {
        SwiftThrowHelper.ThrowIfNull(query, nameof(query));
        SwiftThrowHelper.ThrowIfNull(candidate, nameof(candidate));
        NavigationResultCacheEntry<TPayload>? entry = candidate.Entry;
        SwiftThrowHelper.ThrowIfArgument(
            entry == null || !ReferenceEquals(candidate.Owner, this),
            nameof(candidate),
            "Result lease does not belong to this cache.");

        if (!query.Graph.IsDependencyCurrent(entry!.Dependency))
            return NavigationResultCacheStatus.Stale;

        lock (_gate.SyncRoot)
        {
            if (_disposed)
                return NavigationResultCacheStatus.Disposed;
            NavigationWorldGraph current = _store.Current;
            if (!ReferenceEquals(candidate.Entry, entry)
                || !_gate.IsSafetyEpochCurrent(query.SafetyEpoch)
                || entry.SafetyEpoch != query.SafetyEpoch
                || !current.IsDependencyCurrent(entry.Dependency))
                return NavigationResultCacheStatus.Stale;
            if (entry.IsCached)
                return NavigationResultCacheStatus.Published;

            if (_entries.TryGetValue(entry.Key, out NavigationResultCacheEntry<TPayload> existing))
            {
                if (!_gate.IsSafetyEpochCurrent(existing.SafetyEpoch)
                    || !current.IsDependencyCurrent(existing.Dependency))
                {
                    RemoveCachedEntry(existing);
                }
                else if (!query.Graph.IsDependencyCurrent(existing.Dependency))
                {
                    return NavigationResultCacheStatus.Stale;
                }
                else
                {
                    entry.ActiveCheckoutCount--;
                    _detachedBytes -= entry.PayloadBytes;
                    _gate.ReleasePayloadResultBytesUnderGate(entry.PayloadBytes);
                    _discardedDuplicateBytes = checked(
                        _discardedDuplicateBytes + entry.PayloadBytes);
                    existing.ActiveCheckoutCount++;
                    candidate.Rebind(existing);
                    return NavigationResultCacheStatus.ReusedExisting;
                }
            }

            if (_entries.Count >= _maxEntries)
                return NavigationResultCacheStatus.CapacityExceeded;

            _entries.Add(entry.Key, entry);
            entry.IsCached = true;
            _detachedBytes -= entry.PayloadBytes;
            _cachedBytes += entry.PayloadBytes;
            return NavigationResultCacheStatus.Published;
        }
    }

    internal void Return(NavigationResultEntryLease<TPayload> lease)
    {
        NavigationResultCacheEntry<TPayload> entry = lease.Detach();
        lock (_gate.SyncRoot)
        {
            if (entry.IsCached
                && (_disposed
                    || !_gate.IsSafetyEpochCurrent(entry.SafetyEpoch)
                    || !_store.Current.IsDependencyCurrent(entry.Dependency)))
            {
                RemoveCachedEntry(entry);
            }

            entry.ActiveCheckoutCount--;
            if (!entry.IsCached && entry.ActiveCheckoutCount == 0)
            {
                _detachedBytes -= entry.PayloadBytes;
                _gate.ReleasePayloadResultBytesUnderGate(entry.PayloadBytes);
            }
            _activeLeaseCount--;
            if (!_disposed && _leasePool.Count < _maxActiveLeases)
                _leasePool.Add(lease);
        }
    }

    public void Dispose()
    {
        lock (_gate.SyncRoot)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (KeyValuePair<PathRequestCacheKey, NavigationResultCacheEntry<TPayload>> pair in _entries)
            {
                NavigationResultCacheEntry<TPayload> entry = pair.Value;
                entry.IsCached = false;
                if (entry.ActiveCheckoutCount > 0)
                    _detachedBytes += entry.PayloadBytes;
                else
                    _gate.ReleasePayloadResultBytesUnderGate(entry.PayloadBytes);
            }
            _entries.Clear();
            _cachedBytes = 0;
            _leasePool.Clear();
        }
    }

    private NavigationResultEntryLease<TPayload> RentLease(
        NavigationResultCacheEntry<TPayload> entry)
    {
        if (_leasePool.Count == 0)
            return new NavigationResultEntryLease<TPayload>(this, entry);
        NavigationResultEntryLease<TPayload> lease = _leasePool[_leasePool.Count - 1];
        _leasePool.RemoveAt(_leasePool.Count - 1);
        lease.Reinitialize(this, entry);
        return lease;
    }

    private void RemoveCachedEntry(NavigationResultCacheEntry<TPayload> entry)
    {
        if (!entry.IsCached)
            return;
        _entries.Remove(entry.Key);
        entry.IsCached = false;
        _cachedBytes -= entry.PayloadBytes;
        if (entry.ActiveCheckoutCount > 0)
            _detachedBytes += entry.PayloadBytes;
        else
            _gate.ReleasePayloadResultBytesUnderGate(entry.PayloadBytes);
    }
}

/// <summary>Stores one immutable payload and its exact graph dependencies.</summary>
internal sealed class NavigationResultCacheEntry<TPayload> where TPayload : class
{
    internal NavigationResultCacheEntry(
        PathRequestCacheKey key,
        TPayload payload,
        GraphDependencyStamp dependency,
        long payloadBytes,
        long safetyEpoch)
    {
        Key = key;
        Payload = payload;
        Dependency = dependency;
        PayloadBytes = payloadBytes;
        SafetyEpoch = safetyEpoch;
    }

    internal PathRequestCacheKey Key { get; }
    internal TPayload Payload { get; }
    internal GraphDependencyStamp Dependency { get; }
    internal long PayloadBytes { get; }
    internal long SafetyEpoch { get; }
    internal int ActiveCheckoutCount { get; set; }
    internal bool IsCached { get; set; }
}
