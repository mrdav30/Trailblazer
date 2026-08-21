//=======================================================================
// NavigationFlowFieldWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Builds one bounded weighted destination-centric flow-field prefix.</summary>
internal sealed class NavigationFlowFieldWork : IDisposable
{
    private enum Stage : byte
    {
        Search = 0,
        SortDependencies = 1,
        CaptureDependencies = 2,
        BuildPayload = 3,
        SortPayloadNodes = 4,
        SortPayloadLookup = 5
    }

    private NavigationResolvedPathQuery? _query;
    private NavigationWorldGraph? _graph;
    private TraversalEvaluator _evaluator;
    private readonly NavigationFlowFieldWorkspace _workspace;
    private readonly NavigationWorkMeter _meter;
    private readonly long _maximumPayloadBytes;
    private NavigationFlowFieldOpenHeap _heap;
    private NavigationIncomingSurfaceEdgeEnumerator _incoming;
    private NavigationIncomingSurfaceEdge _pendingIncoming;
    private TraversalExplicitEdgeWork _explicitEdgeWork;
    private NavigationDependencySortWork _dependencySort;
    private NavigationFlowPayloadSortWork _payloadSort;
    private NavigationDependencyStampWork? _dependencyStamp;
    private NavigationFlowFieldNode[]? _payloadNodes;
    private int[]? _payloadLookup;
    private NavigationFlowFieldStatus _resultStatus;
    private Stage _stage;
    private int _currentSlot;
    private int _payloadWrite;
    private bool _hasCurrent;
    private bool _hasPendingIncoming;
    private bool _explicitEdgeActive;
    private bool _originSettled;
    private bool _isComplete;
    private Fixed64 _coverageThreshold;

    internal NavigationFlowFieldWork(
        NavigationResolvedPathQuery query,
        NavigationFlowFieldWorkspace workspace,
        long maximumPayloadBytes = long.MaxValue)
    {
        SwiftThrowHelper.ThrowIfNull(query, nameof(query));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        if (maximumPayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        if (query.Query.Algorithm != PathAlgorithm.FlowField
            || query.Query.AllowTransitions
            || query.Query.Traversal.StartDomain == TraversalDomain.Volume
            || query.Query.Traversal.TargetDomain == TraversalDomain.Volume)
        {
            throw new ArgumentException(
                "Flow work requires one transition-free surface FlowField query.",
                nameof(query));
        }

        _query = query;
        _graph = query.Graph;
        _workspace = workspace;
        _workspace.ResetSearch();
        _meter = query.Meter;
        _maximumPayloadBytes = maximumPayloadBytes;
        _heap = new NavigationFlowFieldOpenHeap(workspace);
        _evaluator = new TraversalEvaluator(
            _graph,
            query.Query.Agent,
            query.AreaPolicy,
            query.Medium);
        _resultStatus = NavigationFlowFieldStatus.Success;

        if (!_graph.AreInSameSurfaceComponent(
                query.Start.Address,
                query.Medium,
                query.End.Address,
                query.Medium))
        {
            Finish(NavigationFlowFieldStatus.NoPath);
            return;
        }
        if (!_graph.TryGetSurfaceComponent(
                query.End.Address,
                query.Medium,
                out NavigationSurfaceComponentKey component,
                out _)
            || !_workspace.TryRecordComponent(component)
            || !TryRecordPage(query.End.Node)
            || !_workspace.TryGetOrAdd(
                query.End.Node,
                out int destinationSlot,
                out _))
        {
            Finish(NavigationFlowFieldStatus.CapacityExceeded);
            return;
        }
        ref NavigationFlowFieldSearchNode destination =
            ref _workspace.GetRecord(destinationSlot);
        destination.Address = query.End.Address;
        destination.IntegrationCost = Fixed64.Zero;
        destination.HeapIndex = -1;
        _heap.Push(destinationSlot);
    }

    internal NavigationFlowFieldStatus Status { get; private set; }

    internal NavigationFlowFieldPayload? Result { get; private set; }

    internal NavigationFlowFieldStatus Advance(
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(connectionStepLimit, nameof(connectionStepLimit));
        if (Status != NavigationFlowFieldStatus.Pending)
            return Status;
        int lookupRemaining = lookupStepLimit;
        int nodeRemaining = nodeStepLimit;
        int edgeRemaining = edgeStepLimit;
        int connectionRemaining = connectionStepLimit;

        while (Status == NavigationFlowFieldStatus.Pending)
        {
            if (_stage == Stage.Search)
            {
                if (!_hasCurrent)
                {
                    if (!TryBeginCurrent(ref nodeRemaining))
                        return Status;
                    if (_stage != Stage.Search)
                        continue;
                }

                if (!_hasPendingIncoming)
                {
                    NavigationSurfaceEdgeAdvanceStatus edgeStatus =
                        _incoming.AdvanceOne(_meter, ref edgeRemaining);
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                    {
                        return _meter.RemainingEvaluatedEdges == 0
                            ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                            : Status;
                    }
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
                        continue;
                    if (edgeStatus == NavigationSurfaceEdgeAdvanceStatus.Complete)
                    {
                        _hasCurrent = false;
                        continue;
                    }
                    _pendingIncoming = _incoming.Current;
                    _hasPendingIncoming = true;
                }

                if (_explicitEdgeActive)
                {
                    if (connectionRemaining == 0)
                    {
                        return _meter.RemainingConnectionLegs == 0
                            ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                            : Status;
                    }
                    if (!_meter.TryConsumeConnectionLegs(1))
                        return Finish(NavigationFlowFieldStatus.BudgetExceeded);
                    connectionRemaining--;
                    TraversalExplicitEdgeStatus explicitStatus =
                        _evaluator.AdvanceExplicitEdge(
                            ref _explicitEdgeWork,
                            out TraversalEdgeEvidence explicitEvidence);
                    NavigationNodeRef dependencyNode = explicitEvidence.DependencyNode;
                    if (dependencyNode.IsValid
                        && dependencyNode != _pendingIncoming.ForwardEdge.Target
                        && !TryRecordDependencyNode(dependencyNode))
                    {
                        return Finish(NavigationFlowFieldStatus.CapacityExceeded);
                    }
                    if (explicitStatus == TraversalExplicitEdgeStatus.Pending)
                        continue;
                    _explicitEdgeActive = false;
                    TraversalEvaluationStatus explicitEvaluation = explicitStatus switch
                    {
                        TraversalExplicitEdgeStatus.Passable =>
                            TraversalEvaluationStatus.Passable,
                        TraversalExplicitEdgeStatus.CostOverflow =>
                            TraversalEvaluationStatus.CostOverflow,
                        TraversalExplicitEdgeStatus.Stale =>
                            TraversalEvaluationStatus.Stale,
                        _ => TraversalEvaluationStatus.Impassable
                    };
                    NavigationFlowFieldStatus explicitCompletion = ApplyIncoming(
                        explicitEvaluation,
                        explicitEvidence.Cost);
                    if (explicitCompletion != NavigationFlowFieldStatus.Pending)
                        return explicitCompletion;
                    continue;
                }

                if (!TryRecordPage(_pendingIncoming.Predecessor))
                    return Finish(NavigationFlowFieldStatus.CapacityExceeded);
                if (_pendingIncoming.ForwardEdge.Kind == NavigationGraphEdgeKind.Explicit)
                {
                    TraversalExplicitEdgeStatus explicitStatus =
                        _evaluator.BeginExplicitEdge(
                            _pendingIncoming.Predecessor,
                            _pendingIncoming.ForwardEdge,
                            out _explicitEdgeWork);
                    if (explicitStatus == TraversalExplicitEdgeStatus.Pending)
                    {
                        _explicitEdgeActive = true;
                        continue;
                    }
                    TraversalEvaluationStatus beginEvaluation = explicitStatus switch
                    {
                        TraversalExplicitEdgeStatus.CostOverflow =>
                            TraversalEvaluationStatus.CostOverflow,
                        TraversalExplicitEdgeStatus.Stale =>
                            TraversalEvaluationStatus.Stale,
                        _ => TraversalEvaluationStatus.Impassable
                    };
                    NavigationFlowFieldStatus beginCompletion = ApplyIncoming(
                        beginEvaluation,
                        Fixed64.Zero);
                    if (beginCompletion != NavigationFlowFieldStatus.Pending)
                        return beginCompletion;
                    continue;
                }
                TraversalEvaluationStatus evaluation = _evaluator.EvaluateEdge(
                    _pendingIncoming.Predecessor,
                    _pendingIncoming.ForwardEdge,
                    out TraversalEdgeEvidence evidence);
                NavigationFlowFieldStatus applied = ApplyIncoming(
                    evaluation,
                    evidence.Cost);
                if (applied != NavigationFlowFieldStatus.Pending)
                    return applied;
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
                        ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                        : Status;
                }
                _stage = Stage.CaptureDependencies;
                continue;
            }

            if (_stage == Stage.CaptureDependencies)
            {
                _dependencyStamp ??= new NavigationDependencyStampWork(
                    _graph!,
                    _query!.AreaPolicy,
                    _workspace.DependencyComponents,
                    _workspace.DependencyComponentCount,
                    _workspace.DependencyPages,
                    _workspace.DependencyPageCount);
                int lookupBefore = _meter.LookupProbes;
                bool complete = _dependencyStamp.Advance(_meter, lookupRemaining);
                lookupRemaining -= _meter.LookupProbes - lookupBefore;
                if (!complete)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                        : Status;
                }
                if (!_dependencyStamp.IsValid
                    || !_graph!.IsDependencyCurrent(_dependencyStamp.Result))
                {
                    return Finish(NavigationFlowFieldStatus.Stale);
                }
                _payloadNodes = new NavigationFlowFieldNode[_workspace.SettledCount];
                _payloadLookup = new int[_workspace.SettledCount];
                _stage = Stage.BuildPayload;
                continue;
            }

            if (_stage == Stage.BuildPayload)
            {
                while (_payloadWrite < _workspace.SettledCount && nodeRemaining > 0)
                {
                    int slot = _workspace.SettledSlots[_payloadWrite];
                    ref NavigationFlowFieldSearchNode record =
                        ref _workspace.GetRecord(slot);
                    _payloadNodes![_payloadWrite] = new NavigationFlowFieldNode(
                        record.Address,
                        record.IntegrationCost,
                        record.SelectedEdge);
                    _payloadLookup![_payloadWrite] = _payloadWrite;
                    _payloadWrite++;
                    nodeRemaining--;
                }
                if (_payloadWrite < _workspace.SettledCount)
                    return Status;
                _payloadSort = new NavigationFlowPayloadSortWork(
                    _payloadNodes!,
                    _payloadLookup!,
                    sortNodes: true);
                _stage = Stage.SortPayloadNodes;
                continue;
            }

            if (_stage == Stage.SortPayloadNodes)
            {
                int lookupBefore = _meter.LookupProbes;
                bool complete = _payloadSort.Advance(_meter, lookupRemaining);
                lookupRemaining -= _meter.LookupProbes - lookupBefore;
                if (!complete)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                        : Status;
                }
                _payloadSort = new NavigationFlowPayloadSortWork(
                    _payloadNodes!,
                    _payloadLookup!,
                    sortNodes: false);
                _stage = Stage.SortPayloadLookup;
                continue;
            }

            int finalLookupBefore = _meter.LookupProbes;
            bool lookupComplete = _payloadSort.Advance(_meter, lookupRemaining);
            lookupRemaining -= _meter.LookupProbes - finalLookupBefore;
            if (!lookupComplete)
            {
                return _meter.RemainingLookupProbes == 0
                    ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                    : Status;
            }
            NavigationResolvedPathQuery resolved = _query!;
            Result = new NavigationFlowFieldPayload(
                new NavigationFlowFieldPayloadKey(
                    resolved.Query,
                    resolved.End.Address),
                _payloadNodes!,
                _payloadLookup!,
                _dependencyStamp!.Result,
                _isComplete);
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
        _incoming = default;
        ClearPendingIncoming();
        _dependencySort = default;
        _dependencyStamp = null;
        _payloadNodes = null;
        _payloadLookup = null;
        _payloadSort = default;
    }

    private bool TryBeginCurrent(ref int nodeRemaining)
    {
        if (!_heap.TryPeek(out int nextSlot))
        {
            _isComplete = true;
            _resultStatus = _originSettled
                ? NavigationFlowFieldStatus.Success
                : NavigationFlowFieldStatus.NoPath;
            return TryBeginDependencySort();
        }
        if (_originSettled
            && _workspace.GetRecord(nextSlot).IntegrationCost > _coverageThreshold)
        {
            _isComplete = false;
            _resultStatus = NavigationFlowFieldStatus.Success;
            return TryBeginDependencySort();
        }
        if (nodeRemaining == 0)
        {
            if (_meter.RemainingExpandedNodes == 0)
                Finish(NavigationFlowFieldStatus.BudgetExceeded);
            return false;
        }
        if (!_meter.TryConsumeExpandedNodes(1))
        {
            Finish(NavigationFlowFieldStatus.BudgetExceeded);
            return false;
        }
        nodeRemaining--;
        _currentSlot = _heap.Pop();
        ref NavigationFlowFieldSearchNode current =
            ref _workspace.GetRecord(_currentSlot);
        current.Closed = true;
        _workspace.SettledSlots[_workspace.SettledCount++] = _currentSlot;
        if (_workspace.GetNode(_currentSlot) == _query!.Start.Node)
        {
            if (!Fixed64.TryAdd(
                    current.IntegrationCost,
                    _query.Query.FlowField.ExtraIntegrationCost,
                    out _coverageThreshold))
            {
                Finish(NavigationFlowFieldStatus.CostOverflow);
                return false;
            }
            _originSettled = true;
        }
        _incoming = _graph!.EnumerateIncomingStructuralSurfaceEdges(
            _workspace.GetNode(_currentSlot));
        _hasCurrent = true;
        return true;
    }

    private NavigationFlowFieldStatus ApplyIncoming(
        TraversalEvaluationStatus evaluation,
        Fixed64 edgeCost)
    {
        NavigationIncomingSurfaceEdge incoming = _pendingIncoming;
        ClearPendingIncoming();
        if (evaluation == TraversalEvaluationStatus.CostOverflow)
            return Finish(NavigationFlowFieldStatus.CostOverflow);
        if (evaluation == TraversalEvaluationStatus.Stale)
            return Finish(NavigationFlowFieldStatus.Stale);
        if (evaluation != TraversalEvaluationStatus.Passable)
            return Status;
        ref NavigationFlowFieldSearchNode current =
            ref _workspace.GetRecord(_currentSlot);
        if (!Fixed64.TryAdd(
                current.IntegrationCost,
                edgeCost,
                out Fixed64 candidate))
        {
            return Finish(NavigationFlowFieldStatus.CostOverflow);
        }
        if (!_workspace.TryGetOrAdd(
                incoming.Predecessor,
                out int predecessorSlot,
                out bool added))
        {
            return Finish(NavigationFlowFieldStatus.CapacityExceeded);
        }
        ref NavigationFlowFieldSearchNode predecessor =
            ref _workspace.GetRecord(predecessorSlot);
        if (added)
        {
            if (!_graph!.TryGetNodeAddress(
                    incoming.Predecessor,
                    out predecessor.Address))
            {
                return Finish(NavigationFlowFieldStatus.Stale);
            }
            predecessor.IntegrationCost = candidate;
            predecessor.SelectedEdge = incoming.SelectedEdge;
            predecessor.HasSelectedEdge = true;
            predecessor.HeapIndex = -1;
            _heap.Push(predecessorSlot);
            return Status;
        }
        if (candidate > predecessor.IntegrationCost)
            return Status;
        if (candidate == predecessor.IntegrationCost)
        {
            if (!predecessor.HasSelectedEdge
                || CompareSelectedEdge(
                    incoming.SelectedEdge,
                    predecessor.SelectedEdge) < 0)
            {
                predecessor.SelectedEdge = incoming.SelectedEdge;
                predecessor.HasSelectedEdge = true;
            }
            return Status;
        }
        if (predecessor.Closed)
            return Finish(NavigationFlowFieldStatus.Stale);
        predecessor.IntegrationCost = candidate;
        predecessor.SelectedEdge = incoming.SelectedEdge;
        predecessor.HasSelectedEdge = true;
        _heap.DecreaseKey(predecessorSlot);
        return Status;
    }

    private bool TryRecordPage(NavigationNodeRef node)
    {
        return _graph!.TryGetNodeAddress(node, out NavigationCellAddress address)
            && _workspace.TryRecordPage(
                address.MapId,
                node.CellSlot / NavigationSemanticPage.SlotCount);
    }

    private bool TryRecordDependencyNode(NavigationNodeRef node)
    {
        if (!_graph!.TryGetNodeAddress(node, out NavigationCellAddress address)
            || !_workspace.TryRecordPage(
                address.MapId,
                node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return false;
        }
        return _graph.TryGetSurfaceComponent(
                address,
                _query!.Medium,
                out NavigationSurfaceComponentKey component,
                out _)
            && _workspace.TryRecordComponent(component);
    }

    private void ClearPendingIncoming()
    {
        _pendingIncoming = default;
        _hasPendingIncoming = false;
        _explicitEdgeActive = false;
        _explicitEdgeWork = default;
    }

    private NavigationFlowFieldStatus Finish(NavigationFlowFieldStatus status)
    {
        Status = status;
        _query?.ReleaseLease();
        ReleaseRuntimeState();
        return Status;
    }

    private bool TryBeginDependencySort()
    {
        long maximumRetainedBytes =
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                _workspace.SettledCount,
                _workspace.DependencyComponentCount,
                _workspace.DependencyPageCount);
        if (maximumRetainedBytes > _maximumPayloadBytes)
        {
            Finish(NavigationFlowFieldStatus.CapacityExceeded);
            return false;
        }
        _dependencySort = new NavigationDependencySortWork(
            _workspace.DependencyComponents,
            _workspace.DependencyComponentCount,
            _workspace.DependencyPages,
            _workspace.DependencyPageCount);
        _stage = Stage.SortDependencies;
        return true;
    }

    private static int CompareSelectedEdge(
        NavigationSelectedEdgeRef left,
        NavigationSelectedEdgeRef right)
    {
        int comparison = left.CanonicalOutgoingOrdinal.CompareTo(
            right.CanonicalOutgoingOrdinal);
        return comparison != 0
            ? comparison
            : left.Target.CompareTo(right.Target);
    }
}

/// <summary>Canonically heap-sorts flow nodes or lookup ordinals with bounded comparisons.</summary>
internal struct NavigationFlowPayloadSortWork
{
    private enum SiftStage : byte
    {
        None = 0,
        ChooseChild = 1,
        CompareRoot = 2
    }

    private readonly NavigationFlowFieldNode[] _nodes;
    private readonly int[] _lookup;
    private readonly bool _sortNodes;
    private SiftStage _siftStage;
    private int _heapSize;
    private int _buildIndex;
    private int _sortEnd;
    private int _siftRoot;
    private int _siftCandidate;
    private bool _building;

    internal NavigationFlowPayloadSortWork(
        NavigationFlowFieldNode[] nodes,
        int[] lookup,
        bool sortNodes)
    {
        _nodes = nodes;
        _lookup = lookup;
        _sortNodes = sortNodes;
        _siftStage = SiftStage.None;
        _heapSize = nodes.Length;
        _buildIndex = (nodes.Length / 2) - 1;
        _sortEnd = nodes.Length - 1;
        _siftRoot = 0;
        _siftCandidate = 0;
        _building = true;
    }

    internal bool Advance(NavigationWorkMeter meter, int lookupStepLimit)
    {
        int remaining = Math.Min(lookupStepLimit, meter.RemainingLookupProbes);
        while (true)
        {
            if (_siftStage == SiftStage.None)
            {
                if (_building)
                {
                    if (_buildIndex < 0)
                    {
                        _building = false;
                        continue;
                    }
                    BeginSift(_buildIndex--);
                    continue;
                }
                if (_sortEnd <= 0)
                    return true;
                Swap(0, _sortEnd);
                _heapSize = _sortEnd--;
                BeginSift(0);
                continue;
            }
            if (_siftStage == SiftStage.ChooseChild)
            {
                int left = checked((_siftRoot * 2) + 1);
                if (left >= _heapSize)
                {
                    _siftStage = SiftStage.None;
                    continue;
                }
                _siftCandidate = left;
                int right = left + 1;
                if (right < _heapSize)
                {
                    if (!TryConsumeComparison(meter, ref remaining))
                        return false;
                    if (Compare(left, right) < 0)
                        _siftCandidate = right;
                }
                _siftStage = SiftStage.CompareRoot;
                continue;
            }
            if (!TryConsumeComparison(meter, ref remaining))
                return false;
            if (Compare(_siftRoot, _siftCandidate) >= 0)
            {
                _siftStage = SiftStage.None;
                continue;
            }
            Swap(_siftRoot, _siftCandidate);
            _siftRoot = _siftCandidate;
            _siftStage = SiftStage.ChooseChild;
        }
    }

    private void BeginSift(int root)
    {
        _siftRoot = root;
        _siftStage = SiftStage.ChooseChild;
    }

    private int Compare(int left, int right)
    {
        if (!_sortNodes)
        {
            return _nodes[_lookup[left]].Address.CompareTo(
                _nodes[_lookup[right]].Address);
        }
        NavigationFlowFieldNode leftNode = _nodes[left];
        NavigationFlowFieldNode rightNode = _nodes[right];
        int comparison = leftNode.IntegrationCost.CompareTo(
            rightNode.IntegrationCost);
        return comparison != 0
            ? comparison
            : leftNode.Address.CompareTo(rightNode.Address);
    }

    private void Swap(int left, int right)
    {
        if (_sortNodes)
        {
            (_nodes[left], _nodes[right]) = (_nodes[right], _nodes[left]);
            return;
        }
        (_lookup[left], _lookup[right]) = (_lookup[right], _lookup[left]);
    }

    private static bool TryConsumeComparison(
        NavigationWorkMeter meter,
        ref int remaining)
    {
        if (remaining == 0 || !meter.TryConsumeLookupProbes(1))
            return false;
        remaining--;
        return true;
    }
}
