//=======================================================================
// NavigationSurfaceAStarWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

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
        SortDependencies = 3,
        CaptureDependencies = 4,
        BuildPayload = 5
    }

    private NavigationResolvedPathQuery? _query;
    private NavigationWorldGraph? _graph;
    private TraversalEvaluator _evaluator;
    private readonly NavigationAStarWorkspace _workspace;
    private readonly NavigationWorkMeter _meter;
    private readonly long _maximumPayloadBytes;
    private readonly Vector3d _targetFootAnchor;
    private readonly bool _useEuclideanHeuristic;
    private NavigationSurfaceEdgeEnumerator _edges;
    private NavigationGraphEdge _pendingEdge;
    private TraversalExplicitEdgeWork _explicitEdgeWork;
    private NavigationNodeRef _current;
    private NavigationNodeRef _pathCursor;
    private NavigationDependencySortWork? _dependencySort;
    private NavigationDependencyStampWork? _dependencyStamp;
    private NavigationCellAddress[]? _payloadNodes;
    private NavigationSurfaceAStarStatus _resultStatus;
    private Stage _stage;
    private int _pathWrite;
    private int _reverseLeft;
    private int _reverseRight;
    private int _payloadWrite;
    private bool _hasCurrent;
    private bool _hasPendingEdge;
    private bool _explicitEdgeActive;

    internal NavigationSurfaceAStarWork(
        NavigationResolvedPathQuery query,
        NavigationAStarWorkspace workspace,
        long maximumPayloadBytes = long.MaxValue)
    {
        SwiftThrowHelper.ThrowIfNull(query, nameof(query));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        if (maximumPayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        _query = query;
        _graph = query.Graph;
        _workspace = workspace;
        _workspace.ResetSearch();
        _meter = query.Meter;
        _maximumPayloadBytes = maximumPayloadBytes;
        _evaluator = new TraversalEvaluator(
            _graph,
            query.Query.Agent,
            query.AreaPolicy,
            query.Medium);
        _resultStatus = NavigationSurfaceAStarStatus.Success;
        _useEuclideanHeuristic = _graph.Composition
            .GetComponentRecord(query.Start.Address.MapId)
            .AllSurfaceEdgesEuclideanCertified;
        bool targetResolved = _graph.TryGetNodeState(
            query.End.Node,
            out NavigationNodeState targetState);
        _targetFootAnchor = targetState.FootAnchor;
        if (!targetResolved)
        {
            Finish(NavigationSurfaceAStarStatus.Stale);
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

                if (!_hasPendingEdge)
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
                    _pendingEdge = _edges.Current;
                    _hasPendingEdge = true;
                }
                if (_explicitEdgeActive)
                {
                    if (connectionRemaining == 0)
                    {
                        return _meter.RemainingConnectionLegs == 0
                            ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                            : Status;
                    }
                    if (!_meter.TryConsumeConnectionLegs(1))
                        return Finish(NavigationSurfaceAStarStatus.BudgetExceeded);
                    connectionRemaining--;
                    TraversalExplicitEdgeStatus explicitStatus =
                        _evaluator.AdvanceExplicitEdge(
                            ref _explicitEdgeWork,
                            out NavigationNodeRef dependencyNode,
                            out Fixed64 explicitCost);
                    if (dependencyNode.IsValid && !RecordPage(dependencyNode))
                        return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                    if (explicitStatus == TraversalExplicitEdgeStatus.Pending)
                        continue;
                    TraversalEvaluationStatus explicitEvaluation = explicitStatus switch
                    {
                        TraversalExplicitEdgeStatus.Passable =>
                            TraversalEvaluationStatus.Passable,
                        TraversalExplicitEdgeStatus.CostOverflow =>
                            TraversalEvaluationStatus.CostOverflow,
                        _ => TraversalEvaluationStatus.Impassable
                    };
                    _explicitEdgeActive = false;
                    NavigationSurfaceAStarStatus completion = ApplyPendingEdge(
                        explicitEvaluation,
                        explicitCost);
                    if (completion != NavigationSurfaceAStarStatus.Pending)
                        return completion;
                    continue;
                }

                NavigationGraphEdge edge = _pendingEdge;
                if (_workspace.NodeTable.TryGetSlot(edge.Target, out int existingSlot)
                    && _workspace.NodeTable.GetRecord(existingSlot).Closed)
                {
                    ClearPendingEdge();
                    continue;
                }
                if (!RecordPage(edge.Target))
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                if (edge.Kind == NavigationGraphEdgeKind.Explicit)
                {
                    TraversalExplicitEdgeStatus explicitStatus =
                        _evaluator.BeginExplicitEdge(
                            _current,
                            edge,
                            out _explicitEdgeWork);
                    if (explicitStatus == TraversalExplicitEdgeStatus.Pending)
                    {
                        _explicitEdgeActive = true;
                        continue;
                    }
                    TraversalEvaluationStatus beginEvaluation = explicitStatus ==
                        TraversalExplicitEdgeStatus.CostOverflow
                            ? TraversalEvaluationStatus.CostOverflow
                            : TraversalEvaluationStatus.Impassable;
                    NavigationSurfaceAStarStatus beginCompletion = ApplyPendingEdge(
                        beginEvaluation,
                        Fixed64.Zero);
                    if (beginCompletion != NavigationSurfaceAStarStatus.Pending)
                        return beginCompletion;
                    continue;
                }
                TraversalEvaluationStatus evaluation = _evaluator.EvaluateEdge(
                    _current,
                    edge,
                    out Fixed64 edgeCost);
                NavigationSurfaceAStarStatus edgeCompletion = ApplyPendingEdge(
                    evaluation,
                    edgeCost);
                if (edgeCompletion != NavigationSurfaceAStarStatus.Pending)
                    return edgeCompletion;
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
                _dependencySort = new NavigationDependencySortWork(_workspace);
                _stage = Stage.SortDependencies;
                continue;
            }

            if (_stage == Stage.SortDependencies)
            {
                int lookupBefore = _meter.LookupProbes;
                bool complete = _dependencySort!.Advance(_meter, lookupRemaining);
                lookupRemaining -= _meter.LookupProbes - lookupBefore;
                if (!complete)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationSurfaceAStarStatus.BudgetExceeded)
                        : Status;
                }
                _dependencySort = null;
                _stage = Stage.CaptureDependencies;
                continue;
            }

            if (_stage == Stage.CaptureDependencies)
            {
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
                long requiredPayloadBytes = NavigationAStarPayload.GetRetainedBytes(
                    _workspace.PathNodeCount,
                    _dependencyStamp.Result);
                if (requiredPayloadBytes > _maximumPayloadBytes)
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                _payloadNodes = _workspace.PathNodeCount == 0
                    ? Array.Empty<NavigationCellAddress>()
                    : new NavigationCellAddress[_workspace.PathNodeCount];
                _stage = Stage.BuildPayload;
                continue;
            }

            if (_payloadWrite < _workspace.PathNodeCount)
            {
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                if (!_graph!.TryGetNodeAddress(
                        _workspace.PathNodes[_payloadWrite],
                        out NavigationCellAddress address))
                {
                    return Finish(NavigationSurfaceAStarStatus.Stale);
                }
                _payloadNodes![_payloadWrite] = address;
                _payloadWrite++;
                continue;
            }
            NavigationResolvedPathQuery resolved = _query!;
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
                _payloadNodes!,
                resultCost,
                _dependencyStamp!.Result,
                _resultStatus);
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

    private void ReleaseRuntimeState()
    {
        _graph = null;
        _evaluator = default;
        _edges = default;
        _pendingEdge = default;
        _explicitEdgeWork = default;
    }

    private bool RecordPage(NavigationNodeRef node)
    {
        if (!_graph!.TryGetNodeAddress(node, out NavigationCellAddress address)
            || !_graph.TryGetComponentKey(address.MapId, out string componentKey)
            || !_workspace.TryRecordEndpointComponent(componentKey))
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

    private NavigationSurfaceAStarStatus ApplyPendingEdge(
        TraversalEvaluationStatus evaluation,
        Fixed64 edgeCost)
    {
        NavigationGraphEdge edge = _pendingEdge;
        ClearPendingEdge();
        if (evaluation == TraversalEvaluationStatus.CostOverflow)
            return Finish(NavigationSurfaceAStarStatus.CostOverflow);
        if (evaluation != TraversalEvaluationStatus.Passable)
            return Status;
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
        target.HasParent = true;
        if (added)
            Push(edge.Target, targetSlot);
        else
            SortUp(target.HeapIndex);
        return Status;
    }

    private void ClearPendingEdge()
    {
        _pendingEdge = default;
        _hasPendingEdge = false;
        _explicitEdgeActive = false;
        _explicitEdgeWork = default;
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
            || !NavigationDistanceMath.TryFloor(
                state.FootAnchor,
                _targetFootAnchor,
                out Fixed64 heuristic))
        {
            return Fixed64.Zero;
        }
        return heuristic;
    }
}
