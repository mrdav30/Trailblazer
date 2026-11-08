//=======================================================================
// NavigationAStarQueryWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics;
using System.Threading;
using GridForge.Grids;

namespace Trailblazer.Pathing;

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
    private bool _diagnosticsRecorded;
    private NavigationCohortSource? _sharedSource;
    private NavigationEndpointProofWork _endpointProofWork;
    private bool _endpointProofActive;
    private long _endpointProofReservation;

    internal NavigationAStarQueryWork(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationAStarWorkspace workspace,
        NavigationAStarPayloadCache cache)
    {
        ChronicleGuard.ThrowIfNull(world, nameof(world));
        ChronicleGuard.ThrowIfNull(store, nameof(store));
        ChronicleGuard.ThrowIfNull(workspace, nameof(workspace));
        ChronicleGuard.ThrowIfNull(cache, nameof(cache));
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
        ref NavigationAStarPayloadReservation reservation,
        NavigationCohortSource? sharedSource = null)
    {
        ChronicleGuard.ThrowIfNull(lease, nameof(lease));
        ChronicleGuard.ThrowIfArgument(
            !reservation.HasLeaseSlot && sharedSource == null,
            paramName: nameof(reservation),
            message: "A batch query requires one payload reservation.");
        ChronicleGuard.ThrowIfTrue(
            _started,
            message: "The A* query work is already active.");
        _started = true;
        _sharedSource = sharedSource;
        _endpointProofWork = default;
        _endpointProofActive = false;
        _diagnosticsRecorded = false;
        Volatile.Write(ref _readyToPublish, false);
        _readyStatus = NavigationAStarQueryStatus.Pending;
        Status = NavigationAStarQueryStatus.Pending;
        _payloadReservation = reservation;
        reservation = default;
        _admission.Begin(
            lease,
            query,
            query.Traversal.StartMedium,
            query.Traversal.TargetMedia);
        _admissionActive = true;
        if (_admission.Status != NavigationQueryAdmissionStatus.Pending)
            MarkReady(MapAdmissionStatus(_admission.Status));
    }

    internal NavigationAStarQueryStatus PrepareSearchOrCheckout(
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        var remaining = new NavigationAcquisitionSlice(lookupStepLimit, endpointCandidateStepLimit);
        return PrepareSearchOrCheckout(ref remaining);
    }

    internal NavigationAStarQueryStatus PrepareSearchOrCheckout(ref NavigationAcquisitionSlice remaining)
    {
        if (!_started || Status != NavigationAStarQueryStatus.Pending || IsPrepared)
            return Status;

        NavigationQueryAdmissionStatus admissionStatus = _admission.Advance(ref remaining);
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
            resolved.End.Address,
            resolved.StartMedium,
            resolved.TargetMedia);
        // Even an identical key may admit additional endpoint dependencies after
        // publication. Certify every cached admission, not only cohort followers.
        bool cached = _pendingLease != null;
        if (_sharedSource == null && !cached)
            cached = _cache.TryCheckoutReserved(key, resolved.Graph, ref _payloadReservation, out _pendingLease);
        if (_sharedSource != null || cached)
        {
            if (!_endpointProofActive)
            {
                NavigationAStarPayloadLease? pin = _sharedSource?.AStar;
                NavigationAStarPayloadCache.CacheEntry? sourceEntry = _sharedSource == null
                    ? _pendingLease!.GetOwnedEntry(_cache)
                    : pin?.Generation == _sharedSource.AStarGeneration
                        ? pin!.GetOwnedEntry(_cache) : null;
                if (sourceEntry == null)
                {
                    resolved.Dispose();
                    DisposeAdmission();
                    MarkReady(NavigationAStarQueryStatus.Stale);
                    return Status;
                }
                _endpointProofWork = new NavigationEndpointProofWork(_workspace.EndpointWorkspace, resolved,
                    sourceEntry.Payload.Dependencies);
                _endpointProofActive = true;
            }
            NavigationQueryAdmissionStatus proofStatus = _endpointProofWork.AdvanceCertification(ref remaining);
            if (proofStatus == NavigationQueryAdmissionStatus.Success && !_endpointProofWork.IsCertified)
            {
                if (_endpointProofReservation == 0)
                {
                    long bytes = GraphDependencyStamp.GetRetainedBytes(_workspace.EndpointWorkspace.ComponentCount,
                        _workspace.EndpointWorkspace.PageCount);
                    if (_cache.TryReserveEndpointProof(bytes)) _endpointProofReservation = bytes;
                    else proofStatus = NavigationQueryAdmissionStatus.CapacityExceeded;
                }
                if (proofStatus == NavigationQueryAdmissionStatus.Success)
                    proofStatus = _endpointProofWork.AdvanceCapture(ref remaining);
            }
            if (proofStatus == NavigationQueryAdmissionStatus.Pending)
                return Status;
            NavigationAStarQueryStatus sharedStatus = MapAdmissionStatus(proofStatus);
            if (proofStatus == NavigationQueryAdmissionStatus.Success)
            {
                if (_sharedSource != null)
                    sharedStatus = _cache.TryCheckoutShared(_store, resolved.Graph, key, _sharedSource,
                        _endpointProofWork.Proof, out _pendingLease, _endpointProofReservation);
                else
                {
                    // The gate owns this private checkout until publication or serialized cancellation.
                    if (!_endpointProofWork.IsCertified)
                        _cache.TryAttachEndpointProof(_pendingLease!, _endpointProofWork.Proof!, ref _endpointProofReservation);
                    _cache.AcquisitionDiagnostics.RecordCacheCheckoutHit();
                    sharedStatus = MapSearchStatus(_pendingLease!.Payload.Status);
                }
            }
            if (sharedStatus is not NavigationAStarQueryStatus.Success and not NavigationAStarQueryStatus.NoPath)
            {
                _pendingLease?.Dispose();
                _pendingLease = null;
            }
            _cache.ReleaseEndpointProof(ref _endpointProofReservation);
            _endpointProofWork = default;
            _endpointProofActive = false;
            if (_sharedSource != null && sharedStatus == NavigationAStarQueryStatus.Success)
                _cache.AcquisitionDiagnostics.RecordSharedFollowerCheckout();
            else if (_sharedSource != null && sharedStatus == NavigationAStarQueryStatus.Pending)
                _cache.AcquisitionDiagnostics.RecordSharedCoverageMiss();
            resolved.Dispose();
            DisposeAdmission();
            MarkReady(sharedStatus);
            return Status;
        }
        NavigationRayWork rayWork = _admission.RayWork;
        DisposeAdmission();
        _cache.AcquisitionDiagnostics.RecordBuildStart();
        _search = new NavigationSurfaceAStarWork(
            _cache.World,
            _store,
            resolved,
            _workspace,
            rayWork,
            _payloadReservation.MaximumBytes);
        return Status;
    }

    internal NavigationAStarQueryStatus AdvanceSearch(
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        var remaining = new NavigationAcquisitionSlice(lookupStepLimit, 0, nodeStepLimit, edgeStepLimit, connectionStepLimit);
        return AdvanceSearch(ref remaining);
    }

    internal NavigationAStarQueryStatus AdvanceSearch(ref NavigationAcquisitionSlice remaining)
    {
        if (Status != NavigationAStarQueryStatus.Pending || IsReadyToPublish)
            return Status;
        if (_search == null)
            return Status;

        NavigationSurfaceAStarStatus searchStatus = _search.Advance(ref remaining);
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
        if (_search == null)
            return Finish(_readyStatus);
        NavigationAStarPayload payload = _search!.Result;
        if (!_cache.TryPublish(
                payload,
                _store,
                ref _payloadReservation,
                out NavigationAStarPayloadLease published))
        {
            Debug.Assert(
                !_store.Current.IsDependencyCurrent(payload.Dependencies)
                || !_cache.IsWorldCurrent(payload),
                "A gate-owned reservation must cover its worker's maximum payload.");
            return Finish(NavigationAStarQueryStatus.Stale);
        }
        return _readyStatus == NavigationAStarQueryStatus.Success
            ? FinishSuccess(published)
            : FinishNegative(published, _readyStatus);
    }

    internal NavigationAStarPayloadLease TakeResult()
    {
        ChronicleGuard.ThrowIfTrue(
            Status != NavigationAStarQueryStatus.Success || _result == null,
            message: "The A* query has no successful payload lease.");
        NavigationAStarPayloadLease result = _result;
        _result = null;
        return result;
    }

    public void Dispose()
    {
        RecordDiagnostics();
        _cache.ReleaseEndpointProof(ref _endpointProofReservation);
        if (_sharedSource != null || _endpointProofActive)
            _admission.Result.Dispose();
        _sharedSource = null;
        _endpointProofWork = default;
        _endpointProofActive = false;
        DisposeAdmission();
        _search?.Dispose();
        _search = null;
        _pendingLease?.Dispose();
        _pendingLease = null;
        _result?.Dispose();
        _result = null;
        _cache.ReleasePayloadReservation(ref _payloadReservation);
        Volatile.Write(ref _readyToPublish, false);
        _started = false;
    }

    private void MarkReady(NavigationAStarQueryStatus status)
    {
        _readyStatus = status;
        Volatile.Write(ref _readyToPublish, true);
    }

    private NavigationAStarQueryStatus FinishCached(
        NavigationAStarPayloadLease lease)
    {
        if (!NavigationEndpointProofWork.IsCurrent(_store, lease.EndpointProof))
        {
            lease.Dispose();
            return Finish(NavigationAStarQueryStatus.Stale);
        }
        if (!_store.Current.IsDependencyCurrent(lease.Payload.Dependencies)
            || !_cache.IsWorldCurrent(lease.Payload))
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
        RecordDiagnostics();
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

    private void RecordDiagnostics()
    {
        if (!_started || _diagnosticsRecorded)
            return;
        _cache.AcquisitionDiagnostics.RecordCompletedQuery(_admission.Meter);
        _diagnosticsRecorded = true;
    }

    private static NavigationAStarQueryStatus MapAdmissionStatus(
        NavigationQueryAdmissionStatus status) => (NavigationAStarQueryStatus)status;

    private static NavigationAStarQueryStatus MapSearchStatus(
        NavigationSurfaceAStarStatus status) => status <= NavigationSurfaceAStarStatus.Success
            ? (NavigationAStarQueryStatus)status
            : (NavigationAStarQueryStatus)((byte)status + 5);
}
