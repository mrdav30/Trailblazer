//=======================================================================
// NavigationFlowQueryWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Owns flow admission, cache reservation, search, and result publication.</summary>
internal sealed class NavigationFlowQueryWork : IDisposable
{
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationFlowFieldPayloadCache _cache;
    private readonly NavigationFlowFieldWorkspace _workspace;
    private readonly NavigationQueryAdmissionWork _admission;
    private NavigationFlowFieldWork? _search;
    private NavigationFlowFieldPayloadLease _pendingLease;
    private NavigationFlowFieldPayload? _pendingProof;
    private NavigationFlowQueryResult _result;
    private NavigationFlowFieldReservation _payloadReservation;
    private NavigationCellAddress _resolvedOrigin;
    private NavigationFlowQueryStatus _readyStatus;
    private int _status;
    private bool _hasPendingLease;
    private bool _hasResult;
    private bool _readyToPublish;
    private bool _started;
    private bool _admissionActive;
    private bool _diagnosticsRecorded;
    private bool _scheduled;
    private NavigationCohortSource? _sharedSource;
    private NavigationEndpointProofWork _endpointProofWork;
    private bool _endpointProofActive;
    private NavigationFlowFieldStatus _checkoutStatus;

    internal NavigationFlowFieldWorkspace Workspace => _workspace;
    private long _endpointProofReservation;

    internal NavigationFlowQueryWork(
        NavigationWorldGraphStore store,
        NavigationFlowFieldWorkspace workspace,
        NavigationFlowFieldPayloadCache cache)
    {
        ChronicleGuard.ThrowIfNull(store, nameof(store));
        ChronicleGuard.ThrowIfNull(workspace, nameof(workspace));
        ChronicleGuard.ThrowIfNull(cache, nameof(cache));
        _store = store;
        _cache = cache;
        _workspace = workspace;
        _admission = new NavigationQueryAdmissionWork(
            cache.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.FlowField);
    }

    internal NavigationFlowQueryStatus Status
    {
        get => (NavigationFlowQueryStatus)Volatile.Read(ref _status);
        private set => Volatile.Write(ref _status, (int)value);
    }

    internal bool IsPrepared => _search != null || IsReadyToPublish;

    internal bool IsReadyToPublish => Volatile.Read(ref _readyToPublish);

    internal bool ReservationRejected { get; private set; }

    internal void Begin(PathQuery query, NavigationWorldGraphLease lease, bool scheduled = false,
        NavigationCohortSource? sharedSource = null)
    {
        ChronicleGuard.ThrowIfNull(lease, nameof(lease));
        ChronicleGuard.ThrowIfTrue(
            _started,
            message: "The flow query work is already active.");
        _started = true;
        _scheduled = scheduled;
        _sharedSource = sharedSource;
        _endpointProofWork = default;
        _endpointProofActive = false;
        _diagnosticsRecorded = false;
        ReservationRejected = false;
        _resolvedOrigin = default;
        _readyStatus = NavigationFlowQueryStatus.Pending;
        Volatile.Write(ref _readyToPublish, false);
        Status = NavigationFlowQueryStatus.Pending;
        _admission.Begin(
            lease,
            query,
            query.Traversal.StartMedium,
            query.Traversal.TargetMedia);
        _admissionActive = true;
        if (_admission.Status != NavigationQueryAdmissionStatus.Pending)
            MarkReady(MapAdmissionStatus(_admission.Status));
    }

    internal NavigationFlowQueryStatus PrepareSearchOrCheckout(
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        var remaining = new NavigationAcquisitionSlice(lookupStepLimit, endpointCandidateStepLimit);
        return PrepareSearchOrCheckout(ref remaining);
    }

    internal NavigationFlowQueryStatus PrepareSearchOrCheckout(ref NavigationAcquisitionSlice remaining)
    {
        if (!_started || Status != NavigationFlowQueryStatus.Pending || IsPrepared)
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
        _resolvedOrigin = resolved.Start.Address;
        var key = new NavigationFlowFieldPayloadKey(
            resolved.Query,
            resolved.End.Address,
            resolved.StartMedium,
            resolved.TargetMedia);
        NavigationFlowFieldStatus checkout = _checkoutStatus;
        if (!_endpointProofActive)
        {
            NavigationFlowFieldPayload? payload;
            if (_sharedSource != null)
            {
                checkout = _sharedSource.Flow.TryGetPayload(out payload);
                if (!_sharedSource.HasFlow) checkout = NavigationFlowFieldStatus.Stale;
            }
            else checkout = _cache.TryCheckout(_store, resolved.Graph, key, _resolvedOrigin,
                out _pendingLease, out payload, _scheduled, resolved.WorldChangeSequence);
            _hasPendingLease = _sharedSource == null && checkout == NavigationFlowFieldStatus.Success;
            if (checkout is NavigationFlowFieldStatus.Success or NavigationFlowFieldStatus.NoPath or NavigationFlowFieldStatus.CostOverflow)
            {
                _pendingProof = payload;
                _endpointProofWork = new NavigationEndpointProofWork(_workspace.EndpointWorkspace, resolved, payload!.Dependencies);
                _endpointProofActive = true;
                _checkoutStatus = checkout;
            }
        }
        if (_endpointProofActive)
        {
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
            if (proofStatus != NavigationQueryAdmissionStatus.Success)
            {
                _pendingLease.Dispose();
                _pendingLease = default;
                _hasPendingLease = false;
                _pendingProof = null;
                resolved.Dispose();
                DisposeAdmission();
                MarkReady(MapAdmissionStatus(proofStatus));
                return Status;
            }
            if (_sharedSource != null)
                checkout = _cache.TryCheckoutShared(_store, resolved.Graph, key, _resolvedOrigin,
                    _sharedSource, _endpointProofWork.Proof, out _pendingLease, out _pendingProof, _endpointProofReservation);
            else if (_hasPendingLease && !_endpointProofWork.IsCertified
                     && !_cache.TryAttachEndpointProof(_pendingLease, _endpointProofWork.Proof,
                         ref _endpointProofReservation))
            {
                _pendingLease.Dispose();
                _pendingLease = default;
                _pendingProof = null;
                checkout = NavigationFlowFieldStatus.Stale;
            }
            if (checkout is not NavigationFlowFieldStatus.NoPath and not NavigationFlowFieldStatus.CostOverflow)
            {
                _cache.ReleaseEndpointProof(ref _endpointProofReservation);
                _endpointProofWork = default;
                _endpointProofActive = false;
            }
            if (_sharedSource != null && checkout == NavigationFlowFieldStatus.Success)
                _cache.AcquisitionDiagnostics.RecordSharedFollowerCheckout();
            else if (_sharedSource != null && checkout == NavigationFlowFieldStatus.Pending)
                _cache.AcquisitionDiagnostics.RecordSharedCoverageMiss();
        }
        if (checkout != NavigationFlowFieldStatus.Pending || _sharedSource != null)
        {
            if (checkout == NavigationFlowFieldStatus.Pending)
                _pendingProof = null;
            _hasPendingLease = checkout == NavigationFlowFieldStatus.Success;
            if (_hasPendingLease && _sharedSource == null)
                _cache.AcquisitionDiagnostics.RecordCacheCheckoutHit();
            resolved.Dispose();
            DisposeAdmission();
            if (checkout == NavigationFlowFieldStatus.CapacityExceeded)
                ReservationRejected = true;
            MarkReady(MapFlowStatus(checkout));
            return Status;
        }

        int maximumNodes = Math.Min(
            _workspace.NodeCapacity,
            resolved.Query.Budget.MaxExpandedNodes);
        int maximumTransitions = resolved.Query.AllowTransitions
            ? Math.Max(0, maximumNodes - 1)
            : 0;
        long maximumBytes = NavigationFlowFieldPayload.GetMaximumRetainedBytes(
            maximumNodes,
            maximumTransitions,
            _workspace.DependencyComponentCapacity,
            _workspace.DependencyPageCapacity);
        if (!_cache.TryReservePayload(maximumBytes, out _payloadReservation))
        {
            ReservationRejected = true;
            resolved.Dispose();
            DisposeAdmission();
            MarkReady(NavigationFlowQueryStatus.CapacityExceeded);
            return Status;
        }
        _cache.AcquisitionDiagnostics.RecordBuildStart();
        _search = new NavigationFlowFieldWork(
            _cache.World,
            resolved,
            _workspace,
            _cache.MaximumSinglePayloadBytes);
        DisposeAdmission();
        if (_search.Status != NavigationFlowFieldStatus.Pending)
            MarkReady(MapFlowStatus(_search.Status));
        return Status;
    }

    internal NavigationFlowQueryStatus AdvanceSearch(
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        var remaining = new NavigationAcquisitionSlice(lookupStepLimit, 0, nodeStepLimit, edgeStepLimit, connectionStepLimit);
        return AdvanceSearch(ref remaining);
    }

    internal NavigationFlowQueryStatus AdvanceSearch(ref NavigationAcquisitionSlice remaining)
    {
        if (Status != NavigationFlowQueryStatus.Pending || IsReadyToPublish)
            return Status;
        if (_search == null)
            return Status;

        NavigationFlowFieldStatus searchStatus = _search.Advance(ref remaining);
        if (searchStatus != NavigationFlowFieldStatus.Pending)
            MarkReady(MapFlowStatus(searchStatus));
        return Status;
    }

    internal NavigationFlowQueryStatus Publish()
    {
        if (!IsReadyToPublish)
            return Status;
        if (_hasPendingLease)
        {
            NavigationFlowFieldPayloadLease lease = _pendingLease;
            _pendingLease = default;
            _hasPendingLease = false;
            return FinishCached(lease);
        }
        if (_pendingProof != null)
        {
            NavigationFlowFieldStatus expected =
                _readyStatus == NavigationFlowQueryStatus.NoPath
                    ? NavigationFlowFieldStatus.NoPath
                    : NavigationFlowFieldStatus.CostOverflow;
            bool current = _sharedSource != null
                ? _cache.IsPayloadCurrent(_store.Current, _pendingProof)
                    && NavigationEndpointProofWork.IsCurrent(_store, _endpointProofWork.Proof)
                : _cache.IsExactProofCurrent(
                    _store,
                    _pendingProof,
                    _resolvedOrigin,
                    expected)
                    && NavigationEndpointProofWork.IsCurrent(_store, _endpointProofWork.Proof);
            if (!current)
            {
                return Finish(NavigationFlowQueryStatus.Stale);
            }
            _pendingProof = null;
        }
        if (_search?.Result == null)
            return Finish(_readyStatus);

        NavigationFlowFieldStatus publishedStatus = _cache.TryPublishOrPromote(
            _store,
            _search.Result,
            _resolvedOrigin,
            ref _payloadReservation,
            out NavigationFlowFieldPayloadLease published);
        if (publishedStatus == NavigationFlowFieldStatus.Success)
            return FinishSuccess(published);
        published.Dispose();
        return Finish(MapFlowStatus(publishedStatus));
    }

    internal NavigationFlowQueryResult TakeResult()
    {
        ChronicleGuard.ThrowIfTrue(
            Status != NavigationFlowQueryStatus.Success || !_hasResult,
            message: "The flow query has no successful payload lease.");
        NavigationFlowQueryResult result = _result;
        _result = default;
        _hasResult = false;
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
        if (_hasPendingLease)
            _pendingLease.Dispose();
        _pendingLease = default;
        _hasPendingLease = false;
        _pendingProof = null;
        if (_hasResult)
            _result.Dispose();
        _result = default;
        _hasResult = false;
        _cache.ReleasePayloadReservation(ref _payloadReservation);
        ReservationRejected = false;
        Volatile.Write(ref _readyToPublish, false);
        _started = false;
    }

    private void MarkReady(NavigationFlowQueryStatus status)
    {
        _readyStatus = status;
        Volatile.Write(ref _readyToPublish, true);
    }

    private NavigationFlowQueryStatus FinishCached(
        NavigationFlowFieldPayloadLease lease)
    {
        NavigationFlowFieldStatus leaseStatus = lease.TryGetPayload(
            out NavigationFlowFieldPayload payload);
        if (!_cache.IsEndpointProofCurrent(lease, _store.Current))
        {
            lease.Dispose();
            return Finish(NavigationFlowQueryStatus.Stale);
        }
        if (leaseStatus != NavigationFlowFieldStatus.Success
            || (!_store.Current.IsDependencyCurrent(payload.Dependencies)
                || !_cache.IsWorldCurrent(payload)))
        {
            if (leaseStatus == NavigationFlowFieldStatus.Success)
                _cache.RemoveExact(payload);
            lease.Dispose();
            return Finish(NavigationFlowQueryStatus.Stale);
        }
        return FinishSuccess(lease);
    }

    private NavigationFlowQueryStatus FinishSuccess(
        NavigationFlowFieldPayloadLease lease)
    {
        _result = new NavigationFlowQueryResult(_resolvedOrigin, lease);
        _hasResult = true;
        return Finish(NavigationFlowQueryStatus.Success);
    }

    private NavigationFlowQueryStatus Finish(NavigationFlowQueryStatus status)
    {
        RecordDiagnostics();
        _cache.ReleaseEndpointProof(ref _endpointProofReservation);
        _endpointProofWork = default;
        _endpointProofActive = false;
        _pendingProof = null;
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

    private static NavigationFlowQueryStatus MapAdmissionStatus(
        NavigationQueryAdmissionStatus status) => (NavigationFlowQueryStatus)status;

    private static NavigationFlowQueryStatus MapFlowStatus(
        NavigationFlowFieldStatus status) => status <= NavigationFlowFieldStatus.Success
            ? (NavigationFlowQueryStatus)status
            : (NavigationFlowQueryStatus)((byte)status + 5);
}
