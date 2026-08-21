//=======================================================================
// NavigationTraversalEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using NavigationTransitionEdgeStatus = Trailblazer.Pathing.NavigationTraversalEvaluationStatus;
using NavigationVolumeEdgeStatus = Trailblazer.Pathing.NavigationTraversalEvaluationStatus;

namespace Trailblazer.Pathing;

/// <summary>Identifies which existing authority produced one evaluated traversal edge.</summary>
internal enum NavigationTraversalEdgeKind : byte
{
    Surface = 0,
    Volume = 1,
    Transition = 2
}

/// <summary>Reports bounded progress through canonical outgoing traversal edges.</summary>
internal enum NavigationTraversalEdgeAdvanceStatus : byte
{
    Pending = 0,
    Edge = 1,
    Complete = 2,
    Blocked = 3,
    BudgetExceeded = 4,
    CapacityExceeded = 5,
    CostOverflow = 6,
    Stale = 7
}

/// <summary>Dispatches one medium-state's outgoing edges to the existing evaluators.</summary>
internal struct NavigationTraversalEdgeEnumerator
{
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationMediumStateRef _source;
    private NavigationSurfaceEdgeEnumerator _surfaceEdges;
    private TraversalEvaluator _surfaceEvaluator;
    private NavigationGraphEdge _surfaceEdge;
    private NavigationSurfaceEdgeRouteWork _surfaceRouteWork;
    private NavigationVolumeEdgeEvaluator _volumeEvaluator;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _volumeSeams;
    private NavigationAutomaticSeamRef _volumeSeamLookahead;
    private NavigationTransitionPage.Enumerator _transitions;
    private NavigationTransitionEdgeEvaluator _transitionEvaluator;
    private NavigationPublishedTransition _definitionLookahead;
    private int _surfaceOrdinal;
    private int _volumeEdgeOrdinal;
    private int _transitionOrdinal;
    private uint _visitedVolumeDirections;
    private bool _surfaceRouteActive;
    private int _emittedSurfaceOrdinal;
    private bool _hasVolumeSeamLookahead;
    private bool _volumeSeamsComplete;
    private bool _allowTransitions;
    private bool _baseComplete;
    private bool _transitionStarted;
    private bool _hasPendingTransition;
    private bool _pendingIsRule;
    private bool _explicitTransitionsComplete;
    private bool _hasDefinitionLookahead;
    private bool _definitionNeedsDebit;
    private bool _hasLastRule;
    private NavigationCellAddress _lastRuleTargetAddress;
    private TraversalMedium _lastRuleTargetMedium;
    private TraversalTransitionType _lastRuleType;
    private string _lastRuleId;
    private TraversalTransitionRule _scannedRule;
    private NavigationMediumStateRef _scannedRuleTarget;
    private NavigationCellAddress _scannedRuleAddress;
    private int _ruleScanIndex;
    private int _ruleContactDirection;
    private bool _ruleScanActive;
    private bool _hasScannedRule;

    internal NavigationTraversalEdgeEnumerator(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationMediumStateRef source,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        NavigationRayWorkspace workspace,
        bool allowTransitions,
        int emittedSurfaceOrdinal)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        _graph = source.IsValid ? graph : null;
        _source = source;
        _surfaceEdges = source.Medium == TraversalMedium.Solid
            ? graph.EnumerateSurfaceEdges(source.Node)
            : default;
        _surfaceEvaluator = source.Medium == TraversalMedium.Solid
            ? new TraversalEvaluator(graph, profile, areaPolicy, TraversalMedium.Solid)
            : default;
        _surfaceEdge = default;
        _surfaceRouteWork = default;
        _volumeEvaluator = source.Medium == TraversalMedium.Gas
            || source.Medium == TraversalMedium.Liquid
                ? new NavigationVolumeEdgeEvaluator(
                    world,
                    graph,
                    profile,
                    areaPolicy,
                    source.Medium,
                    workspace)
                : default;
        _volumeSeams = (source.Medium == TraversalMedium.Gas
                || source.Medium == TraversalMedium.Liquid)
            && graph.TryGetNodeAddress(source.Node, out NavigationCellAddress volumeAddress)
                ? graph.AutomaticSeams.GetActiveEndpointEnumerator(volumeAddress)
                : default;
        _volumeSeamLookahead = default;
        _transitions = allowTransitions
            ? graph.EnumerateOutgoingTransitionCandidates(source)
            : default;
        _transitionEvaluator = allowTransitions
            ? new NavigationTransitionEdgeEvaluator(
                world,
                graph,
                profile,
                areaPolicy,
                workspace)
            : default;
        _definitionLookahead = default;
        _surfaceOrdinal = -1;
        _volumeEdgeOrdinal = -1;
        _transitionOrdinal = -1;
        _visitedVolumeDirections = 0;
        _surfaceRouteActive = false;
        _emittedSurfaceOrdinal = emittedSurfaceOrdinal;
        _hasVolumeSeamLookahead = false;
        _volumeSeamsComplete = source.Medium != TraversalMedium.Gas
            && source.Medium != TraversalMedium.Liquid;
        _allowTransitions = allowTransitions;
        _baseComplete = false;
        _transitionStarted = false;
        _hasPendingTransition = false;
        _pendingIsRule = false;
        _explicitTransitionsComplete = !allowTransitions;
        _hasDefinitionLookahead = false;
        _definitionNeedsDebit = false;
        _hasLastRule = false;
        _lastRuleTargetAddress = default;
        _lastRuleTargetMedium = default;
        _lastRuleType = default;
        _lastRuleId = string.Empty;
        _scannedRule = default;
        _scannedRuleTarget = default;
        _scannedRuleAddress = default;
        _ruleScanIndex = 0;
        _ruleContactDirection = -1;
        _ruleScanActive = false;
        _hasScannedRule = false;
        CurrentTarget = default;
        CurrentKind = default;
        CurrentOrdinal = -1;
        CurrentCost = default;
        CurrentSurfaceEdge = default;
        CurrentVolumeIsShortcut = false;
        CurrentTransitionId = string.Empty;
        CurrentTransitionOwnerMapId = string.Empty;
        CurrentTransitionType = default;
        CurrentTransitionHints = default;
        CurrentTransitionIdentityKind = default;
        CurrentTransitionSourceAction = default;
        CurrentTransitionDestinationAction = default;
        RequiresWorldStamp = source.Medium == TraversalMedium.Gas
            || source.Medium == TraversalMedium.Liquid;
    }

    internal NavigationMediumStateRef CurrentTarget { get; private set; }

    internal NavigationTraversalEdgeKind CurrentKind { get; private set; }

    internal int CurrentOrdinal { get; private set; }

    internal Fixed64 CurrentCost { get; private set; }

    internal NavigationGraphEdge CurrentSurfaceEdge { get; private set; }

    internal bool CurrentVolumeIsShortcut { get; private set; }

    internal string CurrentTransitionId { get; private set; }

    internal string CurrentTransitionOwnerMapId { get; private set; }

    internal TraversalTransitionType CurrentTransitionType { get; private set; }

    internal TraversalTransitionLocomotionHints CurrentTransitionHints { get; private set; }

    internal NavigationTransitionIdentityKind CurrentTransitionIdentityKind { get; private set; }

    internal Vector3d CurrentTransitionSourceAction { get; private set; }

    internal Vector3d CurrentTransitionDestinationAction { get; private set; }

    internal bool RequiresWorldStamp { get; private set; }

    internal bool RequiresConnectionProgress => _surfaceRouteActive
        && _surfaceEdge.Kind == NavigationGraphEdgeKind.Explicit;

    internal bool HasCurrentSurfacePoint => _surfaceRouteWork.HasCurrentPoint;

    internal NavigationAStarGuidePoint CurrentSurfacePoint =>
        _surfaceRouteWork.CurrentPoint;

    internal bool CurrentSurfacePointIsTargetFootAnchor =>
        _surfaceRouteWork.CurrentPointIsTargetFootAnchor;

    internal void ConsumeCurrentSurfacePoint() => _surfaceRouteWork.ConsumePoint();

    internal NavigationTraversalEdgeAdvanceStatus AdvanceOne(
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        ref int edgeStepRemaining,
        ref int connectionStepRemaining)
    {
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        SwiftThrowHelper.ThrowIfNegative(edgeStepRemaining, nameof(edgeStepRemaining));
        SwiftThrowHelper.ThrowIfNegative(
            connectionStepRemaining,
            nameof(connectionStepRemaining));
        if (_graph == null)
            return Complete();
        if (_baseComplete)
            return AdvanceTransition(meter, dependencies, ref edgeStepRemaining);
        if (_source.Medium == TraversalMedium.Gas
            || _source.Medium == TraversalMedium.Liquid)
        {
            return AdvanceVolume(meter, dependencies, ref edgeStepRemaining);
        }
        if (_source.Medium != TraversalMedium.Solid)
            return Complete();

        if (_surfaceRouteActive)
        {
            return AdvanceSurfaceRoute(
                meter,
                dependencies,
                ref connectionStepRemaining);
        }

        NavigationSurfaceEdgeAdvanceStatus status = _surfaceEdges.AdvanceOne(
            meter,
            ref edgeStepRemaining);
        if (status == NavigationSurfaceEdgeAdvanceStatus.Complete)
        {
            _baseComplete = true;
            return AdvanceTransition(meter, dependencies, ref edgeStepRemaining);
        }
        if (status == NavigationSurfaceEdgeAdvanceStatus.Blocked)
        {
            return edgeStepRemaining == 0 && meter.RemainingEvaluatedEdges > 0
                ? NavigationTraversalEdgeAdvanceStatus.Blocked
                : NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
        }
        if (status != NavigationSurfaceEdgeAdvanceStatus.Edge)
            return NavigationTraversalEdgeAdvanceStatus.Pending;

        _surfaceOrdinal = _surfaceEdges.CurrentOrdinal;
        _surfaceEdge = _surfaceEdges.Current;
        if (!_graph!.TryGetNodeAddress(
                _surfaceEdge.Target,
                out NavigationCellAddress targetAddress)
            || !dependencies.TryRecordPage(
                targetAddress.MapId,
                _surfaceEdge.Target.CellSlot / NavigationSemanticPage.SlotCount))
        {
            return NavigationTraversalEdgeAdvanceStatus.CapacityExceeded;
        }
        if (_emittedSurfaceOrdinal >= 0)
        {
            if (_surfaceOrdinal < _emittedSurfaceOrdinal)
                return NavigationTraversalEdgeAdvanceStatus.Pending;
            if (_surfaceOrdinal > _emittedSurfaceOrdinal)
                return NavigationTraversalEdgeAdvanceStatus.Stale;
        }
        NavigationSurfaceEdgeRouteStatus begin = _surfaceRouteWork.Begin(
            _surfaceEvaluator,
            _source.Node,
            _surfaceEdge,
            emitPoints: _emittedSurfaceOrdinal >= 0);
        _surfaceRouteActive = begin == NavigationSurfaceEdgeRouteStatus.Pending;
        return _surfaceRouteActive
            ? AdvanceSurfaceRoute(
                meter,
                dependencies,
                ref connectionStepRemaining)
            : MapSurfaceRoute(begin);
    }

    private NavigationTraversalEdgeAdvanceStatus AdvanceVolume(
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        ref int edgeStepRemaining)
    {
        while (TrySelectNextVolumeCandidate(
            out int directionOrdinal,
            out NavigationMediumStateRef target,
            out bool isPrimary,
            out NavigationAutomaticSeamRef seam,
            out bool hasSeam))
        {
            if (edgeStepRemaining == 0)
            {
                return meter.RemainingEvaluatedEdges == 0
                    ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
                    : NavigationTraversalEdgeAdvanceStatus.Blocked;
            }
            if (!meter.TryConsumeEvaluatedEdges(1))
            {
                return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
            }
            edgeStepRemaining--;
            if (hasSeam)
            {
                _volumeSeamLookahead = default;
                _hasVolumeSeamLookahead = false;
            }
            else
            {
                _visitedVolumeDirections |= 1U << directionOrdinal;
            }
            _volumeEdgeOrdinal++;
            NavigationVolumeEdgeStatus status = _volumeEvaluator.Evaluate(
                _source,
                target,
                isPrimary,
                seam,
                hasSeam,
                meter,
                dependencies,
                out Fixed64 cost);
            if (status == NavigationVolumeEdgeStatus.BudgetExceeded)
                return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
            if (status == NavigationVolumeEdgeStatus.CapacityExceeded)
                return NavigationTraversalEdgeAdvanceStatus.CapacityExceeded;
            if (status == NavigationVolumeEdgeStatus.CostOverflow)
                return NavigationTraversalEdgeAdvanceStatus.CostOverflow;
            if (status == NavigationVolumeEdgeStatus.Stale)
                return NavigationTraversalEdgeAdvanceStatus.Stale;
            if (status != NavigationVolumeEdgeStatus.Passable)
                continue;

            CurrentTarget = target;
            CurrentKind = NavigationTraversalEdgeKind.Volume;
            CurrentSurfaceEdge = default;
            CurrentVolumeIsShortcut = !isPrimary;
            CurrentOrdinal = _volumeEdgeOrdinal;
            CurrentCost = cost;
            return NavigationTraversalEdgeAdvanceStatus.Edge;
        }
        _baseComplete = true;
        return AdvanceTransition(meter, dependencies, ref edgeStepRemaining);
    }

    private NavigationTraversalEdgeAdvanceStatus AdvanceTransition(
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        ref int edgeStepRemaining)
    {
        if (!_allowTransitions)
            return Complete();
        if (!_transitionStarted)
        {
            dependencies.RecordTransitionDependency();
            if (!_graph!.TryGetNodeAddress(
                    _source.Node,
                    out NavigationCellAddress sourceAddress)
                || !dependencies.TryRecordPage(
                    sourceAddress.MapId,
                    _source.Node.CellSlot / NavigationSemanticPage.SlotCount))
            {
                return NavigationTraversalEdgeAdvanceStatus.CapacityExceeded;
            }
            _transitionStarted = true;
        }

        if (!_hasPendingTransition)
        {
            NavigationTraversalEdgeAdvanceStatus prepare = PrepareNextTransition(
                meter,
                ref edgeStepRemaining);
            if (prepare != NavigationTraversalEdgeAdvanceStatus.Pending
                || !_hasPendingTransition)
            {
                return prepare;
            }
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        }

        if (edgeStepRemaining == 0)
        {
            return meter.RemainingTransitionPairs == 0
                ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
                : NavigationTraversalEdgeAdvanceStatus.Blocked;
        }
        if (!meter.TryConsumeTransitionPairs(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
        edgeStepRemaining--;
        NavigationPublishedTransition transition = _definitionLookahead;
        TraversalTransitionRule rule = _scannedRule;
        NavigationMediumStateRef ruleTarget = _scannedRuleTarget;
        bool isRule = _pendingIsRule;
        _hasPendingTransition = false;
        _pendingIsRule = false;
        _hasScannedRule = false;
        _scannedRule = default;
        _scannedRuleTarget = default;
        _scannedRuleAddress = default;
        if (!isRule)
        {
            _definitionLookahead = default;
            _hasDefinitionLookahead = false;
        }
        _transitionOrdinal++;
        NavigationTransitionEdgeStatus status;
        NavigationMediumStateRef target;
        NavigationTransitionEdgeEvidence evidence;
        if (isRule)
        {
            target = ruleTarget;
            status = _transitionEvaluator.EvaluateRule(
                _source,
                target,
                rule,
                meter,
                dependencies,
                out evidence);
        }
        else
        {
            status = _transitionEvaluator.EvaluateDefinition(
                _source,
                transition,
                meter,
                dependencies,
                out target,
                out evidence);
        }
        if (status == NavigationTransitionEdgeStatus.BudgetExceeded)
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
        if (status == NavigationTransitionEdgeStatus.CapacityExceeded)
            return NavigationTraversalEdgeAdvanceStatus.CapacityExceeded;
        if (status == NavigationTransitionEdgeStatus.CostOverflow)
            return NavigationTraversalEdgeAdvanceStatus.CostOverflow;
        if (status == NavigationTransitionEdgeStatus.Stale)
            return NavigationTraversalEdgeAdvanceStatus.Stale;
        if (status != NavigationTransitionEdgeStatus.Passable)
            return NavigationTraversalEdgeAdvanceStatus.Pending;

        CurrentTarget = target;
        CurrentKind = NavigationTraversalEdgeKind.Transition;
        CurrentSurfaceEdge = default;
        CurrentVolumeIsShortcut = false;
        CurrentOrdinal = (_source.Medium == TraversalMedium.Solid
            ? _surfaceOrdinal
            : _volumeEdgeOrdinal) + 1 + _transitionOrdinal;
        CurrentCost = evidence.Cost;
        CurrentTransitionId = isRule ? rule.Id : transition.Owner.TransitionId;
        CurrentTransitionOwnerMapId = isRule ? string.Empty : transition.Owner.MapId;
        CurrentTransitionType = isRule ? rule.Type : transition.Definition.Type;
        CurrentTransitionHints = isRule
            ? rule.LocomotionHints
            : transition.Definition.LocomotionHints;
        CurrentTransitionIdentityKind = isRule
            ? NavigationTransitionIdentityKind.Rule
            : NavigationTransitionIdentityKind.Definition;
        CurrentTransitionSourceAction = evidence.SourceAction;
        CurrentTransitionDestinationAction = evidence.DestinationAction;
        return NavigationTraversalEdgeAdvanceStatus.Edge;
    }

    private NavigationTraversalEdgeAdvanceStatus PrepareNextTransition(
        NavigationWorkMeter meter,
        ref int edgeStepRemaining)
    {
        bool hasDefinition = _hasDefinitionLookahead;
        NavigationPublishedTransition definition = _definitionLookahead;
        if (!hasDefinition && !_explicitTransitionsComplete)
        {
            if (_transitions.MoveNext())
            {
                _definitionLookahead = _transitions.Current;
                MarkWorldDependency(
                    _definitionLookahead.Definition.DestinationMedium);
                _hasDefinitionLookahead = true;
                _definitionNeedsDebit = true;
                hasDefinition = true;
                definition = _definitionLookahead;
            }
            else
            {
                _explicitTransitionsComplete = true;
            }
        }
        if (_definitionNeedsDebit)
        {
            if (!TryConsumeTransitionCandidate(
                    meter,
                    ref edgeStepRemaining,
                    out NavigationTraversalEdgeAdvanceStatus blocked))
            {
                return blocked;
            }
            _definitionNeedsDebit = false;
        }
        if (!_ruleScanActive)
        {
            _ruleScanActive = true;
            _ruleScanIndex = 0;
            _hasScannedRule = false;
            _scannedRule = default;
            _scannedRuleTarget = default;
            _scannedRuleAddress = default;
            _ruleContactDirection = -1;
        }
        while (_ruleScanIndex < _graph!.TransitionRules.Count)
        {
            TraversalTransitionRule candidate = _graph.TransitionRules[_ruleScanIndex];
            MarkWorldDependency(candidate.DestinationMedium);
            if (_ruleContactDirection < 0)
            {
                if (!TryConsumeTransitionCandidate(
                        meter,
                        ref edgeStepRemaining,
                        out NavigationTraversalEdgeAdvanceStatus blocked))
                {
                    return blocked;
                }
                if (candidate.SourceMedium != _source.Medium)
                {
                    CompleteRuleContactScan();
                    continue;
                }
                _ruleContactDirection = 0;
            }
            if (candidate.Scope == TraversalTransitionRuleScope.SameCell)
            {
                if (!TryConsumeTransitionCandidate(
                        meter,
                        ref edgeStepRemaining,
                        out NavigationTraversalEdgeAdvanceStatus blocked))
                {
                    return blocked;
                }
                if (_graph.TryGetNodeAddress(
                        _source.Node,
                        out NavigationCellAddress candidateAddress))
                {
                    ConsiderRuleCandidate(
                        candidate,
                        new NavigationMediumStateRef(
                            _source.Node,
                            candidate.DestinationMedium),
                        candidateAddress,
                        ref _hasScannedRule,
                        ref _scannedRule,
                        ref _scannedRuleTarget,
                        ref _scannedRuleAddress);
                }
                CompleteRuleContactScan();
                continue;
            }
            int primaryCount = _graph.GetPrimaryDirectionCount(_source.Node);
            if (_ruleContactDirection < primaryCount)
            {
                if (!TryConsumeTransitionCandidate(
                        meter,
                        ref edgeStepRemaining,
                        out NavigationTraversalEdgeAdvanceStatus blocked))
                {
                    return blocked;
                }
                int direction = _ruleContactDirection++;
                if (_graph.TryGetPrimaryNeighbor(
                        _source.Node,
                        direction,
                        out NavigationNodeRef candidateNode)
                    && _graph.TryGetNodeAddress(
                        candidateNode,
                        out NavigationCellAddress candidateAddress))
                {
                    ConsiderRuleCandidate(
                        candidate,
                        new NavigationMediumStateRef(
                            candidateNode,
                            candidate.DestinationMedium),
                        candidateAddress,
                        ref _hasScannedRule,
                        ref _scannedRule,
                        ref _scannedRuleTarget,
                        ref _scannedRuleAddress);
                }
                continue;
            }
            if (_ruleContactDirection == primaryCount)
            {
                _volumeSeams = _graph.TryGetNodeAddress(
                        _source.Node,
                        out NavigationCellAddress seamSourceAddress)
                    ? _graph.AutomaticSeams.GetActiveEndpointEnumerator(
                        seamSourceAddress)
                    : default;
                _volumeSeamLookahead = default;
                _hasVolumeSeamLookahead = false;
                _volumeSeamsComplete = false;
                _ruleContactDirection++;
            }
            if (!_hasVolumeSeamLookahead && !_volumeSeamsComplete)
            {
                if (edgeStepRemaining == 0)
                    return NavigationTraversalEdgeAdvanceStatus.Blocked;
                if (_volumeSeams.MoveNext())
                {
                    _volumeSeamLookahead = _volumeSeams.Current;
                    _hasVolumeSeamLookahead = true;
                }
                else
                {
                    _volumeSeamsComplete = true;
                }
            }
            if (!_hasVolumeSeamLookahead)
            {
                CompleteRuleContactScan();
                continue;
            }
            if (!TryConsumeTransitionCandidate(
                    meter,
                    ref edgeStepRemaining,
                    out NavigationTraversalEdgeAdvanceStatus seamBlocked))
            {
                return seamBlocked;
            }
            NavigationAutomaticSeamRef seam = _volumeSeamLookahead;
            _volumeSeamLookahead = default;
            _hasVolumeSeamLookahead = false;
            if (_graph.TryGetNodeRef(
                    seam.Destination,
                    out NavigationNodeRef seamTarget))
            {
                ConsiderRuleCandidate(
                    candidate,
                    new NavigationMediumStateRef(
                        seamTarget,
                        candidate.DestinationMedium),
                    seam.Destination,
                    ref _hasScannedRule,
                    ref _scannedRule,
                    ref _scannedRuleTarget,
                    ref _scannedRuleAddress);
            }
        }

        bool hasRule = _hasScannedRule;
        TraversalTransitionRule selectedRule = _scannedRule;
        NavigationCellAddress selectedRuleAddress = _scannedRuleAddress;
        _ruleScanActive = false;
        _ruleScanIndex = 0;
        _ruleContactDirection = -1;

        if (!hasDefinition && !hasRule)
            return Complete();
        bool selectRule = hasRule && (!hasDefinition
            || CompareTransitionKey(
                selectedRuleAddress,
                selectedRule.DestinationMedium,
                selectedRule.Type,
                NavigationTransitionIdentityKind.Rule,
                selectedRule.Id,
                definition.Definition.Destination,
                definition.Definition.DestinationMedium,
                definition.Definition.Type,
                NavigationTransitionIdentityKind.Definition,
                definition.Owner.TransitionId) < 0);
        _hasPendingTransition = true;
        _pendingIsRule = selectRule;
        if (selectRule)
        {
            _hasLastRule = true;
            _lastRuleTargetAddress = selectedRuleAddress;
            _lastRuleTargetMedium = selectedRule.DestinationMedium;
            _lastRuleType = selectedRule.Type;
            _lastRuleId = selectedRule.Id;
        }
        else
        {
            _definitionLookahead = definition;
        }
        return NavigationTraversalEdgeAdvanceStatus.Pending;
    }

    private void CompleteRuleContactScan()
    {
        _ruleScanIndex++;
        _ruleContactDirection = -1;
        _volumeSeamLookahead = default;
        _hasVolumeSeamLookahead = false;
        _volumeSeamsComplete = true;
    }

    private void ConsiderRuleCandidate(
        TraversalTransitionRule candidate,
        NavigationMediumStateRef candidateTarget,
        NavigationCellAddress candidateAddress,
        ref bool hasRule,
        ref TraversalTransitionRule selectedRule,
        ref NavigationMediumStateRef selectedRuleTarget,
        ref NavigationCellAddress selectedRuleAddress)
    {
        if (!IsAfterLastRule(candidateAddress, candidate)
            || (hasRule
                && CompareTransitionKey(
                    selectedRuleAddress,
                    selectedRule.DestinationMedium,
                    selectedRule.Type,
                    NavigationTransitionIdentityKind.Rule,
                    selectedRule.Id,
                    candidateAddress,
                    candidate.DestinationMedium,
                    candidate.Type,
                    NavigationTransitionIdentityKind.Rule,
                    candidate.Id) <= 0))
        {
            return;
        }
        hasRule = true;
        selectedRule = candidate;
        selectedRuleTarget = candidateTarget;
        selectedRuleAddress = candidateAddress;
    }

    private bool IsAfterLastRule(
        NavigationCellAddress target,
        TraversalTransitionRule rule) => !_hasLastRule
        || CompareTransitionKey(
            _lastRuleTargetAddress,
            _lastRuleTargetMedium,
            _lastRuleType,
            NavigationTransitionIdentityKind.Rule,
            _lastRuleId,
            target,
            rule.DestinationMedium,
            rule.Type,
            NavigationTransitionIdentityKind.Rule,
            rule.Id) < 0;

    internal static int CompareTransitionKey(
        NavigationCellAddress leftAddress,
        TraversalMedium leftMedium,
        TraversalTransitionType leftType,
        NavigationTransitionIdentityKind leftKind,
        string leftId,
        NavigationCellAddress rightAddress,
        TraversalMedium rightMedium,
        TraversalTransitionType rightType,
        NavigationTransitionIdentityKind rightKind,
        string rightId)
    {
        int comparison = leftAddress.CompareTo(rightAddress);
        if (comparison != 0)
            return comparison;
        comparison = ((int)leftMedium).CompareTo((int)rightMedium);
        if (comparison != 0)
            return comparison;
        comparison = ((int)leftType).CompareTo((int)rightType);
        if (comparison != 0)
            return comparison;
        comparison = ((int)leftKind).CompareTo((int)rightKind);
        return comparison != 0 ? comparison : string.CompareOrdinal(leftId, rightId);
    }

    internal static bool TryConsumeTransitionCandidate(
        NavigationWorkMeter meter,
        ref int edgeStepRemaining,
        out NavigationTraversalEdgeAdvanceStatus blocked)
    {
        if (edgeStepRemaining == 0)
        {
            blocked = meter.RemainingTransitionCandidates == 0
                ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
                : NavigationTraversalEdgeAdvanceStatus.Blocked;
            return false;
        }
        if (!meter.TryConsumeTransitionCandidates(1))
        {
            blocked = NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
            return false;
        }
        edgeStepRemaining--;
        blocked = default;
        return true;
    }

    private bool TrySelectNextVolumeCandidate(
        out int directionOrdinal,
        out NavigationMediumStateRef target,
        out bool isPrimary,
        out NavigationAutomaticSeamRef seam,
        out bool hasSeam)
    {
        directionOrdinal = -1;
        target = default;
        isPrimary = false;
        seam = default;
        hasSeam = false;
        NavigationCellAddress selectedAddress = default;
        int directionCount = _graph!.GetCompleteDirectionCount(_source.Node);
        for (int candidateOrdinal = 0; candidateOrdinal < directionCount; candidateOrdinal++)
        {
            if ((_visitedVolumeDirections & (1U << candidateOrdinal)) != 0
                || !_graph.TryGetCompleteNeighbor(
                    _source.Node,
                    candidateOrdinal,
                    out NavigationNodeRef candidateNode,
                    out bool candidateIsPrimary)
                || !_graph.TryGetNodeAddress(
                    candidateNode,
                    out NavigationCellAddress candidateAddress)
                || (directionOrdinal >= 0
                    && selectedAddress.CompareTo(candidateAddress) <= 0))
            {
                continue;
            }

            directionOrdinal = candidateOrdinal;
            target = new NavigationMediumStateRef(candidateNode, _source.Medium);
            isPrimary = candidateIsPrimary;
            selectedAddress = candidateAddress;
        }

        if (!_hasVolumeSeamLookahead && !_volumeSeamsComplete)
        {
            if (_volumeSeams.MoveNext())
            {
                _volumeSeamLookahead = _volumeSeams.Current;
                _hasVolumeSeamLookahead = true;
            }
            else
            {
                _volumeSeamsComplete = true;
            }
        }
        if (_hasVolumeSeamLookahead
            && _graph.TryGetNodeRef(
                _volumeSeamLookahead.Destination,
                out NavigationNodeRef seamTarget)
            && (directionOrdinal < 0
                || _volumeSeamLookahead.Destination.CompareTo(selectedAddress) < 0))
        {
            directionOrdinal = 0;
            target = new NavigationMediumStateRef(seamTarget, _source.Medium);
            isPrimary = true;
            seam = _volumeSeamLookahead;
            hasSeam = true;
        }
        return directionOrdinal >= 0;
    }

    private NavigationTraversalEdgeAdvanceStatus AdvanceSurfaceRoute(
        NavigationWorkMeter meter,
        NavigationDependencyWorkspace dependencies,
        ref int connectionStepRemaining)
    {
        NavigationSurfaceEdgeRouteStatus status = _surfaceRouteWork.Advance(
            meter,
            ref connectionStepRemaining);
        if (_surfaceRouteWork.TryTakeDependencyNode(out NavigationNodeRef dependencyNode)
            && (!_graph!.TryGetNodeAddress(
                    dependencyNode,
                    out NavigationCellAddress dependencyAddress)
                || (dependencyNode != _surfaceEdge.Target
                    && _graph.TryGetSurfaceComponent(
                        dependencyAddress,
                        TraversalMedium.Solid,
                        out NavigationSurfaceComponentKey dependencyComponent,
                        out _)
                    && !dependencies.TryRecordComponent(dependencyComponent))
                || !dependencies.TryRecordPage(
                    dependencyAddress.MapId,
                    dependencyNode.CellSlot / NavigationSemanticPage.SlotCount)))
        {
            _surfaceRouteActive = false;
            return NavigationTraversalEdgeAdvanceStatus.CapacityExceeded;
        }
        if (status == NavigationSurfaceEdgeRouteStatus.Point)
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        if (status == NavigationSurfaceEdgeRouteStatus.Pending)
        {
            return connectionStepRemaining == 0
                && meter.RemainingConnectionLegs > 0
                    ? NavigationTraversalEdgeAdvanceStatus.Blocked
                    : NavigationTraversalEdgeAdvanceStatus.Pending;
        }
        _surfaceRouteActive = false;
        return status == NavigationSurfaceEdgeRouteStatus.Passable
            ? SetCurrent(_surfaceRouteWork.Cost)
            : MapSurfaceRoute(status);
    }

    private static NavigationTraversalEdgeAdvanceStatus MapSurfaceRoute(
        NavigationSurfaceEdgeRouteStatus status) => status switch
        {
            NavigationSurfaceEdgeRouteStatus.Passable =>
                NavigationTraversalEdgeAdvanceStatus.Edge,
            NavigationSurfaceEdgeRouteStatus.BudgetExceeded =>
                NavigationTraversalEdgeAdvanceStatus.BudgetExceeded,
            NavigationSurfaceEdgeRouteStatus.CostOverflow =>
                NavigationTraversalEdgeAdvanceStatus.CostOverflow,
            NavigationSurfaceEdgeRouteStatus.Stale =>
                NavigationTraversalEdgeAdvanceStatus.Stale,
            _ => NavigationTraversalEdgeAdvanceStatus.Pending
        };

    private NavigationTraversalEdgeAdvanceStatus SetCurrent(Fixed64 cost)
    {
        CurrentTarget = new NavigationMediumStateRef(
            _surfaceEdge.Target,
            TraversalMedium.Solid);
        CurrentKind = NavigationTraversalEdgeKind.Surface;
        CurrentOrdinal = _surfaceOrdinal;
        CurrentCost = cost;
        CurrentSurfaceEdge = _surfaceEdge;
        CurrentVolumeIsShortcut = false;
        return NavigationTraversalEdgeAdvanceStatus.Edge;
    }

    private void MarkWorldDependency(TraversalMedium medium)
    {
        if (medium == TraversalMedium.Gas || medium == TraversalMedium.Liquid)
            RequiresWorldStamp = true;
    }

    private NavigationTraversalEdgeAdvanceStatus Complete()
    {
        CurrentTarget = default;
        CurrentKind = default;
        CurrentOrdinal = -1;
        CurrentCost = default;
        CurrentSurfaceEdge = default;
        CurrentVolumeIsShortcut = false;
        CurrentTransitionId = string.Empty;
        CurrentTransitionOwnerMapId = string.Empty;
        CurrentTransitionType = default;
        CurrentTransitionHints = default;
        CurrentTransitionIdentityKind = default;
        CurrentTransitionSourceAction = default;
        CurrentTransitionDestinationAction = default;
        return NavigationTraversalEdgeAdvanceStatus.Complete;
    }
}
