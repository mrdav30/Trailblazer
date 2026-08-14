//=======================================================================
// NavigationCompositionIndex.UpdateWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationCompositionIndex
{
    /// <summary>Advances actual persistent structural-index and weak-component writes.</summary>
    internal sealed class UpdateWork
    {
        private readonly NavigationCompositionIndex _source;
        private readonly NavigationInstanceDirectory _directory;
        private readonly NavigationExplicitConnectionIndex _explicitConnections;
        private readonly NavigationAutomaticSeamIndex _automaticSeams;
        private readonly PersistentStringMap<bool> _changes;
        private readonly long _compositionVersion;
        private readonly long _componentVersion;
        private readonly NavigationCompositionWorkspace _workspace;
        private readonly NavigationStringStampSet _domain;
        private readonly NavigationStringStampSet _rootKeySet;
        private readonly NavigationStringStampSet _buildVisited;
        private readonly string[] _queue;
        private readonly string[] _buildQueue;
        private NavigationPagedSequence<string>.Builder? _componentBuilder;
        private NavigationPagedSequence<string>.Enumerator _pendingMemberEnumerator;
        private string? _componentIdentity;
        private PersistentStringMap<NavigationStructuralNode> _nodes;
        private PersistentStringMap<NavigationIncomingDependencyRecord> _incoming;
        private PersistentStringMap<NavigationStructuralComponent> _components;
        private PersistentStringMap<string> _membership;
        private long _nodeBytes;
        private long _incomingBytes;
        private long _componentBytes;
        private int _nodePages;
        private int _incomingPages;
        private int _componentPages;
        private Stage _stage;
        private int _changeIndex;
        private bool _changedNodeConsumed;
        private StructuralNodeWork? _nodeWork;
        private NavigationStructuralNode? _oldNode;
        private NavigationStructuralNode? _nextNode;
        private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _oldLinks;
        private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _nextLinks;
        private NavigationStructuralLink _oldLink;
        private NavigationStructuralLink _nextLink;
        private int _oldLinksRemaining;
        private int _nextLinksRemaining;
        private bool _oldLinkReady;
        private bool _nextLinkReady;
        private bool _reverseInitialized;
        private readonly string[] _rootKeys;
        private int _rootKeyCount;
        private int _rootIndex;
        private ComponentMemberCursor? _componentMemberCursor;
        private int _queueRead;
        private int _queueWrite;
        private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _edges;
        private int _edgesRemaining;
        private int _incomingIndex;
        private bool _queueNodeConsumed;
        private readonly string[] _cleanupRoots;
        private int _cleanupRootCount;
        private int _buildStartIndex;
        private int _buildRead;
        private int _buildWrite;
        private int _buildMemberCount;
        private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _buildEdges;
        private int _buildEdgesRemaining;
        private int _buildIncomingIndex;
        private bool _buildNodeConsumed;
        private bool _componentActive;
        private bool _componentTraversalComplete;
        private int _publishMemberIndex;
        private NavigationStructuralComponent? _pendingComponent;
        private int _copiedNodes;
        private int _copiedReverse;
        private int _copiedComponents;
        private int _copiedMemberships;
        private int _visitedMaps;
        private int _visitedEdges;
        private int _copiedPersistentPages;
        private long _payloadAdditionalBytes;
        private int _payloadAdditionalPages;
        private PersistentStringMap<PersistentStringMap<int>> _incomingJournal =
            PersistentStringMap<PersistentStringMap<int>>.Empty;
        private int _incomingJournalValuePages;
        private string? _incomingRebuildDestination;
        private PersistentStringMap<int>? _incomingRebuildChanges;
        private NavigationIncomingDependencyRecord? _incomingRebuildSource;
        private PersistentStringMap<int> _incomingRebuildSources =
            PersistentStringMap<int>.Empty;
        private int _incomingRebuildSourceIndex;
        private int _incomingRebuildChangeIndex;

        internal UpdateWork(
            NavigationCompositionIndex source,
            NavigationInstanceDirectory directory,
            PersistentStringMap<bool> changedMapIds,
            long version,
            NavigationCompositionWorkspace workspace)
            : this(
                source,
                directory,
                NavigationExplicitConnectionIndex.Empty,
                NavigationAutomaticSeamIndex.Empty,
                changedMapIds,
                version,
                version,
                workspace)
        {
        }

        internal UpdateWork(
            NavigationCompositionIndex source,
            NavigationInstanceDirectory directory,
            NavigationExplicitConnectionIndex explicitConnections,
            PersistentStringMap<bool> changedMapIds,
            long version,
            NavigationCompositionWorkspace workspace)
            : this(
                source,
                directory,
                explicitConnections,
                NavigationAutomaticSeamIndex.Empty,
                changedMapIds,
                version,
                version,
                workspace)
        {
        }

        internal UpdateWork(
            NavigationCompositionIndex source,
            NavigationInstanceDirectory directory,
            NavigationExplicitConnectionIndex explicitConnections,
            NavigationAutomaticSeamIndex automaticSeams,
            PersistentStringMap<bool> changedMapIds,
            long compositionVersion,
            long componentVersion,
            NavigationCompositionWorkspace workspace)
        {
            _source = source;
            _directory = directory;
            _explicitConnections = explicitConnections;
            _automaticSeams = automaticSeams;
            _changes = changedMapIds;
            _compositionVersion = compositionVersion;
            _componentVersion = componentVersion;
            _workspace = workspace;
            _workspace.Reset();
            _nodes = source._nodes;
            _incoming = source._incoming;
            _components = source._components;
            _membership = source._componentMembership;
            _nodeBytes = source._nodeValueBytes;
            _incomingBytes = source._incomingValueBytes;
            _componentBytes = source._componentValueBytes;
            _nodePages = source._nodeValuePages;
            _incomingPages = source._incomingValuePages;
            _componentPages = source._componentValuePages;
            _domain = workspace.Domain;
            _rootKeySet = workspace.RootKeySet;
            _buildVisited = workspace.BuildVisited;
            _queue = workspace.DomainQueue;
            _buildQueue = workspace.BuildQueue;
            _rootKeys = workspace.RootKeys;
            _cleanupRoots = _rootKeys;
        }

        internal bool IsComplete => _stage == Stage.Complete;

        internal NavigationCompositionIndex Result { get; private set; } = null!;

        internal int CopiedNodeRecords => _copiedNodes;

        internal int CopiedReverseRecords => _copiedReverse;

        internal int CopiedComponentRecords => _copiedComponents;

        internal int CopiedMembershipRecords => _copiedMemberships;

        internal int RetainedCopiedPersistentPages => GetRetainedCopiedPersistentPages();

        internal long PayloadAdditionalRetainedBytes => _payloadAdditionalBytes;

        internal int PayloadAdditionalPersistentPages => _payloadAdditionalPages;

        internal long NonPayloadRetainedBytes => checked(
            RetainedBytes - PayloadAdditionalRetainedBytes);

        internal int NonPayloadPersistentPageCount => checked(
            PersistentPageCount - PayloadAdditionalPersistentPages);

        internal long RetainedBytes => checked(
            192L
            + _nodes.RetainedBytes
            + _incoming.RetainedBytes
            + _components.RetainedBytes
            + _membership.RetainedBytes
            + _nodeBytes
            + _incomingBytes
            + _componentBytes
            + _payloadAdditionalBytes
            + GetIncomingJournalScratchBytes()
            + GetIncomingRebuildScratchBytes()
            + _workspace.RetainedBytes
            + (_componentBuilder?.RetainedBytes ?? 0)
            + (_componentMemberCursor?.RetainedBytes ?? 0)
            + (_nodeWork?.RetainedBytes ?? 0)
            + (_pendingComponent?.RetainedBytes ?? 0)
            + (GetRetainedCopiedPersistentPages() * 64L));

        internal static long GetMinimumScratchBytes(
            int sourceMapCount,
            int candidateMapCount,
            int changedMapCount)
        {
            int capacity = Math.Max(1, Math.Max(sourceMapCount, candidateMapCount));
            return checked(
                192L
                + ((long)capacity * IntPtr.Size)
                + NavigationCompositionWorkspace.GetRetainedBytes(capacity));
        }

        internal int PersistentPageCount => checked(
            4
            + _nodes.PersistentNodeCount
            + _incoming.PersistentNodeCount
            + _components.PersistentNodeCount
            + _membership.PersistentNodeCount
            + _nodePages
            + _incomingPages
            + _componentPages
            + _payloadAdditionalPages
            + _incomingJournal.PersistentNodeCount
            + _incomingJournalValuePages
            + _incomingRebuildSources.PersistentNodeCount
            + (_componentBuilder?.PersistentPageCount ?? 0)
            + (_componentMemberCursor?.PersistentPageCount ?? 0)
            + (_nodeWork?.PersistentPageCount ?? 0)
            + (_pendingComponent?.PersistentPageCount ?? 0)
            + GetRetainedCopiedPersistentPages());

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            while (true)
            {
                switch (_stage)
                {
                    case Stage.CaptureNodes:
                        if (!AdvanceNodeCapture(meter))
                            return false;
                        break;
                    case Stage.ApplyIncomingJournal:
                        if (!AdvanceIncomingJournal(meter))
                            return false;
                        break;
                    case Stage.SeedOldComponents:
                        if (!AdvanceOldComponentSeeds(meter))
                            return false;
                        break;
                    case Stage.ExpandAffectedDomain:
                        if (!AdvanceDomainExpansion(meter))
                            return false;
                        break;
                    case Stage.CleanupComponents:
                        if (!AdvanceComponentCleanup(meter))
                            return false;
                        break;
                    case Stage.BuildComponents:
                        if (!AdvanceComponentBuild(meter))
                            return false;
                        break;
                    default:
                        return true;
                }
            }
        }

        private bool AdvanceNodeCapture(MaintenanceWorkMeter meter)
        {
            while (_changeIndex < _changes.Count)
            {
                string mapId = _changes.GetKeyAt(_changeIndex);
                if (!_changedNodeConsumed)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    _changedNodeConsumed = true;
                    _source._nodes.TryGetValue(mapId, out _oldNode!);
                    if (_source.TryGetRootComponent(mapId, out NavigationStructuralComponent oldRoot)
                        && _rootKeySet.Add(oldRoot.Key))
                    {
                        _rootKeys[_rootKeyCount++] = oldRoot.Key;
                    }
                    if (_directory.TryGet(mapId, out NavigationMapInstance next))
                    {
                        _nodeWork = new StructuralNodeWork(
                            next,
                            _explicitConnections,
                            _automaticSeams,
                            _oldNode);
                    }
                }
                if (_nodeWork != null && !_nodeWork.Advance(meter))
                    return false;
                _nextNode = _nodeWork?.Result;

                if (!AdvanceReverseIndex(mapId, meter))
                    return false;

                if (!ReferenceEquals(_oldNode, _nextNode) && _oldNode != null)
                {
                    _nodes = _nodes.Remove(mapId, out _, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                    _nodeBytes = checked(_nodeBytes - _oldNode.RetainedBytes);
                    _nodePages = checked(_nodePages - _oldNode.PersistentPageCount);
                    _copiedNodes++;
                }
                if (!ReferenceEquals(_oldNode, _nextNode) && _nextNode != null)
                {
                    _nodes = _nodes.Set(mapId, _nextNode, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                    _nodeBytes = checked(_nodeBytes + _nextNode.RetainedBytes);
                    _nodePages = checked(_nodePages + _nextNode.PersistentPageCount);
                    _payloadAdditionalBytes = checked(
                        _payloadAdditionalBytes + _nextNode.RetainedBytes);
                    _payloadAdditionalPages = checked(
                        _payloadAdditionalPages + _nextNode.PersistentPageCount);
                    _copiedNodes++;
                    AddDomain(mapId);
                }
                ResetChangeCursor();
                _changeIndex++;
            }
            _stage = Stage.ApplyIncomingJournal;
            return true;
        }

        private bool AdvanceReverseIndex(string mapId, MaintenanceWorkMeter meter)
        {
            if (ReferenceEquals(_oldNode, _nextNode))
                return true;
            if (!_reverseInitialized)
            {
                _oldLinksRemaining = _oldNode?.LinkCount ?? 0;
                _nextLinksRemaining = _nextNode?.LinkCount ?? 0;
                if (_oldNode != null)
                    _oldLinks = _oldNode.GetLinkEnumerator();
                if (_nextNode != null)
                    _nextLinks = _nextNode.GetLinkEnumerator();
                _reverseInitialized = true;
            }
            while (_oldLinksRemaining != 0 || _nextLinksRemaining != 0)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                if (!_oldLinkReady && _oldLinksRemaining != 0)
                {
                    _oldLinks.MoveNext();
                    _oldLink = _oldLinks.Current;
                    _oldLinkReady = true;
                }
                if (!_nextLinkReady && _nextLinksRemaining != 0)
                {
                    _nextLinks.MoveNext();
                    _nextLink = _nextLinks.Current;
                    _nextLinkReady = true;
                }
                int comparison = !_oldLinkReady
                    ? 1
                    : !_nextLinkReady
                        ? -1
                        : string.CompareOrdinal(
                            _oldLink.DestinationMapId,
                            _nextLink.DestinationMapId);
                string destination;
                int count;
                if (comparison < 0)
                {
                    destination = _oldLink.DestinationMapId;
                    count = 0;
                    _oldLinkReady = false;
                    _oldLinksRemaining--;
                }
                else
                {
                    destination = _nextLink.DestinationMapId;
                    count = _nextLink.Count;
                    _nextLinkReady = false;
                    _nextLinksRemaining--;
                    if (comparison == 0)
                    {
                        int oldCount = _oldLink.Count;
                        _oldLinkReady = false;
                        _oldLinksRemaining--;
                        if (oldCount == count)
                        {
                            continue;
                        }
                    }
                }
                RecordIncomingChange(destination, mapId, count);
                _copiedReverse++;
            }
            return true;
        }

        private bool AdvanceOldComponentSeeds(MaintenanceWorkMeter meter)
        {
            while (_rootIndex < _rootKeyCount)
            {
                if (_componentMemberCursor == null)
                {
                    if (!_source._components.TryGetValue(
                            _rootKeys[_rootIndex],
                            out NavigationStructuralComponent root))
                    {
                        _rootIndex++;
                        continue;
                    }
                    _componentMemberCursor = new ComponentMemberCursor(root);
                }
                if (!_componentMemberCursor.TryAdvance(
                        meter,
                        out string? treeKey,
                        out string? member))
                    return false;
                _ = treeKey;
                if (member != null && _nodes.ContainsKey(member))
                    AddDomain(member);
                if (!_componentMemberCursor.IsComplete)
                    continue;
                _componentMemberCursor = null;
                _cleanupRoots[_cleanupRootCount++] = _rootKeys[_rootIndex];
                _rootIndex++;
            }
            _queueRead = 0;
            _rootKeyCount = 0;
            _stage = Stage.ExpandAffectedDomain;
            return true;
        }

        private bool AdvanceDomainExpansion(MaintenanceWorkMeter meter)
        {
            while (_queueRead < _queueWrite)
            {
                string mapId = _queue[_queueRead];
                if (!_queueNodeConsumed)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    _queueNodeConsumed = true;
                    _visitedMaps++;
                    _nodes.TryGetValue(mapId, out NavigationStructuralNode capturedNode);
                    _edges = capturedNode.GetLinkEnumerator();
                    _edgesRemaining = capturedNode.LinkCount;
                }
                if (_source.TryGetRootComponent(mapId, out NavigationStructuralComponent root)
                    && _rootKeySet.Add(root.Key))
                {
                    _cleanupRoots[_cleanupRootCount++] = root.Key;
                }
                while (_edgesRemaining != 0)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    _edges.MoveNext();
                    NavigationStructuralLink link = _edges.Current;
                    _edgesRemaining--;
                    _visitedEdges = checked(_visitedEdges + link.Count);
                    if (_nodes.ContainsKey(link.DestinationMapId))
                        AddDomain(link.DestinationMapId);
                }
                _incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord incoming);
                while (_incomingIndex < (incoming?.Count ?? 0))
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    NavigationIncomingDependency source = incoming!.GetAt(_incomingIndex++);
                    _visitedEdges = checked(_visitedEdges + source.Count);
                    if (_nodes.ContainsKey(source.SourceMapId))
                        AddDomain(source.SourceMapId);
                }
                _queueRead++;
                ResetQueueCursor();
            }
            _rootIndex = 0;
            _stage = Stage.CleanupComponents;
            return true;
        }

        private bool AdvanceComponentCleanup(MaintenanceWorkMeter meter)
        {
            while (_rootIndex < _cleanupRootCount)
            {
                string rootKey = _cleanupRoots[_rootIndex];
                if (_componentMemberCursor == null)
                {
                    if (!_source._components.TryGetValue(
                            rootKey,
                            out NavigationStructuralComponent root))
                    {
                        _rootIndex++;
                        continue;
                    }
                    _componentMemberCursor = new ComponentMemberCursor(root);
                }
                if (!_componentMemberCursor.TryAdvance(
                        meter,
                        out string? treeKey,
                        out string? member))
                    return false;
                _ = treeKey;
                if (member != null)
                {
                    _membership = _membership.Remove(
                        member,
                        out bool membershipRemoved,
                        out int membershipCopies);
                    RecordPersistentCopies(membershipCopies);
                    if (membershipRemoved)
                        _copiedMemberships++;
                }
                if (!_componentMemberCursor.IsComplete)
                    continue;
                _componentMemberCursor = null;
                if (_components.TryGetValue(rootKey, out NavigationStructuralComponent removed))
                {
                    _components = _components.Remove(rootKey, out _, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                    _componentBytes = checked(_componentBytes - removed.RetainedBytes);
                    _componentPages = checked(
                        _componentPages - removed.PersistentPageCount);
                    _copiedComponents++;
                }
                _rootIndex++;
            }
            _stage = Stage.BuildComponents;
            return true;
        }

        private bool AdvanceComponentBuild(MaintenanceWorkMeter meter)
        {
            while (true)
            {
                if (_pendingComponent != null)
                {
                    while (_publishMemberIndex < _buildMemberCount)
                    {
                        if (!meter.TryConsumeComponentNodes(1))
                            return false;
                        _pendingMemberEnumerator.MoveNext();
                        _membership = _membership.Set(
                            _pendingMemberEnumerator.Current,
                            _pendingComponent.Key,
                            out int copiedNodes);
                        _publishMemberIndex++;
                        RecordPersistentCopies(copiedNodes);
                        _copiedMemberships++;
                    }
                    _components = _components.Set(
                        _pendingComponent.Key,
                        _pendingComponent,
                        out int componentCopies);
                    RecordPersistentCopies(componentCopies);
                    _componentBytes = checked(
                        _componentBytes + _pendingComponent.RetainedBytes);
                    _componentPages = checked(
                        _componentPages + _pendingComponent.PersistentPageCount);
                    _payloadAdditionalBytes = checked(
                        _payloadAdditionalBytes + _pendingComponent.RetainedBytes);
                    _payloadAdditionalPages = checked(
                        _payloadAdditionalPages + _pendingComponent.PersistentPageCount);
                    _copiedComponents++;
                    _pendingComponent = null;
                    _buildMemberCount = 0;
                    _publishMemberIndex = 0;
                }

                if (_componentTraversalComplete)
                {
                    NavigationPagedSequence<string> members = _componentBuilder!.Seal();
                    _pendingComponent = NavigationStructuralComponent.CreateFlat(
                        members,
                        _componentIdentity!,
                        _componentVersion);
                    _pendingMemberEnumerator = members.GetEnumerator();
                    _componentBuilder = null;
                    _componentIdentity = null;
                    _componentActive = false;
                    _componentTraversalComplete = false;
                    _buildWrite = 0;
                    continue;
                }

                if (!_componentActive)
                {
                    while (_buildStartIndex < _queueWrite)
                    {
                        string start = _queue[_buildStartIndex++];
                        if (!TryVisitBuild(start))
                            continue;
                        _buildQueue[0] = start;
                        _buildRead = 0;
                        _buildWrite = 1;
                        _componentActive = true;
                        break;
                    }
                }
                if (!_componentActive)
                {
                    Result = new NavigationCompositionIndex(
                        _compositionVersion,
                        _nodes,
                        _incoming,
                        _components,
                        _membership,
                        _nodeBytes,
                        _incomingBytes,
                        _componentBytes,
                        _nodePages,
                        _incomingPages,
                        _componentPages,
                        new NavigationCompositionUpdateCounters(
                            _changes.Count,
                            _visitedMaps,
                            _visitedEdges,
                            _copiedNodes,
                            _copiedReverse,
                            _copiedComponents,
                            _copiedMemberships,
                            Math.Max(0, _source.ComponentCount - _cleanupRootCount)));
                    _stage = Stage.Complete;
                    return true;
                }
                while (_buildRead < _buildWrite)
                {
                    string mapId = _buildQueue[_buildRead];
                    if (!_buildNodeConsumed)
                    {
                        if (!meter.TryConsumeComponentNodes(1))
                            return false;
                        _buildNodeConsumed = true;
                        _componentBuilder ??= new NavigationPagedSequence<string>.Builder(
                            IntPtr.Size);
                        _componentBuilder.Append(mapId);
                        if (_componentIdentity == null
                            || string.CompareOrdinal(mapId, _componentIdentity) < 0)
                        {
                            _componentIdentity = mapId;
                        }
                        _buildMemberCount++;
                        _nodes.TryGetValue(mapId, out NavigationStructuralNode capturedNode);
                        _buildEdges = capturedNode.GetLinkEnumerator();
                        _buildEdgesRemaining = capturedNode.LinkCount;
                    }
                    while (_buildEdgesRemaining != 0)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        _buildEdges.MoveNext();
                        string neighbor = _buildEdges.Current.DestinationMapId;
                        _buildEdgesRemaining--;
                        if (_domain.Contains(neighbor) && TryVisitBuild(neighbor))
                        {
                            _buildQueue[_buildWrite++] = neighbor;
                        }
                    }
                    _incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord incoming);
                    while (_buildIncomingIndex < (incoming?.Count ?? 0))
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        string neighbor = incoming!.GetAt(_buildIncomingIndex++).SourceMapId;
                        if (_domain.Contains(neighbor) && TryVisitBuild(neighbor))
                        {
                            _buildQueue[_buildWrite++] = neighbor;
                        }
                    }
                    _buildRead++;
                    _buildNodeConsumed = false;
                    _buildEdges = default;
                    _buildEdgesRemaining = 0;
                    _buildIncomingIndex = 0;
                }
                if (_buildMemberCount == 0)
                    _componentActive = false;
                else
                {
                    _componentTraversalComplete = true;
                }
            }
        }

        private void AddDomain(string mapId)
        {
            if (_domain.Add(mapId))
                _queue[_queueWrite++] = mapId;
        }

        private bool TryVisitBuild(string mapId)
        {
            return _buildVisited.Add(mapId);
        }

        private void RecordPersistentCopies(int copiedNodes)
        {
            _copiedPersistentPages = checked(_copiedPersistentPages + copiedNodes);
        }

        private void RecordIncomingChange(
            string destination,
            string source,
            int count)
        {
            bool alreadyTracked = _incomingJournal.TryGetValue(
                destination,
                out PersistentStringMap<int> changes);
            changes ??= PersistentStringMap<int>.Empty;
            PersistentStringMap<int> next = changes.Set(source, count);
            _incomingJournalValuePages = checked(
                _incomingJournalValuePages
                + next.PersistentNodeCount
                - (alreadyTracked ? changes.PersistentNodeCount : 0));
            _incomingJournal = _incomingJournal.Set(destination, next);
        }

        private bool AdvanceIncomingJournal(MaintenanceWorkMeter meter)
        {
            while (_incomingJournal.Count != 0)
            {
                if (_incomingRebuildDestination == null)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _incomingRebuildDestination = _incomingJournal.GetKeyAt(0);
                    _incomingRebuildChanges = _incomingJournal.GetValueAt(0);
                    _source._incoming.TryGetValue(
                        _incomingRebuildDestination,
                        out _incomingRebuildSource!);
                }
                int sourceCount = _incomingRebuildSource?.Count ?? 0;
                PersistentStringMap<int> changes = _incomingRebuildChanges!;
                while (_incomingRebuildSourceIndex < sourceCount
                    || _incomingRebuildChangeIndex < changes.Count)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    bool hasSource = _incomingRebuildSourceIndex < sourceCount;
                    bool hasChange = _incomingRebuildChangeIndex
                        < changes.Count;
                    NavigationIncomingDependency source = hasSource
                        ? _incomingRebuildSource!.GetAt(_incomingRebuildSourceIndex)
                        : default;
                    string? changedSource = hasChange
                        ? changes.GetKeyAt(_incomingRebuildChangeIndex)
                        : null;
                    int comparison = !hasSource
                        ? 1
                        : !hasChange
                            ? -1
                            : string.CompareOrdinal(source.SourceMapId, changedSource);
                    string sourceMapId;
                    int count;
                    if (comparison < 0)
                    {
                        sourceMapId = source.SourceMapId;
                        count = source.Count;
                        _incomingRebuildSourceIndex++;
                    }
                    else
                    {
                        sourceMapId = changedSource!;
                        count = changes.GetValueAt(
                            _incomingRebuildChangeIndex++);
                        if (comparison == 0)
                            _incomingRebuildSourceIndex++;
                    }
                    if (count != 0)
                    {
                        _incomingRebuildSources = _incomingRebuildSources.Set(
                            sourceMapId,
                            count);
                    }
                }
                PublishIncomingRebuild();
            }
            _stage = Stage.SeedOldComponents;
            return true;
        }

        private void PublishIncomingRebuild()
        {
            if (_incomingRebuildSource != null)
            {
                _incomingBytes = checked(
                    _incomingBytes - _incomingRebuildSource.RetainedBytes);
                _incomingPages = checked(
                    _incomingPages - _incomingRebuildSource.PersistentPageCount);
            }
            if (_incomingRebuildSources.Count == 0)
            {
                _incoming = _incoming.Remove(
                    _incomingRebuildDestination!,
                    out _,
                    out int copiedNodes);
                RecordPersistentCopies(copiedNodes);
            }
            else
            {
                var next = new NavigationIncomingDependencyRecord(
                    _incomingRebuildSources);
                _incoming = _incoming.Set(
                    _incomingRebuildDestination!,
                    next,
                    out int copiedNodes);
                RecordPersistentCopies(copiedNodes);
                _incomingBytes = checked(_incomingBytes + next.RetainedBytes);
                _incomingPages = checked(_incomingPages + next.PersistentPageCount);
                _payloadAdditionalBytes = checked(
                    _payloadAdditionalBytes + next.RetainedBytes);
                _payloadAdditionalPages = checked(
                    _payloadAdditionalPages + next.PersistentPageCount);
            }
            _incomingJournal = _incomingJournal.Remove(
                _incomingRebuildDestination!,
                out _,
                out _);
            _incomingJournalValuePages = checked(
                _incomingJournalValuePages
                - _incomingRebuildChanges!.PersistentNodeCount);
            ResetIncomingRebuildCursor();
        }

        private long GetIncomingJournalScratchBytes() =>
            _incomingJournal.Count == 0
                ? 0
                : checked(
                    _incomingJournal.RetainedBytes
                    + (32L * _incomingJournal.Count)
                    + (64L * _incomingJournalValuePages));

        private long GetIncomingRebuildScratchBytes() =>
            _incomingRebuildSources.Count == 0
                ? 0
                : _incomingRebuildSources.RetainedBytes;

        private void ResetIncomingRebuildCursor()
        {
            _incomingRebuildDestination = null;
            _incomingRebuildChanges = null;
            _incomingRebuildSource = null;
            _incomingRebuildSources = PersistentStringMap<int>.Empty;
            _incomingRebuildSourceIndex = 0;
            _incomingRebuildChangeIndex = 0;
        }

        private int GetRetainedCopiedPersistentPages()
        {
            int reachableNodes = checked(
                _nodes.PersistentNodeCount
                + _incoming.PersistentNodeCount
                + _components.PersistentNodeCount
                + _membership.PersistentNodeCount);
            // A live copied node is both a recorded allocation and reachable from a current root.
            // The smaller of those two upper bounds cannot undercount live COW ownership.
            return Math.Min(_copiedPersistentPages, reachableNodes);
        }

        private void ResetChangeCursor()
        {
            _changedNodeConsumed = false;
            _nodeWork = null;
            _oldNode = null;
            _nextNode = null;
            _oldLinks = default;
            _nextLinks = default;
            _oldLinksRemaining = 0;
            _nextLinksRemaining = 0;
            _oldLinkReady = false;
            _nextLinkReady = false;
            _reverseInitialized = false;
        }

        private void ResetQueueCursor()
        {
            _queueNodeConsumed = false;
            _edges = default;
            _edgesRemaining = 0;
            _incomingIndex = 0;
        }

        private enum Stage
        {
            CaptureNodes,
            ApplyIncomingJournal,
            SeedOldComponents,
            ExpandAffectedDomain,
            CleanupComponents,
            BuildComponents,
            Complete
        }
    }

    private sealed class StructuralNodeWork
    {
        private readonly NavigationMapInstance _instance;
        private readonly NavigationExplicitConnectionIndex _explicitConnections;
        private readonly NavigationAutomaticSeamIndex _automaticSeams;
        private readonly NavigationStructuralNode? _oldNode;
        private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _oldLinks;
        private PersistentStringMap<int> _counts = PersistentStringMap<int>.Empty;
        private int _index;
        private int _seamRemaining;
        private int _seamCurrent;
        private string? _seamDestination;
        private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _seamLinks;
        private bool _ownersCaptured;
        private bool _seamsCaptured;
        private bool _matchesOld;
        private NavigationPagedSequence<NavigationStructuralLink>.Builder? _links;

        internal StructuralNodeWork(
            NavigationMapInstance instance,
            NavigationExplicitConnectionIndex explicitConnections,
            NavigationAutomaticSeamIndex automaticSeams,
            NavigationStructuralNode? oldNode)
        {
            _instance = instance;
            _explicitConnections = explicitConnections;
            _automaticSeams = automaticSeams;
            _oldNode = oldNode;
            _matchesOld = oldNode != null;
            if (oldNode != null)
                _oldLinks = oldNode.GetLinkEnumerator();
        }

        internal NavigationStructuralNode Result { get; private set; } = null!;

        internal long RetainedBytes => checked(
            40L
            + _counts.RetainedBytes
            + (_links?.RetainedBytes
                ?? (Result != null && !ReferenceEquals(Result, _oldNode)
                    ? Result.RetainedBytes
                    : 0)));

        internal int PersistentPageCount => checked(
            1
            + _counts.PersistentNodeCount
            + (_links?.PersistentPageCount
                ?? (Result != null && !ReferenceEquals(Result, _oldNode)
                    ? Result.PersistentPageCount
                    : 0)));

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            if (!_ownersCaptured)
            {
                int count = _explicitConnections.GetSourceOwnerCount(_instance.MapId);
                while (_index < count)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    NavigationExplicitConnectionRecord record =
                        _explicitConnections.GetSourceOwnerAt(_instance.MapId, _index++);
                    if (!record.IsActive)
                        continue;
                    string destination = record.Destination.MapId;
                    _counts.TryGetValue(destination, out int current);
                    _counts = _counts.Set(destination, checked(current + 1));
                }
                _ownersCaptured = true;
                _index = 0;
            }
            if (!_seamsCaptured)
            {
                if (_index == 0 && _seamDestination == null)
                    _seamLinks = _automaticSeams.GetStructuralLinks(_instance.MapId).GetEnumerator();
                while (true)
                {
                    if (_seamDestination == null)
                    {
                        if (!_seamLinks.MoveNext())
                            break;
                        NavigationStructuralLink seamLink = _seamLinks.Current;
                        _seamDestination = seamLink.DestinationMapId;
                        _seamRemaining = seamLink.Count;
                        _counts.TryGetValue(_seamDestination, out _seamCurrent);
                    }
                    while (_seamRemaining > 0)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        _seamCurrent = checked(_seamCurrent + 1);
                        _seamRemaining--;
                        _counts = _counts.Set(_seamDestination, _seamCurrent);
                    }
                    _seamDestination = null;
                }
                _seamsCaptured = true;
                _index = 0;
            }
            while (_index < _counts.Count)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _links ??= new NavigationPagedSequence<NavigationStructuralLink>.Builder(16);
                var link = new NavigationStructuralLink(
                    _counts.GetKeyAt(_index),
                    _counts.GetValueAt(_index));
                _links.Append(link);
                if (_matchesOld
                    && (!_oldLinks.MoveNext() || !_oldLinks.Current.Equals(link)))
                {
                    _matchesOld = false;
                }
                _index++;
            }
            if (Result == null)
            {
                if (_matchesOld && !_oldLinks.MoveNext())
                {
                    Result = _oldNode!;
                }
                else
                {
                    Result = new NavigationStructuralNode(
                        _links?.Seal()
                            ?? NavigationPagedSequence<NavigationStructuralLink>.Empty);
                }
                _links = null;
            }
            return true;
        }
    }

    private sealed class ComponentMemberCursor
    {
        private readonly NavigationStructuralComponent _component;
        private NavigationPagedSequence<string>.Enumerator _members;
        private bool _rootConsumed;
        private int _membersConsumed;

        internal ComponentMemberCursor(NavigationStructuralComponent root)
        {
            _component = root;
            _members = root.FlatMembers.GetEnumerator();
        }

        internal bool IsComplete => _rootConsumed
            && _membersConsumed == _component.MemberCount;

        internal long RetainedBytes => 32L;

        internal int PersistentPageCount => 1;

        internal bool TryAdvance(
            MaintenanceWorkMeter meter,
            out string? treeKey,
            out string? member)
        {
            treeKey = null;
            member = null;
            if (!_rootConsumed)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                treeKey = _component.Key;
                _rootConsumed = true;
                return true;
            }
            if (_membersConsumed == _component.MemberCount)
                return true;
            if (!meter.TryConsumeComponentNodes(1))
                return false;
            _members.MoveNext();
            member = _members.Current;
            _membersConsumed++;
            return true;
        }
    }
}
