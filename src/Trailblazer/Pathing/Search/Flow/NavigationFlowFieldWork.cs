//=======================================================================
// NavigationFlowFieldWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Builds one bounded weighted destination-centric flow-field prefix.</summary>
internal sealed class NavigationFlowFieldWork : IDisposable
{
    private enum Stage : byte
    {
        Search = 0,
        CountTransitions = 1,
        ReplayTransitions = 2,
        SortDependencies = 3,
        CaptureDependencies = 4,
        BuildPayload = 5,
        SortPayloadNodes = 6,
        SortPayloadLookup = 7,
        ResetSearch = 8
    }

    private NavigationResolvedPathQuery? _query;
    private GridWorld? _world;
    private NavigationWorldGraph? _graph;
    private readonly NavigationFlowFieldWorkspace _workspace;
    private readonly NavigationWorkMeter _meter;
    private readonly long _maximumPayloadBytes;
    private NavigationFlowFieldOpenHeap _heap;
    private NavigationIncomingTraversalEdgeEnumerator _incoming;
    private NavigationTraversalEdgeEnumerator _replayEdges;
    private NavigationDependencySortWork _dependencySort;
    private NavigationFlowPayloadSortWork _payloadSort;
    private NavigationDependencyStampWork? _dependencyStamp;
    private NavigationFlowFieldNode[]? _payloadNodes;
    private NavigationTransitionInstruction[]? _payloadTransitionInstructions;
    private int[]? _payloadLookup;
    private NavigationFlowFieldStatus _resultStatus;
    private Stage _stage;
    private int _currentSlot;
    private int _postNodeOrdinal;
    private int _transitionOrdinal;
    private readonly ulong _worldChangeSequence;
    private bool _hasCurrent;
    private bool _replayActive;
    private bool _originSettled;
    private bool _isComplete;
    private bool _requiresWorldStamp;
    private Fixed64 _coverageThreshold;

    internal NavigationFlowFieldWork(
        GridWorld world,
        NavigationResolvedPathQuery query,
        NavigationFlowFieldWorkspace workspace,
        long maximumPayloadBytes = long.MaxValue)
    {
        ChronicleGuard.ThrowIfNull(world, nameof(world));
        ChronicleGuard.ThrowIfNull(query, nameof(query));
        ChronicleGuard.ThrowIfNull(workspace, nameof(workspace));

        _query = query;
        _world = world;
        _graph = query.Graph;
        _workspace = workspace;
        _stage = Stage.ResetSearch;
        _meter = query.Meter;
        _maximumPayloadBytes = maximumPayloadBytes;
        _worldChangeSequence = query.WorldChangeSequence;
        _requiresWorldStamp = query.RequiresWorldStamp
            || query.StartMedium == TraversalMedium.Gas
            || query.StartMedium == TraversalMedium.Liquid;
        _heap = new NavigationFlowFieldOpenHeap(workspace);
        _resultStatus = NavigationFlowFieldStatus.Success;

        if (query.Planar == null)
        {
            if (!TryRecordPage(query.Start.Node)
                || !TryRecordPage(query.End.Node))
            {
                Finish(NavigationFlowFieldStatus.CapacityExceeded);
                return;
            }
            if (!query.Query.AllowTransitions
                && !_graph.AreInSameSurfaceComponent(
                    query.Start.Address,
                    query.StartMedium,
                    query.End.Address,
                    query.StartMedium))
            {
                Finish(NavigationFlowFieldStatus.NoPath);
                return;
            }
        }

        int nodeRemaining = 0;
        if (_workspace.AdvanceResetSearch(ref nodeRemaining))
            TryBeginSearch();
    }

    internal NavigationFlowFieldStatus Status { get; private set; }

    internal NavigationFlowFieldPayload? Result { get; private set; }

    internal NavigationFlowFieldStatus Advance(
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit)
    {
        var remaining = new NavigationAcquisitionSlice(lookupStepLimit, 0, nodeStepLimit, edgeStepLimit, connectionStepLimit);
        return Advance(ref remaining);
    }

    internal NavigationFlowFieldStatus Advance(ref NavigationAcquisitionSlice remaining)
    {
        if (Status != NavigationFlowFieldStatus.Pending)
            return Status;
        ref int lookupRemaining = ref remaining.LookupRemaining;
        ref int nodeRemaining = ref remaining.NodeRemaining;
        ref int edgeRemaining = ref remaining.EdgeRemaining;
        ref int connectionRemaining = ref remaining.ConnectionRemaining;

        while (true)
        {
            if (!IsWorldCurrent())
                return Finish(NavigationFlowFieldStatus.Stale);
            if (_stage == Stage.ResetSearch)
            {
                if (!_workspace.AdvanceResetSearch(ref nodeRemaining)
                    || !TryBeginSearch())
                {
                    return Status;
                }
            }
            if (_stage == Stage.Search)
            {
                if (!_hasCurrent)
                {
                    if (!TryBeginCurrent(ref nodeRemaining))
                        return Status;
                    if (_stage != Stage.Search)
                        continue;
                }
                NavigationPlanarSearch? planar = _query!.Planar;
                NavigationMediumStateRef predecessor = default;
                Fixed64 cost = default;
                int forwardOrdinal = default;
                NavigationTraversalEdgeKind kind = default;
                NavigationTraversalEdgeAdvanceStatus edgeStatus;
                if (planar == null)
                {
                    edgeStatus = _incoming.AdvanceOne(
                        _meter,
                        _workspace.EndpointWorkspace.Dependencies,
                        ref edgeRemaining,
                        ref connectionRemaining,
                        ref lookupRemaining);
                    CaptureWorldDependency(_incoming.RequiresWorldStamp);
                }
                else
                {
                    edgeStatus = planar.AdvanceEdge(ref remaining, out predecessor, out cost, out forwardOrdinal, out kind);
                }
                edgeStatus = ResolvePostEnumerationStatus(
                    IsWorldCurrent(),
                    edgeStatus);
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Pending)
                    continue;
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Complete)
                {
                    _hasCurrent = false;
                    continue;
                }
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Blocked)
                    return Status;
                if (edgeStatus != NavigationTraversalEdgeAdvanceStatus.Edge)
                    return Finish(NavigationGuideStatusMapper.ToFlowField(edgeStatus));
                NavigationFlowFieldStatus applied = planar == null
                    ? ApplyIncoming(_incoming.CurrentPredecessor, _incoming.CurrentCost,
                        _incoming.CurrentOrdinal, _incoming.CurrentKind)
                    : ApplyIncoming(predecessor, cost, forwardOrdinal, kind);
                if (applied != NavigationFlowFieldStatus.Pending)
                    return applied;
                continue;
            }

            if (_stage == Stage.CountTransitions)
            {
                while (_postNodeOrdinal < _workspace.SettledCount
                    && nodeRemaining > 0)
                {
                    int slot = _workspace.SettledSlots[_postNodeOrdinal++];
                    if (_workspace.GetRecord(slot).SelectedIsTransition)
                        _transitionOrdinal++;
                    nodeRemaining--;
                }
                if (_postNodeOrdinal < _workspace.SettledCount)
                    return Status;
                _payloadTransitionInstructions = _transitionOrdinal == 0
                    ? Array.Empty<NavigationTransitionInstruction>()
                    : new NavigationTransitionInstruction[_transitionOrdinal];
                _postNodeOrdinal = 0;
                _transitionOrdinal = 0;
                _stage = Stage.ReplayTransitions;
                continue;
            }

            if (_stage == Stage.ReplayTransitions)
            {
                NavigationFlowFieldStatus replay = AdvanceTransitionReplay(
                    ref nodeRemaining,
                    ref edgeRemaining,
                    ref connectionRemaining,
                    ref lookupRemaining);
                if (replay != NavigationFlowFieldStatus.Pending)
                    return replay;
                if (_stage == Stage.ReplayTransitions)
                    return Status;
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
                    _workspace.DependencyPageCount,
                    _workspace.EndpointWorkspace.Dependencies
                        .HasTransitionDependency);
                int lookupBefore = _meter.LookupProbes;
                bool complete = _dependencyStamp.Advance(_meter, lookupRemaining);
                lookupRemaining -= _meter.LookupProbes - lookupBefore;
                if (!complete)
                {
                    return _meter.RemainingLookupProbes == 0
                        ? Finish(NavigationFlowFieldStatus.BudgetExceeded)
                        : Status;
                }
                _payloadNodes = new NavigationFlowFieldNode[_workspace.SettledCount];
                _payloadLookup = new int[_workspace.SettledCount];
                _stage = Stage.BuildPayload;
                continue;
            }

            if (_stage == Stage.BuildPayload)
            {
                while (_postNodeOrdinal < _workspace.SettledCount && nodeRemaining > 0)
                {
                    int slot = _workspace.SettledSlots[_postNodeOrdinal];
                    ref NavigationFlowFieldSearchNode record =
                        ref _workspace.GetRecord(slot);
                    _payloadNodes![_postNodeOrdinal] = new NavigationFlowFieldNode(
                        record.Address,
                        _workspace.GetNode(slot).Medium,
                        record.IntegrationCost,
                        record.SelectedEdge,
                        record.SelectedIsTransition
                            ? _transitionOrdinal++
                            : -1);
                    _payloadLookup![_postNodeOrdinal] = _postNodeOrdinal;
                    _postNodeOrdinal++;
                    nodeRemaining--;
                }
                if (_postNodeOrdinal < _workspace.SettledCount)
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
                    resolved.End.Address,
                    resolved.StartMedium,
                    resolved.TargetMedia),
                _payloadNodes!,
                _payloadLookup!,
                _payloadTransitionInstructions!,
                _dependencyStamp!.Result,
                _isComplete,
                _requiresWorldStamp ? _worldChangeSequence : null);
            NavigationFlowFieldPayload? result = Result;
            NavigationFlowFieldStatus resultStatus = ResolveFinalPublication(
                IsWorldCurrent(),
                _resultStatus,
                ref result);
            Result = result;
            return Finish(resultStatus);
        }
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
        _world = null;
        _incoming = default;
        _replayEdges = default;
        _dependencySort = default;
        _dependencyStamp = null;
        _payloadNodes = null;
        _payloadTransitionInstructions = null;
        _payloadLookup = null;
        _payloadSort = default;
    }

    private bool TryBeginCurrent(ref int nodeRemaining)
    {
        NavigationResolvedPathQuery query = _query!;
        NavigationPlanarSearch? planar = query.Planar;
        if (!_heap.TryPeek(out int nextSlot))
        {
            if (planar != null)
            {
                Finish(NavigationFlowFieldStatus.NoPath);
                return false;
            }
            _isComplete = true;
            _resultStatus = _originSettled
                ? NavigationFlowFieldStatus.Success
                : NavigationFlowFieldStatus.NoPath;
            return TryBeginTransitionCount();
        }
        if (_originSettled
            && _workspace.GetRecord(nextSlot).IntegrationCost > _coverageThreshold)
        {
            _isComplete = false;
            _resultStatus = NavigationFlowFieldStatus.Success;
            return TryBeginTransitionCount();
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
        NavigationMediumStateRef currentState = _workspace.GetNode(_currentSlot);
        if (currentState.Equals(new NavigationMediumStateRef(
                query.Start.Node,
                query.StartMedium)))
        {
            if (planar != null)
            {
                // Positive leg costs ensure every selected successor has already settled.
                Finish(NavigationFlowFieldStatus.Success);
                return false;
            }
            if (!Fixed64.TryAdd(
                    current.IntegrationCost,
                    query.Query.FlowField.ExtraIntegrationCost,
                    out _coverageThreshold))
            {
                Finish(NavigationFlowFieldStatus.CostOverflow);
                return false;
            }
            _originSettled = true;
        }
        if (planar == null)
        {
            _incoming = new NavigationIncomingTraversalEdgeEnumerator(
                _world!,
                _graph!,
                currentState,
                query.Query.Agent,
                query.AreaPolicy,
                _workspace.RayWorkspace,
                query.Query.AllowTransitions);
        }
        else
        {
            planar.BeginEdges(currentState, incoming: true);
        }
        _hasCurrent = true;
        return true;
    }

    private NavigationFlowFieldStatus ApplyIncoming(NavigationMediumStateRef predecessorState,
        Fixed64 edgeCost, int forwardOrdinal, NavigationTraversalEdgeKind edgeKind)
    {
        if (_query!.Planar == null && !TryRecordStateDependencies(predecessorState))
            return Finish(NavigationFlowFieldStatus.CapacityExceeded);
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
                predecessorState,
                out int predecessorSlot,
                out bool added))
        {
            return Finish(NavigationFlowFieldStatus.CapacityExceeded);
        }
        ref NavigationFlowFieldSearchNode predecessor =
            ref _workspace.GetRecord(predecessorSlot);
        if (added)
        {
            _graph!.TryGetNodeAddress(predecessorState.Node, out predecessor.Address);
            predecessor.IntegrationCost = candidate;
            SetSelectedEdge(ref predecessor, current.Address, _workspace.GetNode(_currentSlot),
                forwardOrdinal, edgeKind);
            predecessor.HeapIndex = -1;
            _heap.Push(predecessorSlot);
            return Status;
        }
        if (predecessor.Closed)
            return Status;
        if (candidate > predecessor.IntegrationCost)
            return Status;
        if (candidate == predecessor.IntegrationCost)
        {
            if (predecessor.SelectedEdge.IsValid
                && CompareSelectedEdge(
                    CreateSelectedEdge(current.Address, _workspace.GetNode(_currentSlot), forwardOrdinal),
                    predecessor.SelectedEdge) < 0)
            {
                SetSelectedEdge(ref predecessor, current.Address, _workspace.GetNode(_currentSlot),
                    forwardOrdinal, edgeKind);
            }
            return Status;
        }
        predecessor.IntegrationCost = candidate;
        SetSelectedEdge(ref predecessor, current.Address, _workspace.GetNode(_currentSlot),
            forwardOrdinal, edgeKind);
        _heap.DecreaseKey(predecessorSlot);
        return Status;
    }

    private NavigationFlowFieldStatus AdvanceTransitionReplay(
        ref int nodeRemaining,
        ref int edgeRemaining,
        ref int connectionRemaining,
        ref int lookupRemaining)
    {
        while (_postNodeOrdinal < _workspace.SettledCount)
        {
            int slot = _workspace.SettledSlots[_postNodeOrdinal];
            ref NavigationFlowFieldSearchNode record = ref _workspace.GetRecord(slot);
            if (!record.SelectedIsTransition)
            {
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                _postNodeOrdinal++;
                continue;
            }
            NavigationMediumStateRef source = _workspace.GetNode(slot);
            if (!_replayActive)
            {
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                _replayEdges = new NavigationTraversalEdgeEnumerator(
                    _world!,
                    _graph!,
                    source,
                    _query!.Query.Agent,
                    _query.AreaPolicy,
                    _workspace.RayWorkspace,
                    allowTransitions: true,
                    emittedSurfaceOrdinal: -1);
                _replayActive = true;
            }
            NavigationTraversalEdgeAdvanceStatus status = _replayEdges.AdvanceOne(
                _meter,
                _workspace.EndpointWorkspace.Dependencies,
                ref edgeRemaining,
                ref connectionRemaining,
                ref lookupRemaining);
            CaptureWorldDependency(_replayEdges.RequiresWorldStamp);
            status = ResolvePostEnumerationStatus(IsWorldCurrent(), status);
            if (status == NavigationTraversalEdgeAdvanceStatus.Pending)
                continue;
            if (status == NavigationTraversalEdgeAdvanceStatus.Blocked)
                return Status;
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
                return Finish(NavigationGuideStatusMapper.ToFlowField(status));
            int selectedOrdinal = record.SelectedEdge.CanonicalOutgoingOrdinal;
            if (_replayEdges.CurrentOrdinal < selectedOrdinal)
                continue;
            NavigationMediumStateRef target = _replayEdges.CurrentTarget;
            _payloadTransitionInstructions![_transitionOrdinal++] =
                new NavigationTransitionInstruction(
                    _replayEdges.CurrentTransitionIdentityKind,
                    _replayEdges.CurrentTransitionOwnerMapId,
                    _replayEdges.CurrentTransitionId,
                    _replayEdges.CurrentTransitionType,
                    record.Address,
                    record.SelectedEdge.Target,
                    source.Medium,
                    target.Medium,
                    _replayEdges.CurrentTransitionSourceAction,
                    _replayEdges.CurrentTransitionDestinationAction,
                    _replayEdges.CurrentTransitionHints);
            _replayEdges = default;
            _replayActive = false;
            _postNodeOrdinal++;
        }
        _postNodeOrdinal = 0;
        _transitionOrdinal = 0;
        TryBeginDependencySort();
        return Status;
    }

    internal static NavigationTraversalEdgeAdvanceStatus ResolvePostEnumerationStatus(
        bool worldCurrent,
        NavigationTraversalEdgeAdvanceStatus status) => worldCurrent
        ? status
        : NavigationTraversalEdgeAdvanceStatus.Stale;

    internal static NavigationFlowFieldStatus ResolveFinalPublication(
        bool worldCurrent,
        NavigationFlowFieldStatus status,
        ref NavigationFlowFieldPayload? payload)
    {
        if (worldCurrent)
            return status;
        payload = null;
        return NavigationFlowFieldStatus.Stale;
    }

    private bool TryRecordPage(NavigationNodeRef node)
    {
        _graph!.TryGetNodeAddress(node, out NavigationCellAddress address);
        return _workspace.TryRecordPage(
            address.MapId,
            node.CellSlot / NavigationSemanticPage.SlotCount);
    }

    private bool TryRecordStateDependencies(NavigationMediumStateRef state)
    {
        _graph!.TryGetNodeAddress(state.Node, out NavigationCellAddress address);
        if (!_workspace.TryRecordPage(
                address.MapId,
                state.Node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return false;
        }
        _graph.TryGetSurfaceComponent(
            address,
            state.Medium,
            out NavigationSurfaceComponentKey component,
            out _);
        return _workspace.TryRecordComponent(component);
    }

    private NavigationFlowFieldStatus Finish(NavigationFlowFieldStatus status)
    {
        Status = status;
        _query!.ReleaseLease();
        ReleaseRuntimeState();
        return Status;
    }

    private bool TryBeginTransitionCount()
    {
        _stage = Stage.CountTransitions;
        return true;
    }

    private bool TryBeginDependencySort()
    {
        long maximumRetainedBytes =
            NavigationFlowFieldPayload.GetMaximumRetainedBytes(
                _workspace.SettledCount,
                _payloadTransitionInstructions!.Length,
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
        NavigationSelectedEdgeRef right) =>
        left.CanonicalOutgoingOrdinal.CompareTo(right.CanonicalOutgoingOrdinal);

    private static NavigationSelectedEdgeRef CreateSelectedEdge(
        NavigationCellAddress target,
        NavigationMediumStateRef targetState,
        int forwardOrdinal) => new(
        target,
        targetState.Medium,
        forwardOrdinal);

    private static void SetSelectedEdge(
        ref NavigationFlowFieldSearchNode record,
        NavigationCellAddress target,
        NavigationMediumStateRef currentState,
        int forwardOrdinal,
        NavigationTraversalEdgeKind edgeKind)
    {
        record.SelectedEdge = CreateSelectedEdge(target, currentState, forwardOrdinal);
        record.SelectedIsTransition = edgeKind == NavigationTraversalEdgeKind.Transition;
    }

    private bool TryBeginSearch()
    {
        if (!TrySeedTargetMedium(TraversalMedium.Solid)
            || !TrySeedTargetMedium(TraversalMedium.Gas)
            || !TrySeedTargetMedium(TraversalMedium.Liquid))
        {
            Finish(NavigationFlowFieldStatus.CapacityExceeded);
            return false;
        }
        _stage = Stage.Search;
        return true;
    }

    private bool TrySeedTargetMedium(TraversalMedium medium)
    {
        if (medium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            if ((_query!.TargetMedia & NavigationCell.ToMedia(medium)) != 0)
                _requiresWorldStamp = true;
        }
        if ((_query!.End.Media & NavigationCell.ToMedia(medium)) == 0)
            return true;
        if (_query.Planar == null)
        {
            _graph!.TryGetSurfaceComponent(
                _query.End.Address,
                medium,
                out NavigationSurfaceComponentKey component,
                out _);
            if (!_workspace.TryRecordComponent(component))
                return false;
        }
        var state = new NavigationMediumStateRef(_query.End.Node, medium);
        if (!_workspace.TryGetOrAdd(state, out int slot, out _))
        {
            return false;
        }
        ref NavigationFlowFieldSearchNode destination =
            ref _workspace.GetRecord(slot);
        destination.Address = _query.End.Address;
        destination.IntegrationCost = Fixed64.Zero;
        destination.HeapIndex = -1;
        _heap.Push(slot);
        return true;
    }

    private void CaptureWorldDependency(bool requiresWorldStamp)
    {
        if (requiresWorldStamp)
            _requiresWorldStamp = true;
    }

    private bool IsWorldCurrent() =>
        !_requiresWorldStamp
        || _world!.ChangeSequence == _worldChangeSequence;

}
