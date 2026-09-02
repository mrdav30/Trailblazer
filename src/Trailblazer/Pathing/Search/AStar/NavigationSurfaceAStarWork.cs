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
    private readonly NavigationAStarWorkspace _workspace;
    private readonly NavigationWorkMeter _meter;
    private readonly long _maximumPayloadBytes;
    private readonly Vector3d _targetFootAnchor;
    private readonly bool _useEuclideanHeuristic;
    private NavigationTraversalEdgeEnumerator _edges;
    private NavigationMediumStateRef _current;
    private NavigationMediumStateRef _pathCursor;
    private NavigationMediumStateRef _goal;
    private NavigationDependencySortWork _dependencySort;
    private NavigationDependencyStampWork? _dependencyStamp;
    private NavigationAStarGuidePoint[]? _payloadGuidePoints;
    private NavigationTransitionInstruction[]? _payloadTransitionInstructions;
    private NavigationSurfaceAStarStatus _resultStatus;
    private Stage _stage;
    private int _pathWrite;
    private int _reverseLeft;
    private int _reverseRight;
    private int _pathEdgeOrdinal;
    private int _payloadWrite;
    private int _transitionPayloadWrite;
    private int _simplificationSourcePathOrdinal;
    private int _simplificationCandidatePathOrdinal;
    private int _simplificationWriteOrdinal;
    private int _transitionBarrierScanOrdinal;
    private int _nextTransitionPathOrdinal;
    private int _rawCopyOrdinal;
    private int _rawCopyEndOrdinal;
    private int _finalizationLookupReservation;
    private ulong _simplificationWorldChangeSequence;
    private bool _requiresWorldStamp;
    private bool _hasCurrent;
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
        _simplificationWorldChangeSequence = query.WorldChangeSequence;
        _requiresWorldStamp = query.RequiresWorldStamp
            || query.StartMedium == TraversalMedium.Gas
            || query.StartMedium == TraversalMedium.Liquid;
        _resultStatus = NavigationSurfaceAStarStatus.Success;
        _useEuclideanHeuristic = !query.Query.AllowTransitions
            && _graph.SurfaceComponents.TryGet(
                query.Start.Address,
                query.StartMedium,
                out NavigationSurfaceComponent startComponent)
            && startComponent.AllSurfaceEdgesEuclideanCertified;
        _targetFootAnchor = query.End.FootAnchor;
        if (!query.Query.AllowTransitions
            && !_graph.AreInSameSurfaceComponent(
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
        var startStateRef = new NavigationMediumStateRef(
            query.Start.Node,
            query.StartMedium);
        if (!_workspace.NodeTable.TryGetOrAdd(
                startStateRef,
                out int startSlot,
                out _))
        {
            Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
            return;
        }
        ref NavigationAStarNodeRecord start = ref _workspace.NodeTable.GetRecord(startSlot);
        start.Cost = Fixed64.Zero;
        start.Heuristic = GetHeuristic(startStateRef);
        start.EstimatedTotalCost = start.Heuristic;
        Push(startStateRef, startSlot);
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
        if (!IsWorldCurrent())
            return Finish(NavigationSurfaceAStarStatus.Stale);
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
                    if (IsTarget(_current))
                    {
                        _goal = _current;
                        _pathCursor = _current;
                        _stage = Stage.Reconstruct;
                        continue;
                    }
                    _edges = EnumerateEdges(_current, emittedSurfaceOrdinal: -1);
                    _hasCurrent = true;
                }

                NavigationTraversalEdgeAdvanceStatus edgeStatus = _edges.AdvanceOne(
                    _meter,
                    _workspace.EndpointWorkspace.Dependencies,
                    ref edgeRemaining,
                    ref connectionRemaining);
                CaptureWorldDependency(_edges);
                edgeStatus = NavigationSearchFinalizationRules.ResolveTraversalEpochStatus(
                    edgeStatus,
                    IsWorldCurrent());
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Blocked)
                {
                    return ApplyTerminalStatus(
                        NavigationSearchFinalizationRules.ResolveBlockedTraversalStatus(
                            _edges.RequiresConnectionProgress,
                            _meter.RemainingConnectionLegs,
                            _meter.RemainingEvaluatedEdges));
                }
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Pending)
                    continue;
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Complete)
                {
                    _edges = default;
                    _hasCurrent = false;
                    continue;
                }
                if (NavigationSearchFinalizationRules.TryResolveTraversalTerminalStatus(
                        edgeStatus,
                        out NavigationSurfaceAStarStatus terminalStatus))
                {
                    return Finish(terminalStatus);
                }
                if (edgeStatus != NavigationTraversalEdgeAdvanceStatus.Edge)
                    continue;
                NavigationMediumStateRef target = _edges.CurrentTarget;
                if (_workspace.NodeTable.TryGetSlot(target, out int existingSlot)
                    && _workspace.NodeTable.GetRecord(existingSlot).Closed)
                {
                    continue;
                }
                NavigationSurfaceAStarStatus completion = ApplyRoute(
                    target,
                    _edges.CurrentCost,
                    _edges.CurrentOrdinal,
                    _edges.CurrentKind);
                if (completion != NavigationSurfaceAStarStatus.Pending)
                    return completion;
                continue;
            }

            if (_stage == Stage.Reconstruct)
            {
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                _workspace.PathNodes[_pathWrite++] = _pathCursor;
                if (_pathCursor.Equals(new NavigationMediumStateRef(
                        _query!.Start.Node,
                        _query.StartMedium)))
                {
                    _workspace.PathNodeCount = _pathWrite;
                    _reverseLeft = 0;
                    _reverseRight = _pathWrite - 1;
                    _stage = Stage.ReversePath;
                    continue;
                }
                _workspace.NodeTable.TryGetSlot(_pathCursor, out int pathSlot);
                NavigationAStarNodeRecord pathRecord =
                    _workspace.NodeTable.GetRecord(pathSlot);
                if (pathRecord.ParentEdgeKind == NavigationTraversalEdgeKind.Transition)
                    _transitionPayloadWrite = checked(_transitionPayloadWrite + 1);
                _pathCursor = pathRecord.Parent;
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
                if (nodeRemaining == 0)
                    return Status;
                nodeRemaining--;
                _payloadTransitionInstructions = _transitionPayloadWrite == 0
                    ? Array.Empty<NavigationTransitionInstruction>()
                    : new NavigationTransitionInstruction[_transitionPayloadWrite];
                _transitionPayloadWrite = 0;
                NavigationMediumStateRef startNode = _workspace.PathNodes[0];
                _graph!.TryGetNodeAddress(
                    startNode.Node,
                    out NavigationCellAddress startAddress);
                _graph.TryGetNodeState(
                    startNode.Node,
                    startNode.Medium,
                    out NavigationNodeState startState);
                if (!AppendGuidePoint(
                        new NavigationAStarGuidePoint(
                            startAddress,
                            GetGuideAnchor(startState, startNode.Medium),
                            startNode.Medium),
                        pathNodeOrdinal: 0))
                {
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                }
                _pathEdgeOrdinal = 0;
                BeginSelectedEdgeReplay(_pathEdgeOrdinal);
                _stage = Stage.ExpandGuide;
                continue;
            }

            if (_stage == Stage.ExpandGuide)
            {
                if (_pathEdgeOrdinal + 1 >= _workspace.PathNodeCount)
                {
                    _edges = default;
                    BeginSimplification();
                    continue;
                }
                NavigationMediumStateRef sourceNode =
                    _workspace.PathNodes[_pathEdgeOrdinal];
                NavigationMediumStateRef targetNode =
                    _workspace.PathNodes[_pathEdgeOrdinal + 1];
                _workspace.NodeTable.TryGetSlot(targetNode, out int targetSlot);
                NavigationAStarNodeRecord targetRecord = _workspace.NodeTable
                    .GetRecord(targetSlot);
                int parentEdgeOrdinal = targetRecord.ParentEdgeOrdinal;
                NavigationTraversalEdgeAdvanceStatus edgeStatus = _edges.AdvanceOne(
                    _meter,
                    _workspace.EndpointWorkspace.Dependencies,
                    ref edgeRemaining,
                    ref connectionRemaining);
                CaptureWorldDependency(_edges);
                edgeStatus = NavigationSearchFinalizationRules.ResolveTraversalEpochStatus(
                    edgeStatus,
                    IsWorldCurrent());
                if (NavigationSearchFinalizationRules.ShouldConsumeTraversalSurfacePoint(
                        _edges.HasCurrentSurfacePoint,
                        edgeStatus))
                {
                    if (nodeRemaining == 0)
                        return Status;
                    nodeRemaining--;
                    bool isTarget = _edges.CurrentSurfacePointIsTargetFootAnchor;
                    if (!AppendGuidePoint(
                            _edges.CurrentSurfacePoint,
                            isTarget ? _pathEdgeOrdinal + 1 : -1))
                    {
                        return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                    }
                    _edges.ConsumeCurrentSurfacePoint();
                    continue;
                }
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Blocked)
                {
                    return ApplyTerminalStatus(
                        NavigationSearchFinalizationRules.ResolveBlockedTraversalStatus(
                            _edges.RequiresConnectionProgress,
                            _meter.RemainingConnectionLegs,
                            _meter.RemainingEvaluatedEdges));
                }
                if (edgeStatus == NavigationTraversalEdgeAdvanceStatus.Pending)
                    continue;
                if (NavigationSearchFinalizationRules.TryResolveTraversalTerminalStatus(
                        edgeStatus,
                        out NavigationSurfaceAStarStatus terminalStatus))
                {
                    return Finish(terminalStatus);
                }
                System.Diagnostics.Debug.Assert(
                    edgeStatus == NavigationTraversalEdgeAdvanceStatus.Edge,
                    "the immutable selected edge passed the same evaluator during search");
                if (_edges.CurrentOrdinal < parentEdgeOrdinal)
                    continue;
                if (_edges.CurrentKind == NavigationTraversalEdgeKind.Transition)
                {
                    if (!AppendTransitionGuidePoints(sourceNode, targetNode))
                        return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                }
                else if (_edges.CurrentKind == NavigationTraversalEdgeKind.Volume)
                {
                    _graph!.TryGetNodeAddress(
                        targetNode.Node,
                        out NavigationCellAddress targetAddress);
                    _graph.TryGetNodeState(
                        targetNode.Node,
                        targetNode.Medium,
                        out NavigationNodeState targetState);
                    if (!AppendGuidePoint(
                            new NavigationAStarGuidePoint(
                                targetAddress,
                                GetGuideAnchor(targetState, targetNode.Medium),
                                targetNode.Medium),
                            _pathEdgeOrdinal + 1))
                    {
                        return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                    }
                }
                _pathEdgeOrdinal++;
                BeginSelectedEdgeReplay(_pathEdgeOrdinal);
                continue;
            }

            if (_stage == Stage.Simplify)
                return AdvanceSimplification(ref nodeRemaining);

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
                if (_simplificationSourcePathOrdinal >= _nextTransitionPathOrdinal)
                {
                    _nextTransitionPathOrdinal = -1;
                    _transitionBarrierScanOrdinal =
                        _simplificationSourcePathOrdinal + 1;
                }
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
                    return ApplyTerminalStatus(
                        NavigationSearchFinalizationRules.ResolveIncompleteLookupStatus(
                            _meter.RemainingLookupProbes));
                }
                _dependencySort = default;
                _stage = Stage.CaptureDependencies;
                continue;
            }

            if (_stage == Stage.CaptureDependencies)
            {
                if (!AdvanceDependencyCapture(ref lookupRemaining))
                    return Status;
                long requiredPayloadBytes = NavigationAStarPayload.GetRetainedBytes(
                    _workspace.GuidePointCount,
                    _payloadTransitionInstructions?.Length ?? 0,
                    _dependencyStamp!.Result);
                if (requiredPayloadBytes > _maximumPayloadBytes)
                    return Finish(NavigationSurfaceAStarStatus.CapacityExceeded);
                _payloadGuidePoints = _workspace.GuidePointCount == 0
                    ? Array.Empty<NavigationAStarGuidePoint>()
                    : new NavigationAStarGuidePoint[_workspace.GuidePointCount];
                _payloadTransitionInstructions ??=
                    Array.Empty<NavigationTransitionInstruction>();
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
            CompletePayloadBuild(IsWorldCurrent());
            break;
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

    private void BeginSimplification()
    {
        _simplificationSourcePathOrdinal = 0;
        _simplificationCandidatePathOrdinal = _workspace.PathNodeCount - 1;
        _simplificationWriteOrdinal = 1;
        _transitionBarrierScanOrdinal = 1;
        _nextTransitionPathOrdinal = -1;
        if (!NavigationSearchFinalizationRules.TryAdmitSimplification(
                _workspace.PathNodeCount,
                _meter.RemainingSimplificationRays,
                _workspace.EndpointComponentCount,
                _workspace.EndpointPageCount,
                _meter,
                out _finalizationLookupReservation))
        {
            FinalizeWithoutSimplification();
        }
        else
        {
            _stage = Stage.Simplify;
        }
    }

    private void FinalizeWithoutSimplification()
    {
        _simplificationWriteOrdinal = _workspace.GuidePointCount;
        PrepareDependencyFinalization();
    }

    private NavigationSurfaceAStarStatus AdvanceSimplification(
        ref int nodeRemaining)
    {
        NavigationSurfaceAStarStatus result =
            NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                NavigationSurfaceAStarStatus.Pending,
                IsWorldCurrent());
        return AdvanceSimplification(result, ref nodeRemaining);
    }

    internal NavigationSurfaceAStarStatus AdvanceSimplification(
        NavigationSurfaceAStarStatus result,
        ref int nodeRemaining)
    {
        if (result == NavigationSurfaceAStarStatus.Pending)
            result = AdvanceCurrentSimplification(ref nodeRemaining);
        if (Status == NavigationSurfaceAStarStatus.Pending)
            ApplyTerminalStatus(result);
        return Status;
    }

    private NavigationSurfaceAStarStatus AdvanceCurrentSimplification(
        ref int nodeRemaining)
    {
        while (_nextTransitionPathOrdinal < 0
            && _transitionBarrierScanOrdinal < _workspace.PathNodeCount)
        {
            if (nodeRemaining == 0)
                return Status;
            nodeRemaining--;
            NavigationMediumStateRef scanned =
                _workspace.PathNodes[_transitionBarrierScanOrdinal];
            _workspace.NodeTable.TryGetSlot(scanned, out int scannedSlot);
            if (_workspace.NodeTable.GetRecord(scannedSlot).ParentEdgeKind
                == NavigationTraversalEdgeKind.Transition)
            {
                _nextTransitionPathOrdinal = _transitionBarrierScanOrdinal;
                break;
            }
            _transitionBarrierScanOrdinal++;
        }
        if (_nextTransitionPathOrdinal < 0)
            _nextTransitionPathOrdinal = int.MaxValue;
        if (_simplificationCandidatePathOrdinal >= _nextTransitionPathOrdinal)
        {
            _simplificationCandidatePathOrdinal =
                _nextTransitionPathOrdinal - 1;
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
        _meter.TryConsumeSimplificationRays(1);

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
            _workspace.PathNodes[sourcePathOrdinal].Medium,
            source.Position,
            candidate.Position,
            NavigationRayEndpointAllowance.None,
            NavigationRayChainConstraint.SeedAt(source.Address)));
        NavigationRayStatus rayStatus = _rayWork.Advance(_meter);
        NavigationSurfaceAStarStatus simplificationStatus =
            rayStatus == NavigationRayStatus.Stale
                ? NavigationSurfaceAStarStatus.Stale
                : NavigationSurfaceAStarStatus.Pending;
        if (simplificationStatus == NavigationSurfaceAStarStatus.Pending
            && rayStatus is not NavigationRayStatus.Success
                and not NavigationRayStatus.Blocked)
        {
            BeginRawCopy(copySuffix: true);
            return Status;
        }

        bool accepted = false;
        if (simplificationStatus == NavigationSurfaceAStarStatus.Pending
            && rayStatus == NavigationRayStatus.Success)
        {
            NavigationRayResult ray = _rayWork.Result;
            NavigationMediumStateRef sourceNode =
                _workspace.PathNodes[sourcePathOrdinal];
            NavigationMediumStateRef candidateNode =
                _workspace.PathNodes[candidatePathOrdinal];
            _workspace.NodeTable.TryGetSlot(sourceNode, out int sourceSlot);
            _workspace.NodeTable.TryGetSlot(candidateNode, out int candidateSlot);
            Fixed64.TrySubtract(
                _workspace.NodeTable.GetRecord(candidateSlot).Cost,
                _workspace.NodeTable.GetRecord(sourceSlot).Cost,
                out Fixed64 rawCost);
            accepted = NavigationSearchFinalizationRules.ShouldAcceptSimplificationRay(
                ray.EndAddress,
                candidate.Address,
                ray.TraversalCost,
                rawCost);
        }

        if (simplificationStatus == NavigationSurfaceAStarStatus.Pending)
        {
            simplificationStatus = NavigationSearchFinalizationRules
                .ResolveAStarEpochStatus(
                    NavigationSurfaceAStarStatus.Pending,
                    IsSimplificationEpochCurrent());
        }
        bool dependenciesMerged = simplificationStatus
                == NavigationSurfaceAStarStatus.Pending
            && TryMergeRayDependencies();
        if (simplificationStatus == NavigationSurfaceAStarStatus.Pending
            && !dependenciesMerged)
        {
            simplificationStatus = NavigationSearchFinalizationRules
                .ResolveAStarEpochStatus(
                    NavigationSurfaceAStarStatus.Pending,
                    IsSimplificationEpochCurrent());
            if (simplificationStatus == NavigationSurfaceAStarStatus.Pending)
            {
                BeginRawCopy(copySuffix: true);
                return Status;
            }
        }
        if (simplificationStatus == NavigationSurfaceAStarStatus.Pending)
        {
            _hasCompletedSimplificationProof = true;
            simplificationStatus = NavigationSearchFinalizationRules
                .ResolveAStarEpochStatus(
                    NavigationSurfaceAStarStatus.Pending,
                    IsWorldCurrent());
        }
        if (simplificationStatus != NavigationSurfaceAStarStatus.Pending)
            return Finish(simplificationStatus);

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
        NavigationDependencyWorkspace target = _workspace.EndpointWorkspace.Dependencies;
        NavigationDependencyWorkspace source = _workspace.RayWorkspace.Dependencies;
        int priorReservation = _finalizationLookupReservation;
        // A terminal ray and both dependency passes are one bounded atomic unit:
        // prove and debit the complete append pass before mutating the target.
        if (!NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                IsSimplificationEpochCurrent(),
                target,
                source,
                _meter,
                priorReservation,
                out int enlargedReservation))
        {
            return false;
        }
        target.CommitMerge(source);
        _meter.RecordSuccessfulDependencyMerge();
        _finalizationLookupReservation = enlargedReservation;
        return IsSimplificationEpochCurrent();
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
        NavigationSurfaceAStarStatus finalizationStatus =
            NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                NavigationSurfaceAStarStatus.Pending,
                IsWorldCurrent());
        PrepareDependencyFinalization(finalizationStatus);
    }

    internal void PrepareDependencyFinalization(
        NavigationSurfaceAStarStatus finalizationStatus)
    {
        if (finalizationStatus == NavigationSurfaceAStarStatus.Pending)
        {
            _workspace.GuidePointCount = _simplificationWriteOrdinal;
            _meter.ReleaseLookupReservationFloor();
            _finalizationLookupReservation = 0;
            _dependencySort = new NavigationDependencySortWork(_workspace);
            _stage = Stage.SortDependencies;
        }
        ApplyTerminalStatus(finalizationStatus);
    }

    private bool AdvanceDependencyCapture(ref int lookupRemaining) =>
        AdvanceDependencyCapture(
            NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                NavigationSurfaceAStarStatus.Pending,
                IsWorldCurrent()),
            ref lookupRemaining);

    internal bool AdvanceDependencyCapture(
        NavigationSurfaceAStarStatus captureStatus,
        ref int lookupRemaining)
    {
        bool complete = false;
        if (captureStatus == NavigationSurfaceAStarStatus.Pending)
        {
            _dependencyStamp ??= new NavigationDependencyStampWork(
                _graph!,
                _query!.AreaPolicy,
                _workspace.EndpointComponents,
                _workspace.EndpointComponentCount,
                _workspace.EndpointPages,
                _workspace.EndpointPageCount,
                _workspace.EndpointWorkspace.Dependencies.HasTransitionDependency);
            int lookupBefore = _meter.LookupProbes;
            complete = _dependencyStamp.Advance(_meter, lookupRemaining);
            lookupRemaining -= _meter.LookupProbes - lookupBefore;
            captureStatus = complete
                ? NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                    NavigationSurfaceAStarStatus.Pending,
                    IsWorldCurrent())
                : _meter.RemainingLookupProbes == 0
                    ? NavigationSurfaceAStarStatus.BudgetExceeded
                    : NavigationSurfaceAStarStatus.Pending;
        }
        if (captureStatus != NavigationSurfaceAStarStatus.Pending)
        {
            Finish(captureStatus);
            return false;
        }
        return complete;
    }

    internal void CompletePayloadBuild(bool buildEpochCurrent)
    {
        NavigationSurfaceAStarStatus finalStatus =
            NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                _resultStatus,
                buildEpochCurrent);
        if (buildEpochCurrent)
        {
            NavigationResolvedPathQuery resolved = _query!;
            Fixed64 resultCost = Fixed64.Zero;
            if (_resultStatus == NavigationSurfaceAStarStatus.Success)
            {
                _workspace.NodeTable.TryGetSlot(_goal, out int endSlot);
                resultCost = _workspace.NodeTable.GetRecord(endSlot).Cost;
            }
            Result = new NavigationAStarPayload(
                new NavigationAStarPayloadKey(
                    resolved.Query,
                    resolved.Start.Address,
                    resolved.End.Address,
                    resolved.StartMedium,
                    resolved.TargetMedia),
                _payloadGuidePoints!,
                _payloadTransitionInstructions!,
                resultCost,
                _dependencyStamp!.Result,
                _requiresWorldStamp || _hasCompletedSimplificationProof
                    ? _simplificationWorldChangeSequence
                    : null,
                _resultStatus);
            finalStatus = NavigationSearchFinalizationRules.ResolveAStarEpochStatus(
                _resultStatus,
                IsWorldCurrent());
        }
        ApplyTerminalStatus(finalStatus);
    }

    private bool IsWorldCurrent() => NavigationSearchFinalizationRules.IsEpochCurrent(
        _requiresWorldStamp || _hasCompletedSimplificationProof,
        _simplificationWorldChangeSequence,
        _world.ChangeSequence);

    private bool IsSimplificationEpochCurrent() =>
        NavigationSearchFinalizationRules.IsEpochCurrent(
            epochRequired: true,
            expectedEpoch: _simplificationWorldChangeSequence,
            currentEpoch: _world.ChangeSequence);

    private void CaptureWorldDependency(
        in NavigationTraversalEdgeEnumerator edges)
    {
        if (edges.RequiresWorldStamp)
            _requiresWorldStamp = true;
    }

    private void ReleaseRuntimeState()
    {
        _graph = null;
        _edges = default;
        _meter.ReleaseLookupReservationFloor();
        _finalizationLookupReservation = 0;
        _rayWork.Reset();
        _dependencySort = default;
    }

    private NavigationTraversalEdgeEnumerator EnumerateEdges(
        NavigationMediumStateRef source,
        int emittedSurfaceOrdinal) => new(
        _world,
        _graph!,
        source,
        _query!.Query.Agent,
        _query.AreaPolicy,
        _workspace.RayWorkspace,
        _query.Query.AllowTransitions,
        emittedSurfaceOrdinal);

    private void BeginSelectedEdgeReplay(int sourcePathOrdinal)
    {
        _edges = default;
        int targetPathOrdinal = sourcePathOrdinal + 1;
        if (targetPathOrdinal >= _workspace.PathNodeCount)
            return;
        NavigationMediumStateRef target = _workspace.PathNodes[targetPathOrdinal];
        _workspace.NodeTable.TryGetSlot(target, out int targetSlot);
        _edges = EnumerateEdges(
            _workspace.PathNodes[sourcePathOrdinal],
            _workspace.NodeTable.GetRecord(targetSlot).ParentEdgeOrdinal);
    }

    private bool IsTarget(NavigationMediumStateRef state) =>
        state.Node.Equals(_query!.End.Node)
        && (_query.TargetMedia & NavigationCell.ToMedia(state.Medium)) != 0;

    private Vector3d GetGuideAnchor(
        NavigationNodeState state,
        TraversalMedium medium)
    {
        if (medium == TraversalMedium.Solid)
            return state.FootAnchor;
        state.TryGetCenteredVolumeFootAnchor(
            _query!.Query.Agent.Shape.Height,
            out Vector3d anchor);
        return anchor;
    }

    private NavigationSurfaceAStarStatus Finish(NavigationSurfaceAStarStatus status)
    {
        Status = status;
        System.Diagnostics.Debug.Assert(_query != null);
        _query!.ReleaseLease();
        ReleaseRuntimeState();
        return Status;
    }

    private NavigationSurfaceAStarStatus ApplyTerminalStatus(
        NavigationSurfaceAStarStatus status) =>
        status == NavigationSurfaceAStarStatus.Pending
            ? Status
            : Finish(status);

    private NavigationSurfaceAStarStatus ApplyRoute(
        NavigationMediumStateRef targetState,
        Fixed64 edgeCost,
        int edgeOrdinal,
        NavigationTraversalEdgeKind edgeKind)
    {
        if (!_workspace.NodeTable.TryGetOrAdd(
                targetState,
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
        target.Heuristic = GetHeuristic(targetState);
        if (!Fixed64.TryAdd(
                target.Cost,
                target.Heuristic,
                out target.EstimatedTotalCost))
        {
            return Finish(NavigationSurfaceAStarStatus.CostOverflow);
        }
        target.Parent = _current;
        target.ParentEdgeOrdinal = edgeOrdinal;
        target.ParentEdgeKind = edgeKind;
        target.HasParent = true;
        if (added)
            Push(targetState, targetSlot);
        else
            SortUp(target.HeapIndex);
        return Status;
    }

    private bool AppendTransitionGuidePoints(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target)
    {
        int transitionOrdinal = _transitionPayloadWrite;
        _graph!.TryGetNodeAddress(source.Node, out NavigationCellAddress sourceAddress);
        _graph.TryGetNodeAddress(target.Node, out NavigationCellAddress targetAddress);

        int guideRollback = _workspace.GuidePointCount;
        _payloadTransitionInstructions![transitionOrdinal] =
            new NavigationTransitionInstruction(
                _edges.CurrentTransitionIdentityKind,
                _edges.CurrentTransitionOwnerMapId,
                _edges.CurrentTransitionId,
                _edges.CurrentTransitionType,
                sourceAddress,
                targetAddress,
                source.Medium,
                target.Medium,
                _edges.CurrentTransitionSourceAction,
                _edges.CurrentTransitionDestinationAction,
                _edges.CurrentTransitionHints);
        _transitionPayloadWrite++;
        _graph.TryGetNodeState(
            target.Node,
            target.Medium,
            out NavigationNodeState targetState);
        if (!AppendGuidePoint(
                new NavigationAStarGuidePoint(
                    sourceAddress,
                    _edges.CurrentTransitionSourceAction,
                    source.Medium,
                    transitionOrdinal),
                pathNodeOrdinal: -1)
            || !AppendGuidePoint(
                new NavigationAStarGuidePoint(
                    targetAddress,
                    _edges.CurrentTransitionDestinationAction,
                    target.Medium),
                pathNodeOrdinal: -1)
            || !AppendGuidePoint(
                new NavigationAStarGuidePoint(
                    targetAddress,
                    GetGuideAnchor(targetState, target.Medium),
                    target.Medium),
                _pathEdgeOrdinal + 1))
        {
            _workspace.GuidePointCount = guideRollback;
            _payloadTransitionInstructions[transitionOrdinal] = default;
            _transitionPayloadWrite--;
            return false;
        }
        return true;
    }

    private bool AppendGuidePoint(
        NavigationAStarGuidePoint point,
        int pathNodeOrdinal)
    {
        bool isNode = pathNodeOrdinal >= 0;
        int count = _workspace.GuidePointCount;
        if (count != 0
            && !point.HasTransition
            && !_workspace.GuidePoints[count - 1].HasTransition
            && _workspace.GuidePoints[count - 1].Medium == point.Medium
            && _workspace.GuidePoints[count - 1].Position == point.Position)
        {
            int updatedCount = count;
            bool updatedLastGuidePointIsNode = _lastGuidePointIsNode;
            bool applied = TryApplyCoincidentGuidePoint(
                point,
                pathNodeOrdinal,
                _workspace.GuidePoints,
                _workspace.PathNodeGuidePointOrdinals,
                ref updatedCount,
                ref updatedLastGuidePointIsNode);
            _workspace.GuidePointCount = updatedCount;
            _lastGuidePointIsNode = updatedLastGuidePointIsNode;
            return applied;
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

    internal static bool TryApplyCoincidentGuidePoint(
        NavigationAStarGuidePoint point,
        int pathNodeOrdinal,
        NavigationAStarGuidePoint[] guidePoints,
        int[] pathNodeGuidePointOrdinals,
        ref int guidePointCount,
        ref bool lastGuidePointIsNode)
    {
        bool isNode = pathNodeOrdinal >= 0;
        int previousOrdinal = guidePointCount - 1;
        NavigationAStarGuidePoint previous = guidePoints[previousOrdinal];
        if (lastGuidePointIsNode && !isNode)
            return true;
        if (!lastGuidePointIsNode || previous.Address == point.Address)
        {
            guidePoints[previousOrdinal] = point;
            lastGuidePointIsNode = isNode;
            if (isNode)
                pathNodeGuidePointOrdinals[pathNodeOrdinal] = previousOrdinal;
            return true;
        }
        if (guidePointCount >= guidePoints.Length)
            return false;
        System.Diagnostics.Debug.Assert(
            isNode,
            "only a distinct addressed node can append after coincident-point preservation and replacement are rejected");
        guidePoints[guidePointCount] = point;
        pathNodeGuidePointOrdinals[pathNodeOrdinal] = guidePointCount;
        guidePointCount++;
        lastGuidePointIsNode = isNode;
        return true;
    }

    private void Push(NavigationMediumStateRef node, int slot)
    {
        int index = _workspace.HeapCount++;
        _workspace.HeapNodes[index] = node;
        _workspace.NodeTable.GetRecord(slot).HeapIndex = index;
        SortUp(index);
    }

    private NavigationMediumStateRef Pop()
    {
        NavigationMediumStateRef result = _workspace.HeapNodes[0];
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
        NavigationMediumStateRef left = _workspace.HeapNodes[leftIndex];
        NavigationMediumStateRef right = _workspace.HeapNodes[rightIndex];
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
        _graph!.TryGetNodeAddress(left.Node, out NavigationCellAddress leftAddress);
        _graph.TryGetNodeAddress(right.Node, out NavigationCellAddress rightAddress);
        comparison = leftAddress.CompareTo(rightAddress);
        return comparison != 0
            ? comparison
            : ((int)left.Medium).CompareTo((int)right.Medium);
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

    private Fixed64 GetHeuristic(NavigationMediumStateRef node)
    {
        Fixed64 heuristic = Fixed64.Zero;
        if (_useEuclideanHeuristic)
        {
            bool hasState = _graph!.TryGetNodeState(
                node.Node,
                node.Medium,
                out NavigationNodeState state);
            System.Diagnostics.Debug.Assert(hasState);
            bool hasFootAnchor = TryGetHeuristicFootAnchor(
                state,
                node.Medium,
                out Vector3d footAnchor);
            NavigationSearchFinalizationRules.TryGetEuclideanHeuristic(
                hasFootAnchor,
                footAnchor,
                _targetFootAnchor,
                out heuristic);
        }
        return heuristic;
    }

    private bool TryGetHeuristicFootAnchor(
        NavigationNodeState state,
        TraversalMedium medium,
        out Vector3d footAnchor)
    {
        if (medium == TraversalMedium.Solid)
        {
            footAnchor = state.FootAnchor;
            return true;
        }

        return state.TryGetCenteredVolumeFootAnchor(
            _query!.Query.Agent.Shape.Height,
            out footAnchor);
    }
}
