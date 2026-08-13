//=======================================================================
// NavigationCompositionIndex.UpdateWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationCompositionIndex
{
    /// <summary>Advances actual persistent structural-index and weak-component writes.</summary>
    internal sealed class UpdateWork
    {
        private readonly NavigationCompositionIndex _source;
        private readonly NavigationInstanceDirectory _directory;
        private readonly string[] _changes;
        private readonly long _version;
        private readonly SwiftHashSet<string> _domain;
        private readonly SwiftHashSet<string> _oldRoots;
        private readonly SwiftHashSet<string> _cleanupRootSet;
        private readonly int _hashCapacity;
        private PersistentStringMap<bool> _buildVisited = PersistentStringMap<bool>.Empty;
        private readonly string[] _queue;
        private readonly string[] _buildQueue;
        private readonly string[] _componentMembers;
        private readonly NavigationStructuralComponent[] _treeStack;
        private string[]? _pendingMembers;
        private PersistentStringMap<NavigationStructuralNode> _nodes;
        private PersistentStringMap<NavigationIncomingDependencyRecord> _incoming;
        private PersistentStringMap<NavigationStructuralComponent> _components;
        private PersistentStringMap<string> _membership;
        private PersistentStringMap<string> _aliases;
        private long _nodeBytes;
        private long _incomingBytes;
        private long _componentBytes;
        private Stage _stage;
        private int _changeIndex;
        private bool _changedNodeConsumed;
        private StructuralNodeWork? _nodeWork;
        private NavigationStructuralNode? _oldNode;
        private NavigationStructuralNode? _nextNode;
        private int _oldLinkIndex;
        private int _nextLinkIndex;
        private readonly string[] _rootKeys;
        private int _rootKeyCount;
        private int _rootIndex;
        private ComponentTreeCursor? _treeCursor;
        private int _queueRead;
        private int _queueWrite;
        private int _edgeIndex;
        private int _incomingIndex;
        private bool _queueNodeConsumed;
        private readonly string[] _cleanupRoots;
        private int _cleanupRootCount;
        private int _cleanupMemberIndex;
        private int _buildStartIndex;
        private int _buildRead;
        private int _buildWrite;
        private int _buildMemberCount;
        private int _buildEdgeIndex;
        private int _buildIncomingIndex;
        private bool _buildNodeConsumed;
        private bool _componentActive;
        private bool _componentTraversalComplete;
        private int _publishMemberIndex;
        private NavigationStructuralComponent? _pendingComponent;
        private int _prepareMemberIndex;
        private int _copiedNodes;
        private int _copiedReverse;
        private int _copiedComponents;
        private int _copiedMemberships;
        private int _visitedMaps;
        private int _visitedEdges;
        private int _copiedPersistentPages;

        internal UpdateWork(
            NavigationCompositionIndex source,
            NavigationInstanceDirectory directory,
            ReadOnlySpan<string> changedMapIds,
            long version)
        {
            _source = source;
            _directory = directory;
            _changes = NormalizeChanges(changedMapIds);
            _version = version;
            _nodes = source._nodes;
            _incoming = source._incoming;
            _components = source._components;
            _membership = source._componentMembership;
            _aliases = source._componentAliases;
            _nodeBytes = source._nodeValueBytes;
            _incomingBytes = source._incomingValueBytes;
            _componentBytes = source._componentValueBytes;
            int capacity = Math.Max(1, Math.Max(directory.Count, source._nodes.Count));
            _hashCapacity = GetHashCapacity(capacity);
            _domain = new SwiftHashSet<string>(_hashCapacity, StringComparer.Ordinal);
            _oldRoots = new SwiftHashSet<string>(_hashCapacity, StringComparer.Ordinal);
            _cleanupRootSet = new SwiftHashSet<string>(_hashCapacity, StringComparer.Ordinal);
            _queue = new string[capacity];
            _buildQueue = new string[capacity];
            _componentMembers = new string[capacity];
            _treeStack = new NavigationStructuralComponent[capacity];
            _rootKeys = new string[capacity];
            _cleanupRoots = new string[capacity];
        }

        internal bool IsComplete => _stage == Stage.Complete;

        internal NavigationCompositionIndex Result { get; private set; } = null!;

        internal int CopiedNodeRecords => _copiedNodes;

        internal int CopiedReverseRecords => _copiedReverse;

        internal int CopiedComponentRecords => _copiedComponents;

        internal int CopiedMembershipRecords => _copiedMemberships;

        internal ReadOnlySpan<string> AffectedMapIds => _queue.AsSpan(0, _queueWrite);

        internal long RetainedBytes => checked(
            192L
            + _nodes.RetainedBytes
            + _incoming.RetainedBytes
            + _components.RetainedBytes
            + _membership.RetainedBytes
            + _aliases.RetainedBytes
            + _nodeBytes
            + _incomingBytes
            + _componentBytes
            + _buildVisited.RetainedBytes
            + ((long)(_changes.Length
                + _queue.Length
                + _buildQueue.Length
                + _componentMembers.Length
                + _rootKeys.Length
                + _cleanupRoots.Length)
                * IntPtr.Size)
            + ((long)_treeStack.Length * IntPtr.Size)
            + ((long)(_pendingMembers?.Length ?? 0) * IntPtr.Size)
            + ((long)_hashCapacity * 3L * 16L)
            + (_treeCursor?.RetainedBytes ?? 0)
            + (_nodeWork?.RetainedBytes ?? 0)
            + (_pendingComponent?.RetainedBytes ?? 0)
            + (GetRetainedCopiedPersistentPages() * 64L));

        internal static long GetMinimumScratchBytes(
            int sourceMapCount,
            int candidateMapCount,
            int changedMapCount)
        {
            int capacity = Math.Max(1, Math.Max(sourceMapCount, candidateMapCount));
            int hashCapacity = GetHashCapacity(capacity);
            return checked(
                192L
                + ((long)(changedMapCount + (capacity * 6L)) * IntPtr.Size)
                + ((long)capacity * IntPtr.Size)
                + ((long)hashCapacity * 3L * 16L));
        }

        internal int PersistentPageCount => checked(
            5
            + _nodes.PersistentNodeCount
            + _incoming.PersistentNodeCount
            + _components.PersistentNodeCount
            + _membership.PersistentNodeCount
            + _aliases.PersistentNodeCount
            + _buildVisited.PersistentNodeCount
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
            while (_changeIndex < _changes.Length)
            {
                string mapId = _changes[_changeIndex];
                if (!_changedNodeConsumed)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    _changedNodeConsumed = true;
                    _source._nodes.TryGetValue(mapId, out _oldNode!);
                    if (_source.TryGetRootComponent(mapId, out NavigationStructuralComponent oldRoot)
                        && _oldRoots.Add(oldRoot.Key))
                    {
                        _rootKeys[_rootKeyCount++] = oldRoot.Key;
                    }
                    if (_directory.TryGet(mapId, out NavigationMapInstance next))
                        _nodeWork = new StructuralNodeWork(next);
                }
                if (_nodeWork != null && !_nodeWork.Advance(meter))
                    return false;
                _nextNode = _nodeWork?.Result;

                if (!AdvanceReverseIndex(mapId, meter))
                    return false;

                if (_oldNode != null)
                {
                    _nodes = _nodes.Remove(mapId, out _, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                    _nodeBytes = checked(_nodeBytes - _oldNode.RetainedBytes);
                    _copiedNodes++;
                }
                if (_nextNode != null)
                {
                    _nodes = _nodes.Set(mapId, _nextNode, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                    _nodeBytes = checked(_nodeBytes + _nextNode.RetainedBytes);
                    _copiedNodes++;
                    AddDomain(mapId);
                }
                ResetChangeCursor();
                _changeIndex++;
            }
            _stage = Stage.SeedOldComponents;
            return true;
        }

        private bool AdvanceReverseIndex(string mapId, MaintenanceWorkMeter meter)
        {
            ReadOnlySpan<NavigationStructuralLink> oldLinks = _oldNode == null
                ? ReadOnlySpan<NavigationStructuralLink>.Empty
                : _oldNode.Links;
            ReadOnlySpan<NavigationStructuralLink> nextLinks = _nextNode == null
                ? ReadOnlySpan<NavigationStructuralLink>.Empty
                : _nextNode.Links;
            while (_oldLinkIndex < oldLinks.Length || _nextLinkIndex < nextLinks.Length)
            {
                int comparison = _oldLinkIndex >= oldLinks.Length
                    ? 1
                    : _nextLinkIndex >= nextLinks.Length
                        ? -1
                        : string.CompareOrdinal(
                            oldLinks[_oldLinkIndex].DestinationMapId,
                            nextLinks[_nextLinkIndex].DestinationMapId);
                string destination;
                int count;
                if (comparison < 0)
                {
                    destination = oldLinks[_oldLinkIndex++].DestinationMapId;
                    count = 0;
                }
                else
                {
                    destination = nextLinks[_nextLinkIndex].DestinationMapId;
                    count = nextLinks[_nextLinkIndex++].Count;
                    if (comparison == 0)
                    {
                        if (oldLinks[_oldLinkIndex].Count == count)
                        {
                            _oldLinkIndex++;
                            continue;
                        }
                        _oldLinkIndex++;
                    }
                }
                if (!meter.TryConsumeDependencyEntries(1))
                {
                    if (comparison < 0)
                        _oldLinkIndex--;
                    else
                    {
                        _nextLinkIndex--;
                        if (comparison == 0)
                            _oldLinkIndex--;
                    }
                    return false;
                }
                _incoming = SetIncoming(
                    _incoming,
                    destination,
                    mapId,
                    count,
                    ref _incomingBytes,
                    out bool copied,
                    out int copiedNodes);
                RecordPersistentCopies(copiedNodes);
                if (copied)
                    _copiedReverse++;
            }
            return true;
        }

        private bool AdvanceOldComponentSeeds(MaintenanceWorkMeter meter)
        {
            while (_rootIndex < _rootKeyCount)
            {
                if (_treeCursor == null)
                {
                    if (!_source._components.TryGetValue(
                            _rootKeys[_rootIndex],
                            out NavigationStructuralComponent root))
                    {
                        _rootIndex++;
                        continue;
                    }
                    _treeCursor = new ComponentTreeCursor(root, _treeStack);
                }
                if (!_treeCursor.TryAdvance(meter, out string? treeKey, out string? member))
                    return false;
                if (treeKey != null)
                {
                    _aliases = _aliases.Remove(treeKey, out _, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                }
                if (member != null && _nodes.ContainsKey(member))
                    AddDomain(member);
                if (!_treeCursor.IsComplete)
                    continue;
                _treeCursor = null;
                _rootIndex++;
            }
            _queueRead = 0;
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
                }
                _nodes.TryGetValue(mapId, out NavigationStructuralNode node);
                if (_source.TryGetRootComponent(mapId, out NavigationStructuralComponent root)
                    && _cleanupRootSet.Add(root.Key))
                {
                    _cleanupRoots[_cleanupRootCount++] = root.Key;
                }
                ReadOnlySpan<NavigationStructuralLink> links = node.Links;
                while (_edgeIndex < links.Length)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    NavigationStructuralLink link = links[_edgeIndex++];
                    _visitedEdges = checked(_visitedEdges + link.Count);
                    if (_nodes.ContainsKey(link.DestinationMapId))
                        AddDomain(link.DestinationMapId);
                }
                ReadOnlySpan<NavigationIncomingDependency> sources =
                    _incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord incoming)
                        ? incoming.Sources
                        : ReadOnlySpan<NavigationIncomingDependency>.Empty;
                while (_incomingIndex < sources.Length)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    NavigationIncomingDependency source = sources[_incomingIndex++];
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
                if (_treeCursor == null)
                {
                    if (!_source._components.TryGetValue(
                            rootKey,
                            out NavigationStructuralComponent root))
                    {
                        _rootIndex++;
                        continue;
                    }
                    _treeCursor = new ComponentTreeCursor(root, _treeStack);
                }
                if (!_treeCursor.TryAdvance(meter, out string? treeKey, out _))
                    return false;
                if (treeKey != null)
                {
                    _aliases = _aliases.Remove(treeKey, out _, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                }
                if (!_treeCursor.IsComplete)
                    continue;
                _treeCursor = null;
                if (_components.TryGetValue(rootKey, out NavigationStructuralComponent removed))
                {
                    _components = _components.Remove(rootKey, out _, out int copiedNodes);
                    RecordPersistentCopies(copiedNodes);
                    _componentBytes = checked(_componentBytes - removed.RetainedBytes);
                    _copiedComponents++;
                }
                _rootIndex++;
            }
            while (_cleanupMemberIndex < _queueWrite)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _membership = _membership.Remove(
                    _queue[_cleanupMemberIndex++],
                    out bool removed,
                    out int copiedNodes);
                RecordPersistentCopies(copiedNodes);
                if (removed)
                    _copiedMemberships++;
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
                        _membership = _membership.Set(
                            _componentMembers[_publishMemberIndex++],
                            _pendingComponent.Key,
                            out int copiedNodes);
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
                    _copiedComponents++;
                    _pendingComponent = null;
                    _buildMemberCount = 0;
                    _publishMemberIndex = 0;
                }

                if (_componentTraversalComplete)
                {
                    while (_prepareMemberIndex < _buildMemberCount)
                    {
                        if (!meter.TryConsumeComponentNodes(1))
                            return false;
                        _pendingMembers![_prepareMemberIndex] =
                            _componentMembers[_prepareMemberIndex];
                        _prepareMemberIndex++;
                    }
                    _pendingComponent = NavigationStructuralComponent.CreateFlat(
                        _pendingMembers!,
                        _version);
                    _pendingMembers = null;
                    _prepareMemberIndex = 0;
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
                        _version,
                        _nodes,
                        _incoming,
                        _components,
                        _membership,
                        _aliases,
                        _nodeBytes,
                        _incomingBytes,
                        _componentBytes,
                        new NavigationCompositionUpdateCounters(
                            _changes.Length,
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
                        _componentMembers[_buildMemberCount++] = mapId;
                    }
                    _nodes.TryGetValue(mapId, out NavigationStructuralNode node);
                    ReadOnlySpan<NavigationStructuralLink> links = node.Links;
                    while (_buildEdgeIndex < links.Length)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        string neighbor = links[_buildEdgeIndex++].DestinationMapId;
                        if (_domain.Contains(neighbor) && TryVisitBuild(neighbor))
                        {
                            _buildQueue[_buildWrite++] = neighbor;
                        }
                    }
                    ReadOnlySpan<NavigationIncomingDependency> sources =
                        _incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord incoming)
                            ? incoming.Sources
                            : ReadOnlySpan<NavigationIncomingDependency>.Empty;
                    while (_buildIncomingIndex < sources.Length)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        string neighbor = sources[_buildIncomingIndex++].SourceMapId;
                        if (_domain.Contains(neighbor) && TryVisitBuild(neighbor))
                        {
                            _buildQueue[_buildWrite++] = neighbor;
                        }
                    }
                    _buildRead++;
                    _buildNodeConsumed = false;
                    _buildEdgeIndex = 0;
                    _buildIncomingIndex = 0;
                }
                _prepareMemberIndex = 0;
                if (_buildMemberCount == 0)
                    _componentActive = false;
                else
                {
                    _pendingMembers = new string[_buildMemberCount];
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
            if (_buildVisited.ContainsKey(mapId))
                return false;
            _buildVisited = _buildVisited.Set(mapId, true, out int copiedNodes);
            RecordPersistentCopies(copiedNodes);
            return true;
        }

        private void RecordPersistentCopies(int copiedNodes)
        {
            _copiedPersistentPages = checked(_copiedPersistentPages + copiedNodes);
        }

        private int GetRetainedCopiedPersistentPages()
        {
            int reachableNodes = checked(
                _nodes.PersistentNodeCount
                + _incoming.PersistentNodeCount
                + _components.PersistentNodeCount
                + _membership.PersistentNodeCount
                + _aliases.PersistentNodeCount
                + _buildVisited.PersistentNodeCount);
            // A live copied node is both a recorded allocation and reachable from a current root.
            // The smaller of those two upper bounds cannot undercount live COW ownership.
            return Math.Min(_copiedPersistentPages, reachableNodes);
        }

        private static int GetHashCapacity(int itemCapacity)
        {
            long required = ((long)itemCapacity * 100L + 84L) / 85L;
            SwiftThrowHelper.ThrowIfArgumentOutOfRange(
                required > 1L << 30,
                itemCapacity,
                nameof(itemCapacity),
                "Structural composition scratch capacity is too large.");
            return SwiftHashTools.NextPowerOfTwo((int)Math.Max(SwiftHashSet<string>.DefaultCapacity, required));
        }

        private void ResetChangeCursor()
        {
            _changedNodeConsumed = false;
            _nodeWork = null;
            _oldNode = null;
            _nextNode = null;
            _oldLinkIndex = 0;
            _nextLinkIndex = 0;
        }

        private void ResetQueueCursor()
        {
            _queueNodeConsumed = false;
            _edgeIndex = 0;
            _incomingIndex = 0;
        }

        private enum Stage
        {
            CaptureNodes,
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
        private PersistentStringMap<int> _counts = PersistentStringMap<int>.Empty;
        private int _stage;
        private int _index;
        private NavigationStructuralLink[]? _links;

        internal StructuralNodeWork(NavigationMapInstance instance) => _instance = instance;

        internal NavigationStructuralNode Result { get; private set; } = null!;

        internal long RetainedBytes => checked(
            40L
            + _counts.RetainedBytes
            + ((_links?.Length ?? 0) * 24L));

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            while (_stage < 5)
            {
                int count = GetStageCount();
                while (_index < count)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    string? destination = GetDestination(_index++);
                    if (destination == null)
                        continue;
                    _counts.TryGetValue(destination, out int current);
                    _counts = _counts.Set(destination, checked(current + 1));
                }
                _stage++;
                _index = 0;
            }
            _links ??= new NavigationStructuralLink[_counts.Count];
            while (_index < _links.Length)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _links[_index] = new NavigationStructuralLink(
                    _counts.GetKeyAt(_index),
                    _counts.GetValueAt(_index));
                _index++;
            }
            Result ??= new NavigationStructuralNode(_links);
            return true;
        }

        private int GetStageCount() => _stage switch
        {
            0 => _instance.Map.ConnectionSpan.Length,
            1 => _instance.Map.TransitionSpan.Length,
            2 => _instance.Overlay.ConnectionCount,
            3 => _instance.Overlay.TransitionCount,
            _ => 0
        };

        private string? GetDestination(int index)
        {
            switch (_stage)
            {
                case 0:
                    NavigationConnection connection = _instance.Map.ConnectionSpan[index];
                    return _instance.Overlay.TryGetConnection(connection.Id, out _)
                        ? null
                        : connection.Destination.MapId;
                case 1:
                    TraversalTransitionDefinition transition = _instance.Map.TransitionSpan[index];
                    return _instance.Overlay.TryGetTransition(transition.Id, out _)
                        ? null
                        : transition.Destination.MapId;
                case 2:
                    NavigationConnectionOverlayOperation connectionOverlay =
                        _instance.Overlay.GetConnectionAt(index);
                    return connectionOverlay.Kind == NavigationConnectionOverlayOperationKind.Upsert
                        ? connectionOverlay.Connection!.Destination.MapId
                        : null;
                default:
                    TraversalTransitionOverlayOperation transitionOverlay =
                        _instance.Overlay.GetTransitionAt(index);
                    return transitionOverlay.Kind == TraversalTransitionOverlayOperationKind.Upsert
                        ? transitionOverlay.Transition.Destination.MapId
                        : null;
            }
        }
    }

    private sealed class ComponentTreeCursor
    {
        private readonly NavigationStructuralComponent[] _stack;
        private int _stackCount;
        private NavigationStructuralComponent? _leaf;
        private int _memberIndex;

        internal ComponentTreeCursor(
            NavigationStructuralComponent root,
            NavigationStructuralComponent[] stack)
        {
            _stack = stack;
            _stack[_stackCount++] = root;
        }

        internal bool IsComplete => _stackCount == 0 && _leaf == null;

        internal long RetainedBytes => 32L;

        internal bool TryAdvance(
            MaintenanceWorkMeter meter,
            out string? treeKey,
            out string? member)
        {
            treeKey = null;
            member = null;
            if (_leaf != null)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                member = _leaf.FlatMembers![_memberIndex++];
                if (_memberIndex == _leaf.FlatMembers!.Length)
                {
                    _leaf = null;
                    _memberIndex = 0;
                }
                return true;
            }
            if (_stackCount == 0)
                return true;
            if (!meter.TryConsumeComponentNodes(1))
                return false;
            NavigationStructuralComponent current = _stack[--_stackCount];
            treeKey = current.Key;
            if (current.FlatMembers != null)
                _leaf = current;
            else
            {
                _stack[_stackCount++] = current.Right!;
                _stack[_stackCount++] = current.Left!;
            }
            return true;
        }
    }
}
