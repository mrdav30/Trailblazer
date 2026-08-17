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
    Stale = 10
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
    private readonly PathAlgorithm _expectedAlgorithm;
    private readonly NavigationWorkMeter _meter;
    private readonly NavigationEndpointResolutionWork _endpointWork;
    private readonly NavigationResolvedPathQuery _result;
    private NavigationWorldGraphLease? _lease;
    private NavigationAreaPolicy? _areaPolicy;
    private PathQuery _query;
    private TraversalMedium _medium;
    private NavigationResolvedEndpoint _start;
    private NavigationResolvedEndpoint _end;
    private Stage _stage;
    private bool _endpointActive;
    private bool _active;

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
        _expectedAlgorithm = expectedAlgorithm;
        _meter = new NavigationWorkMeter(default);
        _endpointWork = new NavigationEndpointResolutionWork(
            world,
            store,
            _meter,
            workspace,
            rayWorkspace);
        _result = new NavigationResolvedPathQuery();
    }

    internal void Begin(
        NavigationWorldGraphLease lease,
        PathQuery query)
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
        _stage = Stage.ResolvePolicy;
        _endpointActive = false;
        Status = NavigationQueryAdmissionStatus.Pending;
        bool supported = TryResolveSurfaceMedium(
            query,
            _expectedAlgorithm,
            out _medium);
        if (!query.Agent.IsValid)
            Finish(NavigationQueryAdmissionStatus.InvalidProfile);
        else if (!supported)
            Finish(NavigationQueryAdmissionStatus.Unsupported);
    }

    internal NavigationQueryAdmissionStatus Status { get; private set; }

    internal NavigationResolvedPathQuery Result => _result;

    internal NavigationWorkMeter Meter => _meter;

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
                if (_stage == Stage.ResolveStart)
                {
                    _start = _endpointWork.Result;
                    _stage = Stage.ResolveEnd;
                    _endpointActive = false;
                }
                else
                {
                    _end = _endpointWork.Result;
                    _endpointWork.Reset();
                    NavigationWorldGraphLease lease = _lease!;
                    _result.Bind(
                        lease,
                        _query,
                        _start,
                        _end,
                        _areaPolicy!,
                        _medium,
                        _meter);
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
        _medium = default;
        _start = default;
        _end = default;
        _endpointActive = false;
        _active = false;
    }

    private void BeginEndpointWork(
        NavigationEndpoint endpoint,
        NavigationEndpointRole role)
    {
        NavigationWorldGraph graph = _lease!.Graph;
        _endpointWork.Begin(
            graph,
            endpoint,
            role,
            _query.Agent,
            _areaPolicy!,
            _query.Traversal);
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

    private static bool TryResolveSurfaceMedium(
        PathQuery query,
        PathAlgorithm expectedAlgorithm,
        out TraversalMedium medium)
    {
        bool supported = query.Algorithm == expectedAlgorithm
            && expectedAlgorithm is PathAlgorithm.AStar or PathAlgorithm.FlowField
            && !query.AllowTransitions
            && query.Traversal.StartDomain != TraversalDomain.Volume
            && query.Traversal.TargetDomain != TraversalDomain.Volume
            && query.Traversal.CurrentMedium is TraversalMedium.Unknown or TraversalMedium.Solid;
        medium = TraversalMedium.Solid;
        return supported;
    }
}
