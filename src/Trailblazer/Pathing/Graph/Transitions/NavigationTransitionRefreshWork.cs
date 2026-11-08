//=======================================================================
// NavigationTransitionRefreshWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Refreshes affected explicit-transition pages and the bounded rule table.</summary>
internal sealed class NavigationTransitionRefreshWork
{
    private const long FixedRetainedBytes = 536L;
    private readonly NavigationWorldGraph _source;
    private readonly NavigationWorldGraph _candidate;
    private readonly NavigationOperationCandidate? _operationCandidate;
    private readonly PersistentStringMap<bool> _changedMaps;
    private readonly bool _rebuildRules;
    private readonly long _version;
    private PersistentStringMap<bool> _affectedSources = PersistentStringMap<bool>.Empty;
    private PersistentStringMap<PersistentIntMap<PageBuildState>> _pageBuilds =
        PersistentStringMap<PersistentIntMap<PageBuildState>>.Empty;
    private long _pageBuildInnerBytes;
    private int _pageBuildInnerPages;
    private int _pageBuildStateCount;
    private NavigationTransitionPageRoot _pages;
    private NavigationTransitionRuleTable _rules;
    private int _changedMapOrdinal;
    private int _incomingOrdinal = -1;
    private int _definitionPhase;
    private int _definitionSourceOrdinal;
    private EffectiveTransitionCursor _definitionCursor;
    private bool _definitionCursorActive;
    private string? _pendingOwnerMapId;
    private TraversalTransitionDefinition _pendingDefinition;
    private NavigationPublishedTransition _pendingTransition;
    private int _pendingPlacementStage = -1;
    private int _pageMapOrdinal;
    private int _pageOrdinal;
    private long _workingBufferBytes;
    private int _workingBufferPages;
    private long _publicationOwnedBytes;
    private int _publicationOwnedPages;
    private bool _publicationRootOwned;
    private string? _publicationRootMapId;
    private int _ruleCountMapOrdinal;
    private int _ruleCount;
    private TraversalTransitionRule[]? _ruleBuffer;
    private int[]? _ruleOrdinals;
    private int _ruleOutputOrdinal;
    private int _ruleScanMapOrdinal;
    private int _ruleBestMapOrdinal = -1;
    private bool _ruleScanActive;
    private bool _ruleContentEqual;

    internal NavigationTransitionRefreshWork(
        NavigationWorldGraph source,
        NavigationWorldGraph candidate,
        NavigationOperationCandidate? operationCandidate,
        PersistentStringMap<bool> changedMaps,
        bool rebuildRules,
        long version)
    {
        _source = source;
        _candidate = candidate;
        _operationCandidate = operationCandidate;
        _changedMaps = changedMaps;
        _rebuildRules = rebuildRules;
        _version = version;
        _pages = source.TransitionPages;
        _rules = source.TransitionRules;
    }

    internal bool IsComplete { get; private set; }

    internal NavigationTransitionPageRoot Pages => _pages;

    internal NavigationTransitionRuleTable Rules => _rules;

    internal long RetainedBytes => checked(
        FixedRetainedBytes
        + _affectedSources.RetainedBytes
        + _pageBuilds.RetainedBytes
        + _pageBuildInnerBytes
        + ((long)_pageBuildStateCount * PageBuildState.BaseRetainedBytes)
        + _workingBufferBytes
        + (_ruleOrdinals == null ? 0L : GetArrayBytes(_ruleOrdinals.Length, sizeof(int)))
        + _publicationOwnedBytes);

    internal int PersistentPageCount => checked(
        1
        + _affectedSources.PersistentNodeCount
        + _pageBuilds.PersistentNodeCount
        + _pageBuildInnerPages
        + _pageBuildStateCount
        + _workingBufferPages
        + (_ruleOrdinals == null || _ruleOrdinals.Length == 0 ? 0 : 1)
        + _publicationOwnedPages);

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        if (IsComplete)
            return true;
        if (!CaptureAffectedSources(meter))
            return false;
        if (!RefreshDefinitions(meter))
            return false;
        if (_rebuildRules && !RefreshRules(meter))
            return false;
        IsComplete = true;
        return true;
    }

    private bool CaptureAffectedSources(MaintenanceWorkMeter meter)
    {
        while (_changedMapOrdinal < _changedMaps.Count)
        {
            string mapId = _changedMaps.GetKeyAt(_changedMapOrdinal);
            if (_incomingOrdinal < 0)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _affectedSources = _affectedSources.Set(mapId, true);
                _incomingOrdinal = 0;
            }
            int incomingCount = _operationCandidate?.GetIncomingSourceCount(mapId) ?? 0;
            while (_incomingOrdinal < incomingCount)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _affectedSources = _affectedSources.Set(
                    _operationCandidate!.GetIncomingSource(mapId, _incomingOrdinal++),
                    true);
            }
            _changedMapOrdinal++;
            _incomingOrdinal = -1;
        }
        return true;
    }

    private bool RefreshDefinitions(MaintenanceWorkMeter meter)
    {
        while (_definitionPhase < 5)
        {
            switch (_definitionPhase)
            {
                case 0:
                    if (!AdvancePriorPageDiscovery(meter))
                        return false;
                    CompleteDefinitionPhase();
                    break;
                case 1:
                    if (!AdvanceNextPageCount(meter))
                        return false;
                    CompleteDefinitionPhase();
                    break;
                case 2:
                    if (!AdvancePagePreparation(meter))
                        return false;
                    ResetPageCursor();
                    _definitionPhase++;
                    break;
                case 3:
                    if (!AdvanceNextPageFill(meter))
                        return false;
                    CompleteDefinitionPhase();
                    break;
                default:
                    if (!AdvancePageSeal(meter))
                        return false;
                    ClearPageBuildScratch();
                    _definitionPhase++;
                    break;
            }
        }
        return true;
    }

    private bool AdvancePriorPageDiscovery(MaintenanceWorkMeter meter)
    {
        while (true)
        {
            if (_pendingPlacementStage < 0)
            {
                if (!TryAdvanceEffectiveDefinition(_source, meter))
                {
                    return !_definitionCursorActive
                        && _definitionSourceOrdinal >= _affectedSources.Count;
                }
                if (!TryGetRetainedPrior(
                        _pendingOwnerMapId!,
                        _pendingDefinition,
                        out _pendingTransition))
                {
                    ClearPendingDefinition();
                    continue;
                }
                _pendingPlacementStage = 0;
            }
            if (_pendingPlacementStage == 0)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                GetOrAddPage(_pendingTransition.SourcePage);
                _pendingPlacementStage = 1;
            }
            if (_pendingTransition.DestinationPage.HasValue)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                GetOrAddPage(_pendingTransition.DestinationPage.Value);
            }
            ClearPendingDefinition();
        }
    }

    private bool AdvanceNextPageCount(MaintenanceWorkMeter meter)
    {
        while (true)
        {
            if (_pendingPlacementStage < 0)
            {
                if (!TryAdvanceEffectiveDefinition(_candidate, meter))
                {
                    return !_definitionCursorActive
                        && _definitionSourceOrdinal >= _affectedSources.Count;
                }
                _pendingTransition = CreatePublished(
                    _candidate,
                    _pendingOwnerMapId!,
                    _pendingDefinition);
                _pendingPlacementStage = 0;
            }
            if (_pendingPlacementStage == 0)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                GetOrAddPage(_pendingTransition.SourcePage).NewOutgoingCount++;
                _pendingPlacementStage = 1;
            }
            if (_pendingTransition.DestinationPage.HasValue)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                GetOrAddPage(_pendingTransition.DestinationPage.Value).NewIncomingCount++;
            }
            ClearPendingDefinition();
        }
    }

    private bool AdvancePagePreparation(MaintenanceWorkMeter meter)
    {
        while (TryGetCurrentPageState(out PageBuildState state))
        {
            bool complete = state.AdvancePreparation(
                _affectedSources,
                meter,
                out long addedBytes,
                out int addedPages);
            AddWorkingBuffers(addedBytes, addedPages);
            if (!complete)
                return false;
            AdvancePageCursor();
        }
        return true;
    }

    private bool AdvanceNextPageFill(MaintenanceWorkMeter meter)
    {
        while (true)
        {
            if (_pendingPlacementStage < 0)
            {
                if (!TryAdvanceEffectiveDefinition(_candidate, meter))
                {
                    return !_definitionCursorActive
                        && _definitionSourceOrdinal >= _affectedSources.Count;
                }
                _pendingTransition = CreatePublished(
                    _candidate,
                    _pendingOwnerMapId!,
                    _pendingDefinition);
                _pendingPlacementStage = 0;
            }
            if (_pendingPlacementStage == 0)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                GetPage(_pendingTransition.SourcePage).AppendOutgoing(_pendingTransition);
                _pendingPlacementStage = 1;
            }
            if (_pendingTransition.DestinationPage.HasValue)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                GetPage(_pendingTransition.DestinationPage.Value).AppendIncoming(
                    new NavigationIncomingTransitionRef(_pendingTransition));
            }
            ClearPendingDefinition();
        }
    }

    private bool AdvancePageSeal(MaintenanceWorkMeter meter)
    {
        while (TryGetCurrentPageState(out PageBuildState state))
        {
            bool complete = state.AdvanceSeal(
                meter,
                out bool changed,
                out long releasedWorkingBytes,
                out int releasedWorkingPages);
            ReleaseWorkingBuffers(releasedWorkingBytes, releasedWorkingPages);
            if (!complete)
                return false;
            if (changed)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                NavigationTransitionPage page = state.CreatePage(_version);
                _pages = _pages.Set(page, out int copiedNodes, out long copiedBytes);
                OwnPublishedPageRoot(state.Address.MapId, page, copiedNodes, copiedBytes);
            }
            state.ReleaseFinalBuffers(
                out releasedWorkingBytes,
                out releasedWorkingPages);
            ReleaseWorkingBuffers(releasedWorkingBytes, releasedWorkingPages);
            AdvancePageCursor();
        }
        return true;
    }

    private bool TryAdvanceEffectiveDefinition(
        NavigationWorldGraph graph,
        MaintenanceWorkMeter meter)
    {
        while (_definitionSourceOrdinal < _affectedSources.Count)
        {
            string ownerMapId = _affectedSources.GetKeyAt(_definitionSourceOrdinal);
            if (!_definitionCursorActive)
            {
                graph.TryGetMap(ownerMapId, out NavigationMapInstance? instance);
                _definitionCursor = new EffectiveTransitionCursor(instance);
                _definitionCursorActive = true;
            }
            if (!meter.TryConsumeExplicitEdges(1))
                return false;
            bool hasValue = _definitionCursor.AdvanceOne(
                out TraversalTransitionDefinition definition,
                out bool complete);
            if (complete)
            {
                _definitionCursorActive = false;
                _definitionSourceOrdinal++;
                continue;
            }
            if (hasValue)
            {
                _pendingOwnerMapId = ownerMapId;
                _pendingDefinition = definition;
                return true;
            }
        }
        return false;
    }

    private bool TryGetRetainedPrior(
        string ownerMapId,
        TraversalTransitionDefinition definition,
        out NavigationPublishedTransition transition)
    {
        NavigationPublishedTransition located = CreatePublished(
            _source,
            ownerMapId,
            definition);
        if (_pages.TryGet(located.SourcePage, out NavigationTransitionPage page)
            && page.TryGetOutgoing(located.Owner, out transition))
        {
            return true;
        }
        transition = default;
        return false;
    }

    private PageBuildState GetOrAddPage(NavigationTransitionPageAddress address)
    {
        bool hadMap = _pageBuilds.TryGetValue(
            address.MapId,
            out PersistentIntMap<PageBuildState> map);
        map ??= PersistentIntMap<PageBuildState>.Empty;
        if (map.TryGetValue(address.PageIndex, out PageBuildState state))
            return state;
        _pages.TryGet(address, out NavigationTransitionPage prior);
        state = new PageBuildState(address, prior);
        long priorInnerBytes = hadMap ? map.RetainedBytes : 0L;
        int priorInnerPages = hadMap ? map.PersistentNodeCount : 0;
        map = map.Set(address.PageIndex, state);
        _pageBuilds = _pageBuilds.Set(address.MapId, map);
        _pageBuildInnerBytes = checked(
            _pageBuildInnerBytes - priorInnerBytes + map.RetainedBytes);
        _pageBuildInnerPages = checked(
            _pageBuildInnerPages - priorInnerPages + map.PersistentNodeCount);
        _pageBuildStateCount++;
        return state;
    }

    private PageBuildState GetPage(NavigationTransitionPageAddress address)
    {
        _pageBuilds.TryGetValue(address.MapId, out PersistentIntMap<PageBuildState> map);
        map.TryGetValue(address.PageIndex, out PageBuildState state);
        return state;
    }

    private bool TryGetCurrentPageState(out PageBuildState state)
    {
        while (_pageMapOrdinal < _pageBuilds.Count)
        {
            PersistentIntMap<PageBuildState> map = _pageBuilds.GetValueAt(_pageMapOrdinal);
            if (_pageOrdinal < map.Count)
            {
                state = map.GetValueAt(_pageOrdinal);
                return true;
            }
            _pageMapOrdinal++;
            _pageOrdinal = 0;
        }
        state = null!;
        return false;
    }

    private void AdvancePageCursor() => _pageOrdinal++;

    private void ResetPageCursor()
    {
        _pageMapOrdinal = 0;
        _pageOrdinal = 0;
    }

    private void CompleteDefinitionPhase()
    {
        _definitionPhase++;
        _definitionSourceOrdinal = 0;
        _definitionCursorActive = false;
        ClearPendingDefinition();
    }

    private void ClearPendingDefinition()
    {
        _pendingOwnerMapId = null;
        _pendingDefinition = default;
        _pendingTransition = default;
        _pendingPlacementStage = -1;
    }

    private void ClearPageBuildScratch()
    {
        _pageBuilds = PersistentStringMap<PersistentIntMap<PageBuildState>>.Empty;
        _pageBuildInnerBytes = 0;
        _pageBuildInnerPages = 0;
        _pageBuildStateCount = 0;
        ResetPageCursor();
    }

    private void AddWorkingBuffers(long bytes, int pages)
    {
        _workingBufferBytes = checked(_workingBufferBytes + bytes);
        _workingBufferPages = checked(_workingBufferPages + pages);
    }

    private void ReleaseWorkingBuffers(long bytes, int pages)
    {
        _workingBufferBytes = checked(_workingBufferBytes - bytes);
        _workingBufferPages = checked(_workingBufferPages - pages);
    }

    private void OwnPublishedPageRoot(
        string mapId,
        NavigationTransitionPage page,
        int copiedNodes,
        long copiedBytes)
    {
        if (!_publicationRootOwned)
        {
            _publicationRootOwned = true;
            _publicationOwnedBytes = checked(_publicationOwnedBytes + 88L);
            _publicationOwnedPages++;
        }
        if (!string.Equals(_publicationRootMapId, mapId, StringComparison.Ordinal))
        {
            _publicationRootMapId = mapId;
            _publicationOwnedBytes = checked(_publicationOwnedBytes + 32L);
        }
        _publicationOwnedBytes = checked(
            _publicationOwnedBytes
            + copiedBytes
            + (page.IsEmpty ? 0L : page.RetainedBytes));
        _publicationOwnedPages = checked(
            _publicationOwnedPages
            + copiedNodes
            + (page.IsEmpty ? 0 : page.PersistentPageCount));
    }

    private bool RefreshRules(MaintenanceWorkMeter meter)
    {
        while (_ruleCountMapOrdinal < _candidate.MapCount)
        {
            if (!meter.TryConsumeComponentNodes(1))
                return false;
            _ruleCount = checked(
                _ruleCount
                + _candidate.GetInstance(_ruleCountMapOrdinal++).Map.TransitionRuleSpan.Length);
        }
        if (_ruleBuffer == null)
        {
            _ruleBuffer = new TraversalTransitionRule[_ruleCount];
            _ruleOrdinals = new int[_candidate.MapCount];
            _ruleContentEqual = _rules.Count == _ruleCount;
            AddWorkingBuffers(
                GetArrayBytes(_ruleBuffer.Length, NavigationTransitionRuleTable.RecordRetainedBytes),
                _ruleBuffer.Length == 0 ? 0 : 1);
        }
        while (_ruleOutputOrdinal < _ruleBuffer.Length)
        {
            if (!_ruleScanActive)
            {
                _ruleScanMapOrdinal = 0;
                _ruleBestMapOrdinal = -1;
                _ruleScanActive = true;
            }
            while (_ruleScanMapOrdinal < _candidate.MapCount)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                int mapOrdinal = _ruleScanMapOrdinal++;
                ReadOnlySpan<TraversalTransitionRule> rules =
                    _candidate.GetInstance(mapOrdinal).Map.TransitionRuleSpan;
                if (_ruleOrdinals![mapOrdinal] >= rules.Length)
                    continue;
                if (_ruleBestMapOrdinal < 0)
                {
                    _ruleBestMapOrdinal = mapOrdinal;
                    continue;
                }
                TraversalTransitionRule candidate = rules[_ruleOrdinals[mapOrdinal]];
                ReadOnlySpan<TraversalTransitionRule> bestRules =
                    _candidate.GetInstance(_ruleBestMapOrdinal).Map.TransitionRuleSpan;
                TraversalTransitionRule best = bestRules[_ruleOrdinals[_ruleBestMapOrdinal]];
                int comparison = string.CompareOrdinal(candidate.Id, best.Id);
                System.Diagnostics.Debug.Assert(comparison != 0,
                    "Map folding rejects duplicate global transition-rule ownership.");
                if (comparison < 0)
                    _ruleBestMapOrdinal = mapOrdinal;
            }
            TraversalTransitionRule rule = _candidate.GetInstance(_ruleBestMapOrdinal)
                .Map.TransitionRuleSpan[_ruleOrdinals![_ruleBestMapOrdinal]++];
            _ruleBuffer[_ruleOutputOrdinal] = rule;
            if (_ruleContentEqual && !_rules[_ruleOutputOrdinal].Equals(rule))
                _ruleContentEqual = false;
            _ruleOutputOrdinal++;
            _ruleScanActive = false;
        }
        long bufferBytes = GetArrayBytes(
            _ruleBuffer.Length,
            NavigationTransitionRuleTable.RecordRetainedBytes);
        int bufferPages = _ruleBuffer.Length == 0 ? 0 : 1;
        ReleaseWorkingBuffers(bufferBytes, bufferPages);
        if (_ruleContentEqual)
            _ruleBuffer = null;
        else
        {
            _rules = new NavigationTransitionRuleTable(_ruleBuffer, _version);
            _publicationOwnedBytes = checked(
                _publicationOwnedBytes
                + NavigationTransitionRuleTable.BaseRetainedBytes
                + bufferBytes);
            _publicationOwnedPages = checked(
                _publicationOwnedPages + _rules.PersistentPageCount);
        }
        _ruleOrdinals = null;
        return true;
    }

    private static NavigationPublishedTransition CreatePublished(
        NavigationWorldGraph graph,
        string ownerMapId,
        TraversalTransitionDefinition definition)
    {
        bool foundOwner = graph.TryGetMap(ownerMapId, out NavigationMapInstance source);
        System.Diagnostics.Debug.Assert(foundOwner,
            "Effective transition cursors enumerate definitions from an owned map.");
        bool foundSource = source.TryGetSlot(definition.SourceIndex, out int sourceSlot);
        System.Diagnostics.Debug.Assert(foundSource,
            "Published transition definitions retain their validated local source cell.");
        var sourcePage = new NavigationTransitionPageAddress(
            ownerMapId,
            sourceSlot / NavigationSemanticPage.SlotCount);
        NavigationTransitionPageAddress? destinationPage = null;
        if (graph.TryGetMap(definition.Destination.MapId, out NavigationMapInstance destination)
            && destination.TryGetSlot(definition.Destination.Index, out int destinationSlot))
        {
            destinationPage = new NavigationTransitionPageAddress(
                definition.Destination.MapId,
                destinationSlot / NavigationSemanticPage.SlotCount);
        }
        return new NavigationPublishedTransition(
            ownerMapId,
            definition,
            sourcePage,
            destinationPage);
    }

    private static long GetArrayBytes(int count, long elementBytes) => count == 0
        ? 0L
        : checked(24L + ((long)count * elementBytes));

    private sealed class PageBuildState
    {
        internal const long BaseRetainedBytes = 240L;
        private readonly NavigationTransitionPage? _prior;
        private NavigationPublishedTransition[] _outgoing =
            Array.Empty<NavigationPublishedTransition>();
        private NavigationIncomingTransitionRef[] _incoming =
            Array.Empty<NavigationIncomingTransitionRef>();
        private NavigationPublishedTransition[]? _outgoingScratch;
        private NavigationIncomingTransitionRef[]? _incomingScratch;
        private MergeSortWork<NavigationPublishedTransition, OutgoingComparer> _outgoingSort;
        private MergeSortWork<NavigationIncomingTransitionRef, IncomingComparer> _incomingSort;
        private int _prepareStage;
        private int _prepareOrdinal;
        private int _retainedOutgoingCount;
        private int _retainedIncomingCount;
        private int _outgoingFill;
        private int _incomingFill;
        private int _sealStage;
        private int _compareOrdinal;
        private bool _sameContent;

        internal PageBuildState(
            NavigationTransitionPageAddress address,
            NavigationTransitionPage? prior)
        {
            Address = address;
            _prior = prior;
        }

        internal NavigationTransitionPageAddress Address { get; }

        internal int NewOutgoingCount { get; set; }

        internal int NewIncomingCount { get; set; }

        internal void AppendOutgoing(NavigationPublishedTransition transition) =>
            _outgoing[_outgoingFill++] = transition;

        internal void AppendIncoming(NavigationIncomingTransitionRef incoming) =>
            _incoming[_incomingFill++] = incoming;

        internal bool AdvancePreparation(
            PersistentStringMap<bool> affectedSources,
            MaintenanceWorkMeter meter,
            out long addedBytes,
            out int addedPages)
        {
            addedBytes = 0;
            addedPages = 0;
            while (_prepareStage < 5)
            {
                if (_prepareStage == 0)
                {
                    int count = _prior?.OutgoingCount ?? 0;
                    while (_prepareOrdinal < count)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        if (!affectedSources.ContainsKey(
                                _prior!.GetOutgoingAt(_prepareOrdinal++).Owner.MapId))
                        {
                            _retainedOutgoingCount++;
                        }
                    }
                    _prepareStage++;
                    _prepareOrdinal = 0;
                }
                else if (_prepareStage == 1)
                {
                    int count = _prior?.IncomingCount ?? 0;
                    while (_prepareOrdinal < count)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        if (!affectedSources.ContainsKey(
                                _prior!.GetIncomingAt(_prepareOrdinal++).Owner.MapId))
                        {
                            _retainedIncomingCount++;
                        }
                    }
                    _prepareStage++;
                    _prepareOrdinal = 0;
                }
                else if (_prepareStage == 2)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    int outgoingLength = checked(_retainedOutgoingCount + NewOutgoingCount);
                    int incomingLength = checked(_retainedIncomingCount + NewIncomingCount);
                    _outgoing = outgoingLength == 0
                        ? Array.Empty<NavigationPublishedTransition>()
                        : new NavigationPublishedTransition[outgoingLength];
                    _incoming = incomingLength == 0
                        ? Array.Empty<NavigationIncomingTransitionRef>()
                        : new NavigationIncomingTransitionRef[incomingLength];
                    _outgoingScratch = outgoingLength > 1
                        ? new NavigationPublishedTransition[outgoingLength]
                        : null;
                    _incomingScratch = incomingLength > 1
                        ? new NavigationIncomingTransitionRef[incomingLength]
                        : null;
                    _outgoingSort.Initialize(_outgoing, _outgoingScratch);
                    _incomingSort.Initialize(_incoming, _incomingScratch);
                    addedBytes = WorkingBufferBytes;
                    addedPages = WorkingBufferPages;
                    _prepareStage++;
                }
                else if (_prepareStage == 3)
                {
                    int count = _prior?.OutgoingCount ?? 0;
                    while (_prepareOrdinal < count)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        NavigationPublishedTransition transition =
                            _prior!.GetOutgoingAt(_prepareOrdinal++);
                        if (!affectedSources.ContainsKey(transition.Owner.MapId))
                            _outgoing[_outgoingFill++] = transition;
                    }
                    _prepareStage++;
                    _prepareOrdinal = 0;
                }
                else
                {
                    int count = _prior?.IncomingCount ?? 0;
                    while (_prepareOrdinal < count)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        NavigationIncomingTransitionRef incoming =
                            _prior!.GetIncomingAt(_prepareOrdinal++);
                        if (!affectedSources.ContainsKey(incoming.Owner.MapId))
                            _incoming[_incomingFill++] = incoming;
                    }
                    _prepareStage++;
                }
            }
            return true;
        }

        internal bool AdvanceSeal(
            MaintenanceWorkMeter meter,
            out bool changed,
            out long releasedWorkingBytes,
            out int releasedWorkingPages)
        {
            changed = false;
            releasedWorkingBytes = 0;
            releasedWorkingPages = 0;
            if (_sealStage == 0)
            {
                if (!_outgoingSort.Advance(meter) || !_incomingSort.Advance(meter))
                    return false;
                releasedWorkingBytes = SortScratchBytes;
                releasedWorkingPages = SortScratchPages;
                _outgoingScratch = null;
                _incomingScratch = null;
                _outgoingSort.Release();
                _incomingSort.Release();
                _sameContent = _prior != null
                    && _prior.OutgoingCount == _outgoing.Length
                    && _prior.IncomingCount == _incoming.Length;
                _sealStage++;
            }
            if (_sealStage == 1 && _sameContent)
            {
                int total = checked(_outgoing.Length + _incoming.Length);
                while (_compareOrdinal < total)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    bool equal = _compareOrdinal < _outgoing.Length
                        ? _prior!.GetOutgoingAt(_compareOrdinal).Equals(
                            _outgoing[_compareOrdinal])
                        : _prior!.GetIncomingAt(_compareOrdinal - _outgoing.Length).Equals(
                            _incoming[_compareOrdinal - _outgoing.Length]);
                    _compareOrdinal++;
                    if (!equal)
                    {
                        _sameContent = false;
                        break;
                    }
                }
                _sealStage++;
            }
            else if (_sealStage == 1)
                _sealStage++;
            changed = !_sameContent;
            return true;
        }

        internal NavigationTransitionPage CreatePage(long version) =>
            new(Address, version, _outgoing, _incoming);

        internal void ReleaseFinalBuffers(out long bytes, out int pages)
        {
            bytes = FinalArrayBytes;
            pages = FinalArrayPages;
            _outgoing = Array.Empty<NavigationPublishedTransition>();
            _incoming = Array.Empty<NavigationIncomingTransitionRef>();
        }

        private long WorkingBufferBytes => checked(FinalArrayBytes + SortScratchBytes);

        private int WorkingBufferPages => checked(FinalArrayPages + SortScratchPages);

        private long FinalArrayBytes => checked(
            NavigationTransitionPage.GetArrayBytes(
                _outgoing.Length,
                NavigationTransitionPage.OutgoingRecordBytes)
            + NavigationTransitionPage.GetArrayBytes(
                _incoming.Length,
                NavigationTransitionPage.IncomingRecordBytes));

        private int FinalArrayPages =>
            (_outgoing.Length == 0 ? 0 : 1) + (_incoming.Length == 0 ? 0 : 1);

        private long SortScratchBytes => checked(
            NavigationTransitionPage.GetArrayBytes(
                _outgoingScratch?.Length ?? 0,
                NavigationTransitionPage.OutgoingRecordBytes)
            + NavigationTransitionPage.GetArrayBytes(
                _incomingScratch?.Length ?? 0,
                NavigationTransitionPage.IncomingRecordBytes));

        private int SortScratchPages =>
            (_outgoingScratch == null ? 0 : 1) + (_incomingScratch == null ? 0 : 1);
    }

    private interface ITransitionRecordComparer<T>
    {
        int Compare(T left, T right);
    }

    private readonly struct OutgoingComparer : ITransitionRecordComparer<NavigationPublishedTransition>
    {
        public int Compare(
            NavigationPublishedTransition left,
            NavigationPublishedTransition right) =>
            NavigationTransitionPage.CompareOutgoing(left, right);
    }

    private readonly struct IncomingComparer : ITransitionRecordComparer<NavigationIncomingTransitionRef>
    {
        public int Compare(
            NavigationIncomingTransitionRef left,
            NavigationIncomingTransitionRef right) =>
            NavigationTransitionPage.CompareIncoming(left, right);
    }

    private struct MergeSortWork<T, TComparer>
        where TComparer : struct, ITransitionRecordComparer<T>
    {
        private T[]? _values;
        private T[]? _source;
        private T[]? _destination;
        private int _width;
        private int _left;
        private int _middle;
        private int _right;
        private int _sourceLeft;
        private int _sourceRight;
        private int _destinationOrdinal;
        private bool _runActive;

        internal void Initialize(T[] values, T[]? scratch)
        {
            _values = values;
            _source = values;
            _destination = scratch;
            _width = 1;
            _left = 0;
        }

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            System.Diagnostics.Debug.Assert(_values != null,
                "Transition page sorting is initialized during page preparation.");
            if (_values!.Length < 2)
                return true;
            var comparer = default(TComparer);
            while (_width < _values.Length)
            {
                if (!_runActive)
                {
                    _middle = Math.Min(_left + _width, _values.Length);
                    _right = Math.Min(_left + (_width * 2), _values.Length);
                    _sourceLeft = _left;
                    _sourceRight = _middle;
                    _destinationOrdinal = _left;
                    _runActive = true;
                }
                while (_destinationOrdinal < _right)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    T[] source = _source!;
                    T[] destination = _destination!;
                    bool takeLeft = _sourceRight >= _right
                        || (_sourceLeft < _middle
                            && comparer.Compare(
                                source[_sourceLeft],
                                source[_sourceRight]) <= 0);
                    destination[_destinationOrdinal++] = takeLeft
                        ? source[_sourceLeft++]
                        : source[_sourceRight++];
                }
                _runActive = false;
                _left = _right;
                if (_left < _values.Length)
                    continue;
                T[] priorSource = _source!;
                _source = _destination;
                _destination = priorSource;
                _width = _width > _values.Length / 2
                    ? _values.Length
                    : _width * 2;
                _left = 0;
            }
            if (!ReferenceEquals(_source, _values))
            {
                while (_left < _values.Length)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    _values[_left] = _source![_left];
                    _left++;
                }
            }
            return true;
        }

        internal void Release()
        {
            _values = null;
            _source = null;
            _destination = null;
        }
    }

    private struct EffectiveTransitionCursor
    {
        private readonly NavigationMap? _map;
        private readonly NavigationMapOverlayState? _overlay;
        private int _bakedOrdinal;
        private int _overlayOrdinal;

        internal EffectiveTransitionCursor(NavigationMapInstance? instance)
        {
            _map = instance?.Map;
            _overlay = instance?.Overlay;
            _bakedOrdinal = 0;
            _overlayOrdinal = 0;
        }

        internal bool AdvanceOne(
            out TraversalTransitionDefinition definition,
            out bool complete)
        {
            ReadOnlySpan<TraversalTransitionDefinition> baked = _map == null
                ? ReadOnlySpan<TraversalTransitionDefinition>.Empty
                : _map.TransitionSpan;
            int overlayCount = _overlay?.TransitionCount ?? 0;
            if (_bakedOrdinal >= baked.Length && _overlayOrdinal >= overlayCount)
            {
                definition = default;
                complete = true;
                return false;
            }
            TraversalTransitionOverlayOperation overlay = _overlayOrdinal < overlayCount
                ? _overlay!.GetTransitionAt(_overlayOrdinal)
                : default;
            int comparison = _bakedOrdinal >= baked.Length
                ? 1
                : _overlayOrdinal >= overlayCount
                    ? -1
                    : string.CompareOrdinal(baked[_bakedOrdinal].Id, overlay.Id);
            if (comparison < 0)
            {
                definition = baked[_bakedOrdinal++];
                complete = false;
                return true;
            }
            if (comparison == 0)
                _bakedOrdinal++;
            _overlayOrdinal++;
            complete = false;
            if (overlay.Kind == TraversalTransitionOverlayOperationKind.Upsert)
            {
                definition = overlay.Transition;
                return true;
            }
            definition = default;
            return false;
        }
    }
}
