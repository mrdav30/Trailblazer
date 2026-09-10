//=======================================================================
// NavigationGuideScheduler.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns a bounded FIFO and one single-item gate continuation across both search families.</summary>
internal sealed class NavigationGuideScheduler
{
    private readonly object _sync = new();
    private readonly TrailblazerWorldContext _context;
    private readonly NavigationSchedulingSettings _settings;
    private NavigationGuideRequest? _first;
    private NavigationGuideRequest? _last;
    private NavigationGuideRequest? _active;
    private int _count;
    private long _descriptorBytes;
    private bool _closed;
    private NavigationAStarBatchWork _aStarWork;
    private NavigationFlowBatchWork _flowWork;

    internal NavigationGuideScheduler(TrailblazerWorldContext context)
    {
        _context = context;
        _settings = context.Settings.Scheduling;
    }

    internal int RetainedRequestCount
    {
        get { lock (_sync) return _count; }
    }

    internal long RetainedDescriptorBytes
    {
        get { lock (_sync) return _descriptorBytes; }
    }

    internal NavigationGuideStatus Schedule(PathQuery query, out NavigationGuideRequest? request)
    {
        request = null;
        if (!query.Agent.IsValid)
            return NavigationGuideStatus.InvalidProfile;
        long bytes = PathQueryBatchItem.GetLogicalRetainedBytes(query);
        lock (_sync)
        {
            SwiftThrowHelper.ThrowIfDisposed(_closed, nameof(TrailblazerWorldContext));
            if (_count >= _settings.MaxRetainedRequests
                || bytes > _settings.MaxRetainedDescriptorBytes - _descriptorBytes)
                return NavigationGuideStatus.CapacityExceeded;
            request = new NavigationGuideRequest(this, query, bytes) { Previous = _last };
            if (_last == null)
                _first = request;
            else
                _last.Next = request;
            _last = request;
            _count++;
            _descriptorBytes += bytes;
            return NavigationGuideStatus.Pending;
        }
    }

    internal void Advance()
    {
        lock (_sync)
        {
            if (_active == null)
            {
                NavigationGuideRequest? queued = _first;
                // A completed ticket retains capacity, but does not prevent later FIFO work.
                for (; queued != null; queued = queued.Next)
                    if (queued.Status == NavigationGuideStatus.Pending)
                        break;
                if (queued != null)
                    Activate(queued);
                return;
            }

            NavigationGuideRequest request = _active;
            if (!IsWorldCurrent(request))
            {
                Complete(request, NavigationGuideStatus.Stale);
                return;
            }
            bool flow = request.Algorithm == PathAlgorithm.FlowField;
            if (request.Phase == NavigationGuideRequestPhase.Admission)
            {
                if (flow)
                {
                    _flowWork.AdvanceAdmission(_settings.LookupStepLimit, _settings.EndpointCandidateStepLimit);
                    if (_flowWork.IsAdmissionComplete)
                        request.Phase = NavigationGuideRequestPhase.Search;
                }
                else
                {
                    _aStarWork.AdvanceAdmission(_settings.LookupStepLimit, _settings.EndpointCandidateStepLimit);
                    if (_aStarWork.IsAdmissionComplete)
                        request.Phase = NavigationGuideRequestPhase.Search;
                }
                return;
            }
            if (request.Phase == NavigationGuideRequestPhase.Search)
            {
                if (flow)
                {
                    _flowWork.AdvanceSearch(0, _settings.LookupStepLimit, _settings.NodeStepLimit,
                        _settings.EdgeStepLimit, _settings.ConnectionStepLimit);
                    if (_flowWork.IsReadyToPublish(0))
                        request.Phase = NavigationGuideRequestPhase.Publication;
                }
                else
                {
                    _aStarWork.AdvanceSearch(0, _settings.LookupStepLimit, _settings.NodeStepLimit,
                        _settings.EdgeStepLimit, _settings.ConnectionStepLimit);
                    if (_aStarWork.IsReadyToPublish(0))
                        request.Phase = NavigationGuideRequestPhase.Publication;
                }
                return;
            }
            Publish(request, flow);
        }
    }

    private void Activate(NavigationGuideRequest request)
    {
        _active = request;
        request.WorldChangeSequence = _context.World.ChangeSequence;
        if (request.Algorithm == PathAlgorithm.FlowField)
        {
            NavigationFlowQueryStatus status = _context.Pathing.NavigationFlowAdmissionGate.Begin(request.Query, out _flowWork);
            if (status != NavigationFlowQueryStatus.Pending)
            {
                Complete(request, NavigationGuideStatusMapper.ToPublic(status));
                return;
            }
        }
        else
        {
            NavigationAStarQueryStatus status = _context.Pathing.NavigationAStarAdmissionGate.Begin(request.Query, out _aStarWork);
            if (status != NavigationAStarQueryStatus.Pending)
            {
                Complete(request, NavigationGuideStatusMapper.ToPublic(status));
                return;
            }
        }
        request.Phase = NavigationGuideRequestPhase.Admission;
    }

    private void Publish(NavigationGuideRequest request, bool flow)
    {
        NavigationGuideStatus status;
        if (flow)
        {
            _flowWork.PublishReadyPrefix(1);
            status = NavigationGuideStatusMapper.ToPublic(_flowWork.GetStatus(0));
            if (status == NavigationGuideStatus.Success)
            {
                using NavigationFlowQueryResult payload = _flowWork.TakeResult(0);
                status = _context.Pathing.NavigationFlowAdmissionGate.PayloadCache.TryCreateGuide(
                    _context.Pathing.NavigationGraphStore, payload, out NavigationFlowFieldLease lease);
                if (status == NavigationGuideStatus.Success)
                    request.FlowField = lease;
            }
        }
        else
        {
            _aStarWork.PublishReadyPrefix(1);
            status = NavigationGuideStatusMapper.ToPublic(_aStarWork.GetStatus(0));
            if (status == NavigationGuideStatus.Success)
            {
                status = _context.Pathing.NavigationAStarAdmissionGate.PayloadCache.TryCreatePublicGuide(
                    _context.Pathing.NavigationGraphStore, _aStarWork.TakeResult(0), out NavigationGuideLease? lease);
                request.Guide = lease;
            }
        }
        Complete(request, status);
    }

    private void Complete(NavigationGuideRequest request, NavigationGuideStatus status)
    {
        request.Status = status;
        request.Phase = NavigationGuideRequestPhase.Completed;
        ReleaseActiveWork();
    }

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
            if (_active == request)
                ReleaseActiveWork();
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
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            for (NavigationGuideRequest? request = _first; request != null; request = _first)
                Release(request);
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
