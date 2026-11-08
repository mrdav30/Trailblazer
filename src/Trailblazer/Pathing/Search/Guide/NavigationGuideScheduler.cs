//=======================================================================
// NavigationGuideScheduler.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns a bounded FIFO and one single-item gate continuation across both search families.</summary>
internal sealed partial class NavigationGuideScheduler
{
    private readonly object _sync = new();
    private readonly TrailblazerWorldContext _context;
    private readonly NavigationSchedulingSettings _settings;
    private NavigationGuideRequest? _first;
    private NavigationGuideRequest? _last;
    private NavigationGuideRequest? _active;
    private NavigationGuideRequest? _scan;
    private NavigationAcquisitionSlice _frameRemaining;
    private int _count;
    private long _descriptorBytes;
    private bool _closed;
    private NavigationAStarBatchWork _aStarWork;
    private NavigationFlowBatchWork _flowWork;
    private readonly NavigationGuideRequest?[] _cohort;
    private readonly NavigationCohortSource _source = new();
    private PathQuery _cohortQuery;
    private NavigationGuideRequestFlavor _cohortFlavor;
    private int _cohortCount;
    private int _cohortPending;
    private int _nextBuild;
    private int _fanout;
    private int _clearIndex;
    private bool _grouping;
    private bool _sharing;
    private bool _sharedAdmission;
    private long _submissionSequence;
    private long _freezeSequence;
    private ulong _cohortWorldSequence;
    private NavigationGuideRequestPhase _phase;

    internal NavigationGuideScheduler(TrailblazerWorldContext context)
    {
        _context = context;
        _settings = context.Settings.Scheduling;
        _cohort = new NavigationGuideRequest[_settings.MaxRetainedRequests];
    }

    internal int RetainedRequestCount
    {
        get { lock (_sync) return _count; }
    }

    internal long RetainedDescriptorBytes
    {
        get { lock (_sync) return _descriptorBytes; }
    }

    internal int LastFrameOperationCount { get; private set; }
    internal NavigationAcquisitionSlice LastFrameRemaining => _frameRemaining;
    /// <summary>Counted allowance spent, with lookup overruns saturated; not elapsed time or total atomic lookup probes.</summary>
    internal NavigationAcquisitionSlice LastFrameCountedWork => new(
        _settings.LookupStepLimit - _frameRemaining.LookupRemaining,
        _settings.EndpointCandidateStepLimit - _frameRemaining.EndpointCandidateRemaining,
        _settings.NodeStepLimit - _frameRemaining.NodeRemaining,
        _settings.EdgeStepLimit - _frameRemaining.EdgeRemaining,
        _settings.ConnectionStepLimit - _frameRemaining.ConnectionRemaining);

    internal NavigationGuideStatus Schedule(PathQuery query, out NavigationGuideRequest? request,
        NavigationGuideRequestFlavor flavor = NavigationGuideRequestFlavor.PublicGraph)
    {
        request = null;
        if (!query.Agent.IsValid)
            return NavigationGuideStatus.InvalidProfile;
        long bytes = PathQueryBatchItem.GetLogicalRetainedBytes(query);
        lock (_sync)
        {
            ChronicleGuard.ThrowIfDisposed(_closed, nameof(TrailblazerWorldContext));
            if (_count >= _settings.MaxRetainedRequests
                || bytes > _settings.MaxRetainedDescriptorBytes - _descriptorBytes)
                return NavigationGuideStatus.CapacityExceeded;
            _submissionSequence = NavigationGenerationCounter.Advance(_submissionSequence,
                "The scheduled request sequence is exhausted.");
            request = new NavigationGuideRequest(this, query, bytes)
            { Previous = _last, SubmissionSequence = _submissionSequence, Flavor = flavor };
            if (_last == null)
                _first = request;
            else
                _last.Next = request;
            _last = request;
            _scan ??= request;
            _count++;
            _descriptorBytes += bytes;
            return NavigationGuideStatus.Pending;
        }
    }

    internal void Advance()
    {
        lock (_sync)
        {
            _frameRemaining = new NavigationAcquisitionSlice(_settings.LookupStepLimit,
                _settings.EndpointCandidateStepLimit, _settings.NodeStepLimit,
                _settings.EdgeStepLimit, _settings.ConnectionStepLimit);
            LastFrameOperationCount = 0;
            GrantValidationTurns();
            while (LastFrameOperationCount < _settings.OperationStepLimit)
            {
                if (_grouping)
                {
                    LastFrameOperationCount++;
                    NavigationGuideRequest? candidate = _scan;
                    if (candidate == null || candidate.SubmissionSequence > _freezeSequence
                        || (candidate.Status == NavigationGuideStatus.Pending
                            && (candidate.Flavor != _cohortFlavor || !Compatible(_cohortQuery, candidate.Query))))
                    {
                        _grouping = false;
                        continue;
                    }
                    _scan = candidate.Next;
                    if (candidate.Status == NavigationGuideStatus.Pending)
                        AddMember(candidate);
                    continue;
                }
                if (_active == null)
                {
                    if (_cohortCount != 0)
                    {
                        LastFrameOperationCount++;
                        if (_sharing && _fanout < _cohortCount && !_source.IsEmpty)
                        {
                            NavigationGuideRequest member = _cohort[_fanout++]!;
                            if (member.Owner == this && member.Status == NavigationGuideStatus.Pending)
                                Activate(member, shared: true);
                            continue;
                        }
                        if (_sharing)
                        {
                            _source.Dispose();
                            _sharing = false;
                        }
                        if (_nextBuild < _cohortCount)
                        {
                            NavigationGuideRequest member = _cohort[_nextBuild++]!;
                            if (member.Owner == this && member.Status == NavigationGuideStatus.Pending)
                                Activate(member);
                            continue;
                        }
                        if (_clearIndex < _cohortCount)
                        {
                            _cohort[_clearIndex]!.InCohort = false;
                            _cohort[_clearIndex++] = null;
                            continue;
                        }
                        ResetCohortState();
                        continue;
                    }
                    NavigationGuideRequest? queued = _scan;
                    if (queued == null)
                        break;
                    // Every inspected ticket costs one operation. Never restart at a retained prefix.
                    LastFrameOperationCount++;
                    _scan = queued.Next;
                    if (queued.Status == NavigationGuideStatus.Pending)
                    {
                        _cohortQuery = queued.Query;
                        _cohortFlavor = queued.Flavor;
                        _cohortWorldSequence = _context.World.ChangeSequence;
                        _freezeSequence = _submissionSequence;
                        AddMember(queued);
                        _grouping = true;
                    }
                    continue;
                }

                LastFrameOperationCount++;
                NavigationGuideRequest request = _active;
                if (!IsWorldCurrent(request))
                {
                    Complete(request, NavigationGuideStatus.Stale);
                    continue;
                }
                bool flow = request.Algorithm == PathAlgorithm.FlowField;
                NavigationAcquisitionSlice before = _frameRemaining;
                NavigationGuideRequestPhase phase = _phase;
                if (phase == NavigationGuideRequestPhase.Direct)
                {
                    NavigationRayStatus direct = (request.Query.Traversal.TargetMedia
                        & NavigationCell.ToMedia(request.Query.Traversal.StartMedium)) == 0
                        ? NavigationRayStatus.Blocked
                        : _context.Guides.TryGetDirectHeading(request.Query, request.Query.Start.Position, out _);
                    if (direct == NavigationRayStatus.Success)
                    {
                        request.IsDirect = true;
                        Complete(request, NavigationGuideStatus.Success);
                    }
                    else if (direct == NavigationRayStatus.Blocked && request.Flavor != NavigationGuideRequestFlavor.NavigatorDirectOnly)
                        BeginGraph(request);
                    else
                        Complete(request, direct == NavigationRayStatus.Blocked
                            ? NavigationGuideStatus.NoPath : NavigationGuideStatusMapper.ToPublic(direct));
                    continue;
                }
                if (phase == NavigationGuideRequestPhase.Admission)
                {
                    if (flow)
                    {
                        _flowWork.AdvanceAdmission(ref _frameRemaining);
                        if (_flowWork.IsAdmissionComplete)
                            SetPhase(NavigationGuideRequestPhase.Search);
                    }
                    else
                    {
                        _aStarWork.AdvanceAdmission(ref _frameRemaining);
                        if (_aStarWork.IsAdmissionComplete)
                            SetPhase(NavigationGuideRequestPhase.Search);
                    }
                }
                else if (phase == NavigationGuideRequestPhase.Search)
                {
                    if (flow)
                    {
                        _flowWork.AdvanceSearch(0, ref _frameRemaining);
                        if (_flowWork.IsReadyToPublish(0))
                            SetPhase(NavigationGuideRequestPhase.Publication);
                    }
                    else
                    {
                        _aStarWork.AdvanceSearch(0, ref _frameRemaining);
                        if (_aStarWork.IsReadyToPublish(0))
                            SetPhase(NavigationGuideRequestPhase.Publication);
                    }
                }
                else
                {
                    Publish(request, flow);
                    continue;
                }
                if (_phase == phase && _frameRemaining.HasSameRemaining(before))
                    break;
            }
        }
    }

    private void Activate(NavigationGuideRequest request, bool shared = false)
    {
        _active = request;
        _sharedAdmission = shared;
        _source.Transfer = _cohortPending == 1;
        if (!IsWorldCurrent(request))
        {
            Complete(request, NavigationGuideStatus.Stale);
            return;
        }
        if (request.Flavor is NavigationGuideRequestFlavor.NavigatorDirectFirst or NavigationGuideRequestFlavor.NavigatorDirectOnly
            && !request.DirectChecked)
        {
            request.DirectChecked = true;
            SetPhase(NavigationGuideRequestPhase.Direct);
            return;
        }
        BeginGraph(request);
    }

    private void BeginGraph(NavigationGuideRequest request)
    {
        if (request.Algorithm == PathAlgorithm.FlowField)
        {
            NavigationFlowQueryStatus status = _context.Pathing.NavigationFlowAdmissionGate.Begin(request.Query, out _flowWork,
                scheduled: true, sharedSource: _sharedAdmission ? _source : null);
            if (status != NavigationFlowQueryStatus.Pending)
            {
                Complete(request, NavigationGuideStatusMapper.ToPublic(status));
                return;
            }
        }
        else
        {
            NavigationAStarQueryStatus status = _context.Pathing.NavigationAStarAdmissionGate.Begin(request.Query, out _aStarWork,
                _sharedAdmission ? _source : null);
            if (status != NavigationAStarQueryStatus.Pending)
            {
                Complete(request, NavigationGuideStatusMapper.ToPublic(status));
                return;
            }
        }
        SetPhase(NavigationGuideRequestPhase.Admission);
    }

    private void Publish(NavigationGuideRequest request, bool flow)
    {
        NavigationGuideStatus status;
        if (flow)
        {
            _flowWork.PublishReadyPrefix(1);
            NavigationFlowQueryStatus resultStatus = _flowWork.GetStatus(0);
            status = resultStatus == NavigationFlowQueryStatus.Pending
                ? NavigationGuideStatus.Pending : NavigationGuideStatusMapper.ToPublic(resultStatus);
            if (status == NavigationGuideStatus.Success)
            {
                NavigationFlowQueryResult payload = _flowWork.TakeResult(0);
                if (_sharedAdmission)
                    status = CreateFlowGuide(request, payload);
                else
                {
                    _source.Flow = payload.PayloadLease;
                    _source.HasFlow = true;
                    _source.Origin = payload.ResolvedOrigin;
                }
            }
        }
        else
        {
            _aStarWork.PublishReadyPrefix(1);
            NavigationAStarQueryStatus resultStatus = _aStarWork.GetStatus(0);
            status = resultStatus == NavigationAStarQueryStatus.Pending
                ? NavigationGuideStatus.Pending : NavigationGuideStatusMapper.ToPublic(resultStatus);
            if (status == NavigationGuideStatus.Success)
            {
                NavigationAStarPayloadLease payload = _aStarWork.TakeResult(0);
                if (_sharedAdmission)
                    status = CreateAStarGuide(request, payload);
                else
                    _source.AStar = payload;
            }
        }
        if (!_sharedAdmission && status == NavigationGuideStatus.Success)
        {
            // The leader's admitted result is the pin. Finish the gate before follower admission.
            ReleaseActiveWork();
            _sharing = true;
            _fanout = _nextBuild;
            if (request.Owner == this)
                CompleteMember(request, PublishLeader(request, flow));
            return;
        }
        Complete(request, status);
    }

    private void Complete(NavigationGuideRequest request, NavigationGuideStatus status)
    {
        if (status != NavigationGuideStatus.Pending)
            CompleteMember(request, status);
        ReleaseActiveWork();
    }

    private void CompleteMember(NavigationGuideRequest request, NavigationGuideStatus status)
    {
        if (request.Owner != this)
            return;
        request.Status = status;
        request.Phase = NavigationGuideRequestPhase.Completed;
        _cohortPending--;
    }

    private NavigationGuideStatus PublishLeader(NavigationGuideRequest request, bool flow)
    {
        _source.Transfer = _cohortPending == 1;
        NavigationWorldGraphStore store = _context.Pathing.NavigationGraphStore;
        if (flow)
        {
            NavigationFlowFieldPayloadCache cache = _context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
            if (_source.Flow.TryGetPayload(out NavigationFlowFieldPayload payload) != NavigationFlowFieldStatus.Success)
                return NavigationGuideStatus.Stale;
            NavigationFlowFieldStatus status = cache.TryCheckoutShared(store, store.Current, payload.Key,
                _source.Origin, _source, null, out NavigationFlowFieldPayloadLease lease, out _, preserveSourceProof: true);
            return status == NavigationFlowFieldStatus.Success
                ? CreateFlowGuide(request, new NavigationFlowQueryResult(_source.Origin, lease))
                : NavigationGuideStatusMapper.ToPublic(status);
        }
        NavigationAStarPayloadCache aStarCache = _context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationAStarQueryStatus aStarStatus = aStarCache.TryCheckoutShared(store, store.Current,
            _source.AStar!.Payload.Key, _source, null, out NavigationAStarPayloadLease? aStarLease, preserveSourceProof: true);
        return aStarStatus == NavigationAStarQueryStatus.Success
            ? CreateAStarGuide(request, aStarLease!) : NavigationGuideStatusMapper.ToPublic(aStarStatus);
    }

    private NavigationGuideStatus CreateAStarGuide(NavigationGuideRequest request, NavigationAStarPayloadLease payload)
    {
        // Publication holds the scheduler lock and has retained this Pending owner's interest.
        NavigationGuideStatus status = _context.Pathing.NavigationAStarAdmissionGate.PayloadCache.TryCreatePublicGuide(
            _context.Pathing.NavigationGraphStore, payload, out NavigationGuideLease? lease);
        request.Guide = lease;
        return status;
    }

    private NavigationGuideStatus CreateFlowGuide(NavigationGuideRequest request, NavigationFlowQueryResult payload)
    {
        // Publication holds the scheduler lock and has retained this Pending owner's interest.
        using (payload)
        {
            NavigationGuideStatus status = _context.Pathing.NavigationFlowAdmissionGate.PayloadCache.TryCreateGuide(
                _context.Pathing.NavigationGraphStore, payload, out NavigationFlowFieldLease lease);
            if (status == NavigationGuideStatus.Success)
                request.FlowField = lease;
            return status;
        }
    }

    private void SetPhase(NavigationGuideRequestPhase phase)
    {
        _phase = phase;
        if (_active!.Owner == this)
            _active.Phase = phase;
    }

    private void AddMember(NavigationGuideRequest request)
    {
        _cohort[_cohortCount++] = request;
        _cohortPending++;
        request.InCohort = true;
        request.WorldChangeSequence = _cohortWorldSequence;
    }

    private void ClearCohort()
    {
        // The selection scan has already charged each slot; clearing only drops bounded references.
        for (int i = 0; i < _cohortCount; i++)
        {
            if (_cohort[i] != null)
                _cohort[i]!.InCohort = false;
            _cohort[i] = null;
        }
        ResetCohortState();
    }

    private void ResetCohortState()
    {
        _cohortCount = _cohortPending = _nextBuild = _fanout = _clearIndex = 0;
        _cohortQuery = default;
        _grouping = _sharing = false;
        _source.Dispose();
    }

    private static bool Compatible(PathQuery first, PathQuery candidate) =>
        first.Algorithm == PathAlgorithm.AStar ? first == candidate
        : candidate.Algorithm == PathAlgorithm.FlowField
            && first.End == candidate.End && first.Agent == candidate.Agent
            && first.AreaPolicy == candidate.AreaPolicy && first.Traversal == candidate.Traversal
            && first.Budget == candidate.Budget && first.AllowTransitions == candidate.AllowTransitions
            && first.FlowField == candidate.FlowField;

    private void ReleaseActiveWork()
    {
        _aStarWork.Dispose();
        _flowWork.Dispose();
        _aStarWork = default;
        _flowWork = default;
        _active = null;
    }

    internal bool TryTakeGuide(NavigationGuideRequest request, out NavigationGuideLease? result)
    {
        lock (_sync)
        {
            result = null;
            if (request.Owner != this || !request.Guide.HasValue)
                return false;
            request.Status = request.Guide.Value.Status;
            if (!IsWorldCurrent(request))
                request.Status = NavigationGuideStatus.Stale;
            if (request.Status == NavigationGuideStatus.Success)
            {
                result = request.Guide;
                request.Guide = null;
            }
            Release(request);
            return result.HasValue;
        }
    }

    internal bool TryTakeFlowField(NavigationGuideRequest request, out NavigationFlowFieldLease? result)
    {
        lock (_sync)
        {
            result = null;
            if (request.Owner != this || !request.FlowField.HasValue)
                return false;
            request.Status = request.FlowField.Value.Status;
            if (!IsWorldCurrent(request))
                request.Status = NavigationGuideStatus.Stale;
            if (request.Status == NavigationGuideStatus.Success)
            {
                result = request.FlowField;
                request.FlowField = null;
            }
            Release(request);
            return result.HasValue;
        }
    }

    internal void Release(NavigationGuideRequest request)
    {
        lock (_sync)
        {
            if (request.Owner != this)
                return;
            if (request.InCohort && request.Status == NavigationGuideStatus.Pending)
                _cohortPending--;
            if (_active == request && (_sharedAdmission
                || _phase is NavigationGuideRequestPhase.Direct or NavigationGuideRequestPhase.Admission))
                ReleaseActiveWork();
            if (_scan == request)
                _scan = request.Next;
            if (request.Status == NavigationGuideStatus.Pending)
                request.Status = NavigationGuideStatus.Cancelled;
            request.Phase = NavigationGuideRequestPhase.Completed;
            request.Guide?.Dispose();
            request.FlowField?.Dispose();
            request.Guide = null;
            request.FlowField = null;
            if (request.Previous == null)
                _first = request.Next;
            else
                request.Previous.Next = request.Next;
            if (request.Next == null)
                _last = request.Previous;
            else
                request.Next.Previous = request.Previous;
            _count--;
            _descriptorBytes -= request.DescriptorBytes;
            request.Owner = null;
            request.Previous = null;
            request.Next = null;
            request.Query = default;
            if (_cohortPending == 0 && !_grouping)
            {
                ReleaseActiveWork();
                ClearCohort();
            }
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            while (_validationFirst != null)
            {
                _validationFirst.ConnectorGuide.Dispose();
                ReleaseValidation(_validationFirst);
            }
            _validationTurn = true;
            for (NavigationGuideRequest? request = _first; request != null; request = _first)
                Release(request);
            ReleaseActiveWork();
            ClearCohort();
        }
    }

    internal void Dispose()
    {
        lock (_sync)
        {
            _closed = true;
            Reset();
        }
    }

    private bool IsWorldCurrent(NavigationGuideRequest request) =>
        _context.World.IsActive && _context.World.ChangeSequence == request.WorldChangeSequence;
}
