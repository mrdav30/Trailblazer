//=======================================================================
// NavigationSurfaceAStarWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

internal enum NavigationSurfaceAStarStatus : byte
{
    Pending = 0,
    Success = 1,
    NoPath = 2,
    BudgetExceeded = 3,
    CostOverflow = 4,
    CapacityExceeded = 5,
    Stale = 6
}

/// <summary>Runs one bounded fixed-point surface A* query over a leased graph.</summary>
internal sealed class NavigationSurfaceAStarWork : IDisposable
{
    private enum Stage : byte
    {
        Search = 0,
        Reconstruct = 1,
        ReversePath = 2,
        ExpandGuide = 3,
        Simplify = 4,
        CopyRaw = 5,
        SortDependencies = 6,
        CaptureDependencies = 7,
        BuildPayload = 8
    }

    private NavigationResolvedPathQuery? _query;
    private NavigationWorldGraph? _graph;
    private readonly GridWorld _world;
    private readonly NavigationWorldGraphStore _store;
    private readonly NavigationRayWork _rayWork;
    private TraversalEvaluator _evaluator;
    private readonly NavigationAStarWorkspace _workspace;
    private readonly NavigationWorkMeter _meter;
    private readonly long _maximumPayloadBytes;
    private readonly Vector3d _targetFootAnchor;
    private readonly bool _useEuclideanHeuristic;
    private NavigationSurfaceEdgeEnumerator _edges;
    private NavigationSurfaceEdgeRouteWork _routeWork;
    private NavigationNodeRef _current;
    private NavigationNodeRef _pathCursor;
    private NavigationDependencySortWork _dependencySort;
    private NavigationDependencyStampWork? _dependencyStamp;
    private NavigationAStarGuidePoint[]? _payloadGuidePoints;
    private NavigationSurfaceAStarStatus _resultStatus;
    private Stage _stage;
    private int _pathWrite;
    private int _reverseLeft;
    private int _reverseRight;
    private int _pathEdgeOrdinal;
    private int _routeEdgeOrdinal;
    private int _guideRollback;
    private int _payloadWrite;
    private int _simplificationSourcePathOrdinal;
    private int _simplificationCandidatePathOrdinal;
    private int _simplificationWriteOrdinal;
    private int _rawCopyOrdinal;
    private int _rawCopyEndOrdinal;
    private int _finalizationLookupReservation;
    private ulong _simplificationWorldChangeSequence;
    private bool _hasCurrent;
    private bool _routeActive;
    private bool _reconstructionEdgeActive;
    private bool _lastGuidePointIsNode;
    private bool _hasCompletedSimplificationProof;

    internal NavigationSurfaceAStarWork(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationResolvedPathQuery query,
        NavigationAStarWorkspace workspace,
        NavigationRayWork rayWork,
        long maximumPayloadBytes)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(query, nameof(query));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        SwiftThrowHelper.ThrowIfNull(rayWork, nameof(rayWork));
        if (maximumPayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        _world = world;
        _store = store;
        _query = query;
        _graph = query.Graph;
        _workspace = workspace;
        _rayWork = rayWork;
        _workspace.ResetSearch();
        _meter = query.Meter;
        _maximumPayloadBytes = maximumPayloadBytes;
        _evaluator = new TraversalEvaluator(
            _graph,
            query.Query.Agent,
            query.AreaPolicy,
            query.StartMedium);
        _resultStatus = NavigationSurfaceAStarStatus.Success;
        _useEuclideanHeuristic = !query.Query.AllowTransitions
            && _graph.SurfaceComponents.TryGet(
                query.Start.Address,
                query.StartMedium,
                out NavigationSurfaceComponent startComponent)
            && startComponent.AllSurfaceEdgesEuclideanCertified;
        _targetFootAnchor = query.End.FootAnchor;
        if (!_graph.AreInSameSurfaceComponent(
                query.Start.Address,
                query.StartMedium,
                query.End.Address,
                query.StartMedium))
        {
            _resultStatus = NavigationSurfaceAStarStatus.NoPath;
            _workspace.PathNodeCount = 0;
            _dependencySort = new NavigationDependencySortWork(_workspace);
            _stage = Stage.SortDependencies;
            return;
        }
        if (!_workspace.NodeTable.TryGetOrAdd(
                query.Start.Node,
                out int startSlot,
                out _))
        {
            Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
            return;
        }
        ref NavigationAStarNodeRecord start = ref _workspace.NodeTable.GetRecord(startSlot);
        start.Cost = Fixed64.Zero;
        start.Heuristic = GetHeuristic(query.Start.Node);
        start.EstimatedTotalCost = start.Heuristic;
        Push(query.Start.Node, startSlot);
    }

    internal NavigationSurfaceAStarStatus Status { get; private set; }

    internal NavigationAStarPayload Result { get; private set; } = null!;

    internal NavigationSurfaceAStarStatus Advance(
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(connectionStepLimit, nameof(connectionStepLimit));
        if (Status != NavigationSurfaceAStarStatus.Pending)
            return Status;
        int lookupRemaining = lookupStepLimit;
        int nodeRemaining = nodeStepLimit;
        int edgeRemaining = edgeStepLimit;
        int connectionRemaining = connectionStepLimit;

        while (Status == NavigationSurfaceAStarStatus.Pending)
        {
            if (_stage == Stage.Search)
            {
                if (!_hasCurrent)
                {
                    if (_workspace.HeapCount == 0)
                    {
                        _resultStatus = NavigationSurfaceAStarStatus.NoPath;
                        _workspace.PathNodeCount = 0;
                        _dependencySort = new NavigationDependencySortWork(_workspace);
                        _stage = Stage.SortDependencies;
                        continue;
                    }
                    if (nodeRemaining == 0)
                    {
                        return _meter.RemainingExpandedNodes == 0
                            ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                            : Status;
                    }
                    if (!_meter.TryConsumeExpandedNodes(1))
                        return Finish(NavigationSurfaceAStarStatus.BudgetExceeded);
                    nodeRemaining--;
                    _current = Pop();
                    _workspace.NodeTable.TryGetSlot(_current, out int currentSlot);
                    ref NavigationAStarNodeRecord current =
                        ref _workspace.NodeTable.GetRecord(currentSlot);
                    current.Closed = true;
                    if (_current == _query!.End.Node)
                    {
                        _pathCursor = _current;
                        _stage = Stage.Reconstruct;
                        continue;
                    }
                    _edges = _graph!.EnumerateSurfaceEdges(_current);
                    _hasCurrent = true;
                }

                if (!_routeActive)
                {
                    NavigationSurfaceEdgeAdvanceStatus edgeStatus =
                        _edges.AdvanceOne(_meter, ref edgeRemaining);
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                    {
                        return _meter.RemainingEvaluatedEdges == 0
                            ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                            : Status;
                    }
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
                        continue;
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Complete)
                    {
                        _edges = default;
                        _hasCurrent = false;
                        continue;
                    }
                    NavigationGraphEdge edge = _edges.Current;
                    if (_workspace.NodeTable.TryGetSlot(
                            edge.Target,
                            out int existingSlot)
                        && _workspace.NodeTable.GetRecord(existingSlot).Closed)
                    {
                        continue;
                    }
                    if (!RecordPage(edge.Target, false))
                        return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                    _routeEdgeOrdinal = _edges.CurrentOrdinal;
                    NavigationSurfaceEdgeRouteStatus begin = _routeWork.Begin(
                        _evaluator,
                        _current,
                        edge,
                        emitPoints: false);
                    if (begin == NavigationSurfaceEdgeRouteStatus.Stale)
                        return Finish(NavigationSurfaceAStarStatus.Stale);
                    if (begin == NavigationSurfaceEdgeRouteStatus.CostOverflow)
                        return Finish(NavigationSurfaceAStarStatus.CostOverflow);
                    if (begin == NavigationSurfaceEdgeRouteStatus.Impassable)
                    {
                        _routeWork.Reset();
                        continue;
                    }
                    _routeActive = true;
                }
                NavigationSurfaceEdgeRouteStatus routeStatus = _routeWork.Advance(
                    _meter,
                    ref connectionRemaining);
                if (_routeWork.TryTakeDependencyNode(
                        out NavigationNodeRef dependencyNode)
                    && dependencyNode != _routeWork.Edge.Target
                    && !RecordPage(dependencyNode, true))
                {
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                }
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.Point)
                {
                    _routeWork.ConsumePoint();
                    continue;
                }
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.Pending)
                    return Status;
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.BudgetExceeded)
                    return Finish(NavigationSurfaceAStarStatus.BudgetExceeded);
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.CostOverflow)
                    return Finish(NavigationSurfaceAStarStatus.CostOverflow);
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.Stale)
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.Passable)
                {
                    NavigationSurfaceAStarStatus completion = ApplyRoute();
                    if (completion != NavigationSurfaceAStarStatus.Pending)
                        return completion;
                    continue;
                }
                ClearRoute();
                continue;
            }

            if (_stage == Stage.Reconstruct)
            {
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                if (_pathWrite >= _workspace.PathNodes.Length)
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                _workspace.PathNodes[_pathWrite++] = _pathCursor;
                if (_pathCursor == _query!.Start.Node)
                {
                    _workspace.PathNodeCount = _pathWrite;
                    _reverseLeft = 0;
                    _reverseRight = _pathWrite - 1;
                    _stage = Stage.ReversePath;
                    continue;
                }
                if (!_workspace.NodeTable.TryGetSlot(_pathCursor, out int pathSlot)
                    || !_workspace.NodeTable.GetRecord(pathSlot).HasParent)
                {
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                }
                _pathCursor = _workspace.NodeTable.GetRecord(pathSlot).Parent;
                continue;
            }

            if (_stage == Stage.ReversePath)
            {
                if (_reverseLeft < _reverseRight)
                {
                    if (nodeRemaining == 0)
                        return Status;
                    nodeRemaining--;
                    (_workspace.PathNodes[_reverseLeft], _workspace.PathNodes[_reverseRight]) =
                        (_workspace.PathNodes[_reverseRight], _workspace.PathNodes[_reverseLeft]);
                    _reverseLeft++;
                    _reverseRight--;
                    continue;
                }
                if (_workspace.PathNodeCount == 0)
                {
                    _dependencySort = new NavigationDependencySortWork(_workspace);
                    _stage = Stage.SortDependencies;
                    continue;
                }
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                NavigationNodeRef startNode = _workspace.PathNodes[0];
                if (!_graph!.TryGetNodeAddress(
                        startNode,
                        out NavigationCellAddress startAddress)
                    || !_graph.TryGetNodeState(
                        startNode,
                        out NavigationNodeState startState))
                {
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                }
                if (!AppendGuidePoint(
                        new NavigationAStarGuidePoint(
                            startAddress,
                            startState.FootAnchor),
                        pathNodeOrdinal: 0))
                {
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                }
                _edges = _graph.EnumerateSurfaceEdges(startNode);
                _pathEdgeOrdinal = 0;
                _stage = Stage.ExpandGuide;
                continue;
            }

            if (_stage == Stage.ExpandGuide)
            {
                if (_pathEdgeOrdinal + 1 >= _workspace.PathNodeCount)
                {
                    _routeWork.Reset();
                    _edges = default;
                    NavigationSurfaceAStarStatus begin = BeginSimplification();
                    if (begin != NavigationSurfaceAStarStatus.Pending)
                        return begin;
                    continue;
                }
                NavigationNodeRef sourceNode =
                    _workspace.PathNodes[_pathEdgeOrdinal];
                NavigationNodeRef targetNode =
                    _workspace.PathNodes[_pathEdgeOrdinal + 1];
                if (!_reconstructionEdgeActive)
                {
                    if (!_workspace.NodeTable.TryGetSlot(
                            targetNode,
                            out int targetSlot))
                    {
                        return Finish(NavigationSurfaceAStarStatus.Stale);
                    }
                    int parentEdgeOrdinal = _workspace.NodeTable
                        .GetRecord(targetSlot)
                        .ParentEdgeOrdinal;
                    NavigationSurfaceEdgeAdvanceStatus edgeStatus =
                        _edges.AdvanceOne(_meter, ref edgeRemaining);
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                    {
                        return _meter.RemainingEvaluatedEdges == 0
                            ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                            : Status;
                    }
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
                        continue;
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Complete
                        || _edges.CurrentOrdinal > parentEdgeOrdinal)
                    {
                        return Finish(NavigationSurfaceAStarStatus.Stale);
                    }
                    if (_edges.CurrentOrdinal < parentEdgeOrdinal)
                        continue;
                    NavigationGraphEdge edge = _edges.Current;
                    if (edge.Target != targetNode)
                        return Finish(NavigationSurfaceAStarStatus.Stale);
                    NavigationSurfaceEdgeRouteStatus begin = _routeWork.Begin(
                        _evaluator,
                        sourceNode,
                        edge,
                        emitPoints: true);
                    if (begin != NavigationSurfaceEdgeRouteStatus.Pending)
                    {
                        return Finish(begin == NavigationSurfaceEdgeRouteStatus.CostOverflow
                            ? NavigationSurfaceAStarStatus.CostOverflow
                            : NavigationSurfaceAStarStatus.Stale);
                    }
                    _guideRollback = _workspace.GuidePointCount;
                    _reconstructionEdgeActive = true;
                }

                NavigationSurfaceEdgeRouteStatus routeStatus = _routeWork.Advance(
                    _meter,
                    ref connectionRemaining);
                if (_routeWork.TryTakeDependencyNode(
                        out NavigationNodeRef dependencyNode)
                    && dependencyNode != targetNode
                    && !RecordPage(dependencyNode, true))
                {
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                }
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.Point)
                {
                    if (nodeRemaining == 0)
                        return Status;
                    nodeRemaining--;
                    bool isTarget = _routeWork.CurrentPointIsTargetFootAnchor;
                    if (!AppendGuidePoint(
                            _routeWork.CurrentPoint,
                            isTarget ? _pathEdgeOrdinal + 1 : -1))
                    {
                        return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                    }
                    _routeWork.ConsumePoint();
                    continue;
                }
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.Pending)
                    return Status;
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.BudgetExceeded)
                    return Finish(NavigationSurfaceAStarStatus.BudgetExceeded);
                if (routeStatus == NavigationSurfaceEdgeRouteStatus.CostOverflow)
                    return Finish(NavigationSurfaceAStarStatus.CostOverflow);
                if (routeStatus != NavigationSurfaceEdgeRouteStatus.Passable
                    || !HasExpectedRouteCost(sourceNode, targetNode))
                {
                    _workspace.GuidePointCount = _guideRollback;
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                }
                _routeWork.Reset();
                _edges = _graph!.EnumerateSurfaceEdges(targetNode);
                _reconstructionEdgeActive = false;
                _pathEdgeOrdinal++;
                continue;
            }

            if (_stage == Stage.Simplify)
                return AdvanceSimplification();

            if (_stage == Stage.CopyRaw)
            {
                if (_rawCopyOrdinal <= _rawCopyEndOrdinal)
                {
                    if (nodeRemaining == 0)
                        return Status;
                    nodeRemaining--;
                    _workspace.GuidePoints[_simplificationWriteOrdinal++] =
                        _workspace.GuidePoints[_rawCopyOrdinal++];
                    continue;
                }
                if (_rawCopyEndOrdinal == _workspace.GuidePointCount - 1)
                {
                    PrepareDependencyFinalization();
                    continue;
                }
                _simplificationSourcePathOrdinal++;
                _simplificationCandidatePathOrdinal = _workspace.PathNodeCount - 1;
                _stage = Stage.Simplify;
                continue;
            }

            if (_stage == Stage.SortDependencies)
            {
                int lookupBefore = _meter.LookupProbes;
                bool complete = _dependencySort.Advance(_meter, lookupRemaining);
                lookupRemaining -= _meter.LookupProbes - lookupBefore;
                if (!complete)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                        : Status;
                }
                _dependencySort = default;
                _stage = Stage.CaptureDependencies;
                continue;
            }

            if (_stage == Stage.CaptureDependencies)
            {
                if (!IsSimplificationWorldCurrent())
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                _dependencyStamp ??= new NavigationDependencyStampWork(
                    _graph!,
                    _query!.AreaPolicy,
                    _workspace.EndpointComponents,
                    _workspace.EndpointComponentCount,
                    _workspace.EndpointPages,
                    _workspace.EndpointPageCount);
                int lookupBefore = _meter.LookupProbes;
                bool complete = _dependencyStamp.Advance(_meter, lookupRemaining);
                lookupRemaining -= _meter.LookupProbes - lookupBefore;
                if (!complete)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                        : Status;
                }
                if (!_dependencyStamp.IsValid)
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                if (!IsSimplificationWorldCurrent())
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                long requiredPayloadBytes = NavigationAStarPayload.GetRetainedBytes(
                    _workspace.GuidePointCount,
                    _dependencyStamp.Result);
                if (requiredPayloadBytes > _maximumPayloadBytes)
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                _payloadGuidePoints = _workspace.GuidePointCount == 0
                    ? Array.Empty<NavigationAStarGuidePoint>()
                    : new NavigationAStarGuidePoint[_workspace.GuidePointCount];
                _stage = Stage.BuildPayload;
                continue;
            }

            if (_payloadWrite < _workspace.GuidePointCount)
            {
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                _payloadGuidePoints![_payloadWrite] =
                    _workspace.GuidePoints[_payloadWrite];
                _payloadWrite++;
                continue;
            }
            NavigationResolvedPathQuery resolved = _query!;
            if (!IsSimplificationWorldCurrent())
                return Finish(NavigationSurfaceAStarStatus.Stale);
            Fixed64 resultCost = Fixed64.Zero;
            if (_resultStatus == NavigationSurfaceAStarStatus.Success)
            {
                _workspace.NodeTable.TryGetSlot(resolved.End.Node, out int endSlot);
                resultCost = _workspace.NodeTable.GetRecord(endSlot).Cost;
            }
            Result = new NavigationAStarPayload(
                new NavigationAStarPayloadKey(
                    resolved.Query,
                    resolved.Start.Address,
                    resolved.End.Address),
                _payloadGuidePoints!,
                resultCost,
                _dependencyStamp!.Result,
                _hasCompletedSimplificationProof
                    ? _simplificationWorldChangeSequence
                    : null,
                _resultStatus);
            if (!IsSimplificationWorldCurrent())
                return Finish(NavigationSurfaceAStarStatus.Stale);
            return Finish(_resultStatus);
        }
        return Status;
    }

    public void Dispose()
    {
        NavigationResolvedPathQuery? query = _query;
        _query = null;
        query?.Dispose();
        ReleaseRuntimeState();
    }

    private NavigationSurfaceAStarStatus BeginSimplification()
    {
        _simplificationWorldChangeSequence = _world.ChangeSequence;
        _simplificationSourcePathOrdinal = 0;
        _simplificationCandidatePathOrdinal = _workspace.PathNodeCount - 1;
        _simplificationWriteOrdinal = 1;
        if (_workspace.PathNodeCount < 2 || _meter.RemainingSimplificationRays == 0)
        {
            _simplificationWriteOrdinal = _workspace.GuidePointCount;
            PrepareDependencyFinalization();
            return Status;
        }
        if (!TryGetFinalizationLookupReservation(
                _workspace.EndpointComponentCount,
                _workspace.EndpointPageCount,
                out _finalizationLookupReservation))
        {
            _simplificationWriteOrdinal = _workspace.GuidePointCount;
            PrepareDependencyFinalization();
            return Status;
        }
        if (!_meter.TrySetLookupReservationFloor(_finalizationLookupReservation))
        {
            _simplificationWriteOrdinal = _workspace.GuidePointCount;
            PrepareDependencyFinalization();
            return Status;
        }
        _stage = Stage.Simplify;
        return Status;
    }

    private NavigationSurfaceAStarStatus AdvanceSimplification()
    {
        if (!IsSimplificationWorldCurrent())
            return Finish(NavigationSurfaceAStarStatus.Stale);
        if (_simplificationSourcePathOrdinal + 1 >= _workspace.PathNodeCount)
        {
            PrepareDependencyFinalization();
            return Status;
        }
        if (_meter.RemainingSimplificationRays == 0)
        {
            BeginRawCopy(copySuffix: true);
            return Status;
        }
        if (_simplificationCandidatePathOrdinal <= _simplificationSourcePathOrdinal)
        {
            BeginRawCopy(copySuffix: false);
            return Status;
        }
        if (_simplificationCandidatePathOrdinal == _simplificationSourcePathOrdinal + 1
            && _workspace.PathNodeGuidePointOrdinals[_simplificationCandidatePathOrdinal]
                == _workspace.PathNodeGuidePointOrdinals[_simplificationSourcePathOrdinal] + 1)
        {
            BeginRawCopy(copySuffix: false);
            return Status;
        }
        if (!_meter.TryConsumeSimplificationRays(1))
        {
            BeginRawCopy(copySuffix: true);
            return Status;
        }

        int sourcePathOrdinal = _simplificationSourcePathOrdinal;
        int candidatePathOrdinal = _simplificationCandidatePathOrdinal;
        NavigationAStarGuidePoint source = _workspace.GuidePoints[
            _workspace.PathNodeGuidePointOrdinals[sourcePathOrdinal]];
        NavigationAStarGuidePoint candidate = _workspace.GuidePoints[
            _workspace.PathNodeGuidePointOrdinals[candidatePathOrdinal]];
        _rayWork.Begin(new NavigationRayRequest(
            _world,
            _store,
            _graph!,
            _query!.Query.Agent,
            _query.AreaPolicy,
            TraversalMedium.Solid,
            source.Position,
            candidate.Position,
            NavigationRayEndpointAllowance.None,
            NavigationRayChainConstraint.SeedAt(source.Address)));
        NavigationRayStatus rayStatus = _rayWork.Advance(_meter);
        if (rayStatus == NavigationRayStatus.Pending)
            return Status;
        if (rayStatus == NavigationRayStatus.Stale)
            return Finish(NavigationSurfaceAStarStatus.Stale);
        if (rayStatus is not NavigationRayStatus.Success
            and not NavigationRayStatus.Blocked)
        {
            BeginRawCopy(copySuffix: true);
            return Status;
        }

        bool accepted = false;
        if (rayStatus == NavigationRayStatus.Success)
        {
            NavigationRayResult ray = _rayWork.Result;
            if (ray.StartAddress == source.Address
                && ray.EndAddress == candidate.Address)
            {
                NavigationNodeRef sourceNode = _workspace.PathNodes[sourcePathOrdinal];
                NavigationNodeRef candidateNode = _workspace.PathNodes[candidatePathOrdinal];
                if (!_workspace.NodeTable.TryGetSlot(sourceNode, out int sourceSlot)
                    || !_workspace.NodeTable.TryGetSlot(candidateNode, out int candidateSlot)
                    || !Fixed64.TrySubtract(
                        _workspace.NodeTable.GetRecord(candidateSlot).Cost,
                        _workspace.NodeTable.GetRecord(sourceSlot).Cost,
                        out Fixed64 rawCost))
                {
                    BeginRawCopy(copySuffix: true);
                    return Status;
                }
                accepted = ray.TraversalCost <= rawCost;
            }
        }

        if (_world.ChangeSequence != _simplificationWorldChangeSequence)
            return Finish(NavigationSurfaceAStarStatus.Stale);
        if (!TryMergeRayDependencies())
        {
            if (_world.ChangeSequence != _simplificationWorldChangeSequence)
                return Finish(NavigationSurfaceAStarStatus.Stale);
            BeginRawCopy(copySuffix: true);
            return Status;
        }
        _hasCompletedSimplificationProof = true;
        if (!IsSimplificationWorldCurrent())
            return Finish(NavigationSurfaceAStarStatus.Stale);

        if (!accepted)
        {
            _simplificationCandidatePathOrdinal--;
            return Status;
        }
        _workspace.GuidePoints[_simplificationWriteOrdinal++] = candidate;
        _simplificationSourcePathOrdinal = candidatePathOrdinal;
        _simplificationCandidatePathOrdinal = _workspace.PathNodeCount - 1;
        if (_simplificationSourcePathOrdinal + 1 >= _workspace.PathNodeCount)
            PrepareDependencyFinalization();
        return Status;
    }

    private bool TryMergeRayDependencies()
    {
        if (_world.ChangeSequence != _simplificationWorldChangeSequence)
            return false;
        NavigationDependencyWorkspace target = _workspace.EndpointWorkspace.Dependencies;
        NavigationDependencyWorkspace source = _workspace.RayWorkspace.Dependencies;
        if (!target.TryCountMissing(
                source,
                _meter,
                out int missingComponents,
                out int missingPages)
            || !target.CanFit(missingComponents, missingPages))
        {
            return false;
        }
        int componentCount;
        int pageCount;
        try
        {
            componentCount = checked(target.ComponentCount + missingComponents);
            pageCount = checked(target.PageCount + missingPages);
        }
        catch (OverflowException)
        {
            return false;
        }
        int priorReservation = _finalizationLookupReservation;
        if (!TryGetFinalizationLookupReservation(
                componentCount,
                pageCount,
                out int enlargedReservation)
            || !_meter.TrySetLookupReservationFloor(enlargedReservation))
        {
            return false;
        }
        if (source.ComponentCount > int.MaxValue - source.PageCount)
        {
            _meter.TrySetLookupReservationFloor(priorReservation);
            return false;
        }
        int appendProbeCount = source.ComponentCount + source.PageCount;
        // A terminal ray and both dependency passes are one bounded atomic unit:
        // prove and debit the complete append pass before mutating the target.
        if (!_meter.TryConsumeLookupProbes(appendProbeCount))
        {
            _meter.TrySetLookupReservationFloor(priorReservation);
            return false;
        }
        target.CommitMerge(source);
        _finalizationLookupReservation = enlargedReservation;
        return _world.ChangeSequence == _simplificationWorldChangeSequence;
    }

    private static bool TryGetFinalizationLookupReservation(
        int componentCount,
        int pageCount,
        out int reservation)
    {
        try
        {
            reservation = checked(
                NavigationDependencySortWork.GetMaximumComparisonCount(
                    componentCount,
                    pageCount)
                + componentCount
                + pageCount);
        }
        catch (OverflowException)
        {
            reservation = 0;
            return false;
        }
        return true;
    }

    private void BeginRawCopy(bool copySuffix)
    {
        _rawCopyOrdinal = _workspace.PathNodeGuidePointOrdinals[
            _simplificationSourcePathOrdinal] + 1;
        _rawCopyEndOrdinal = copySuffix
            ? _workspace.GuidePointCount - 1
            : _workspace.PathNodeGuidePointOrdinals[_simplificationSourcePathOrdinal + 1];
        _stage = Stage.CopyRaw;
    }

    private void PrepareDependencyFinalization()
    {
        if (!IsSimplificationWorldCurrent())
        {
            Finish(NavigationSurfaceAStarStatus.Stale);
            return;
        }
        _workspace.GuidePointCount = _simplificationWriteOrdinal;
        _meter.ReleaseLookupReservationFloor();
        _finalizationLookupReservation = 0;
        _dependencySort = new NavigationDependencySortWork(_workspace);
        _stage = Stage.SortDependencies;
    }

    private bool IsSimplificationWorldCurrent() =>
        !_hasCompletedSimplificationProof
        || _world.ChangeSequence == _simplificationWorldChangeSequence;

    private void ReleaseRuntimeState()
    {
        _graph = null;
        _evaluator = default;
        _edges = default;
        _routeWork.Reset();
        _meter.ReleaseLookupReservationFloor();
        _finalizationLookupReservation = 0;
        _rayWork.Reset();
        _dependencySort = default;
    }

    private bool RecordPage(NavigationNodeRef node, bool recordComponent)
    {
        if (!_graph!.TryGetNodeAddress(node, out NavigationCellAddress address)
            || (recordComponent
                && (!_graph.TryGetSurfaceComponent(
                        address,
                        _query!.StartMedium,
                        out NavigationSurfaceComponentKey componentKey,
                        out _)
                    || !_workspace.TryRecordEndpointComponent(componentKey))))
        {
            return false;
        }
        return _workspace.TryRecordEndpointPage(
            address.MapId,
            node.CellSlot / NavigationSemanticPage.SlotCount);
    }

    private NavigationSurfaceAStarStatus Finish(NavigationSurfaceAStarStatus status)
    {
        Status = status;
        _query?.ReleaseLease();
        ReleaseRuntimeState();
        return Status;
    }

    private NavigationSurfaceAStarStatus ApplyRoute()
    {
        NavigationGraphEdge edge = _routeWork.Edge;
        Fixed64 edgeCost = _routeWork.Cost;
        int edgeOrdinal = _routeEdgeOrdinal;
        ClearRoute();
        if (!_workspace.NodeTable.TryGetOrAdd(
                edge.Target,
                out int targetSlot,
                out bool added))
        {
            return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
        }
        ref NavigationAStarNodeRecord target =
            ref _workspace.NodeTable.GetRecord(targetSlot);
        _workspace.NodeTable.TryGetSlot(_current, out int sourceSlot);
        Fixed64 sourceCost = _workspace.NodeTable.GetRecord(sourceSlot).Cost;
        if (!Fixed64.TryAdd(sourceCost, edgeCost, out Fixed64 nextCost))
            return Finish(NavigationSurfaceAStarStatus.CostOverflow);
        if (!added && nextCost >= target.Cost)
            return Status;
        target.Cost = nextCost;
        target.Heuristic = GetHeuristic(edge.Target);
        if (!Fixed64.TryAdd(
                target.Cost,
                target.Heuristic,
                out target.EstimatedTotalCost))
        {
            return Finish(NavigationSurfaceAStarStatus.CostOverflow);
        }
        target.Parent = _current;
        target.ParentEdgeOrdinal = edgeOrdinal;
        target.HasParent = true;
        if (added)
            Push(edge.Target, targetSlot);
        else
            SortUp(target.HeapIndex);
        return Status;
    }

    private void ClearRoute()
    {
        _routeWork.Reset();
        _routeActive = false;
    }

    private bool HasExpectedRouteCost(
        NavigationNodeRef source,
        NavigationNodeRef target)
    {
        if (!_workspace.NodeTable.TryGetSlot(source, out int sourceSlot)
            || !_workspace.NodeTable.TryGetSlot(target, out int targetSlot))
        {
            return false;
        }
        Fixed64 sourceCost = _workspace.NodeTable.GetRecord(sourceSlot).Cost;
        Fixed64 targetCost = _workspace.NodeTable.GetRecord(targetSlot).Cost;
        return Fixed64.TrySubtract(
                targetCost,
                sourceCost,
                out Fixed64 expectedCost)
            && expectedCost == _routeWork.Cost;
    }

    private bool AppendGuidePoint(
        NavigationAStarGuidePoint point,
        int pathNodeOrdinal)
    {
        bool isNode = pathNodeOrdinal >= 0;
        int count = _workspace.GuidePointCount;
        if (count != 0
            && _workspace.GuidePoints[count - 1].Position == point.Position)
        {
            NavigationAStarGuidePoint previous = _workspace.GuidePoints[count - 1];
            if (_lastGuidePointIsNode && !isNode)
                return true;
            if (!_lastGuidePointIsNode || previous.Address == point.Address)
            {
                _workspace.GuidePoints[count - 1] = point;
                _lastGuidePointIsNode = isNode;
                if (isNode)
                    _workspace.PathNodeGuidePointOrdinals[pathNodeOrdinal] = count - 1;
                return true;
            }
        }
        if (count >= _workspace.GuidePoints.Length)
            return false;
        _workspace.GuidePoints[count] = point;
        _workspace.GuidePointCount = count + 1;
        _lastGuidePointIsNode = isNode;
        if (isNode)
            _workspace.PathNodeGuidePointOrdinals[pathNodeOrdinal] = count;
        return true;
    }

    private void Push(NavigationNodeRef node, int slot)
    {
        int index = _workspace.HeapCount++;
        _workspace.HeapNodes[index] = node;
        _workspace.NodeTable.GetRecord(slot).HeapIndex = index;
        SortUp(index);
    }

    private NavigationNodeRef Pop()
    {
        NavigationNodeRef result = _workspace.HeapNodes[0];
        int last = --_workspace.HeapCount;
        if (last > 0)
        {
            _workspace.HeapNodes[0] = _workspace.HeapNodes[last];
            _workspace.NodeTable.TryGetSlot(_workspace.HeapNodes[0], out int slot);
            _workspace.NodeTable.GetRecord(slot).HeapIndex = 0;
            SortDown(0);
        }
        return result;
    }

    private void SortUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (CompareHeap(index, parent) >= 0)
                break;
            SwapHeap(index, parent);
            index = parent;
        }
    }

    private void SortDown(int index)
    {
        while (true)
        {
            int left = checked((index * 2) + 1);
            if (left >= _workspace.HeapCount)
                return;
            int right = left + 1;
            int best = right < _workspace.HeapCount && CompareHeap(right, left) < 0
                ? right
                : left;
            if (CompareHeap(best, index) >= 0)
                return;
            SwapHeap(index, best);
            index = best;
        }
    }

    private int CompareHeap(int leftIndex, int rightIndex)
    {
        NavigationNodeRef left = _workspace.HeapNodes[leftIndex];
        NavigationNodeRef right = _workspace.HeapNodes[rightIndex];
        _workspace.NodeTable.TryGetSlot(left, out int leftSlot);
        _workspace.NodeTable.TryGetSlot(right, out int rightSlot);
        ref NavigationAStarNodeRecord leftRecord =
            ref _workspace.NodeTable.GetRecord(leftSlot);
        ref NavigationAStarNodeRecord rightRecord =
            ref _workspace.NodeTable.GetRecord(rightSlot);
        int comparison = leftRecord.EstimatedTotalCost.CompareTo(
            rightRecord.EstimatedTotalCost);
        if (comparison != 0)
            return comparison;
        comparison = leftRecord.Heuristic.CompareTo(rightRecord.Heuristic);
        if (comparison != 0)
            return comparison;
        _graph!.TryGetNodeAddress(left, out NavigationCellAddress leftAddress);
        _graph.TryGetNodeAddress(right, out NavigationCellAddress rightAddress);
        return leftAddress.CompareTo(rightAddress);
    }

    private void SwapHeap(int left, int right)
    {
        (_workspace.HeapNodes[left], _workspace.HeapNodes[right]) =
            (_workspace.HeapNodes[right], _workspace.HeapNodes[left]);
        _workspace.NodeTable.TryGetSlot(_workspace.HeapNodes[left], out int leftSlot);
        _workspace.NodeTable.TryGetSlot(_workspace.HeapNodes[right], out int rightSlot);
        _workspace.NodeTable.GetRecord(leftSlot).HeapIndex = left;
        _workspace.NodeTable.GetRecord(rightSlot).HeapIndex = right;
    }

    private Fixed64 GetHeuristic(NavigationNodeRef node)
    {
        if (!_useEuclideanHeuristic
            || !_graph!.TryGetNodeState(node, out NavigationNodeState state)
            || !TryGetHeuristicFootAnchor(state, out Vector3d footAnchor)
            || !NavigationDistanceMath.TryFloor(
                footAnchor,
                _targetFootAnchor,
                out Fixed64 heuristic))
        {
            return Fixed64.Zero;
        }
        return heuristic;
    }

    private bool TryGetHeuristicFootAnchor(
        NavigationNodeState state,
        out Vector3d footAnchor)
    {
        if (_query!.StartMedium == TraversalMedium.Solid)
        {
            footAnchor = state.FootAnchor;
            return true;
        }

        return state.TryGetCenteredVolumeFootAnchor(
            _query.Query.Agent.Shape.Height,
            out footAnchor);
    }
}
