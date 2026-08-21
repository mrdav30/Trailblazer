//=======================================================================
// NavigationIncomingTraversalEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Discovers incoming candidates and recovers their exact forward edge.</summary>
internal struct NavigationIncomingTraversalEdgeEnumerator
{
    private readonly GridWorld _world;
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationMediumStateRef _destination;
    private readonly NavigationAgentProfile _profile;
    private readonly NavigationAreaPolicy _areaPolicy;
    private readonly NavigationRayWorkspace _workspace;
    private readonly bool _allowTransitions;
    private NavigationIncomingSurfaceEdgeEnumerator _surfaceIncoming;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _volumeSeams;
    private NavigationAutomaticSeamRef _volumeSeamLookahead;
    private NavigationTransitionPage.Enumerator _definitions;
    private NavigationPublishedTransition _definitionLookahead;
    private NavigationTraversalEdgeEnumerator _outgoing;
    private NavigationMediumStateRef _candidateSource;
    private NavigationTraversalEdgeKind _candidateTraversalKind;
    private NavigationGraphEdge _candidateSurfaceEdge;
    private TraversalTransitionType _candidateType;
    private NavigationTransitionIdentityKind _candidateKind;
    private string _candidateOwnerMapId;
    private string _candidateId;
    private NavigationCellAddress _lastRuleSourceAddress;
    private TraversalMedium _lastRuleSourceMedium;
    private TraversalTransitionType _lastRuleType;
    private string _lastRuleId;
    private TraversalTransitionRule _scannedRule;
    private NavigationMediumStateRef _scannedRuleSource;
    private NavigationCellAddress _scannedRuleAddress;
    private int _ruleScanIndex;
    private int _ruleContactDirection;
    private bool _started;
    private bool _definitionsComplete;
    private bool _hasDefinitionLookahead;
    private bool _definitionNeedsDebit;
    private bool _hasLastRule;
    private bool _rescanActive;
    private bool _ruleScanActive;
    private bool _hasScannedRule;
    private bool _hasVolumeSeamLookahead;
    private bool _volumeSeamsComplete;
    private bool _baseComplete;
    private uint _visitedVolumeDirections;

    internal NavigationIncomingTraversalEdgeEnumerator(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationMediumStateRef destination,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        NavigationRayWorkspace workspace,
        bool allowTransitions)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        SwiftThrowHelper.ThrowIfNull(workspace, nameof(workspace));
        _world = world;
        _graph = destination.IsValid ? graph : null;
        _destination = destination;
        _profile = profile;
        _areaPolicy = areaPolicy;
        _workspace = workspace;
        _allowTransitions = allowTransitions;
        _surfaceIncoming = destination.Medium == TraversalMedium.Solid
            ? graph.EnumerateIncomingSurfaceEdges(destination.Node)
            : default;
        _volumeSeams = (destination.Medium == TraversalMedium.Gas
                || destination.Medium == TraversalMedium.Liquid)
            && graph.TryGetNodeAddress(
                destination.Node,
                out NavigationCellAddress volumeAddress)
                ? graph.AutomaticSeams.GetActiveEndpointEnumerator(volumeAddress)
                : default;
        _volumeSeamLookahead = default;
        _definitions = allowTransitions
            ? graph.EnumerateIncomingTransitionCandidates(destination)
            : default;
        _definitionLookahead = default;
        _outgoing = default;
        _candidateSource = default;
        _candidateTraversalKind = default;
        _candidateSurfaceEdge = default;
        _candidateType = default;
        _candidateKind = default;
        _candidateOwnerMapId = string.Empty;
        _candidateId = string.Empty;
        _lastRuleSourceAddress = default;
        _lastRuleSourceMedium = default;
        _lastRuleType = default;
        _lastRuleId = string.Empty;
        _scannedRule = default;
        _scannedRuleSource = default;
        _scannedRuleAddress = default;
        _ruleScanIndex = 0;
        _ruleContactDirection = -1;
        _started = false;
        _definitionsComplete = !allowTransitions;
        _hasDefinitionLookahead = false;
        _definitionNeedsDebit = false;
        _hasLastRule = false;
        _rescanActive = false;
        _ruleScanActive = false;
        _hasScannedRule = false;
        _hasVolumeSeamLookahead = false;
        _volumeSeamsComplete = destination.Medium != TraversalMedium.Gas
            && destination.Medium != TraversalMedium.Liquid;
        _baseComplete = false;
        _visitedVolumeDirections = 0;
        CurrentPredecessor = default;
        CurrentKind = default;
        CurrentOrdinal = -1;
        CurrentCost = default;
        CurrentTransitionId = string.Empty;
        CurrentTransitionOwnerMapId = string.Empty;
        CurrentTransitionType = default;
        CurrentTransitionHints = default;
        CurrentTransitionIdentityKind = default;
    }

    internal NavigationMediumStateRef CurrentPredecessor { get; private set; }

    internal NavigationTraversalEdgeKind CurrentKind { get; private set; }

    internal int CurrentOrdinal { get; private set; }

    internal Fixed64 CurrentCost { get; private set; }

    internal string CurrentTransitionId { get; private set; }

    internal string CurrentTransitionOwnerMapId { get; private set; }

    internal TraversalTransitionType CurrentTransitionType { get; private set; }

    internal TraversalTransitionLocomotionHints CurrentTransitionHints { get; private set; }

    internal NavigationTransitionIdentityKind CurrentTransitionIdentityKind { get; private set; }

    internal Vector3d CurrentTransitionSourceAction =>
        CurrentKind == NavigationTraversalEdgeKind.Transition
            ? _outgoing.CurrentTransitionSourceAction
            : default;

    internal Vector3d CurrentTransitionDestinationAction =>
        CurrentKind == NavigationTraversalEdgeKind.Transition
            ? _outgoing.CurrentTransitionDestinationAction
            : default;

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
        if (!_started && _allowTransitions)
        {
            dependencies.RecordTransitionDependency();
            if (!_graph.TryGetNodeAddress(
                    _destination.Node,
                    out NavigationCellAddress address)
                || !dependencies.TryRecordPage(
                    address.MapId,
                    _destination.Node.CellSlot / NavigationSemanticPage.SlotCount))
            {
                return NavigationTraversalEdgeAdvanceStatus.CapacityExceeded;
            }
            _started = true;
        }

        while (true)
        {
            if (!_rescanActive)
            {
                if (!_baseComplete)
                {
                    NavigationTraversalEdgeAdvanceStatus baseStatus = PrepareBaseCandidate(
                        meter,
                        ref edgeStepRemaining);
                    if (baseStatus != NavigationTraversalEdgeAdvanceStatus.Complete)
                        return baseStatus;
                    _baseComplete = true;
                }
                if (!_allowTransitions)
                    return Complete();
                NavigationTraversalEdgeAdvanceStatus prepared = PrepareCandidate(
                    meter,
                    ref edgeStepRemaining);
                if (prepared != NavigationTraversalEdgeAdvanceStatus.Pending
                    || !_rescanActive)
                {
                    return prepared;
                }
                return NavigationTraversalEdgeAdvanceStatus.Pending;
            }

            NavigationTraversalEdgeAdvanceStatus status = _outgoing.AdvanceOne(
                meter,
                dependencies,
                ref edgeStepRemaining,
                ref connectionStepRemaining);
            if (status == NavigationTraversalEdgeAdvanceStatus.Complete)
            {
                _rescanActive = false;
                continue;
            }
            if (status != NavigationTraversalEdgeAdvanceStatus.Edge)
                return status;
            if (!MatchesCandidate())
            {
                continue;
            }

            CurrentPredecessor = _candidateSource;
            CurrentKind = _outgoing.CurrentKind;
            CurrentOrdinal = _outgoing.CurrentOrdinal;
            CurrentCost = _outgoing.CurrentCost;
            CurrentTransitionId = _outgoing.CurrentTransitionId;
            CurrentTransitionOwnerMapId = _outgoing.CurrentTransitionOwnerMapId;
            CurrentTransitionType = _outgoing.CurrentTransitionType;
            CurrentTransitionHints = _outgoing.CurrentTransitionHints;
            CurrentTransitionIdentityKind = _outgoing.CurrentTransitionIdentityKind;
            _rescanActive = false;
            return NavigationTraversalEdgeAdvanceStatus.Edge;
        }
    }

    private NavigationTraversalEdgeAdvanceStatus PrepareBaseCandidate(
        NavigationWorkMeter meter,
        ref int edgeStepRemaining)
    {
        if (_destination.Medium == TraversalMedium.Solid)
        {
            NavigationSurfaceEdgeAdvanceStatus status = _surfaceIncoming.AdvanceOne(
                meter,
                ref edgeStepRemaining);
            if (status == NavigationSurfaceEdgeAdvanceStatus.Complete)
                return NavigationTraversalEdgeAdvanceStatus.Complete;
            if (status == NavigationSurfaceEdgeAdvanceStatus.Blocked)
            {
                return meter.RemainingEvaluatedEdges == 0
                    ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
                    : NavigationTraversalEdgeAdvanceStatus.Blocked;
            }
            if (status != NavigationSurfaceEdgeAdvanceStatus.Edge)
                return NavigationTraversalEdgeAdvanceStatus.Pending;
            NavigationIncomingSurfaceEdge candidate = _surfaceIncoming.Current;
            _candidateSource = new NavigationMediumStateRef(
                candidate.Predecessor,
                TraversalMedium.Solid);
            _candidateTraversalKind = NavigationTraversalEdgeKind.Surface;
            _candidateSurfaceEdge = candidate.ForwardEdge;
            BeginOutgoingRescan();
            return NavigationTraversalEdgeAdvanceStatus.Pending;
        }
        if (_destination.Medium != TraversalMedium.Gas
            && _destination.Medium != TraversalMedium.Liquid)
        {
            return NavigationTraversalEdgeAdvanceStatus.Complete;
        }

        int selectedDirection = -1;
        NavigationMediumStateRef selected = default;
        NavigationCellAddress selectedAddress = default;
        int count = _graph!.GetCompleteDirectionCount(_destination.Node);
        for (int direction = 0; direction < count; direction++)
        {
            if ((_visitedVolumeDirections & (1U << direction)) != 0
                || !_graph.TryGetCompleteNeighbor(
                    _destination.Node,
                    direction,
                    out NavigationNodeRef candidateNode,
                    out _)
                || !_graph.TryGetNodeAddress(
                    candidateNode,
                    out NavigationCellAddress candidateAddress)
                || (selectedDirection >= 0
                    && selectedAddress.CompareTo(candidateAddress) <= 0))
            {
                continue;
            }
            selectedDirection = direction;
            selected = new NavigationMediumStateRef(
                candidateNode,
                _destination.Medium);
            selectedAddress = candidateAddress;
        }
        bool selectedSeam = false;
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
                out NavigationNodeRef seamSource)
            && (selectedDirection < 0
                || _volumeSeamLookahead.Destination.CompareTo(selectedAddress) < 0))
        {
            selectedDirection = 0;
            selected = new NavigationMediumStateRef(
                seamSource,
                _destination.Medium);
            selectedAddress = _volumeSeamLookahead.Destination;
            selectedSeam = true;
        }
        if (selectedDirection < 0)
            return NavigationTraversalEdgeAdvanceStatus.Complete;
        if (edgeStepRemaining == 0)
        {
            return meter.RemainingEvaluatedEdges == 0
                ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
                : NavigationTraversalEdgeAdvanceStatus.Blocked;
        }
        if (!meter.TryConsumeEvaluatedEdges(1))
            return NavigationTraversalEdgeAdvanceStatus.BudgetExceeded;
        edgeStepRemaining--;
        if (selectedSeam)
        {
            _volumeSeamLookahead = default;
            _hasVolumeSeamLookahead = false;
        }
        else
        {
            _visitedVolumeDirections |= 1U << selectedDirection;
        }
        _candidateSource = selected;
        _candidateTraversalKind = NavigationTraversalEdgeKind.Volume;
        _candidateSurfaceEdge = default;
        BeginOutgoingRescan();
        return NavigationTraversalEdgeAdvanceStatus.Pending;
    }

    private void BeginOutgoingRescan()
    {
        _outgoing = new NavigationTraversalEdgeEnumerator(
            _world,
            _graph!,
            _candidateSource,
            _profile,
            _areaPolicy,
            _workspace,
            _allowTransitions,
            emittedSurfaceOrdinal: -1);
        _rescanActive = true;
    }

    private bool MatchesCandidate()
    {
        if (_outgoing.CurrentKind != _candidateTraversalKind
            || !_outgoing.CurrentTarget.Equals(_destination))
        {
            return false;
        }
        if (_candidateTraversalKind == NavigationTraversalEdgeKind.Volume)
            return true;
        if (_candidateTraversalKind == NavigationTraversalEdgeKind.Surface)
            return MatchesSurfaceEdge(
                _candidateSurfaceEdge,
                _outgoing.CurrentSurfaceEdge);
        return _outgoing.CurrentTransitionIdentityKind == _candidateKind
            && _outgoing.CurrentTransitionType == _candidateType
            && string.Equals(
                _outgoing.CurrentTransitionOwnerMapId,
                _candidateOwnerMapId,
                System.StringComparison.Ordinal)
            && string.Equals(
                _outgoing.CurrentTransitionId,
                _candidateId,
                System.StringComparison.Ordinal);
    }

    private static bool MatchesSurfaceEdge(
        NavigationGraphEdge candidate,
        NavigationGraphEdge forward)
    {
        if (candidate.Kind != forward.Kind || candidate.Target != forward.Target)
            return false;
        return candidate.Kind switch
        {
            NavigationGraphEdgeKind.Explicit => ReferenceEquals(
                candidate.ExplicitConnection,
                forward.ExplicitConnection),
            NavigationGraphEdgeKind.Seam => ReferenceEquals(
                candidate.AutomaticSeam.Pair,
                forward.AutomaticSeam.Pair),
            _ => true
        };
    }

    private NavigationTraversalEdgeAdvanceStatus PrepareCandidate(
        NavigationWorkMeter meter,
        ref int edgeStepRemaining)
    {
        bool hasDefinition = _hasDefinitionLookahead;
        NavigationPublishedTransition definition = _definitionLookahead;
        NavigationMediumStateRef definitionSource = default;
        if (!hasDefinition && !_definitionsComplete)
        {
            if (_definitions.MoveNext())
            {
                _definitionLookahead = _definitions.Current;
                _hasDefinitionLookahead = true;
                _definitionNeedsDebit = true;
                hasDefinition = true;
                definition = _definitionLookahead;
            }
            else
            {
                _definitionsComplete = true;
            }
        }
        if (_definitionNeedsDebit)
        {
            if (!NavigationTraversalEdgeEnumerator.TryConsumeTransitionCandidate(
                    meter,
                    ref edgeStepRemaining,
                    out NavigationTraversalEdgeAdvanceStatus blocked))
            {
                return blocked;
            }
            _definitionNeedsDebit = false;
        }
        if (hasDefinition)
        {
            hasDefinition = _graph!.TryGetNodeRef(
                definition.SourceAddress,
                out NavigationNodeRef definitionSourceNode);
            if (!hasDefinition)
            {
                _definitionLookahead = default;
                _hasDefinitionLookahead = false;
            }
            else
            {
                definitionSource = new NavigationMediumStateRef(
                    definitionSourceNode,
                    definition.Definition.SourceMedium);
            }
        }

        if (!_ruleScanActive)
        {
            _ruleScanActive = true;
            _ruleScanIndex = 0;
            _hasScannedRule = false;
            _scannedRule = default;
            _scannedRuleSource = default;
            _scannedRuleAddress = default;
            _ruleContactDirection = -1;
        }
        while (_ruleScanIndex < _graph!.TransitionRules.Count)
        {
            TraversalTransitionRule rule = _graph.TransitionRules[_ruleScanIndex];
            if (_ruleContactDirection < 0)
            {
                if (!NavigationTraversalEdgeEnumerator.TryConsumeTransitionCandidate(
                        meter,
                        ref edgeStepRemaining,
                        out NavigationTraversalEdgeAdvanceStatus blocked))
                {
                    return blocked;
                }
                if (rule.DestinationMedium != _destination.Medium)
                {
                    CompleteRuleContactScan();
                    continue;
                }
                _ruleContactDirection = 0;
            }
            if (rule.Scope == TraversalTransitionRuleScope.SameCell)
            {
                if (!NavigationTraversalEdgeEnumerator.TryConsumeTransitionCandidate(
                        meter,
                        ref edgeStepRemaining,
                        out NavigationTraversalEdgeAdvanceStatus blocked))
                {
                    return blocked;
                }
                if (_graph.TryGetNodeAddress(
                        _destination.Node,
                        out NavigationCellAddress sourceAddress))
                {
                    ConsiderRule(
                        rule,
                        new NavigationMediumStateRef(
                            _destination.Node,
                            rule.SourceMedium),
                        sourceAddress,
                        ref _hasScannedRule,
                        ref _scannedRule,
                        ref _scannedRuleSource,
                        ref _scannedRuleAddress);
                }
                CompleteRuleContactScan();
                continue;
            }
            int primaryCount = _graph.GetPrimaryDirectionCount(_destination.Node);
            if (_ruleContactDirection < primaryCount)
            {
                if (!NavigationTraversalEdgeEnumerator.TryConsumeTransitionCandidate(
                        meter,
                        ref edgeStepRemaining,
                        out NavigationTraversalEdgeAdvanceStatus blocked))
                {
                    return blocked;
                }
                int direction = _ruleContactDirection++;
                if (_graph.TryGetPrimaryNeighbor(
                        _destination.Node,
                        direction,
                        out NavigationNodeRef sourceNode)
                    && _graph.TryGetNodeAddress(
                        sourceNode,
                        out NavigationCellAddress sourceAddress))
                {
                    ConsiderRule(
                        rule,
                        new NavigationMediumStateRef(
                            sourceNode,
                            rule.SourceMedium),
                        sourceAddress,
                        ref _hasScannedRule,
                        ref _scannedRule,
                        ref _scannedRuleSource,
                        ref _scannedRuleAddress);
                }
                continue;
            }
            if (_ruleContactDirection == primaryCount)
            {
                _volumeSeams = _graph.TryGetNodeAddress(
                        _destination.Node,
                        out NavigationCellAddress seamDestinationAddress)
                    ? _graph.AutomaticSeams.GetActiveEndpointEnumerator(
                        seamDestinationAddress)
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
            if (!NavigationTraversalEdgeEnumerator.TryConsumeTransitionCandidate(
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
                    out NavigationNodeRef seamSource))
            {
                ConsiderRule(
                    rule,
                    new NavigationMediumStateRef(
                        seamSource,
                        rule.SourceMedium),
                    seam.Destination,
                    ref _hasScannedRule,
                    ref _scannedRule,
                    ref _scannedRuleSource,
                    ref _scannedRuleAddress);
            }
        }

        bool hasRule = _hasScannedRule;
        TraversalTransitionRule selectedRule = _scannedRule;
        NavigationMediumStateRef selectedRuleSource = _scannedRuleSource;
        NavigationCellAddress selectedRuleAddress = _scannedRuleAddress;
        _ruleScanActive = false;
        _ruleScanIndex = 0;
        _ruleContactDirection = -1;
        _hasScannedRule = false;
        _scannedRule = default;
        _scannedRuleSource = default;
        _scannedRuleAddress = default;

        NavigationCellAddress definitionAddress = hasDefinition
            ? definition.SourceAddress
            : default;
        bool selectRule = hasRule && (!hasDefinition
            || NavigationTraversalEdgeEnumerator.CompareTransitionKey(
                selectedRuleAddress,
                selectedRule.SourceMedium,
                selectedRule.Type,
                NavigationTransitionIdentityKind.Rule,
                selectedRule.Id,
                definitionAddress,
                definition.Definition.SourceMedium,
                definition.Definition.Type,
                NavigationTransitionIdentityKind.Definition,
                definition.Owner.TransitionId) < 0);
        if (!hasDefinition && !hasRule)
            return Complete();

        if (selectRule)
        {
            _candidateSource = selectedRuleSource;
            _candidateTraversalKind = NavigationTraversalEdgeKind.Transition;
            _candidateType = selectedRule.Type;
            _candidateKind = NavigationTransitionIdentityKind.Rule;
            _candidateOwnerMapId = string.Empty;
            _candidateId = selectedRule.Id;
            _hasLastRule = true;
            _lastRuleSourceAddress = selectedRuleAddress;
            _lastRuleSourceMedium = selectedRule.SourceMedium;
            _lastRuleType = selectedRule.Type;
            _lastRuleId = selectedRule.Id;
        }
        else
        {
            _candidateSource = definitionSource;
            _candidateTraversalKind = NavigationTraversalEdgeKind.Transition;
            _candidateType = definition.Definition.Type;
            _candidateKind = NavigationTransitionIdentityKind.Definition;
            _candidateOwnerMapId = definition.Owner.MapId;
            _candidateId = definition.Owner.TransitionId;
            _definitionLookahead = default;
            _hasDefinitionLookahead = false;
        }
        BeginOutgoingRescan();
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

    private void ConsiderRule(
        TraversalTransitionRule rule,
        NavigationMediumStateRef source,
        NavigationCellAddress sourceAddress,
        ref bool hasRule,
        ref TraversalTransitionRule selectedRule,
        ref NavigationMediumStateRef selectedSource,
        ref NavigationCellAddress selectedAddress)
    {
        if ((_hasLastRule
                && NavigationTraversalEdgeEnumerator.CompareTransitionKey(
                    _lastRuleSourceAddress,
                    _lastRuleSourceMedium,
                    _lastRuleType,
                    NavigationTransitionIdentityKind.Rule,
                    _lastRuleId,
                    sourceAddress,
                    rule.SourceMedium,
                    rule.Type,
                    NavigationTransitionIdentityKind.Rule,
                    rule.Id) >= 0)
            || (hasRule
                && NavigationTraversalEdgeEnumerator.CompareTransitionKey(
                    selectedAddress,
                    selectedRule.SourceMedium,
                    selectedRule.Type,
                    NavigationTransitionIdentityKind.Rule,
                    selectedRule.Id,
                    sourceAddress,
                    rule.SourceMedium,
                    rule.Type,
                    NavigationTransitionIdentityKind.Rule,
                    rule.Id) <= 0))
        {
            return;
        }
        hasRule = true;
        selectedRule = rule;
        selectedSource = source;
        selectedAddress = sourceAddress;
    }

    private NavigationTraversalEdgeAdvanceStatus Complete()
    {
        CurrentPredecessor = default;
        CurrentKind = default;
        CurrentOrdinal = -1;
        CurrentCost = default;
        CurrentTransitionId = string.Empty;
        CurrentTransitionOwnerMapId = string.Empty;
        CurrentTransitionType = default;
        CurrentTransitionHints = default;
        CurrentTransitionIdentityKind = default;
        return NavigationTraversalEdgeAdvanceStatus.Complete;
    }
}
