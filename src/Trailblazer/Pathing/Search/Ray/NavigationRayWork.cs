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

    internal void Begin(in NavigationRayRequest request)
    {
        Reset();
        _request = request;
        _evaluator = new TraversalEvaluator(
            request.ExpectedGraph,
            request.Profile,
            request.AreaPolicy,
            request.Intent.CurrentMedium == TraversalMedium.Unknown
                ? TraversalMedium.Solid
                : request.Intent.CurrentMedium);
        Result = default;
        Status = NavigationRayStatus.Pending;
        _worldChangeSequence = 0;
        _meterBlocked = false;
        _begun = true;
    }

    internal void Reset()
    {
        if (_begun)
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
            : checked((long)gridLimit + addressLimit);
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
        bool consumed = useGuideMeter
            ? guideMeter.TryConsumeCurrentNodeLookupProbes(
                    checked(report.GridCandidateCount + report.AddressCandidateCount))
                && guideMeter.TryConsumeTraceIntervals(traceIntervals)
            : queryMeter!.TryConsumeLookupProbes(report.GridCandidateCount)
                && queryMeter.TryConsumeCoveredVoxelIntervals(report.AddressCandidateCount)
                && queryMeter.TryConsumeTraceIntervals(traceIntervals);
        if (!consumed)
        {
            return NavigationRayStatus.BudgetExceeded;
        }
        if (before != after)
            return NavigationRayStatus.Stale;
        _worldChangeSequence = after;

        return report.Status switch
        {
            GridTraceIntervalStatus.Complete => NavigationRayStatus.Pending,
            GridTraceIntervalStatus.UnrepresentableGeometry => NavigationRayStatus.CostOverflow,
            GridTraceIntervalStatus.GridCandidateLimitExceeded =>
                gridLimit < _workspace.MapCapacity
                    ? NavigationRayStatus.BudgetExceeded
                    : NavigationRayStatus.CapacityExceeded,
            GridTraceIntervalStatus.AddressCandidateLimitExceeded =>
                addressLimit < _workspace.CoveredAddressCapacity
                    ? NavigationRayStatus.BudgetExceeded
                    : NavigationRayStatus.CapacityExceeded,
            GridTraceIntervalStatus.CandidateWorkLimitExceeded =>
                NavigationRayStatus.BudgetExceeded,
            _ => outputLimit < _workspace.TraceIntervalCapacity
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
            if (!graph.TryGetMap(mapId, out NavigationMapInstance? instance)
                || instance == null
                || !instance.GridIdentity.Matches(
                    interval.Cell.WorldSpawnToken,
                    interval.Cell.GridIndex,
                    interval.Cell.GridSpawnToken)
                || instance.GridIdentity.ConfigurationKey != interval.ConfigurationKey
                || instance.GridHighWaterSequence != interval.GridHighWaterSequence)
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
            if (record.State != NavigationRayChainRecordState.Unreached
                || (permitsStartPrefix
                    ? interval.TEnter != firstSeedParameter
                    : interval.TEnter > Fixed64.Zero || interval.TExit < Fixed64.Zero)
                || !CanSeed(
                    ordinal,
                    finishComponent,
                    ref guideMeter,
                    useGuideMeter))
            {
                if (_meterBlocked)
                    return Finish(NavigationRayStatus.BudgetExceeded);
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
                if (!permitsDestinationSuffix
                    && CanFinish(
                        ordinal,
                        ref guideMeter,
                        useGuideMeter))
                    return FinishSuccess(ordinal);
                if (_meterBlocked)
                    return Finish(NavigationRayStatus.BudgetExceeded);

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
            return FinishDestinationSuffix(ref guideMeter, useGuideMeter);
        return FinishBlocked();
    }

    private NavigationRayStatus FinishDestinationSuffix(
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        Fixed64 farthestExit = Fixed64.MinValue;
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        int count = _workspace.TraceIntervals.Count;
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            if (records[ordinal].State == NavigationRayChainRecordState.Expanded
                && _workspace.TraceIntervals[ordinal].TExit > farthestExit)
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
            if (CanFinish(ordinal, ref guideMeter, useGuideMeter))
                return FinishSuccess(ordinal);
            if (_meterBlocked)
                return Finish(NavigationRayStatus.BudgetExceeded);
        }
        return FinishBlocked();
    }

    private NavigationRayStatus Expand(
        int sourceOrdinal,
        NavigationWorkMeter? queryMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        NavigationWorldGraph graph = _request.ExpectedGraph;
        NavigationRayChainRecord[] records = _workspace.ChainRecords;
        ref NavigationRayChainRecord sourceRecord = ref records[sourceOrdinal];
        if (!TryGetIncomingPortal(
                sourceOrdinal,
                ref guideMeter,
                useGuideMeter,
                out GridNavigationPortal incomingPortal)
            || !graph.TryGetNodeAddress(sourceRecord.Node, out NavigationCellAddress sourceAddress)
            || !graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism))
        {
            return _meterBlocked
                ? NavigationRayStatus.BudgetExceeded
                : NavigationRayStatus.Stale;
        }

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
            if (!AllowsEdge(sourceAddress, edge, edgeOrdinal))
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
                if (evaluation == TraversalEvaluationStatus.Stale)
                    return NavigationRayStatus.Stale;
                if (evaluation != TraversalEvaluationStatus.Passable)
                    continue;
                if (!TryConsumePortal(ref guideMeter, useGuideMeter)
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
                if (sourceParameter < sourceRecord.ArrivalParameter
                    || sourceParameter < sourceInterval.TEnter
                    || sourceParameter > sourceInterval.TExit)
                {
                    continue;
                }
                targetOrdinal = FindTarget(edge.Target, targetParameter);
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
                var sourceLeg = new FixedSegment(sourcePoint, portalPoint);
                if (sourceRecord.IncomingExplicitConnection != null
                    && !sourceLeg.Contains(
                        sourceRecord.IncomingExplicitConnection.Definition.ExitAnchor))
                {
                    continue;
                }
                if (!TryConsumePrism(ref guideMeter, useGuideMeter)
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
            if (targetRecord.State != NavigationRayChainRecordState.Unreached
                && !IsEarlierContinuation(
                    targetRecord,
                    targetParameter,
                    incomingExplicit))
            {
                continue;
            }
            targetRecord.PredecessorOrdinal = sourceOrdinal;
            targetRecord.RootOrdinal = sourceRecord.RootOrdinal;
            targetRecord.ArrivalParameter = targetParameter;
            targetRecord.TraversalCost = traversalCost;
            targetRecord.IncomingExplicitConnection = incomingExplicit;
            targetRecord.IsSemanticCostNeutral = sourceRecord.IsSemanticCostNeutral
                && IsSemanticCostNeutral(edge);
            targetRecord.State = NavigationRayChainRecordState.Ready;
        }
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
            if (status == TraversalExplicitEdgeStatus.CostOverflow
                || status == TraversalExplicitEdgeStatus.Stale)
            {
                return MapExplicitStatus(status);
            }
            bool dependencyRecorded = !evidence.DependencyNode.IsValid
                || TryRecordDependencyNode(evidence.DependencyNode);
            if (!dependencyRecorded)
                return NavigationRayStatus.CapacityExceeded;
            if (status == TraversalExplicitEdgeStatus.Impassable)
                return NavigationRayStatus.Blocked;

            cost = evidence.Cost;
            if (!geometryPassable)
                continue;
            if (!TryConsumePortal(ref guideMeter, useGuideMeter))
            {
                geometryPassable = false;
                continue;
            }
            if (!GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                    evidence.SourcePrism,
                    evidence.TargetPrism,
                    evidence.Portal,
                    _request.Start,
                    _request.End,
                    _request.Profile.Shape.Radius,
                    _request.Profile.Shape.Height,
                    out Fixed64 sourceParameter,
                    out Fixed64 nextParameter)
                || sourceParameter < currentParameter
                || !ContainsParameter(currentOrdinal, sourceParameter))
            {
                geometryPassable = false;
                continue;
            }
            int nextOrdinal = FindMapped(evidence.DependencyNode, nextParameter);
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
                if (!firstLeg.Contains(connection.EntryAnchor)
                    || (prior != null
                        && (!firstLeg.Contains(prior.Definition.ExitAnchor)
                            || !AreOrderedAlongRay(
                                prior.Definition.ExitAnchor,
                                connection.EntryAnchor))))
                {
                    geometryPassable = false;
                    continue;
                }
            }
            if (!TryConsumePrism(ref guideMeter, useGuideMeter))
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
        Vector3d finalPortalPoint = Vector3d.Lerp(
            _request.Start,
            _request.End,
            currentParameter);
        if (!new FixedSegment(finalPortalPoint, _request.End).Contains(
                connection.ExitAnchor)
            || _workspace.ChainRecords[currentOrdinal].Node != edge.Target
            || _workspace.ChainRecords[currentOrdinal].State
                == NavigationRayChainRecordState.Unavailable)
        {
            return NavigationRayStatus.Blocked;
        }
        targetOrdinal = currentOrdinal;
        targetParameter = currentParameter;
        return NavigationRayStatus.Success;
    }

    private static NavigationRayStatus MapExplicitStatus(
        TraversalExplicitEdgeStatus status) => status switch
        {
            TraversalExplicitEdgeStatus.Passable => NavigationRayStatus.Success,
            TraversalExplicitEdgeStatus.CostOverflow => NavigationRayStatus.CostOverflow,
            TraversalExplicitEdgeStatus.Stale => NavigationRayStatus.Stale,
            _ => NavigationRayStatus.Blocked
        };

    private bool AreOrderedAlongRay(Vector3d first, Vector3d second) =>
        TryCompareAlongRay(first, second, out int comparison) && comparison <= 0;

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
        return TryCompareAlongRay(candidate, prior, out int comparison)
            && comparison < 0;
    }

    private bool TryCompareAlongRay(
        Vector3d first,
        Vector3d second,
        out int comparison)
    {
        if (_request.End.X != _request.Start.X)
        {
            comparison = _request.End.X > _request.Start.X
                ? first.X.CompareTo(second.X)
                : second.X.CompareTo(first.X);
            return true;
        }
        if (_request.End.Y != _request.Start.Y)
        {
            comparison = _request.End.Y > _request.Start.Y
                ? first.Y.CompareTo(second.Y)
                : second.Y.CompareTo(first.Y);
            return true;
        }
        if (_request.End.Z != _request.Start.Z)
        {
            comparison = _request.End.Z > _request.Start.Z
                ? first.Z.CompareTo(second.Z)
                : second.Z.CompareTo(first.Z);
            return true;
        }
        comparison = 0;
        return false;
    }

    private int FindMapped(NavigationNodeRef target, Fixed64 parameter)
    {
        for (int ordinal = 0; ordinal < _workspace.TraceIntervals.Count; ordinal++)
        {
            GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
            if (interval.TEnter > parameter)
                break;
            ref NavigationRayChainRecord record = ref _workspace.ChainRecords[ordinal];
            if (record.State != NavigationRayChainRecordState.Unavailable
                && record.Node == target
                && interval.TExit >= parameter)
            {
                return ordinal;
            }
        }
        return -1;
    }

    private bool ContainsParameter(int ordinal, Fixed64 parameter)
    {
        GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
        return interval.TEnter <= parameter && interval.TExit >= parameter;
    }

    private int FindTarget(NavigationNodeRef target, Fixed64 parameter)
    {
        for (int ordinal = 0; ordinal < _workspace.TraceIntervals.Count; ordinal++)
        {
            GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
            if (interval.TEnter > parameter)
                break;
            ref NavigationRayChainRecord record = ref _workspace.ChainRecords[ordinal];
            if (record.State != NavigationRayChainRecordState.Unavailable
                && record.Node == target
                && interval.TExit >= parameter)
            {
                return ordinal;
            }
        }
        return -1;
    }

    private bool CanSeed(
        int ordinal,
        NavigationSurfaceComponentKey finishComponent,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
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
            if (!_request.ExpectedGraph.TryGetNodeAddress(node, out address))
                return false;
        }
        else
        {
            if (!_request.ExpectedGraph.TryGetNodeAddress(node, out address)
                || address != constraint.SourceAddress)
            {
                return false;
            }
            if (_request.EndpointAllowance == NavigationRayEndpointAllowance.StartPrefix)
                return true;
        }
        if (!_request.ExpectedGraph.TryGetSeamPrism(address, out GridCellPrism prism))
        {
            return false;
        }
        return TryConsumePrism(ref guideMeter, useGuideMeter)
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
            return _request.ExpectedGraph.TryGetNodeAddress(
                    _workspace.ChainRecords[ordinal].Node,
                    out NavigationCellAddress seedAddress)
                && _request.ExpectedGraph.TryGetSurfaceComponent(
                    seedAddress,
                    out NavigationSurfaceComponentKey actual,
                    out _)
                && actual == finishComponent;
        }
        return _request.ExpectedGraph.TryGetNodeAddress(
                _workspace.ChainRecords[ordinal].Node,
                out NavigationCellAddress address)
            && address == constraint.SourceAddress;
    }

    private bool AllowsEdge(
        NavigationCellAddress source,
        in NavigationGraphEdge edge,
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
            && _request.ExpectedGraph.TryGetNodeAddress(
                edge.Target,
                out NavigationCellAddress target)
            && target == constraint.TargetAddress;
    }

    private bool CanFinish(
        int ordinal,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        GridTraceInterval interval = _workspace.TraceIntervals[ordinal];
        ref NavigationRayChainRecord record = ref _workspace.ChainRecords[ordinal];
        bool permitsDestinationSuffix = _request.EndpointAllowance
            == NavigationRayEndpointAllowance.DestinationSuffix;
        if ((!permitsDestinationSuffix
                && (interval.TEnter > Fixed64.One || interval.TExit < Fixed64.One))
            || !_request.ExpectedGraph.TryGetNodeAddress(
                record.Node,
                out NavigationCellAddress address)
            || (_request.ChainConstraint.Kind
                    == NavigationRayChainConstraintKind.FinishAddress
                && address != _request.ChainConstraint.TargetAddress)
            || (_request.ChainConstraint.Kind
                    == NavigationRayChainConstraintKind.SelectedEdge
                && (record.PredecessorOrdinal < 0
                    || address != _request.ChainConstraint.TargetAddress))
            || !TryGetIncomingPortal(
                ordinal,
                ref guideMeter,
                useGuideMeter,
                out GridNavigationPortal incomingPortal)
            || !_request.ExpectedGraph.TryGetSeamPrism(address, out GridCellPrism prism))
        {
            return false;
        }
        Vector3d arrival = Vector3d.Lerp(
            _request.Start,
            _request.End,
            record.ArrivalParameter);
        if (record.IncomingExplicitConnection != null
            && !new FixedSegment(arrival, _request.End).Contains(
                record.IncomingExplicitConnection.Definition.ExitAnchor))
        {
            return false;
        }
        return TryConsumePrism(ref guideMeter, useGuideMeter)
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
            if (!TryConsumePortal(ref guideMeter, useGuideMeter))
            {
                portal = default;
                return false;
            }
            var portals = record.IncomingExplicitConnection.NavigationPortals;
            portal = portals[portals.Count - 1];
            return portal.IsValid;
        }
        NavigationRayChainRecord predecessor =
            _workspace.ChainRecords[record.PredecessorOrdinal];
        NavigationWorldGraph graph = _request.ExpectedGraph;
        if (TryConsumePortal(ref guideMeter, useGuideMeter)
            && graph.TryGetNodeAddress(predecessor.Node, out NavigationCellAddress source)
            && graph.TryGetNodeAddress(record.Node, out NavigationCellAddress target)
            && graph.TryGetSeamPrism(source, out GridCellPrism sourcePrism)
            && graph.TryGetSeamPrism(target, out GridCellPrism targetPrism)
            && GridCellGeometry.TryCreateNavigationPortal(sourcePrism, targetPrism, out portal))
        {
            return true;
        }
        portal = default;
        return false;
    }

    private bool IsSemanticCostNeutral(in NavigationGraphEdge edge)
    {
        if (!_request.ExpectedGraph.TryGetNodeState(edge.Target, out NavigationNodeState state)
            || !_request.AreaPolicy.TryGetRule(state.Cell.Area, out NavigationAreaRule rule))
        {
            return false;
        }
        return state.Cell.EnterCost == Fixed64.Zero
            && rule.AdditionalEnterCost == Fixed64.Zero
            && (edge.Kind != NavigationGraphEdgeKind.Explicit
                || edge.ExplicitConnection.Definition.AdditionalCost == Fixed64.Zero);
    }

    private bool TryRecordDependencyNode(NavigationNodeRef node)
    {
        NavigationWorldGraph graph = _request.ExpectedGraph;
        if (!graph.TryGetNodeAddress(node, out NavigationCellAddress address)
            || !_workspace.Dependencies.TryRecordPage(
                address.MapId,
                node.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return false;
        }
        return graph.TryGetSurfaceComponent(
                address,
                out NavigationSurfaceComponentKey component,
                out _)
            && _workspace.Dependencies.TryRecordComponent(component);
    }

    private NavigationRayStatus FinishSuccess(int ordinal)
    {
        NavigationRayChainRecord record = _workspace.ChainRecords[ordinal];
        NavigationRayChainRecord root = _workspace.ChainRecords[record.RootOrdinal];
        if (!_request.ExpectedGraph.TryGetNodeAddress(root.Node, out NavigationCellAddress start)
            || !_request.ExpectedGraph.TryGetNodeAddress(record.Node, out NavigationCellAddress end)
            || !AreDependenciesCurrent())
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
                out NavigationAreaPolicy? currentPolicy)
            || currentPolicy == null
            || !currentPolicy.ContentEquals(_request.AreaPolicy))
        {
            return false;
        }
        NavigationDependencyWorkspace dependencies = _workspace.Dependencies;
        for (int i = 0; i < dependencies.ComponentCount; i++)
        {
            NavigationSurfaceComponentKey key = dependencies.Components[i];
            if (!expected.TryGetComponentDependency(key, out GraphComponentDependency prior)
                || !current.TryGetComponentDependency(key, out GraphComponentDependency next)
                || !prior.Equals(next))
            {
                return false;
            }
        }
        for (int i = 0; i < dependencies.PageCount; i++)
        {
            GraphPageDependencyAddress address = dependencies.Pages[i];
            if (!expected.TryGetPageDependency(address, out GraphPageDependency prior)
                || !current.TryGetPageDependency(address, out GraphPageDependency next)
                || !prior.Equals(next))
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
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        bool consumed = !useGuideMeter || guideMeter.TryConsumePortalChecks(1);
        _meterBlocked |= !consumed;
        return consumed;
    }

    private bool TryConsumePrism(
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter)
    {
        bool consumed = !useGuideMeter || guideMeter.TryConsumePrismChecks(1);
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
