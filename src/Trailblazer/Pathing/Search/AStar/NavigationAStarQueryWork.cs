//=======================================================================
// NavigationAStarQueryWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Reports the complete internal lifecycle of one bounded A* query.</summary>
internal enum NavigationAStarQueryStatus : byte
{
    Pending = 0,
    Success = 1,
    Unsupported = 2,
    NoMap = 3,
    InvalidProfile = 4,
    InvalidStart = 5,
    InvalidEnd = 6,
    NoPath = 7,
    BudgetExceeded = 8,
    CostOverflow = 9,
    CapacityExceeded = 10,
    Stale = 11
}

/// <summary>
/// Owns admission and search leases until one immutable payload lease can be returned.
/// </summary>
internal sealed class NavigationAStarQueryWork : IDisposable
{
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationAStarPayloadCache _cache;
    private readonly NavigationAStarWorkspace _workspace;
    private readonly NavigationQueryAdmissionWork _admission;
    private NavigationSurfaceAStarWork? _search;
    private NavigationAStarPayloadLease? _pendingLease;
    private NavigationAStarPayloadLease? _result;
    private NavigationAStarPayloadReservation _payloadReservation;
    private NavigationAStarQueryStatus _readyStatus;
    private int _status;
    private bool _readyToPublish;
    private bool _started;
    private bool _admissionActive;

    internal NavigationAStarQueryWork(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationAStarWorkspace workspace,
        NavigationAStarPayloadCache cache)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        SwiftThrowHelper.ThrowIfNull(cache, nameof(cache));
        _store = store;
        _workspace = workspace;
        _cache = cache;
        _admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
    }

    internal NavigationAStarQueryStatus Status
    {
        get => (NavigationAStarQueryStatus)Volatile.Read(ref _status);
        private set => Volatile.Write(ref _status, (int)value);
    }

    internal bool IsPrepared => _search != null || _readyToPublish;

    internal bool IsReadyToPublish => Volatile.Read(ref _readyToPublish);

    internal void BeginReserved(
        PathQuery query,
        NavigationWorldGraphLease lease,
        ref NavigationAStarPayloadReservation reservation)
    {
        SwiftThrowHelper.ThrowIfNull(lease, nameof(lease));
        if (!reservation.HasLeaseSlot)
            throw new ArgumentException("A batch query requires one payload reservation.", nameof(reservation));
        if (_admissionActive || _search != null || _pendingLease != null || _result != null)
            throw new InvalidOperationException("The A* query work is already active.");
        _started = true;
        Volatile.Write(ref _readyToPublish, false);
        _readyStatus = NavigationAStarQueryStatus.Pending;
        Status = NavigationAStarQueryStatus.Pending;
        _payloadReservation = reservation;
        reservation = default;
        _admission.Begin(lease, query);
        _admissionActive = true;
        if (_admission.Status != NavigationQueryAdmissionStatus.Pending)
            MarkReady(MapAdmissionStatus(_admission.Status));
    }

    internal NavigationAStarQueryStatus PrepareSearchOrCheckout(
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(
            endpointCandidateStepLimit,
            nameof(endpointCandidateStepLimit));
        if (!_started || Status != NavigationAStarQueryStatus.Pending || IsPrepared)
            return Status;

        NavigationQueryAdmissionStatus admissionStatus = _admission.Advance(
            lookupStepLimit,
            endpointCandidateStepLimit);
        if (admissionStatus == NavigationQueryAdmissionStatus.Pending)
            return Status;
        if (admissionStatus != NavigationQueryAdmissionStatus.Success)
        {
            MarkReady(MapAdmissionStatus(admissionStatus));
            return Status;
        }

        NavigationResolvedPathQuery resolved = _admission.Result;
        var key = new NavigationAStarPayloadKey(
            resolved.Query,
            resolved.Start.Address,
            resolved.End.Address);
        bool cacheHit = _cache.TryCheckoutReserved(
            key,
            resolved.Graph,
            ref _payloadReservation,
            out _pendingLease);
        if (cacheHit)
        {
            resolved.Dispose();
            DisposeAdmission();
            MarkReady(MapSearchStatus(_pendingLease.Payload.Status));
            return Status;
        }
        _search = new NavigationSurfaceAStarWork(
            resolved,
            _workspace,
            _cache.MaximumSinglePayloadBytes);
        DisposeAdmission();
        return Status;
    }

    internal NavigationAStarQueryStatus AdvanceSearch(
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(connectionStepLimit, nameof(connectionStepLimit));
        if (Status != NavigationAStarQueryStatus.Pending || IsReadyToPublish)
            return Status;
        if (_search == null)
            return Status;

        NavigationSurfaceAStarStatus searchStatus = _search.Advance(
            lookupStepLimit,
            nodeStepLimit,
            edgeStepLimit,
            connectionStepLimit);
        if (searchStatus != NavigationSurfaceAStarStatus.Pending)
            MarkReady(MapSearchStatus(searchStatus));
        return Status;
    }

    internal NavigationAStarQueryStatus Publish()
    {
        if (!IsReadyToPublish)
            return Status;
        if (_pendingLease != null)
        {
            NavigationAStarPayloadLease cached = _pendingLease;
            _pendingLease = null;
            return FinishCached(cached);
        }
        if (_readyStatus is not NavigationAStarQueryStatus.Success
            and not NavigationAStarQueryStatus.NoPath)
        {
            return Finish(_readyStatus);
        }

        NavigationAStarPayload payload = _search!.Result;
        if (!_store.Current.IsDependencyCurrent(payload.Dependencies))
            return Finish(NavigationAStarQueryStatus.Stale);
        if (!_cache.TryPublish(
                payload,
                _store,
                ref _payloadReservation,
                out NavigationAStarPayloadLease published))
        {
            if (!_store.Current.IsDependencyCurrent(payload.Dependencies))
                return Finish(NavigationAStarQueryStatus.Stale);
            return _readyStatus == NavigationAStarQueryStatus.Success
                ? Finish(NavigationAStarQueryStatus.CapacityExceeded)
                : Finish(_readyStatus);
        }
        if (!_store.Current.IsDependencyCurrent(published.Payload.Dependencies))
        {
            _cache.RemoveExact(published.Payload);
            published.Dispose();
            return Finish(NavigationAStarQueryStatus.Stale);
        }
        return _readyStatus == NavigationAStarQueryStatus.Success
            ? FinishSuccess(published)
            : FinishNegative(published, _readyStatus);
    }

    internal NavigationAStarPayloadLease TakeResult()
    {
        if (Status != NavigationAStarQueryStatus.Success || _result == null)
            throw new InvalidOperationException("The A* query has no successful payload lease.");
        NavigationAStarPayloadLease result = _result;
        _result = null;
        return result;
    }

    public void Dispose()
    {
        DisposeAdmission();
        _search?.Dispose();
        _search = null;
        _pendingLease?.Dispose();
        _pendingLease = null;
        _result?.Dispose();
        _result = null;
        _cache.ReleasePayloadReservation(ref _payloadReservation);
        Volatile.Write(ref _readyToPublish, false);
    }

    private void MarkReady(NavigationAStarQueryStatus status)
    {
        _readyStatus = status;
        Volatile.Write(ref _readyToPublish, true);
    }

    private NavigationAStarQueryStatus FinishCached(
        NavigationAStarPayloadLease lease)
    {
        if (!_store.Current.IsDependencyCurrent(lease.Payload.Dependencies))
        {
            _cache.RemoveExact(lease.Payload);
            lease.Dispose();
            return Finish(NavigationAStarQueryStatus.Stale);
        }
        NavigationAStarQueryStatus status = MapSearchStatus(lease.Payload.Status);
        return status == NavigationAStarQueryStatus.Success
            ? FinishSuccess(lease)
            : FinishNegative(lease, status);
    }

    private NavigationAStarQueryStatus FinishSuccess(
        NavigationAStarPayloadLease lease)
    {
        _result = lease;
        return Finish(NavigationAStarQueryStatus.Success);
    }

    private NavigationAStarQueryStatus FinishNegative(
        NavigationAStarPayloadLease lease,
        NavigationAStarQueryStatus status)
    {
        lease.Dispose();
        return Finish(status);
    }

    private NavigationAStarQueryStatus Finish(NavigationAStarQueryStatus status)
    {
        Status = status;
        Volatile.Write(ref _readyToPublish, false);
        DisposeAdmission();
        _search?.Dispose();
        _search = null;
        _cache.ReleasePayloadReservation(ref _payloadReservation);
        return status;
    }

    private void DisposeAdmission()
    {
        if (!_admissionActive)
            return;
        _admission.Dispose();
        _admissionActive = false;
    }

    private static NavigationAStarQueryStatus MapAdmissionStatus(
        NavigationQueryAdmissionStatus status) => status switch
        {
            NavigationQueryAdmissionStatus.Success => NavigationAStarQueryStatus.Success,
            NavigationQueryAdmissionStatus.Unsupported => NavigationAStarQueryStatus.Unsupported,
            NavigationQueryAdmissionStatus.NoMap => NavigationAStarQueryStatus.NoMap,
            NavigationQueryAdmissionStatus.InvalidProfile => NavigationAStarQueryStatus.InvalidProfile,
            NavigationQueryAdmissionStatus.InvalidStart => NavigationAStarQueryStatus.InvalidStart,
            NavigationQueryAdmissionStatus.InvalidEnd => NavigationAStarQueryStatus.InvalidEnd,
            NavigationQueryAdmissionStatus.BudgetExceeded => NavigationAStarQueryStatus.BudgetExceeded,
            NavigationQueryAdmissionStatus.CostOverflow => NavigationAStarQueryStatus.CostOverflow,
            NavigationQueryAdmissionStatus.CapacityExceeded => NavigationAStarQueryStatus.CapacityExceeded,
            NavigationQueryAdmissionStatus.Stale => NavigationAStarQueryStatus.Stale,
            _ => NavigationAStarQueryStatus.Pending
        };

    private static NavigationAStarQueryStatus MapSearchStatus(
        NavigationSurfaceAStarStatus status) => status switch
        {
            NavigationSurfaceAStarStatus.Success => NavigationAStarQueryStatus.Success,
            NavigationSurfaceAStarStatus.NoPath => NavigationAStarQueryStatus.NoPath,
            NavigationSurfaceAStarStatus.BudgetExceeded => NavigationAStarQueryStatus.BudgetExceeded,
            NavigationSurfaceAStarStatus.CostOverflow => NavigationAStarQueryStatus.CostOverflow,
            NavigationSurfaceAStarStatus.CapacityExceeded => NavigationAStarQueryStatus.CapacityExceeded,
            NavigationSurfaceAStarStatus.Stale => NavigationAStarQueryStatus.Stale,
            _ => NavigationAStarQueryStatus.Pending
        };
}
