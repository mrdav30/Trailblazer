//=======================================================================
// NavigationQueryAdmissionWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Reports bounded internal query-admission progress.</summary>
internal enum NavigationQueryAdmissionStatus : byte
{
    Pending = 0,
    Success = 1,
    Unsupported = 2,
    NoMap = 3,
    InvalidProfile = 4,
    InvalidStart = 5,
    InvalidEnd = 6,
    BudgetExceeded = 7,
    CostOverflow = 8,
    CapacityExceeded = 9,
    Stale = 10,
    NoPath = 11
}

/// <summary>Resolves one surface A* query against an exact leased graph generation.</summary>
internal sealed class NavigationQueryAdmissionWork : IDisposable
{
    private enum Stage : byte
    {
        ResolvePolicy = 0,
        ResolveStart = 1,
        ResolveEnd = 2
    }

    private readonly NavigationEndpointWorkspace _workspace;
    private readonly GridWorld _world;
    private readonly PathAlgorithm _expectedAlgorithm;
    private readonly NavigationWorkMeter _meter;
    private readonly NavigationRayWork _rayWork;
    private readonly NavigationEndpointResolutionWork _endpointWork;
    private readonly NavigationResolvedPathQuery _result;
    private NavigationWorldGraphLease? _lease;
    private NavigationAreaPolicy? _areaPolicy;
    private PathQuery _query;
    private TraversalMedium _startMedium;
    private TraversalMedia _targetMedia;
    private NavigationResolvedEndpoint _start;
    private NavigationResolvedEndpoint _end;
    private Stage _stage;
    private bool _endpointActive;
    private bool _active;
    private ulong _worldChangeSequence;
    private bool _requiresWorldStamp;

    internal NavigationQueryAdmissionWork(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationEndpointWorkspace workspace,
        NavigationRayWorkspace rayWorkspace,
        PathAlgorithm expectedAlgorithm)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        SwiftThrowHelper.ThrowIfNull(rayWorkspace, nameof(rayWorkspace));
        _workspace = workspace;
        _world = world;
        _expectedAlgorithm = expectedAlgorithm;
        _meter = new NavigationWorkMeter(default);
        _rayWork = new NavigationRayWork(rayWorkspace);
        _endpointWork = new NavigationEndpointResolutionWork(
            world,
            store,
            _meter,
            workspace,
            rayWorkspace,
            _rayWork);
        _result = new NavigationResolvedPathQuery();
    }

    internal void Begin(
        NavigationWorldGraphLease lease,
        PathQuery query,
        TraversalMedium startMedium,
        TraversalMedia targetMedia)
    {
        SwiftThrowHelper.ThrowIfNull(lease, nameof(lease));
        if (_active)
            throw new InvalidOperationException("The query admission work is already active.");
        _active = true;
        _lease = lease;
        _query = query;
        _workspace.Reset();
        _meter.Reset(query.Budget);
        _areaPolicy = null;
        _start = default;
        _end = default;
        _worldChangeSequence = _world.ChangeSequence;
        _startMedium = startMedium;
        _targetMedia = targetMedia;
        _stage = Stage.ResolvePolicy;
        _endpointActive = false;
        _requiresWorldStamp = false;
        Status = NavigationQueryAdmissionStatus.Pending;
        if (!query.Agent.IsValid)
            Finish(NavigationQueryAdmissionStatus.InvalidProfile);
        else if (query.Algorithm != _expectedAlgorithm
            || _expectedAlgorithm is not (PathAlgorithm.AStar or PathAlgorithm.FlowField))
            Finish(NavigationQueryAdmissionStatus.Unsupported);
        else if (!NavigationCell.IsKnownMedium(startMedium))
            Finish(NavigationQueryAdmissionStatus.InvalidStart);
        else if (targetMedia == TraversalMedia.None
            || (targetMedia & ~NavigationCell.KnownMedia) != 0)
        {
            Finish(NavigationQueryAdmissionStatus.InvalidEnd);
        }
        else if ((query.Agent.AllowedMedia & NavigationCell.ToMedia(startMedium)) == 0
            || (targetMedia & ~query.Agent.AllowedMedia) != 0)
        {
            Finish(NavigationQueryAdmissionStatus.InvalidProfile);
        }
        else if (!query.AllowTransitions
            && (targetMedia & NavigationCell.ToMedia(startMedium)) == 0)
        {
            Finish(NavigationQueryAdmissionStatus.NoPath);
        }
    }

    internal NavigationQueryAdmissionStatus Status { get; private set; }

    internal NavigationResolvedPathQuery Result => _result;

    internal NavigationWorkMeter Meter => _meter;

    internal NavigationRayWork RayWork => _rayWork;

    internal static bool CanProjectPublicQuery(
        PathQuery query,
        PathAlgorithm expectedAlgorithm) =>
        query.Algorithm == expectedAlgorithm
        && expectedAlgorithm is PathAlgorithm.AStar or PathAlgorithm.FlowField;

    internal NavigationQueryAdmissionStatus Advance(
        int lookupStepLimit,
        int endpointCandidateStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(
            endpointCandidateStepLimit,
            nameof(endpointCandidateStepLimit));
        if (Status != NavigationQueryAdmissionStatus.Pending)
            return Status;

        int lookupRemaining = lookupStepLimit;
        int candidateRemaining = endpointCandidateStepLimit;
        while (Status == NavigationQueryAdmissionStatus.Pending)
        {
            if (_stage == Stage.ResolvePolicy)
            {
                if (lookupRemaining == 0)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationQueryAdmissionStatus.BudgetExceeded)
                        : Status;
                }
                if (!_meter.TryConsumeLookupProbes(1))
                    return Finish(NavigationQueryAdmissionStatus.BudgetExceeded);
                lookupRemaining--;
                if (!_lease!.Graph.AreaCatalog.TryGet(
                        _query.AreaPolicy,
                        out NavigationAreaPolicy? policy)
                    || policy == null)
                {
                    return Finish(NavigationQueryAdmissionStatus.Stale);
                }
                _areaPolicy = policy;
                _stage = Stage.ResolveStart;
                continue;
            }

            if (_stage is Stage.ResolveStart or Stage.ResolveEnd)
            {
                if (!_endpointActive)
                {
                    BeginEndpointWork(
                        _stage == Stage.ResolveStart ? _query.Start : _query.End,
                        _stage == Stage.ResolveStart
                            ? NavigationEndpointRole.Start
                            : NavigationEndpointRole.Destination);
                    _endpointActive = true;
                }
                int lookupBefore = _meter.LookupProbes;
                int candidatesBefore = _meter.EndpointCandidates;
                NavigationEndpointResolutionStatus endpointStatus = _endpointWork.Advance(
                    lookupRemaining,
                    candidateRemaining);
                lookupRemaining = Math.Max(
                    0,
                    lookupRemaining - (_meter.LookupProbes - lookupBefore));
                candidateRemaining = Math.Max(
                    0,
                    candidateRemaining - (_meter.EndpointCandidates - candidatesBefore));
                if (endpointStatus == NavigationEndpointResolutionStatus.Pending)
                    return Status;
                if (endpointStatus != NavigationEndpointResolutionStatus.Success)
                {
                    return Finish(MapEndpointFailure(endpointStatus, _stage));
                }
                _requiresWorldStamp |= _endpointWork.RequiresWorldStamp;
                if (_stage == Stage.ResolveStart)
                {
                    _start = _endpointWork.Result;
                    _stage = Stage.ResolveEnd;
                    _endpointActive = false;
                }
                else
                {
                    if (_world.ChangeSequence != _worldChangeSequence)
                        return Finish(NavigationQueryAdmissionStatus.Stale);
                    _end = _endpointWork.Result;
                    _endpointWork.Reset();
                    NavigationWorldGraphLease lease = _lease!;
                    _result.Bind(
                        lease,
                        _query,
                        _start,
                        _end,
                        _areaPolicy!,
                        _startMedium,
                        _targetMedia,
                        _meter,
                        _worldChangeSequence,
                        _requiresWorldStamp);
                    _lease = null;
                    Status = NavigationQueryAdmissionStatus.Success;
                    _endpointActive = false;
                }
            }
        }
        return Status;
    }

    public void Dispose()
    {
        NavigationWorldGraphLease? lease = _lease;
        _lease = null;
        lease?.Dispose();
        _endpointWork.Reset();
        _areaPolicy = null;
        _query = default;
        _startMedium = default;
        _targetMedia = default;
        _start = default;
        _end = default;
        _worldChangeSequence = 0;
        _requiresWorldStamp = false;
        _endpointActive = false;
        _active = false;
    }

    private void BeginEndpointWork(
        NavigationEndpoint endpoint,
        NavigationEndpointRole role)
    {
        NavigationWorldGraph graph = _lease!.Graph;
        TraversalMedia effectiveTargetMedia = _query.AllowTransitions
            ? _targetMedia
            : NavigationCell.ToMedia(_startMedium);
        _endpointWork.Begin(
            graph,
            endpoint,
            role,
            _query.Agent,
            _areaPolicy!,
            role == NavigationEndpointRole.Start
                ? NavigationCell.ToMedia(_startMedium)
                : effectiveTargetMedia);
    }

    private NavigationQueryAdmissionStatus Finish(
        NavigationQueryAdmissionStatus status)
    {
        Status = status;
        if (status != NavigationQueryAdmissionStatus.Success)
            Dispose();
        return Status;
    }

    private static NavigationQueryAdmissionStatus MapEndpointFailure(
        NavigationEndpointResolutionStatus endpointStatus,
        Stage stage) => endpointStatus switch
        {
            NavigationEndpointResolutionStatus.NoMap => NavigationQueryAdmissionStatus.NoMap,
            NavigationEndpointResolutionStatus.InvalidEndpoint =>
                stage == Stage.ResolveStart
                    ? NavigationQueryAdmissionStatus.InvalidStart
                    : NavigationQueryAdmissionStatus.InvalidEnd,
            NavigationEndpointResolutionStatus.BudgetExceeded =>
                NavigationQueryAdmissionStatus.BudgetExceeded,
            NavigationEndpointResolutionStatus.CostOverflow =>
                NavigationQueryAdmissionStatus.CostOverflow,
            NavigationEndpointResolutionStatus.CapacityExceeded =>
                NavigationQueryAdmissionStatus.CapacityExceeded,
            _ => NavigationQueryAdmissionStatus.Stale
        };

}
