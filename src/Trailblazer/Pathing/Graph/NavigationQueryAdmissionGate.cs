//=======================================================================
// NavigationQueryAdmissionGate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Serializes deterministic query admission and enforces snapshot-before-cache-gate lock order.</summary>
internal sealed class NavigationQueryAdmissionGate : IDisposable
{
    private readonly object _admissionSync = new();
    private readonly NavigationContextCacheGate _cacheGate;
    private readonly NavigationWorldGraphStore _store;
    private readonly PathQueryWorkspacePool _workspaces;
    private readonly int _maxConcurrent;
    private readonly int[] _sortIndexes;
    private readonly long[] _sortOrdinals;
    private readonly SwiftList<NavigationQueryAdmissionLease> _leasePool = new();
    private int _activeCount;
    private bool _disposed;

    internal NavigationQueryAdmissionGate(
        NavigationWorldGraphStore store,
        PathQueryWorkspacePool workspaces,
        TrailblazerWorldContextSettings settings)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(workspaces, nameof(workspaces));
        SwiftThrowHelper.ThrowIfNull(settings, nameof(settings));
        _store = store;
        _cacheGate = store.CacheGate;
        _workspaces = workspaces;
        _maxConcurrent = settings.MaxConcurrentPathQueries;
        _sortIndexes = new int[settings.OperationLimits.MaxBatchItems];
        _sortOrdinals = new long[settings.OperationLimits.MaxBatchItems];
    }

    internal long ActiveResultBytes
    {
        get { return _cacheGate.ReservedResultBytes; }
    }

    internal int ActiveCount
    {
        get { lock (_cacheGate.SyncRoot) return _activeCount; }
    }

    internal bool TryAdmit(
        in NavigationQueryAdmissionRequest request,
        out NavigationQueryAdmissionLease? lease)
    {
        lock (_admissionSync)
            return TryAdmitOrdered(request, out lease);
    }

    internal int AdmitBatch(
        ReadOnlySpan<NavigationQueryAdmissionRequest> requests,
        Span<NavigationQueryAdmissionLease?> leases)
    {
        SwiftThrowHelper.ThrowIfArgument(
            requests.Length > _sortIndexes.Length || leases.Length < requests.Length,
            nameof(requests),
            "Batch exceeds configured sort scratch or output capacity.");
        leases.Slice(0, requests.Length).Clear();
        lock (_admissionSync)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                _sortIndexes[i] = i;
                _sortOrdinals[i] = requests[i].OperationOrdinal;
            }
            Array.Sort(_sortOrdinals, _sortIndexes, 0, requests.Length);
            for (int i = 1; i < requests.Length; i++)
            {
                SwiftThrowHelper.ThrowIfArgument(
                    _sortOrdinals[i] == _sortOrdinals[i - 1],
                    nameof(requests),
                    "Batch operation ordinals must be unique.");
            }

            int admitted = 0;
            for (int sorted = 0; sorted < requests.Length; sorted++)
            {
                int requestIndex = _sortIndexes[sorted];
                if (!TryAdmitOrdered(requests[requestIndex], out NavigationQueryAdmissionLease? lease))
                    break;
                leases[requestIndex] = lease;
                admitted++;
            }
            return admitted;
        }
    }

    internal void Return(NavigationQueryAdmissionLease lease)
    {
        lease.Detach(
            out NavigationWorldGraphLease graphLease,
            out PathQueryWorkspaceLease workspaceLease,
            out long resultBytes);
        lock (_cacheGate.SyncRoot)
        {
            _activeCount--;
            _cacheGate.ReleaseReservedResultBytesUnderGate(resultBytes);
            workspaceLease.Dispose();
            if (!_disposed && _leasePool.Count < _maxConcurrent)
                _leasePool.Add(lease);
        }
        graphLease.Dispose();
    }

    public void Dispose()
    {
        lock (_admissionSync)
        {
            lock (_cacheGate.SyncRoot)
            {
                _disposed = true;
                _leasePool.Clear();
            }
        }
    }

    private bool TryAdmitOrdered(
        in NavigationQueryAdmissionRequest request,
        out NavigationQueryAdmissionLease? lease)
    {
        if (_disposed)
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

        lock (_cacheGate.SyncRoot)
        {
            if (_disposed
                || _cacheGate.IsSafetyPending
                || _activeCount >= _maxConcurrent
                || !_cacheGate.CanReserveResultBytesUnderGate(request.MaximumResultBytes)
                || !_workspaces.TryCheckout(request.MinimumNodeCapacity, out PathQueryWorkspaceLease? workspaceLease))
            {
                graphLease.Dispose();
                lease = null;
                return false;
            }

            _activeCount++;
            _cacheGate.ReserveResultBytesUnderGate(request.MaximumResultBytes);
            if (_leasePool.Count == 0)
            {
                lease = new NavigationQueryAdmissionLease(
                    this,
                    graphLease,
                    workspaceLease!,
                    request.MaximumResultBytes,
                    _cacheGate.SafetyEpoch);
            }
            else
            {
                lease = _leasePool[_leasePool.Count - 1];
                _leasePool.RemoveAt(_leasePool.Count - 1);
                lease.Reinitialize(
                    this,
                    graphLease,
                    workspaceLease!,
                    request.MaximumResultBytes,
                    _cacheGate.SafetyEpoch);
            }
            return true;
        }
    }
}
