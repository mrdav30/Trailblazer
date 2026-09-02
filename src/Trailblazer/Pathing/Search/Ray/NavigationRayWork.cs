//=======================================================================
// NavigationRayWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Utility;

namespace Trailblazer.Pathing;

/// <summary>Evaluates one bounded straight segment through the immutable navigation graph.</summary>
internal sealed class NavigationRayWork
{
    private readonly NavigationRayWorkspace _workspace;
    private NavigationRayRequest _request;
    private TraversalEvaluator _evaluator;
    private ulong _worldChangeSequence;
    private bool _meterBlocked;
    private bool _begun;

    internal NavigationRayWork(NavigationRayWorkspace workspace)
    {
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        _workspace = workspace;
        Status = NavigationRayStatus.Pending;
    }

    internal NavigationRayStatus Status { get; private set; }

    internal NavigationRayResult Result { get; private set; }

    private TraversalMedium Medium => _request.Medium;

    internal void Begin(in NavigationRayRequest request)
    {
        Reset();
        _request = request;
        _evaluator = new TraversalEvaluator(
            request.ExpectedGraph,
            request.Profile,
            request.AreaPolicy,
            request.Medium);
        Result = default;
        Status = NavigationRayStatus.Pending;
        _worldChangeSequence = 0;
        _meterBlocked = false;
        _begun = true;
    }

    internal void Reset()
    {
        ReleaseExplicitConnectionReferences();
        _workspace.Reset();
        _request = default;
        _evaluator = default;
        Result = default;
        Status = NavigationRayStatus.Pending;
        _worldChangeSequence = 0;
        _meterBlocked = false;
        _begun = false;
    }

    internal NavigationRayStatus Advance(NavigationWorkMeter meter)
    {
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        GuideSampleWorkMeter unused = default;
        return AdvanceCore(meter, ref unused, useGuideMeter: false);
    }

    internal NavigationRayStatus Advance(ref GuideSampleWorkMeter meter) =>
        AdvanceCore(null, ref meter, useGuideMeter: true);

    private NavigationRayStatus AdvanceCore(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        if (!_begun || Status != NavigationRayStatus.Pending)
            return Status;
        if (useGuideMeter
            && Medium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return Finish(NavigationRayStatus.Blocked);
        }

        NavigationRayStatus traceStatus = Trace(
            queryMeter,
            ref guideMeter,
            useGuideMeter);
        if (traceStatus != NavigationRayStatus.Pending)
            return Finish(traceStatus);
        NavigationRayStatus mapStatus = MapIntervals(
            queryMeter,
            ref guideMeter,
            useGuideMeter);
        if (mapStatus != NavigationRayStatus.Pending)
            return Finish(mapStatus);
        return EvaluateChain(queryMeter, ref guideMeter, useGuideMeter);
    }

    private NavigationRayStatus Trace(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        int gridLimit = useGuideMeter
            ? _workspace.MapCapacity
            : Math.Min(_workspace.MapCapacity, queryMeter!.RemainingLookupProbes);
        int addressLimit = useGuideMeter
            ? _workspace.CoveredAddressCapacity
            : Math.Min(
                _workspace.CoveredAddressCapacity,
                queryMeter!.RemainingCoveredVoxelIntervals);
        int outputLimit = Math.Min(
            _workspace.TraceIntervalCapacity,
            useGuideMeter
                ? guideMeter.GetTraceIntervalAllowance()
                : queryMeter!.RemainingTraceIntervals);
        long candidateWorkLimit = useGuideMeter
            ? guideMeter.GetCurrentNodeLookupAllowance()
            : queryMeter!.RemainingGridCandidateWork;
        ulong before = _request.World.ChangeSequence;
        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
            _request.World,
            _request.Start,
            _request.End,
            _workspace.TraceIntervals,
            _workspace.TraceScratch,
            gridLimit,
            addressLimit,
            outputLimit,
            candidateWorkLimit);
        ulong after = _request.World.ChangeSequence;

        int traceIntervals = report.Status == GridTraceIntervalStatus.OutputLimitExceeded
            ? outputLimit
            : report.IntervalCount;
        if (useGuideMeter)
        {
            guideMeter.TryConsumeCurrentNodeLookupProbes(
                checked(report.GridCandidateCount + report.AddressCandidateCount));
            guideMeter.TryConsumeTraceIntervals(traceIntervals);
        }
        else
        {
            queryMeter!.TryConsumeLookupProbes(report.GridCandidateCount);
            queryMeter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount);
            queryMeter.TryConsumeTraceIntervals(traceIntervals);
        }
        _worldChangeSequence = after;
        return ResolveTraceStatus(
            before,
            after,
            report.Status,
            gridLimit,
            _workspace.MapCapacity,
            addressLimit,
            _workspace.CoveredAddressCapacity,
            outputLimit,
            _workspace.TraceIntervalCapacity);
    }

    internal static NavigationRayStatus ResolveTraceStatus(
        ulong worldSequenceBefore,
        ulong worldSequenceAfter,
        GridTraceIntervalStatus status,
        int gridLimit,
        int mapCapacity,
        int addressLimit,
        int coveredAddressCapacity,
        int outputLimit,
        int traceIntervalCapacity)
    {
        if (worldSequenceBefore != worldSequenceAfter)
            return NavigationRayStatus.Stale;
        return status switch
        {
            GridTraceIntervalStatus.Complete => NavigationRayStatus.Pending,
            GridTraceIntervalStatus.UnrepresentableGeometry => NavigationRayStatus.CostOverflow,
            GridTraceIntervalStatus.GridCandidateLimitExceeded =>
                gridLimit < mapCapacity
                    ? NavigationRayStatus.BudgetExceeded
                    : NavigationRayStatus.CapacityExceeded,
            GridTraceIntervalStatus.AddressCandidateLimitExceeded =>
                addressLimit < coveredAddressCapacity
                    ? NavigationRayStatus.BudgetExceeded
                    : NavigationRayStatus.CapacityExceeded,
            GridTraceIntervalStatus.CandidateWorkLimitExceeded =>
                NavigationRayStatus.BudgetExceeded,
            _ => outputLimit < traceIntervalCapacity
                ? NavigationRayStatus.BudgetExceeded
                : NavigationRayStatus.CapacityExceeded
        };
    }

    private NavigationRayStatus MapIntervals(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        NavigationWorldGraph graph = _request.ExpectedGraph;
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        for (int ordinal = 0; ordinal < _workspace.TraceIntervals.Count; ordinal++)
        {
            ref NavigationRayChainRecord record = ref records[ordinal];
            record = default;
            record.PredecessorOrdinal = -1;
            record.RootOrdinal = -1;
            GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
            if (!TryConsumeLookup(
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter,
                    1))
                return NavigationRayStatus.BudgetExceeded;
            if (!graph.TryGetMapId(interval.ConfigurationKey, out string mapId))
                continue;
            bool hasInstance = graph.TryGetMap(
                mapId,
                out NavigationMapInstance instance);
            System.Diagnostics.Debug.Assert(
                hasInstance,
                "the immutable configuration index and map directory are built and replaced together");
            if (!IsTraceIntervalCurrent(
                    instance.GridIdentity.Matches(
                        interval.Cell.WorldSpawnToken,
                        interval.Cell.GridIndex,
                        interval.Cell.GridSpawnToken),
                    instance.GridIdentity.ConfigurationKey == interval.ConfigurationKey,
                    instance.GridLastChangeSequence == interval.GridLastChangeSequence))
            {
                return NavigationRayStatus.Stale;
            }
            if (!interval.IsPhysicallyPresent)
                continue;

            var address = new NavigationCellAddress(mapId, interval.Cell.VoxelIndex);
            if (!graph.TryGetNodeRef(address, out NavigationNodeRef node))
                continue;
            if (!_workspace.Dependencies.TryRecordPage(
                    mapId,
                    node.CellSlot / NavigationSemanticPage.SlotCount))
            {
                return NavigationRayStatus.CapacityExceeded;
            }
            if (!graph.TryGetSurfaceComponent(
                    address,
                    Medium,
                    out NavigationSurfaceComponentKey component,
                    out _))
            {
                continue;
            }
            if (!_workspace.Dependencies.TryRecordComponent(component))
                return NavigationRayStatus.CapacityExceeded;
            if (!_evaluator.TryGetPassableNodeState(node, out _))
                continue;

            record.Node = node;
            record.State = NavigationRayChainRecordState.Unreached;
            record.IsSemanticCostNeutral = true;
        }
        return NavigationRayStatus.Pending;
    }

    private NavigationRayStatus EvaluateChain(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        int count = _workspace.TraceIntervals.Count;
        NavigationSurfaceComponentKey finishComponent = default;
        if (_request.ChainConstraint.Kind
                == NavigationRayChainConstraintKind.FinishAddress
            && !_request.ExpectedGraph.TryGetSurfaceComponent(
                _request.ChainConstraint.TargetAddress,
                Medium,
                out finishComponent,
                out _))
        {
            return Finish(NavigationRayStatus.Stale);
        }
        bool permitsStartPrefix = _request.EndpointAllowance
            == NavigationRayEndpointAllowance.StartPrefix;
        bool permitsDestinationSuffix = _request.EndpointAllowance
            == NavigationRayEndpointAllowance.DestinationSuffix;
        Fixed64 firstSeedParameter = Fixed64.MaxValue;
        if (permitsStartPrefix)
        {
            for (int ordinal = 0; ordinal < count; ordinal++)
            {
                if (records[ordinal].State == NavigationRayChainRecordState.Unreached
                    && MatchesSeedConstraint(ordinal, finishComponent)
                    && _workspace.TraceIntervals[ordinal].TEnter < firstSeedParameter)
                {
                    firstSeedParameter = _workspace.TraceIntervals[ordinal].TEnter;
                }
            }
            NavigationRayChainConstraintKind constraintKind =
                _request.ChainConstraint.Kind;
            if (constraintKind is NavigationRayChainConstraintKind.SourceAddress
                    or NavigationRayChainConstraintKind.SelectedEdge)
            {
                for (int ordinal = 0; ordinal < count; ordinal++)
                {
                    if (records[ordinal].State == NavigationRayChainRecordState.Unreached
                        && _workspace.TraceIntervals[ordinal].TEnter < firstSeedParameter)
                    {
                        return FinishBlocked();
                    }
                }
            }
        }
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
            ref NavigationRayChainRecord record = ref records[ordinal];
            NavigationRayStatus seedStatus = NavigationRayStatus.Pending;
            if (record.State != NavigationRayChainRecordState.Unreached
                || (permitsStartPrefix
                    ? interval.TEnter != firstSeedParameter
                    : interval.TEnter > Fixed64.Zero || interval.TExit < Fixed64.Zero)
                || !CanSeed(
                    ordinal,
                    finishComponent,
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter,
                    out seedStatus))
            {
                if (_meterBlocked)
                    return Finish(NavigationRayStatus.BudgetExceeded);
                if (seedStatus != NavigationRayStatus.Pending)
                    return Finish(seedStatus);
                continue;
            }
            record.RootOrdinal = ordinal;
            record.ArrivalParameter = Fixed64.Zero;
            record.TraversalCost = Fixed64.Zero;
            record.IsSemanticCostNeutral = true;
            record.State = NavigationRayChainRecordState.Ready;
        }

        bool progressed;
        do
        {
            progressed = false;
            for (int ordinal = 0; ordinal < count; ordinal++)
            {
                ref NavigationRayChainRecord record = ref records[ordinal];
                if (record.State != NavigationRayChainRecordState.Ready)
                    continue;
                NavigationRayStatus finishStatus = NavigationRayStatus.Pending;
                if (!permitsDestinationSuffix
                    && CanFinish(
                        ordinal,
                        queryMeter,
                        ref guideMeter,
                        useGuideMeter,
                        out finishStatus))
                    return FinishSuccess(ordinal);
                if (_meterBlocked)
                    return Finish(NavigationRayStatus.BudgetExceeded);
                if (finishStatus != NavigationRayStatus.Pending)
                    return Finish(finishStatus);

                record.State = NavigationRayChainRecordState.Expanded;
                progressed = true;
                NavigationRayChainConstraintKind constraintKind =
                    _request.ChainConstraint.Kind;
                if (constraintKind == NavigationRayChainConstraintKind.SourceAddress
                    || (constraintKind == NavigationRayChainConstraintKind.SelectedEdge
                        && record.PredecessorOrdinal >= 0))
                {
                    continue;
                }

                NavigationRayStatus expansion = Expand(
                    ordinal,
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter);
                if (expansion != NavigationRayStatus.Pending)
                    return Finish(expansion);
            }
        }
        while (progressed);

        if (permitsDestinationSuffix)
            return FinishDestinationSuffix(queryMeter, ref guideMeter, useGuideMeter);
        return FinishBlocked();
    }

    private NavigationRayStatus FinishDestinationSuffix(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        Fixed64 farthestExit = Fixed64.MinValue;
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        int count = _workspace.TraceIntervals.Count;
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            if (ShouldAdvanceFarthestExit(
                    records[ordinal].State,
                    _workspace.TraceIntervals[ordinal].TExit,
                    farthestExit))
            {
                farthestExit = _workspace.TraceIntervals[ordinal].TExit;
            }
        }
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            if (records[ordinal].State != NavigationRayChainRecordState.Expanded
                || _workspace.TraceIntervals[ordinal].TExit != farthestExit)
            {
                continue;
            }
            if (CanFinish(
                    ordinal,
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter,
                    out NavigationRayStatus finishStatus))
                return FinishSuccess(ordinal);
            if (_meterBlocked)
                return Finish(NavigationRayStatus.BudgetExceeded);
            if (finishStatus != NavigationRayStatus.Pending)
                return Finish(finishStatus);
        }
        return FinishBlocked();
    }

    private NavigationRayStatus Expand(
        int sourceOrdinal,
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        if (!useGuideMeter
            && Medium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            return ExpandVolume(sourceOrdinal, queryMeter!);
        }

        NavigationWorldGraph graph = _request.ExpectedGraph;
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        ref NavigationRayChainRecord sourceRecord = ref records[sourceOrdinal];
        if (!TryGetIncomingPortal(
                sourceOrdinal,
                queryMeter,
                ref guideMeter,
                useGuideMeter,
                out GridNavigationPortal incomingPortal))
        {
            return NavigationRayStatus.BudgetExceeded;
        }
        graph.TryGetNodeAddress(
            sourceRecord.Node,
            out NavigationCellAddress sourceAddress);
        graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism);

        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceRecord.Node);
        int edgeSteps = int.MaxValue;
        while (true)
        {
            NavigationSurfaceEdgeAdvanceStatus advance = useGuideMeter
                ? edges.AdvanceOne(ref guideMeter, ref edgeSteps)
                : edges.AdvanceOne(queryMeter, ref edgeSteps);
            if (advance == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                return NavigationRayStatus.BudgetExceeded;
            if (advance == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (advance == NavigationSurfaceEdgeAdvanceStatus.Complete)
                return NavigationRayStatus.Pending;

            NavigationGraphEdge edge = edges.Current;
            int edgeOrdinal = edges.CurrentOrdinal;
            if (!AllowsEdge(sourceAddress, edge.Target, edgeOrdinal))
                continue;
            Fixed64 edgeCost;
            int targetOrdinal;
            Fixed64 targetParameter;
            NavigationExplicitConnectionRecord incomingExplicit = null!;
            if (edge.Kind == NavigationGraphEdgeKind.Explicit)
            {
                NavigationRayStatus explicitStatus = EvaluateExplicitEdge(
                    sourceOrdinal,
                    sourceRecord.Node,
                    edge,
                    incomingPortal,
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter,
                    out edgeCost,
                    out targetOrdinal,
                    out targetParameter);
                if (explicitStatus == NavigationRayStatus.BudgetExceeded
                    || explicitStatus == NavigationRayStatus.CostOverflow
                    || explicitStatus == NavigationRayStatus.CapacityExceeded
                    || explicitStatus == NavigationRayStatus.Stale)
                {
                    return explicitStatus;
                }
                if (explicitStatus != NavigationRayStatus.Success)
                {
                    continue;
                }
                incomingExplicit = edge.ExplicitConnection;
            }
            else
            {
                TraversalEvaluationStatus evaluation = _evaluator.EvaluateEdge(
                    sourceRecord.Node,
                    edge,
                    out TraversalEdgeEvidence evidence);
                edgeCost = evidence.Cost;
                if (evaluation == TraversalEvaluationStatus.CostOverflow)
                    return NavigationRayStatus.CostOverflow;
                if (evaluation != TraversalEvaluationStatus.Passable)
                    continue;
                if (!TryConsumePortal(queryMeter, ref guideMeter, useGuideMeter)
                    || !GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                        evidence.SourcePrism,
                        evidence.TargetPrism,
                        evidence.Portal,
                        _request.Start,
                        _request.End,
                        _request.Profile.Shape.Radius,
                        _request.Profile.Shape.Height,
                        out Fixed64 sourceParameter,
                        out targetParameter))
                {
                    if (_meterBlocked)
                        return NavigationRayStatus.BudgetExceeded;
                    continue;
                }
                GridTraceInterval sourceInterval = _workspace.TraceIntervals[sourceOrdinal];
                targetOrdinal = ResolvePortalTargetOrdinal(
                    sourceParameter,
                    sourceRecord.ArrivalParameter,
                    sourceInterval.TEnter,
                    sourceInterval.TExit,
                    FindTarget(edge.Target, targetParameter));
                if (targetOrdinal < 0)
                    continue;
                Vector3d sourcePoint = Vector3d.Lerp(
                    _request.Start,
                    _request.End,
                    sourceRecord.ArrivalParameter);
                Vector3d portalPoint = Vector3d.Lerp(
                    _request.Start,
                    _request.End,
                    sourceParameter);
                System.Diagnostics.Debug.Assert(
                    sourceRecord.IncomingExplicitConnection == null
                    || new FixedSegment(sourcePoint, portalPoint).Contains(
                        sourceRecord.IncomingExplicitConnection.Definition.ExitAnchor),
                    "a successful explicit exit lies on the traced ray inside the convex source prism and therefore on its following ordinary portal leg");
                if (!TryConsumePrism(queryMeter, ref guideMeter, useGuideMeter)
                    || !GridCellGeometry.IsNavigationBodySegmentValid(
                        sourcePrism,
                        sourcePoint,
                        portalPoint,
                        _request.Profile.Shape.Radius,
                        _request.Profile.Shape.Height,
                        incomingPortal,
                        evidence.Portal,
                        GetEndpointAllowance(
                            sourceOrdinal,
                            sourceRecord,
                            isFinalSegment: false)))
                {
                    if (_meterBlocked)
                        return NavigationRayStatus.BudgetExceeded;
                    continue;
                }
            }
            if (!Fixed64.TryAdd(
                    sourceRecord.TraversalCost,
                    edgeCost,
                    out Fixed64 traversalCost))
            {
                return NavigationRayStatus.CostOverflow;
            }

            ref NavigationRayChainRecord targetRecord = ref records[targetOrdinal];
            bool isEarlierContinuation = IsEarlierContinuation(
                    targetRecord,
                    targetParameter,
                    incomingExplicit);
            ApplyContinuation(
                ref targetRecord,
                candidatePassable: true,
                isEarlierContinuation,
                sourceOrdinal,
                sourceRecord.RootOrdinal,
                targetParameter,
                traversalCost,
                incomingExplicit,
                sourceRecord.IsSemanticCostNeutral && IsSemanticCostNeutral(edge));
        }
    }

    private NavigationRayStatus ExpandVolume(
        int sourceOrdinal,
        NavigationWorkMeter meter)
    {
        NavigationWorldGraph graph = _request.ExpectedGraph;
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        ref NavigationRayChainRecord sourceRecord = ref records[sourceOrdinal];
        var source = new NavigationMediumStateRef(sourceRecord.Node, Medium);
        var edges = new NavigationTraversalEdgeEnumerator(
            _request.World,
            graph,
            source,
            _request.Profile,
            _request.AreaPolicy,
            _workspace,
            allowTransitions: false,
            emittedSurfaceOrdinal: -1);
        var segmentEvaluator = new NavigationVolumeEdgeEvaluator(
            _request.World,
            graph,
            _request.Profile,
            _request.AreaPolicy,
            Medium,
            _workspace);
        int edgeSteps = int.MaxValue;
        int connectionSteps = int.MaxValue;
        while (true)
        {
            NavigationTraversalEdgeAdvanceStatus advance = edges.AdvanceOne(
                meter,
                _workspace.Dependencies,
                ref edgeSteps,
                ref connectionSteps);
            System.Diagnostics.Debug.Assert(
                advance != NavigationTraversalEdgeAdvanceStatus.Blocked,
                "volume-ray enumeration uses an unbounded local edge-step allowance");
            if (advance != NavigationTraversalEdgeAdvanceStatus.Edge)
                return MapTraversalAdvanceStatus(advance);
            System.Diagnostics.Debug.Assert(
                advance == NavigationTraversalEdgeAdvanceStatus.Edge
                && edges.CurrentKind == NavigationTraversalEdgeKind.Volume
                && edges.CurrentTarget.Medium == Medium,
                "a transition-disabled volume enumerator emits only same-medium volume edges");
            if (!AllowsEdge(
                    GetAddress(sourceRecord.Node),
                    edges.CurrentTarget.Node,
                    edges.CurrentOrdinal))
            {
                continue;
            }

            int targetOrdinal = FindVolumeTarget(
                sourceOrdinal,
                edges.CurrentTarget.Node,
                edges.CurrentVolumeIsShortcut,
                out Fixed64 targetParameter);
            if (targetOrdinal < 0)
                continue;
            Vector3d sourcePoint = Vector3d.Lerp(
                _request.Start,
                _request.End,
                sourceRecord.ArrivalParameter);
            Vector3d targetPoint = Vector3d.Lerp(
                _request.Start,
                _request.End,
                targetParameter);
            NavigationTraversalEvaluationStatus segmentStatus =
                segmentEvaluator.CertifyRaySegment(
                    source,
                    edges.CurrentTarget,
                    sourcePoint,
                    targetPoint,
                    meter,
                    _workspace.Dependencies);
            NavigationRayStatus terminalStatus = ResolveVolumeTraversalStatus(
                segmentStatus,
                sourceRecord.TraversalCost,
                edges.CurrentCost,
                out Fixed64 traversalCost);
            if (terminalStatus != NavigationRayStatus.Pending)
                return terminalStatus;

            ref NavigationRayChainRecord targetRecord = ref records[targetOrdinal];
            bool isEarlierContinuation = IsEarlierContinuation(
                targetRecord,
                targetParameter,
                null!);
            ApplyContinuation(
                ref targetRecord,
                segmentStatus == NavigationTraversalEvaluationStatus.Passable,
                isEarlierContinuation,
                sourceOrdinal,
                sourceRecord.RootOrdinal,
                targetParameter,
                traversalCost,
                null!,
                sourceRecord.IsSemanticCostNeutral
                    && IsSemanticCostNeutral(edges.CurrentTarget.Node));
        }
    }

    private int FindVolumeTarget(
        int sourceOrdinal,
        NavigationNodeRef target,
        bool isShortcut,
        out Fixed64 targetParameter)
    {
        NavigationWorldGraph graph = _request.ExpectedGraph;
        NavigationRayChainRecord sourceRecord = _workspace.ChainRecords[sourceOrdinal];
        if (!isShortcut)
        {
            graph.TryGetNodeAddress(
                sourceRecord.Node,
                out NavigationCellAddress sourceAddress);
            graph.TryGetNodeAddress(target, out NavigationCellAddress targetAddress);
            graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism);
            graph.TryGetSeamPrism(targetAddress, out GridCellPrism targetPrism);
            GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal);
            if (GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                    sourcePrism,
                    targetPrism,
                    portal,
                    _request.Start,
                    _request.End,
                    _request.Profile.Shape.Radius,
                    _request.Profile.Shape.Height,
                    out Fixed64 sourceParameter,
                    out targetParameter))
            {
                GridTraceInterval sourceInterval =
                    _workspace.TraceIntervals[sourceOrdinal];
                return ResolvePortalTargetOrdinal(
                    sourceParameter,
                    sourceRecord.ArrivalParameter,
                    sourceInterval.TEnter,
                    sourceInterval.TExit,
                    FindTarget(target, targetParameter));
            }
        }
        for (int ordinal = 0; ordinal < _workspace.TraceIntervals.Count; ordinal++)
        {
            ref NavigationRayChainRecord targetRecord =
                ref _workspace.ChainRecords[ordinal];
            if (targetRecord.State == NavigationRayChainRecordState.Unavailable
                || !targetRecord.Node.Equals(target))
            {
                continue;
            }
            GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
            targetParameter = interval.TEnter < sourceRecord.ArrivalParameter
                ? sourceRecord.ArrivalParameter
                : interval.TEnter;
            if (targetParameter <= interval.TExit)
                return ordinal;
        }
        targetParameter = default;
        return -1;
    }

    private NavigationRayStatus EvaluateExplicitEdge(
        int sourceOrdinal,
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        in GridNavigationPortal incomingPortal,
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        out Fixed64 cost,
        out int targetOrdinal,
        out Fixed64 targetParameter)
    {
        cost = Fixed64.Zero;
        targetOrdinal = -1;
        targetParameter = default;
        TraversalExplicitEdgeStatus status = _evaluator.BeginExplicitEdge(
            source,
            edge,
            out TraversalExplicitEdgeWork work);
        if (status != TraversalExplicitEdgeStatus.Pending)
            return MapExplicitStatus(status);

        NavigationExplicitConnectionRecord record = edge.ExplicitConnection;
        NavigationConnection connection = record.Definition;
        var path = new FixedSegment(_request.Start, _request.End);
        bool geometryPassable = path.Contains(connection.EntryAnchor)
            && path.Contains(connection.ExitAnchor);
        int currentOrdinal = sourceOrdinal;
        Fixed64 currentParameter =
            _workspace.ChainRecords[sourceOrdinal].ArrivalParameter;
        GridNavigationPortal currentIncoming = incomingPortal;
        int portalOrdinal = 0;
        while (status == TraversalExplicitEdgeStatus.Pending)
        {
            if (!TryConsumeConnectionLeg(
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter))
                return NavigationRayStatus.BudgetExceeded;
            status = _evaluator.AdvanceExplicitEdge(
                ref work,
                out TraversalEdgeEvidence evidence);
            if (status == TraversalExplicitEdgeStatus.CostOverflow)
                return MapExplicitStatus(status);
            System.Diagnostics.Debug.Assert(
                evidence.DependencyNode.IsValid,
                "every advanced explicit corridor leg identifies its immutable dependency node");
            if (!TryRecordDependencyNode(evidence.DependencyNode))
                return NavigationRayStatus.CapacityExceeded;
            if (status == TraversalExplicitEdgeStatus.Impassable)
                return NavigationRayStatus.Blocked;

            cost = evidence.Cost;
            if (!geometryPassable)
                continue;
            if (!TryConsumePortal(queryMeter, ref guideMeter, useGuideMeter))
            {
                geometryPassable = false;
                continue;
            }
            bool hasPortalParameters =
                GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                    evidence.SourcePrism,
                    evidence.TargetPrism,
                    evidence.Portal,
                    _request.Start,
                    _request.End,
                    _request.Profile.Shape.Radius,
                    _request.Profile.Shape.Height,
                    out Fixed64 sourceParameter,
                    out Fixed64 nextParameter);
            GridTraceInterval currentInterval =
                _workspace.TraceIntervals[currentOrdinal];
            if (!IsExplicitPortalProgressValid(
                    hasPortalParameters,
                    sourceParameter,
                    currentParameter,
                    currentInterval.TEnter,
                    currentInterval.TExit))
            {
                geometryPassable = false;
                continue;
            }
            int nextOrdinal = FindMapped(
                _workspace,
                evidence.DependencyNode,
                nextParameter);
            if (nextOrdinal < 0)
            {
                geometryPassable = false;
                continue;
            }
            Vector3d currentPoint = Vector3d.Lerp(
                _request.Start,
                _request.End,
                currentParameter);
            Vector3d outgoingPoint = Vector3d.Lerp(
                _request.Start,
                _request.End,
                sourceParameter);
            if (portalOrdinal == 0)
            {
                var firstLeg = new FixedSegment(currentPoint, outgoingPoint);
                NavigationExplicitConnectionRecord prior =
                    _workspace.ChainRecords[sourceOrdinal].IncomingExplicitConnection;
                if (!IsExplicitFirstLegValid(
                        firstLeg,
                        connection.EntryAnchor,
                        prior,
                        _request.Start,
                        _request.End))
                {
                    geometryPassable = false;
                    continue;
                }
            }
            if (!TryConsumePrism(queryMeter, ref guideMeter, useGuideMeter))
            {
                geometryPassable = false;
                continue;
            }
            if (!GridCellGeometry.IsNavigationBodySegmentValid(
                    evidence.SourcePrism,
                    currentPoint,
                    outgoingPoint,
                    _request.Profile.Shape.Radius,
                    _request.Profile.Shape.Height,
                    currentIncoming,
                    evidence.Portal,
                    portalOrdinal == 0
                        ? GetEndpointAllowance(
                            sourceOrdinal,
                            _workspace.ChainRecords[sourceOrdinal],
                            isFinalSegment: false)
                        : GridNavigationBodySegmentEndpointAllowance.None))
            {
                geometryPassable = false;
                continue;
            }

            currentOrdinal = nextOrdinal;
            currentParameter = nextParameter;
            currentIncoming = evidence.Portal;
            portalOrdinal++;
        }

        if (_meterBlocked)
            return NavigationRayStatus.BudgetExceeded;
        if (!geometryPassable)
            return NavigationRayStatus.Blocked;
        System.Diagnostics.Debug.Assert(
            _workspace.ChainRecords[currentOrdinal].Node.Equals(edge.Target)
            && _workspace.ChainRecords[currentOrdinal].State
                != NavigationRayChainRecordState.Unavailable,
            "a successful explicit replay ends on the final passable dependency node mapped by FindMapped");
        targetOrdinal = currentOrdinal;
        targetParameter = currentParameter;
        return NavigationRayStatus.Success;
    }

    private static NavigationRayStatus MapExplicitStatus(
        TraversalExplicitEdgeStatus status) =>
        status == TraversalExplicitEdgeStatus.CostOverflow
            ? NavigationRayStatus.CostOverflow
            : NavigationRayStatus.Blocked;

    internal static bool IsExplicitFirstLegValid(
        in FixedSegment firstLeg,
        Vector3d entryAnchor,
        NavigationExplicitConnectionRecord prior,
        Vector3d rayStart,
        Vector3d rayEnd)
    {
        if (!firstLeg.Contains(entryAnchor))
            return false;
        if (prior == null)
            return true;
        Vector3d priorExit = prior.Definition.ExitAnchor;
        return firstLeg.Contains(priorExit)
            && CompareAlongRay(rayStart, rayEnd, priorExit, entryAnchor) <= 0;
    }

    private bool IsEarlierContinuation(
        in NavigationRayChainRecord current,
        Fixed64 candidateArrivalParameter,
        NavigationExplicitConnectionRecord candidateExplicit)
    {
        Vector3d candidate = candidateExplicit != null
            ? candidateExplicit.Definition.ExitAnchor
            : Vector3d.Lerp(
                _request.Start,
                _request.End,
                candidateArrivalParameter);
        Vector3d prior = current.IncomingExplicitConnection != null
            ? current.IncomingExplicitConnection.Definition.ExitAnchor
            : Vector3d.Lerp(
                _request.Start,
                _request.End,
                current.ArrivalParameter);
        return CompareAlongRay(candidate, prior) < 0;
    }

    private int CompareAlongRay(Vector3d first, Vector3d second)
        => CompareAlongRay(_request.Start, _request.End, first, second);

    private static int CompareAlongRay(
        Vector3d rayStart,
        Vector3d rayEnd,
        Vector3d first,
        Vector3d second)
    {
        if (rayEnd.X != rayStart.X)
        {
            return rayEnd.X > rayStart.X
                ? first.X.CompareTo(second.X)
                : second.X.CompareTo(first.X);
        }
        return rayEnd.Z > rayStart.Z
            ? first.Z.CompareTo(second.Z)
            : second.Z.CompareTo(first.Z);
    }

    internal static int FindMapped(
        NavigationRayWorkspace workspace,
        NavigationNodeRef target,
        Fixed64 parameter)
    {
        for (int ordinal = 0; ordinal < workspace.TraceIntervals.Count; ordinal++)
        {
            GridTraceInterval interval = workspace.TraceIntervals[ordinal];
            if (interval.TEnter > parameter)
                break;
            ref NavigationRayChainRecord record = ref workspace.ChainRecords[ordinal];
            if (record.State != NavigationRayChainRecordState.Unavailable
                && record.Node.Equals(target)
                && interval.TExit >= parameter)
            {
                return ordinal;
            }
        }
        return -1;
    }

    internal static bool ContainsParameter(
        Fixed64 enter,
        Fixed64 exit,
        Fixed64 parameter) => enter <= parameter && exit >= parameter;

    internal static int ResolvePortalTargetOrdinal(
        Fixed64 sourceParameter,
        Fixed64 arrivalParameter,
        Fixed64 intervalEnter,
        Fixed64 intervalExit,
        int targetOrdinal) =>
        sourceParameter >= arrivalParameter
        && ContainsParameter(intervalEnter, intervalExit, sourceParameter)
            ? targetOrdinal
            : -1;

    internal static bool IsExplicitPortalProgressValid(
        bool hasPortalParameters,
        Fixed64 sourceParameter,
        Fixed64 currentParameter,
        Fixed64 intervalEnter,
        Fixed64 intervalExit) =>
        hasPortalParameters
        && sourceParameter >= currentParameter
        && ContainsParameter(intervalEnter, intervalExit, sourceParameter);

    internal static bool ShouldAcceptContinuation(
        NavigationRayChainRecordState currentState,
        bool candidateIsEarlier) =>
        currentState == NavigationRayChainRecordState.Unreached
        || candidateIsEarlier;

    internal static void ApplyContinuation(
        ref NavigationRayChainRecord target,
        bool candidatePassable,
        bool candidateIsEarlier,
        int predecessorOrdinal,
        int rootOrdinal,
        Fixed64 arrivalParameter,
        Fixed64 traversalCost,
        NavigationExplicitConnectionRecord incomingExplicitConnection,
        bool isSemanticCostNeutral)
    {
        if (!candidatePassable
            || !ShouldAcceptContinuation(target.State, candidateIsEarlier))
        {
            return;
        }
        target.PredecessorOrdinal = predecessorOrdinal;
        target.RootOrdinal = rootOrdinal;
        target.ArrivalParameter = arrivalParameter;
        target.TraversalCost = traversalCost;
        target.IncomingExplicitConnection = incomingExplicitConnection;
        target.IsSemanticCostNeutral = isSemanticCostNeutral;
        target.State = NavigationRayChainRecordState.Ready;
    }

    private int FindTarget(NavigationNodeRef target, Fixed64 parameter) =>
        FindMapped(_workspace, target, parameter);

    private bool CanSeed(
        int ordinal,
        NavigationSurfaceComponentKey finishComponent,
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        out NavigationRayStatus terminalStatus)
    {
        terminalStatus = NavigationRayStatus.Pending;
        NavigationNodeRef node = _workspace.ChainRecords[ordinal].Node;
        NavigationRayChainConstraint constraint = _request.ChainConstraint;
        NavigationCellAddress address;
        if (constraint.Kind == NavigationRayChainConstraintKind.FinishAddress
            && !MatchesSeedConstraint(ordinal, finishComponent))
        {
            return false;
        }
        if (constraint.Kind is NavigationRayChainConstraintKind.Unrestricted
            or NavigationRayChainConstraintKind.FinishAddress)
        {
            if (_request.EndpointAllowance == NavigationRayEndpointAllowance.StartPrefix)
                return true;
            _request.ExpectedGraph.TryGetNodeAddress(node, out address);
        }
        else
        {
            _request.ExpectedGraph.TryGetNodeAddress(node, out address);
            if (address != constraint.SourceAddress)
            {
                return false;
            }
            if (_request.EndpointAllowance == NavigationRayEndpointAllowance.StartPrefix)
                return true;
        }
        if (!useGuideMeter
            && Medium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            var state = new NavigationMediumStateRef(node, Medium);
            return CertifyVolumeSegment(
                state,
                state,
                _request.Start,
                _request.Start,
                queryMeter!,
                out terminalStatus);
        }
        _request.ExpectedGraph.TryGetSeamPrism(address, out GridCellPrism prism);
        return TryConsumePrism(queryMeter, ref guideMeter, useGuideMeter)
            && GridCellGeometry.IsNavigationBodySegmentValid(
                prism,
                _request.Start,
                _request.Start,
                _request.Profile.Shape.Radius,
                _request.Profile.Shape.Height,
                default,
                default,
                GridNavigationBodySegmentEndpointAllowance.None);
    }

    private bool MatchesSeedConstraint(
        int ordinal,
        NavigationSurfaceComponentKey finishComponent)
    {
        NavigationRayChainConstraint constraint = _request.ChainConstraint;
        if (constraint.Kind == NavigationRayChainConstraintKind.Unrestricted)
            return true;
        if (constraint.Kind == NavigationRayChainConstraintKind.FinishAddress)
        {
            _request.ExpectedGraph.TryGetNodeAddress(
                _workspace.ChainRecords[ordinal].Node,
                out NavigationCellAddress seedAddress);
            _request.ExpectedGraph.TryGetSurfaceComponent(
                seedAddress,
                Medium,
                out NavigationSurfaceComponentKey actual,
                out _);
            return actual.Equals(finishComponent);
        }
        _request.ExpectedGraph.TryGetNodeAddress(
            _workspace.ChainRecords[ordinal].Node,
            out NavigationCellAddress address);
        return address == constraint.SourceAddress;
    }

    private bool AllowsEdge(
        NavigationCellAddress source,
        NavigationNodeRef targetNode,
        int edgeOrdinal)
    {
        NavigationRayChainConstraint constraint = _request.ChainConstraint;
        if (constraint.Kind is NavigationRayChainConstraintKind.Unrestricted
            or NavigationRayChainConstraintKind.SeedAddress
            or NavigationRayChainConstraintKind.FinishAddress)
            return true;
        return constraint.Kind == NavigationRayChainConstraintKind.SelectedEdge
            && source == constraint.SourceAddress
            && edgeOrdinal == constraint.EdgeOrdinal
            && GetAddress(targetNode) == constraint.TargetAddress;
    }

    private bool CanFinish(
        int ordinal,
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        out NavigationRayStatus terminalStatus)
    {
        terminalStatus = NavigationRayStatus.Pending;
        GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
        ref NavigationRayChainRecord record = ref _workspace.ChainRecords[ordinal];
        bool permitsDestinationSuffix = _request.EndpointAllowance
            == NavigationRayEndpointAllowance.DestinationSuffix;
        _request.ExpectedGraph.TryGetNodeAddress(
            record.Node,
            out NavigationCellAddress address);
        if (!IsPermittedFinalTarget(
                permitsDestinationSuffix,
                interval.TEnter,
                interval.TExit,
                _request.ChainConstraint.Kind,
                address,
                _request.ChainConstraint.TargetAddress,
                record.PredecessorOrdinal))
        {
            return false;
        }
        Vector3d arrival = Vector3d.Lerp(
            _request.Start,
            _request.End,
            record.ArrivalParameter);
        // EvaluateExplicitEdge certifies this exact arrival-to-end suffix before
        // publishing an incoming explicit continuation into a chain record.
        if (!useGuideMeter
            && Medium is TraversalMedium.Gas or TraversalMedium.Liquid)
        {
            _request.ExpectedGraph.TryGetSeamPrism(
                address,
                out GridCellPrism targetPrism);
            if (record.PredecessorOrdinal >= 0
                && TryGetIncomingPortal(
                    ordinal,
                    queryMeter,
                    ref guideMeter,
                    useGuideMeter,
                    out GridNavigationPortal volumeIncoming)
                && GridCellGeometry.IsNavigationBodySegmentValid(
                    targetPrism,
                    arrival,
                    _request.End,
                    _request.Profile.Shape.Radius,
                    _request.Profile.Shape.Height,
                    volumeIncoming,
                    default,
                    GetEndpointAllowance(ordinal, record, isFinalSegment: true)))
            {
                return true;
            }
            var state = new NavigationMediumStateRef(record.Node, Medium);
            return CertifyVolumeSegment(
                state,
                state,
                arrival,
                _request.End,
                queryMeter!,
                out terminalStatus);
        }
        _request.ExpectedGraph.TryGetSeamPrism(address, out GridCellPrism prism);
        return TryGetIncomingPortal(
                ordinal,
                queryMeter,
                ref guideMeter,
                useGuideMeter,
                out GridNavigationPortal incomingPortal)
            && TryConsumePrism(queryMeter, ref guideMeter, useGuideMeter)
            && GridCellGeometry.IsNavigationBodySegmentValid(
                prism,
                arrival,
                _request.End,
                _request.Profile.Shape.Radius,
                _request.Profile.Shape.Height,
                incomingPortal,
                default,
                GetEndpointAllowance(ordinal, record, isFinalSegment: true));
    }

    private bool CertifyVolumeSegment(
        NavigationMediumStateRef source,
        NavigationMediumStateRef target,
        Vector3d sourceFoot,
        Vector3d targetFoot,
        NavigationWorkMeter meter,
        out NavigationRayStatus terminalStatus)
    {
        var evaluator = new NavigationVolumeEdgeEvaluator(
            _request.World,
            _request.ExpectedGraph,
            _request.Profile,
            _request.AreaPolicy,
            Medium,
            _workspace);
        NavigationTraversalEvaluationStatus status = evaluator.CertifyRaySegment(
            source,
            target,
            sourceFoot,
            targetFoot,
            meter,
            _workspace.Dependencies);
        terminalStatus = MapVolumeStatus(status);
        return status == NavigationTraversalEvaluationStatus.Passable;
    }

    internal static NavigationRayStatus MapVolumeStatus(
        NavigationTraversalEvaluationStatus status) => status switch
    {
        NavigationTraversalEvaluationStatus.BudgetExceeded =>
            NavigationRayStatus.BudgetExceeded,
        NavigationTraversalEvaluationStatus.CapacityExceeded =>
            NavigationRayStatus.CapacityExceeded,
        NavigationTraversalEvaluationStatus.CostOverflow =>
            NavigationRayStatus.CostOverflow,
        NavigationTraversalEvaluationStatus.Stale => NavigationRayStatus.Stale,
        _ => NavigationRayStatus.Pending
    };

    internal static bool IsTraceIntervalCurrent(
        bool identityMatches,
        bool configurationMatches,
        bool sequenceMatches) =>
        identityMatches
        && configurationMatches
        && sequenceMatches;

    internal static bool ShouldAdvanceFarthestExit(
        NavigationRayChainRecordState state,
        Fixed64 candidateExit,
        Fixed64 farthestExit) =>
        state == NavigationRayChainRecordState.Expanded
        && candidateExit > farthestExit;

    internal static bool IsPermittedFinalTarget(
        bool permitsDestinationSuffix,
        Fixed64 intervalEnter,
        Fixed64 intervalExit,
        NavigationRayChainConstraintKind constraintKind,
        NavigationCellAddress actualAddress,
        NavigationCellAddress targetAddress,
        int predecessorOrdinal)
    {
        System.Diagnostics.Debug.Assert(
            intervalEnter <= Fixed64.One,
            "trace intervals are normalized to the closed segment parameter range");
        if (!permitsDestinationSuffix && intervalExit < Fixed64.One)
        {
            return false;
        }
        if (constraintKind == NavigationRayChainConstraintKind.FinishAddress
            && actualAddress != targetAddress)
        {
            return false;
        }
        if (constraintKind == NavigationRayChainConstraintKind.SelectedEdge
            && (predecessorOrdinal < 0 || actualAddress != targetAddress))
        {
            return false;
        }
        return true;
    }

    internal static bool IsPageDependencyCurrent(
        bool found,
        GraphPageDependency expected,
        GraphPageDependency current) =>
        found && expected.Equals(current);

    internal static NavigationRayStatus ResolveVolumeTraversalStatus(
        NavigationTraversalEvaluationStatus segmentStatus,
        Fixed64 sourceTraversalCost,
        Fixed64 edgeCost,
        out Fixed64 traversalCost)
    {
        traversalCost = default;
        NavigationRayStatus status = MapVolumeStatus(segmentStatus);
        if (status != NavigationRayStatus.Pending
            || segmentStatus != NavigationTraversalEvaluationStatus.Passable)
        {
            return status;
        }
        return Fixed64.TryAdd(sourceTraversalCost, edgeCost, out traversalCost)
            ? NavigationRayStatus.Pending
            : NavigationRayStatus.CostOverflow;
    }

    internal static NavigationRayStatus MapTraversalAdvanceStatus(
        NavigationTraversalEdgeAdvanceStatus status) => status switch
    {
        NavigationTraversalEdgeAdvanceStatus.Blocked => NavigationRayStatus.Blocked,
        NavigationTraversalEdgeAdvanceStatus.BudgetExceeded =>
            NavigationRayStatus.BudgetExceeded,
        NavigationTraversalEdgeAdvanceStatus.CapacityExceeded =>
            NavigationRayStatus.CapacityExceeded,
        NavigationTraversalEdgeAdvanceStatus.CostOverflow =>
            NavigationRayStatus.CostOverflow,
        NavigationTraversalEdgeAdvanceStatus.Stale => NavigationRayStatus.Stale,
        _ => NavigationRayStatus.Pending
    };

    private GridNavigationBodySegmentEndpointAllowance GetEndpointAllowance(
        int ordinal,
        in NavigationRayChainRecord record,
        bool isFinalSegment)
    {
        if (record.PredecessorOrdinal < 0
            && _request.EndpointAllowance == NavigationRayEndpointAllowance.StartPrefix
            && _workspace.TraceIntervals[ordinal].TEnter > Fixed64.Zero)
        {
            return GridNavigationBodySegmentEndpointAllowance.StartFootprintEdge;
        }
        return isFinalSegment
            && _request.EndpointAllowance
                == NavigationRayEndpointAllowance.DestinationSuffix
            && _workspace.TraceIntervals[ordinal].TExit < Fixed64.One
            ? GridNavigationBodySegmentEndpointAllowance.EndFootprintEdge
            : GridNavigationBodySegmentEndpointAllowance.None;
    }

    private bool TryGetIncomingPortal(
        int ordinal,
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        out GridNavigationPortal portal)
    {
        ref NavigationRayChainRecord record = ref _workspace.ChainRecords[ordinal];
        if (record.PredecessorOrdinal < 0)
        {
            portal = default;
            return true;
        }
        if (record.IncomingExplicitConnection != null)
        {
            if (!TryConsumePortal(queryMeter, ref guideMeter, useGuideMeter))
            {
                portal = default;
                return false;
            }
            var portals = record.IncomingExplicitConnection.NavigationPortals;
            portal = portals[portals.Count - 1];
            return true;
        }
        NavigationRayChainRecord predecessor =
            _workspace.ChainRecords[record.PredecessorOrdinal];
        NavigationWorldGraph graph = _request.ExpectedGraph;
        if (!TryConsumePortal(queryMeter, ref guideMeter, useGuideMeter))
        {
            portal = default;
            return false;
        }
        graph.TryGetNodeAddress(predecessor.Node, out NavigationCellAddress source);
        graph.TryGetNodeAddress(record.Node, out NavigationCellAddress target);
        graph.TryGetSeamPrism(source, out GridCellPrism sourcePrism);
        graph.TryGetSeamPrism(target, out GridCellPrism targetPrism);
        GridCellGeometry.TryCreateNavigationPortal(sourcePrism, targetPrism, out portal);
        return true;
    }

    private bool IsSemanticCostNeutral(in NavigationGraphEdge edge)
    {
        return IsSemanticCostNeutral(edge.Target)
            && (edge.Kind != NavigationGraphEdgeKind.Explicit
                || edge.ExplicitConnection.Definition.AdditionalCost == Fixed64.Zero);
    }

    private NavigationCellAddress GetAddress(NavigationNodeRef node)
    {
        _request.ExpectedGraph.TryGetNodeAddress(node, out NavigationCellAddress address);
        return address;
    }

    private bool IsSemanticCostNeutral(NavigationNodeRef target)
    {
        _request.ExpectedGraph.TryGetNodeState(
            target,
            Medium,
            out NavigationNodeState state);
        _request.AreaPolicy.TryGetRule(state.Cell.Area, out NavigationAreaRule rule);
        return state.Cell.EnterCost == Fixed64.Zero
            && rule.AdditionalEnterCost == Fixed64.Zero;
    }

    private bool TryRecordDependencyNode(NavigationNodeRef node)
    {
        NavigationWorldGraph graph = _request.ExpectedGraph;
        graph.TryGetNodeAddress(node, out NavigationCellAddress address);
        if (!_workspace.Dependencies.TryRecordPage(
                address.MapId,
                node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return false;
        }
        graph.TryGetSurfaceComponent(
            address,
            Medium,
            out NavigationSurfaceComponentKey component,
            out _);
        return _workspace.Dependencies.TryRecordComponent(component);
    }

    private NavigationRayStatus FinishSuccess(int ordinal)
    {
        NavigationRayChainRecord record = _workspace.ChainRecords[ordinal];
        NavigationRayChainRecord root = _workspace.ChainRecords[record.RootOrdinal];
        _request.ExpectedGraph.TryGetNodeAddress(
            root.Node,
            out NavigationCellAddress start);
        _request.ExpectedGraph.TryGetNodeAddress(
            record.Node,
            out NavigationCellAddress end);
        if (!AreDependenciesCurrent())
        {
            return Finish(NavigationRayStatus.Stale);
        }
        Result = new NavigationRayResult(
            NavigationRayStatus.Success,
            start,
            end,
            record.TraversalCost,
            record.IsSemanticCostNeutral);
        return Finish(NavigationRayStatus.Success, preserveResult: true);
    }

    private NavigationRayStatus FinishBlocked() =>
        AreDependenciesCurrent()
            ? Finish(NavigationRayStatus.Blocked)
            : Finish(NavigationRayStatus.Stale);

    private bool AreDependenciesCurrent()
    {
        if (_request.World.ChangeSequence != _worldChangeSequence)
            return false;
        NavigationWorldGraph expected = _request.ExpectedGraph;
        NavigationWorldGraph current = _request.Store.Current;
        if (!current.AreaCatalog.TryGet(
                _request.AreaPolicy.Key,
                out NavigationAreaPolicy currentPolicy)
            || !currentPolicy.ContentEquals(_request.AreaPolicy))
        {
            return false;
        }
        NavigationDependencyWorkspace dependencies = _workspace.Dependencies;
        for (int i = 0; i < dependencies.ComponentCount; i++)
        {
            NavigationSurfaceComponentKey key = dependencies.Components[i];
            expected.TryGetComponentDependency(key, out GraphComponentDependency prior);
            if (!current.TryGetComponentDependency(key, out GraphComponentDependency next)
                || !prior.Equals(next))
            {
                return false;
            }
        }
        for (int i = 0; i < dependencies.PageCount; i++)
        {
            GraphPageDependencyAddress address = dependencies.Pages[i];
            expected.TryGetPageDependency(address, out GraphPageDependency prior);
            bool found = current.TryGetPageDependency(
                address,
                out GraphPageDependency next);
            if (!IsPageDependencyCurrent(found, prior, next))
            {
                return false;
            }
        }
        return _request.World.ChangeSequence == _worldChangeSequence;
    }

    private bool TryConsumeLookup(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        int count)
    {
        bool consumed = useGuideMeter
            ? guideMeter.TryConsumeCurrentNodeLookupProbes(count)
            : queryMeter!.TryConsumeLookupProbes(count);
        _meterBlocked |= !consumed;
        return consumed;
    }

    private bool TryConsumeConnectionLeg(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        bool consumed = useGuideMeter
            ? guideMeter.TryConsumeCursorLegScans(1)
            : queryMeter!.TryConsumeConnectionLegs(1);
        _meterBlocked |= !consumed;
        return consumed;
    }

    private bool TryConsumePortal(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        bool consumed = useGuideMeter
            ? guideMeter.TryConsumePortalChecks(1)
            : queryMeter == null
                || !queryMeter.IsGuideSampleBridge
                || queryMeter.TryConsumeGuidePortalChecks(1);
        _meterBlocked |= !consumed;
        return consumed;
    }

    private bool TryConsumePrism(
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        bool consumed = useGuideMeter
            ? guideMeter.TryConsumePrismChecks(1)
            : queryMeter == null
                || !queryMeter.IsGuideSampleBridge
                || queryMeter.TryConsumeGuidePrismChecks(1);
        _meterBlocked |= !consumed;
        return consumed;
    }

    private NavigationRayStatus Finish(
        NavigationRayStatus status,
        bool preserveResult = false)
    {
        Status = status;
        if (!preserveResult)
            Result = new NavigationRayResult(status, default, default, default, false);
        ReleaseExplicitConnectionReferences();
        _request = default;
        _evaluator = default;
        _begun = false;
        return status;
    }

    private void ReleaseExplicitConnectionReferences()
    {
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        for (int ordinal = 0; ordinal < _workspace.TraceIntervals.Count; ordinal++)
        {
            if (records[ordinal].IncomingExplicitConnection != null)
                records[ordinal].IncomingExplicitConnection = null!;
        }
    }
}
