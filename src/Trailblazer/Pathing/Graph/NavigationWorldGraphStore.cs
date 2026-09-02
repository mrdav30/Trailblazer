//=======================================================================
// NavigationWorldGraphStore.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Publishes immutable roots and retains only generations with active leases.</summary>
internal sealed class NavigationWorldGraphStore : System.IDisposable
{
    private readonly object _sync = new();
    private readonly SwiftList<NavigationWorldGraph> _retired = new();
    private readonly SwiftList<NavigationWorldGraphLease> _leasePool = new();
    private NavigationWorldGraph _current = NavigationWorldGraph.Empty;

    private readonly int _maxRetiredSnapshots;
    private readonly int _maxActiveSnapshots;
    private readonly long _maxRetiredBytes;
    private readonly long _maxActiveBytes;
    private readonly int _maxPersistentPages;
    private readonly int _maxConcurrentLeases;
    private int _activeLeaseCount;
    private bool _safetyPending;
    private bool _disposed;

    internal NavigationWorldGraphStore(
        int maxActiveSnapshots,
        int maxRetiredSnapshots,
        long maxRetiredBytes,
        long maxActiveBytes,
        int maxPersistentPages,
        int maxConcurrentLeases)
    {
        _maxActiveSnapshots = maxActiveSnapshots;
        _maxRetiredSnapshots = maxRetiredSnapshots;
        _maxRetiredBytes = maxRetiredBytes;
        _maxActiveBytes = maxActiveBytes;
        _maxPersistentPages = maxPersistentPages;
        _maxConcurrentLeases = maxConcurrentLeases;
    }

    internal NavigationWorldGraph Current => Volatile.Read(ref _current);

    internal int ActiveLeaseCount
    {
        get { lock (_sync) return _activeLeaseCount; }
    }

    internal int ActiveGenerationCount
    {
        get
        {
            lock (_sync)
            {
                CollectReleased();
                return 1 + _retired.Count;
            }
        }
    }

    internal long RetiredBytes
    {
        get
        {
            lock (_sync)
            {
                CollectReleased();
                long bytes = 0;
                for (int i = 0; i < _retired.Count; i++)
                    bytes += _retired[i].RetainedBytes;
                return bytes;
            }
        }
    }

    internal int RetiredGenerationCount
    {
        get
        {
            lock (_sync)
            {
                CollectReleased();
                return _retired.Count;
            }
        }
    }

    internal NavigationWorldGraphLease? TryAcquire()
    {
        lock (_sync)
        {
            CollectReleased();
            if (_disposed
                || _safetyPending
                || _activeLeaseCount >= _maxConcurrentLeases
                || WouldExceedRetiredCapacityAfterLease(Current))
                return null;
            NavigationWorldGraph current = Current;
            return RentLeaseUnderLock(current);
        }
    }

    internal int TryAcquirePrefix(Span<NavigationWorldGraphLease?> output)
    {
        lock (_sync)
        {
            output.Clear();
            CollectReleased();
            if (_disposed || _safetyPending || output.Length == 0)
                return 0;
            NavigationWorldGraph current = Current;
            if (WouldExceedRetiredCapacityAfterLease(current))
                return 0;
            int count = System.Math.Min(
                output.Length,
                _maxConcurrentLeases - _activeLeaseCount);
            for (int i = 0; i < count; i++)
                output[i] = RentLeaseUnderLock(current);
            return count;
        }
    }

    internal void Return(NavigationWorldGraphLease lease)
    {
        NavigationWorldGraph graph = lease.DetachGraph();
        graph.Return();
        lock (_sync)
        {
            _activeLeaseCount--;
            CollectReleased();
            if (!_disposed && _leasePool.Count < _maxConcurrentLeases)
                _leasePool.Add(lease);
        }
    }

    internal NavigationCandidatePublication TryPublish(NavigationWorldGraph next)
    {
        lock (_sync)
        {
            CollectReleased();
            if (_disposed)
                return NavigationCandidatePublication.Deferred;
            if (next.GraphVersion <= _current.GraphVersion)
            {
                throw new InvalidOperationException(
                    "Published navigation graph versions must increase monotonically.");
            }
            if (next.RetainedBytes > _maxActiveBytes
                || next.PersistentPageCount > _maxPersistentPages)
                return NavigationCandidatePublication.PermanentCapacity;
            if (WouldExceedRetiredCapacity(_current))
                return NavigationCandidatePublication.Deferred;
            NavigationWorldGraph prior = _current;
            Volatile.Write(ref _current, next);
            if (prior.LeaseCount > 0)
                _retired.Add(prior);
            CollectReleased();
            return NavigationCandidatePublication.Published;
        }
    }

    internal bool CanPublish
    {
        get
        {
            lock (_sync)
            {
                CollectReleased();
                return !_disposed && !WouldExceedRetiredCapacity(_current);
            }
        }
    }

    internal bool IsSafetyPending
    {
        get { lock (_sync) return _safetyPending; }
    }

    internal void MarkSafetyPending()
    {
        SetSafetyPending(true);
    }

    internal void ClearSafetyPending()
    {
        SetSafetyPending(false);
    }

    internal void SetSafetyPending(bool pending)
    {
        lock (_sync)
            _safetyPending = pending;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _leasePool.Clear();
            CollectReleased();
        }
    }

    private void CollectReleased()
    {
        int destination = 0;
        for (int i = 0; i < _retired.Count; i++)
        {
            NavigationWorldGraph graph = _retired[i];
            if (graph.LeaseCount > 0)
                _retired[destination++] = graph;
        }
        while (_retired.Count > destination)
            _retired.RemoveAt(_retired.Count - 1);
    }

    private NavigationWorldGraphLease RentLeaseUnderLock(
        NavigationWorldGraph current)
    {
        _activeLeaseCount++;
        if (_leasePool.Count == 0)
            return new NavigationWorldGraphLease(this, current);
        NavigationWorldGraphLease lease = _leasePool[_leasePool.Count - 1];
        _leasePool.RemoveAt(_leasePool.Count - 1);
        lease.Reinitialize(this, current);
        return lease;
    }

    private bool WouldExceedRetiredCapacity(NavigationWorldGraph current)
    {
        if (current.LeaseCount == 0)
            return false;
        if (_retired.Count >= _maxRetiredSnapshots
            || _retired.Count + 2 > _maxActiveSnapshots)
            return true;

        long bytes = current.RetainedBytes;
        for (int i = 0; i < _retired.Count; i++)
            bytes += _retired[i].RetainedBytes;
        return bytes > _maxRetiredBytes;
    }

    private bool WouldExceedRetiredCapacityAfterLease(NavigationWorldGraph current)
    {
        // A disabled retirement budget is a writer-blocking mode, not a reader ban.
        // Publication already refuses to retire a leased current root until all readers return.
        if (_maxRetiredSnapshots == 0 || _maxRetiredBytes == 0)
            return false;
        if (_retired.Count >= _maxRetiredSnapshots
            || _retired.Count + 2 > _maxActiveSnapshots)
            return true;
        long bytes = current.RetainedBytes;
        for (int i = 0; i < _retired.Count; i++)
            bytes += _retired[i].RetainedBytes;
        return bytes > _maxRetiredBytes;
    }
}
