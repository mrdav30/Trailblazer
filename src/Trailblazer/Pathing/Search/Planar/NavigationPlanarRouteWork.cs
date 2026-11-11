//=======================================================================
// NavigationPlanarRouteWork.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Owns the snapshot and shared search workspace while a planar route is being built.</summary>
internal sealed partial class NavigationPlanarRouteWork : IDisposable
{
    private readonly GridWorld _world;
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationWorldGraphLease? _graphLease;
    private readonly NavigationQueryCapacityReservation _lane;
    private readonly (long Spawn, uint Version, ulong Sequence) _epoch;
    private readonly NavigationPlanarRouteRequest _request;
    private readonly NavigationAStarWorkspace? _aStar;
    private readonly NavigationRayWork? _ray;
    private readonly NavigationFlowFieldWorkspace? _flow;
    private readonly NavigationPlanarSearch? _planar;
    private readonly NavigationAreaPolicy? _policy;
    private readonly NavigationWorkMeter? _meter;
    private readonly NavigationResolvedPathQuery _query = new();
    private NavigationResolvedEndpoint _start;
    private bool _startResolved;
    private NavigationSurfaceAStarWork? _aStarWork;
    private NavigationFlowFieldWork? _flowWork;
    private int _flowPathSlot = -1;
    private int _length;
    private Fixed64 _cost;
    private int _countedActionsAt;
    private int _actionCount;
    private int _pointWrite;
    private int _actionScan;
    private int _actionWrite;
    private NavigationAStarGuidePoint[]? _points;
    private NavigationTransitionInstruction[]? _instructions;
    private NavigationAStarPayloadReservation _payloadReservation;
    private bool _flowCleared;
    private bool _disposed;

    internal NavigationPlanarRouteWork(GridWorld world, NavigationWorldGraphStore store,
        in NavigationPlanarRouteRequest request, NavigationAStarWorkspace? aStar, NavigationRayWork? ray,
        NavigationFlowFieldWorkspace? flow, NavigationQueryCapacityReservation lane = default,
        in NavigationAgentProfile2D sideViewProfile = default)
    {
        _world = world;
        _store = store;
        _lane = lane;
        _request = request;
        _aStar = aStar;
        _ray = ray;
        _flow = flow;
        _epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
        _graphLease = store.TryAcquire();
        if (_graphLease == null)
        {
            Status = NavigationGuideStatus.CapacityExceeded;
            return;
        }
        NavigationWorldGraph graph = _graphLease.Graph;
        NavigationMode2D mode = sideViewProfile.Mode == NavigationMode2D.SideView
            ? NavigationMode2D.SideView : NavigationMode2D.TopDown;
        if (!NavigationPlanarWork.TryGetMap(graph, request.Start.MapId, out var map, out var binding, mode))
        {
            Status = NavigationGuideStatus.NoMap;
            return;
        }
        if (graph.AreAllStructuralComponentsClosed || !graph.AreaCatalog.TryGet(request.AreaPolicy, out var policy))
        {
            Status = NavigationGuideStatus.Stale;
            return;
        }
        NavigationRayWorkspace rayWorkspace = aStar?.RayWorkspace ?? flow!.RayWorkspace;
        _meter = new NavigationWorkMeter(request.Budget);
        _policy = policy;
        _planar = mode == NavigationMode2D.SideView
            ? new NavigationSideViewSearch(world, graph, rayWorkspace.Planar, map, binding, request, policy, _meter,
                sideViewProfile)
            : new NavigationPlanarSearch(world, graph, rayWorkspace.Planar, map, binding, request, policy, _meter);
    }

    internal NavigationGuideStatus Status { get; private set; } = NavigationGuideStatus.Pending;
    internal bool AdmissionComplete => _query.Planar != null;

    internal static NavigationGuideStatus Find(GridWorld world, NavigationWorldGraphStore store,
        in NavigationPlanarRouteRequest request, NavigationAStarWorkspace? aStar, NavigationRayWork? ray,
        NavigationFlowFieldWorkspace? flow, Span<NavigationCellAddress> route, out int count, out Fixed64 cost,
        NavigationAStarPayloadCache? retention, out NavigationAStarGuideLease? guide,
        in NavigationAgentProfile2D sideViewProfile = default)
    {
        count = 0;
        cost = default;
        guide = default;
        using var work = new NavigationPlanarRouteWork(world, store, request, aStar, ray, flow,
            sideViewProfile: sideViewProfile);
        while (work.Status == NavigationGuideStatus.Pending)
        {
            var unlimited = new NavigationAcquisitionSlice(int.MaxValue, int.MaxValue, int.MaxValue,
                int.MaxValue, int.MaxValue);
            work.Advance(ref unlimited);
        }
        if (work.Status != NavigationGuideStatus.Success)
            return work.Status;
        if (retention != null)
        {
            var unlimited = new NavigationAcquisitionSlice(int.MaxValue, int.MaxValue, int.MaxValue);
            var status = work.PublishPin(retention, ref unlimited, out var pin);
            if (status == NavigationGuideStatus.Success)
                guide = retention.BindGuide(store, pin!);
            return status;
        }
        NavigationGuideStatus copyStatus = work.ResolveSnapshotStatus(route.Length < work._length
            ? NavigationGuideStatus.CapacityExceeded : NavigationGuideStatus.Success);
        if (copyStatus != NavigationGuideStatus.Success)
            return copyStatus;
        for (int i = 0; i < work._length; i++)
            route[i] = work.GetAddress(i);
        count = work._length;
        cost = work._cost;
        return NavigationGuideStatus.Success;
    }

    internal void Advance(ref NavigationAcquisitionSlice remaining, bool endpointOnly = false)
    {
        if (Status != NavigationGuideStatus.Pending)
            return;
        Status = ResolveSnapshotStatus(Status);
        if (Status != NavigationGuideStatus.Pending)
            return;
        if (!AdmissionComplete)
        {
            Status = AdvanceAdmission(ref remaining);
            if (Status != NavigationGuideStatus.Success || endpointOnly)
                return;
        }

        Status = ResolveSnapshotStatus(_aStar != null ? AdvanceAStar(ref remaining) : AdvanceFlow(ref remaining));
    }

    private NavigationGuideStatus AdvanceAdmission(ref NavigationAcquisitionSlice remaining)
    {
        NavigationPlanarSearch planar = _planar!;
        NavigationWorkMeter meter = _meter!;
        while (remaining.LookupRemaining > 0 && remaining.EndpointCandidateRemaining > 0)
        {
            int lookups = meter.LookupProbes;
            int candidates = meter.EndpointCandidates;
            NavigationGuideStatus status;
            if (!_startResolved)
            {
                status = planar.BindSource();
                if (status == NavigationGuideStatus.Success)
                    status = planar.ResolveStart(out _start);
                _startResolved = status == NavigationGuideStatus.Success;
            }
            else
            {
                status = planar.ResolveEnd(out var end);
                if (status == NavigationGuideStatus.Success)
                    _query.BindPlanar(planar, _start, end, _policy!, meter, _epoch.Sequence);
            }
            NavigationAcquisitionSlice.ConsumeAtomicLookups(ref remaining.LookupRemaining, meter, lookups);
            remaining.EndpointCandidateRemaining -= meter.EndpointCandidates - candidates;
            status = ResolveSnapshotStatus(status);
            if (status != NavigationGuideStatus.Success || AdmissionComplete)
                return status;
        }
        return NavigationGuideStatus.Pending;
    }

    private NavigationGuideStatus AdvanceAStar(ref NavigationAcquisitionSlice remaining)
    {
        var workspace = _aStar!;
        _aStarWork ??= new NavigationSurfaceAStarWork(_world, _store, _query, workspace, _ray!, 0);
        NavigationSurfaceAStarStatus status = _aStarWork.Advance(ref remaining);
        if (status == NavigationSurfaceAStarStatus.Pending)
            return NavigationGuideStatus.Pending;
        if (status != NavigationSurfaceAStarStatus.Success)
            return NavigationGuideStatusMapper.ToPublic(NavigationAStarQueryWork.MapSearchStatus(status));
        _length = workspace.PathNodeCount;
        workspace.NodeTable.TryGetSlot(workspace.PathNodes[_length - 1], out int slot);
        _cost = workspace.NodeTable.GetRecord(slot).Cost;
        return NavigationGuideStatus.Success;
    }

    private NavigationGuideStatus AdvanceFlow(ref NavigationAcquisitionSlice remaining)
    {
        var workspace = _flow!;
        _flowWork ??= new NavigationFlowFieldWork(_world, _query, workspace);
        NavigationFlowFieldStatus status = _flowWork.Advance(ref remaining);
        if (status != NavigationFlowFieldStatus.Success)
            return NavigationGuideStatusMapper.ToPublic(status);

        var medium = _query.StartMedium;
        if (_flowPathSlot < 0)
        {
            workspace.TryGetSlot(new NavigationMediumStateRef(_query.Start.Node, medium), out _flowPathSlot);
            _cost = workspace.GetRecord(_flowPathSlot).IntegrationCost;
        }
        while (remaining.NodeRemaining > 0)
        {
            ref NavigationFlowFieldSearchNode current = ref workspace.GetRecord(_flowPathSlot);
            bool atDestination = current.Address.Equals(_query.End.Address);
            if (!atDestination && remaining.LookupRemaining == 0)
                return NavigationGuideStatus.Pending;
            remaining.NodeRemaining--;
            // Each chosen successor was settled earlier, even when an action has zero cost.
            System.Diagnostics.Debug.Assert(_length < workspace.SettledCount);
            workspace.SettledSlots[_length++] = _flowPathSlot;
            if (atDestination)
                return NavigationGuideStatus.Success;
            if (!_query.Meter.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            remaining.LookupRemaining--;
            bool foundNode = _planar!.TryGetNode(current.SelectedEdge.Target, out NavigationNodeRef next);
            bool foundSlot = workspace.TryGetSlot(new NavigationMediumStateRef(next, medium), out _flowPathSlot);
            System.Diagnostics.Debug.Assert(foundNode && foundSlot && workspace.GetRecord(_flowPathSlot).Closed
                && workspace.GetRecord(_flowPathSlot).IntegrationCost <= current.IntegrationCost);
        }
        return NavigationGuideStatus.Pending;
    }

    internal NavigationGuideStatus PublishPin(NavigationAStarPayloadCache retention,
        ref NavigationAcquisitionSlice remaining, out NavigationAStarPayloadLease? pin)
    {
        pin = null;
        if (ResolveSnapshotStatus(NavigationGuideStatus.Success) == NavigationGuideStatus.Stale)
            return NavigationGuideStatus.Stale;
        if (_request.AllowTransitions)
        {
            while (_countedActionsAt < _length - 1)
            {
                if (remaining.NodeRemaining == 0)
                    return NavigationGuideStatus.Pending;
                remaining.NodeRemaining--;
                if (GetActionOrdinal(_countedActionsAt++) >= 0)
                    _actionCount++;
            }
        }
        var sideView = _planar as NavigationSideViewSearch;
        if (_points == null)
        {
            int pointCount = _length;
            if (sideView != null)
            {
                var status = AdvanceSideViewPoints(sideView, ref remaining);
                if (status != NavigationGuideStatus.Success)
                    return status;
                pointCount += sideView.RouteBendCount;
            }
            if (remaining.NodeRemaining == 0)
                return NavigationGuideStatus.Pending;
            if (!retention.TryReservePayload(NavigationPlanarGuidePayload.MeasureBytes(pointCount, _actionCount),
                    out _payloadReservation))
                return NavigationGuideStatus.CapacityExceeded;
            _points = new NavigationAStarGuidePoint[pointCount];
            if (sideView != null)
                sideView.RouteSupportCursor = 0;
            _instructions = _actionCount == 0
                ? Array.Empty<NavigationTransitionInstruction>() : new NavigationTransitionInstruction[_actionCount];
        }
        NavigationWorldGraph graph = _graphLease!.Graph;
        if (sideView != null)
        {
            var status = AdvanceSideViewPoints(sideView, ref remaining);
            if (status != NavigationGuideStatus.Success)
                return status;
            System.Diagnostics.Debug.Assert(_pointWrite == _points.Length);
        }
        else
        {
            while (_pointWrite < _length)
            {
                if (remaining.NodeRemaining == 0)
                    return NavigationGuideStatus.Pending;
                remaining.NodeRemaining--;
                NavigationCellAddress address = GetAddress(_pointWrite);
                graph.TryGetSeamPrism(address, out var prism);
                var position = new Vector3d(prism.Center.X, _query.Start.FootAnchor.Y, prism.Center.Z);
                _points[_pointWrite++] = new NavigationAStarGuidePoint(address, position, _request.Medium);
            }
        }
        while (_actionWrite < _actionCount)
        {
            if (remaining.NodeRemaining == 0)
                return NavigationGuideStatus.Pending;
            remaining.NodeRemaining--;
            int point = _actionScan++;
            int ordinal = GetActionOrdinal(point);
            if (ordinal >= 0)
                FillActionAt(_points, _instructions!, point, ordinal, _actionWrite++);
        }
        if (_flow != null && !_flowCleared)
        {
            _flowCleared = _flow.AdvanceResetSearch(ref remaining.NodeRemaining);
            if (!_flowCleared)
                return NavigationGuideStatus.Pending;
        }
        var payload = new NavigationPlanarGuidePayload(_points, _cost, _request.AreaPolicy, graph.GraphVersion,
            _epoch, _instructions!);
        return retention.PublishPlanarPayload(_store, payload, ref _payloadReservation, out pin);
    }

    private NavigationCellAddress GetAddress(int index)
    {
        if (_aStar == null)
            return _flow!.GetRecord(_flow.SettledSlots[index]).Address;
        _graphLease!.Graph.TryGetNodeAddress(_aStar.PathNodes[index].Node, out var address);
        return address;
    }

    // A missing footprint cell can lack a dependency page, so retain the exact graph and raw-world snapshot.
    private NavigationGuideStatus ResolveSnapshotStatus(NavigationGuideStatus status) =>
        TrailblazerPathingService.ResolvePlanarSnapshotStatus(status, _graphLease!.Graph, _store.Current,
            _epoch, (_world.SpawnToken, _world.Version, _world.ChangeSequence));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _aStarWork?.Dispose();
        _flowWork?.Dispose();
        if (_flowWork != null && !_flowCleared && _lane.Owner == null)
            _flow!.ResetSearch();
        _payloadReservation.Owner?.ReleasePayloadReservation(ref _payloadReservation);
        _query.Dispose();
        _graphLease?.Dispose();
        _lane.Owner?.Release(_lane);
    }
}
