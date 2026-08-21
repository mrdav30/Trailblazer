//=======================================================================
// NavigationFlowQueryWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Reports the complete internal lifecycle of one bounded flow query.</summary>
internal enum NavigationFlowQueryStatus : byte
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

/// <summary>Owns one successful flow payload lease and its exact resolved origin.</summary>
internal readonly struct NavigationFlowQueryResult : IDisposable
{
    internal NavigationFlowQueryResult(
        NavigationCellAddress resolvedOrigin,
        NavigationFlowFieldPayloadLease payloadLease)
    {
        ResolvedOrigin = resolvedOrigin;
        PayloadLease = payloadLease;
    }

    internal NavigationCellAddress ResolvedOrigin { get; }

    internal NavigationFlowFieldPayloadLease PayloadLease { get; }

    public void Dispose() => PayloadLease.Dispose();
}

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

    internal NavigationFlowQueryWork(
        NavigationWorldGraphStore store,
        NavigationFlowFieldWorkspace workspace,
        NavigationFlowFieldPayloadCache cache)
    {
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        SwiftThrowHelper.ThrowIfNull(cache, nameof(cache));
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

    internal void Begin(PathQuery query, NavigationWorldGraphLease lease)
    {
        SwiftThrowHelper.ThrowIfNull(lease, nameof(lease));
        if (_admissionActive
            || _search != null
            || _hasPendingLease
            || _hasResult
            || IsReadyToPublish
            || _payloadReservation.Owner != null)
        {
            throw new InvalidOperationException("The flow query work is already active.");
        }
        _started = true;
        ReservationRejected = false;
        _resolvedOrigin = default;
        _readyStatus = NavigationFlowQueryStatus.Pending;
        Volatile.Write(ref _readyToPublish, false);
        Status = NavigationFlowQueryStatus.Pending;
        if (!NavigationQueryAdmissionWork.CanProjectPublicQuery(
                query,
                PathAlgorithm.FlowField))
        {
            lease.Dispose();
            MarkReady(NavigationFlowQueryStatus.Unsupported);
            return;
        }
        _admission.Begin(
            lease,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        _admissionActive = true;
        if (_admission.Status != NavigationQueryAdmissionStatus.Pending)
            MarkReady(MapAdmissionStatus(_admission.Status));
    }

    internal NavigationFlowQueryStatus PrepareSearchOrCheckout(
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(
            endpointCandidateStepLimit,
            nameof(endpointCandidateStepLimit));
        if (!_started || Status != NavigationFlowQueryStatus.Pending || IsPrepared)
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
        if (resolved.RequiresWorldStamp
            && _cache.World.ChangeSequence != resolved.WorldChangeSequence)
        {
            resolved.Dispose();
            DisposeAdmission();
            MarkReady(NavigationFlowQueryStatus.Stale);
            return Status;
        }
        _resolvedOrigin = resolved.Start.Address;
        var key = new NavigationFlowFieldPayloadKey(
            resolved.Query,
            resolved.End.Address,
            resolved.StartMedium,
            resolved.TargetMedia);
        NavigationFlowFieldStatus checkout = _cache.TryCheckout(
            _store,
            resolved.Graph,
            key,
            _resolvedOrigin,
            out _pendingLease,
            out _pendingProof);
        if (checkout != NavigationFlowFieldStatus.Pending)
        {
            _hasPendingLease = checkout == NavigationFlowFieldStatus.Success;
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
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(connectionStepLimit, nameof(connectionStepLimit));
        if (Status != NavigationFlowQueryStatus.Pending || IsReadyToPublish)
            return Status;
        if (_search == null)
            return Status;

        NavigationFlowFieldStatus searchStatus = _search.Advance(
            lookupStepLimit,
            nodeStepLimit,
            edgeStepLimit,
            connectionStepLimit);
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
            NavigationFlowFieldStatus expected = _readyStatus switch
            {
                NavigationFlowQueryStatus.NoPath => NavigationFlowFieldStatus.NoPath,
                NavigationFlowQueryStatus.CostOverflow =>
                    NavigationFlowFieldStatus.CostOverflow,
                _ => NavigationFlowFieldStatus.Pending
            };
            if (expected == NavigationFlowFieldStatus.Pending
                || !_cache.IsExactProofCurrent(
                    _store,
                    _pendingProof,
                    _resolvedOrigin,
                    expected))
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
        if (Status != NavigationFlowQueryStatus.Success || !_hasResult)
            throw new InvalidOperationException("The flow query has no successful payload lease.");
        NavigationFlowQueryResult result = _result;
        _result = default;
        _hasResult = false;
        return result;
    }

    public void Dispose()
    {
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
        if (leaseStatus != NavigationFlowFieldStatus.Success
            || !_store.Current.IsDependencyCurrent(payload.Dependencies)
            || !_cache.IsWorldCurrent(payload))
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

    private static NavigationFlowQueryStatus MapAdmissionStatus(
        NavigationQueryAdmissionStatus status) => status switch
        {
            NavigationQueryAdmissionStatus.Success => NavigationFlowQueryStatus.Success,
            NavigationQueryAdmissionStatus.Unsupported => NavigationFlowQueryStatus.Unsupported,
            NavigationQueryAdmissionStatus.NoMap => NavigationFlowQueryStatus.NoMap,
            NavigationQueryAdmissionStatus.InvalidProfile => NavigationFlowQueryStatus.InvalidProfile,
            NavigationQueryAdmissionStatus.InvalidStart => NavigationFlowQueryStatus.InvalidStart,
            NavigationQueryAdmissionStatus.InvalidEnd => NavigationFlowQueryStatus.InvalidEnd,
            NavigationQueryAdmissionStatus.NoPath => NavigationFlowQueryStatus.NoPath,
            NavigationQueryAdmissionStatus.BudgetExceeded => NavigationFlowQueryStatus.BudgetExceeded,
            NavigationQueryAdmissionStatus.CostOverflow => NavigationFlowQueryStatus.CostOverflow,
            NavigationQueryAdmissionStatus.CapacityExceeded => NavigationFlowQueryStatus.CapacityExceeded,
            NavigationQueryAdmissionStatus.Stale => NavigationFlowQueryStatus.Stale,
            _ => NavigationFlowQueryStatus.Pending
        };

    private static NavigationFlowQueryStatus MapFlowStatus(
        NavigationFlowFieldStatus status) => status switch
        {
            NavigationFlowFieldStatus.Success => NavigationFlowQueryStatus.Success,
            NavigationFlowFieldStatus.NoPath => NavigationFlowQueryStatus.NoPath,
            NavigationFlowFieldStatus.BudgetExceeded => NavigationFlowQueryStatus.BudgetExceeded,
            NavigationFlowFieldStatus.CostOverflow => NavigationFlowQueryStatus.CostOverflow,
            NavigationFlowFieldStatus.CapacityExceeded => NavigationFlowQueryStatus.CapacityExceeded,
            NavigationFlowFieldStatus.Stale => NavigationFlowQueryStatus.Stale,
            _ => NavigationFlowQueryStatus.Pending
        };
}
