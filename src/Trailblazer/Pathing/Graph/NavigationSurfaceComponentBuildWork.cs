//=======================================================================
// NavigationSurfaceComponentBuildWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Builds exact weak surface components with bounded node and edge progress.</summary>
internal sealed class NavigationSurfaceComponentBuildWork
{
    private const int NavigationCellAddressBytes = 24;
    private const int MaximumUnmeteredRootProbes = 1_024;

    private readonly NavigationWorldGraph _graph;
    private readonly NavigationWorldGraph _previousGraph;
    private readonly NavigationSurfaceComponentKeySet _affectedKeys;
    private readonly NavigationCellAddressSet _seedAddresses;
    private readonly NavigationAddressStampSet _visited;
    private readonly NavigationAddressStampSet _domain;
    private readonly NavigationCellAddress[] _queue;
    private readonly NavigationCellAddress[] _rootScratch;
    private NavigationPagedSequence<NavigationCellAddress>.Builder? _memberBuilder;
    private NavigationSurfaceComponent? _sealingComponent;
    private NavigationPagedSequence<NavigationCellAddress>.Enumerator _sealingMembers;
    private NavigationSurfaceComponentIndex _result = NavigationSurfaceComponentIndex.Empty;
    private NavigationSurfaceEdgeEnumerator _outgoing;
    private NavigationIncomingSurfaceEdgeEnumerator _incoming;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _volumeSeams;
    private int _queueRead;
    private int _queueWrite;
    private int _memberCount;
    private NavigationCellAddress _representative;
    private bool _componentActive;
    private bool _nodeActive;
    private bool _outgoingComplete;
    private int _primaryDirectionOrdinal;
    private int _primaryDirectionCount;
    private TraversalMedium _medium = TraversalMedium.Solid;
    private bool _certified = true;
    private int _affectedKeyOrdinal;
    private NavigationSurfaceComponentKeySet.Enumerator _affectedKeyEnumerator;
    private int _seedOrdinal;
    private int _rootCount;
    private int _rootOrdinal;
    private NavigationSurfaceComponent? _removedComponent;
    private NavigationPagedSequence<NavigationCellAddress>.Enumerator _removedMembers;
    private NavigationCellAddress _pendingRemovedMember;
    private bool _hasPendingRemovedMember;
    private bool _removedRecord;
    private bool _incrementalPreparationComplete;
    private int _copiedPersistentNodes;
    private long _newComponentPayloadBytes;
    private int _newComponentPayloadPages;
    private bool _ownsResultRoot;
    private bool _sealingRecordAdded;
    private bool _hasPendingSealingMember;
    private NavigationCellAddress _pendingSealingMember;

    internal NavigationSurfaceComponentBuildWork(
        NavigationWorldGraph graph,
        NavigationWorldGraph previousGraph,
        NavigationSurfaceComponentKeySet affectedKeys,
        NavigationCellAddressSet seedAddresses,
        int affectedAddressCapacity)
    {
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(previousGraph, nameof(previousGraph));
        SwiftThrowHelper.ThrowIfNull(affectedKeys, nameof(affectedKeys));
        SwiftThrowHelper.ThrowIfNull(seedAddresses, nameof(seedAddresses));
        _graph = graph;
        _previousGraph = previousGraph;
        _affectedKeys = affectedKeys;
        _affectedKeyEnumerator = affectedKeys.GetEnumerator();
        _seedAddresses = seedAddresses;
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            affectedAddressCapacity < seedAddresses.Count,
            affectedAddressCapacity,
            nameof(affectedAddressCapacity));
        int capacity = affectedAddressCapacity;
        int stampCapacity = Math.Max(1, capacity);
        _visited = new NavigationAddressStampSet(stampCapacity);
        _domain = new NavigationAddressStampSet(stampCapacity);
        _queue = capacity == 0
            ? Array.Empty<NavigationCellAddress>()
            : new NavigationCellAddress[capacity];
        _rootScratch = capacity == 0
            ? Array.Empty<NavigationCellAddress>()
            : new NavigationCellAddress[capacity];
        _result = previousGraph.SurfaceComponents;
    }

    internal bool IsComplete { get; private set; }

    internal NavigationSurfaceComponentIndex Result => _result;

    internal long RetainedBytes => checked(
        152L
        + GetArrayBytes(_queue.Length, NavigationCellAddressBytes)
        + GetArrayBytes(_rootScratch.Length, NavigationCellAddressBytes)
        + GetStampSetBytes(Math.Max(1, _queue.Length))
        + GetStampSetBytes(Math.Max(1, _queue.Length))
        + (_memberBuilder?.RetainedBytes ?? 0L)
        + (_ownsResultRoot ? 64L : 0L)
        + ((long)GetRetainedCopiedPersistentNodes() * 64L)
        + _newComponentPayloadBytes);

    internal int PersistentPageCount => checked(
        1
        + (_memberBuilder?.PersistentPageCount ?? 0)
        + (_ownsResultRoot ? 1 : 0)
        + GetRetainedCopiedPersistentNodes()
        + _newComponentPayloadPages);

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        if (IsComplete)
            return true;

        if (!_incrementalPreparationComplete
            && !AdvanceIncrementalPreparation(meter))
        {
            return false;
        }

        int unmeteredRootProbes = 0;
        while (!IsComplete)
        {
            if (_sealingComponent != null)
            {
                if (!AdvanceSealComponent(meter))
                    return false;
                continue;
            }
            if (!_componentActive)
            {
                if (!TryBeginNextComponent(ref unmeteredRootProbes))
                    return IsComplete;
                if (!_componentActive)
                    return false;
            }

            if (!_nodeActive)
            {
                if (_queueRead == _queueWrite)
                {
                    BeginSealComponent();
                    continue;
                }
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                NavigationCellAddress currentAddress = _queue[_queueRead++];
                _memberBuilder!.Append(currentAddress);
                _memberCount++;
                if (_memberCount == 1 || currentAddress.CompareTo(_representative) < 0)
                    _representative = currentAddress;
                if (!_graph.TryGetNodeRef(currentAddress, out NavigationNodeRef current))
                    continue;
                if (_medium != TraversalMedium.Solid)
                {
                    _primaryDirectionOrdinal = 0;
                    _primaryDirectionCount = _graph.GetPrimaryDirectionCount(current);
                    _volumeSeams = _graph.AutomaticSeams.GetActiveEndpointEnumerator(
                        currentAddress);
                    _nodeActive = true;
                    continue;
                }
                _outgoing = _graph.EnumerateStructuralSurfaceEdges(current);
                _incoming = _graph.EnumerateIncomingStructuralSurfaceEdges(current);
                _outgoingComplete = false;
                _nodeActive = true;
            }

            if (_medium != TraversalMedium.Solid)
            {
                if (!AdvanceVolumeNode(meter))
                    return false;
                continue;
            }

            int remainingEdges = meter.RemainingSurfaceComponentEdges;
            if (!_outgoingComplete)
            {
                NavigationSurfaceEdgeAdvanceStatus status =
                    _outgoing.AdvanceOne(meter, ref remainingEdges);
                if (status == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                    return false;
                if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                    continue;
                if (status == NavigationSurfaceEdgeAdvanceStatus.Edge)
                {
                    NavigationGraphEdge edge = _outgoing.Current;
                    if (edge.Kind == NavigationGraphEdgeKind.Explicit)
                    {
                        _certified &= edge.ExplicitConnection.IsLowerBoundCertified;
                    }
                    else if (edge.Kind == NavigationGraphEdgeKind.Seam)
                    {
                        _certified &= edge.AutomaticSeam.Portal.FaceKind
                            == GridForge.Grids.Topology.VoxelContactFaceKind.Vertical;
                    }
                    AddNeighbor(edge.Target);
                    continue;
                }
                _outgoingComplete = true;
            }

            NavigationSurfaceEdgeAdvanceStatus incomingStatus =
                _incoming.AdvanceOne(meter, ref remainingEdges);
            if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                return false;
            if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Edge)
            {
                NavigationIncomingSurfaceEdge edge = _incoming.Current;
                if (edge.ForwardEdge.Kind == NavigationGraphEdgeKind.Explicit)
                {
                    _certified &= edge.ForwardEdge.ExplicitConnection.IsLowerBoundCertified;
                }
                else if (edge.ForwardEdge.Kind == NavigationGraphEdgeKind.Seam)
                {
                    _certified &= edge.ForwardEdge.AutomaticSeam.Portal.FaceKind
                        == GridForge.Grids.Topology.VoxelContactFaceKind.Vertical;
                }
                AddNeighbor(edge.Predecessor);
                continue;
            }
            _outgoing = default;
            _incoming = default;
            _nodeActive = false;
        }
        return true;
    }

    private bool TryBeginNextComponent(ref int unmeteredRootProbes)
    {
        while (true)
        {
            while (_rootOrdinal < _rootCount)
            {
                if (unmeteredRootProbes++ == MaximumUnmeteredRootProbes)
                    return false;
                NavigationCellAddress address = _rootScratch[_rootOrdinal++];
                bool exists = _graph.TryGetStructuralMediumStateRef(
                    address,
                    _medium,
                    out _);
                if (!exists
                    || _result.TryGet(address, _medium, out _)
                    || !_visited.Add(address))
                {
                    continue;
                }
                _queueRead = 0;
                _queueWrite = 1;
                _memberCount = 0;
                _memberBuilder = new NavigationPagedSequence<NavigationCellAddress>.Builder(
                    NavigationCellAddressBytes);
                _certified = true;
                _queue[0] = address;
                _componentActive = true;
                return true;
            }
            if (_medium == TraversalMedium.Liquid)
            {
                IsComplete = true;
                return false;
            }
            _medium++;
            _visited.Reset();
            _rootOrdinal = 0;
            unmeteredRootProbes = 0;
        }
    }

    private void AddNeighbor(NavigationNodeRef neighbor)
    {
        if (!_graph.TryGetNodeAddress(neighbor, out NavigationCellAddress address)
            || !_graph.TryGetStructuralMediumStateRef(
                address,
                TraversalMedium.Solid,
                out _)
            || !_visited.Add(address))
        {
            return;
        }
        if (!_domain.Contains(address))
        {
            throw new InvalidOperationException(
                "Surface-component closure omitted a structurally adjacent prior component.");
        }
        _queue[_queueWrite++] = address;
    }

    private bool AdvanceVolumeNode(MaintenanceWorkMeter meter)
    {
        while (_primaryDirectionOrdinal < _primaryDirectionCount)
        {
            if (!meter.TryConsumeSurfaceComponentEdges(1))
                return false;
            int ordinal = _primaryDirectionOrdinal++;
            NavigationCellAddress current = _queue[_queueRead - 1];
            if (_graph.TryGetStructuralMediumStateRef(
                    current,
                    _medium,
                    out NavigationMediumStateRef source)
                && _graph.TryGetStructuralPrimaryMediumNeighbor(
                    source,
                    ordinal,
                    out NavigationMediumStateRef neighbor)
                && _graph.TryGetNodeAddress(neighbor.Node, out NavigationCellAddress address))
            {
                AddVolumeNeighbor(address);
            }
        }

        while (meter.RemainingSurfaceComponentEdges > 0)
        {
            if (!_volumeSeams.MoveNext())
            {
                _volumeSeams = default;
                _nodeActive = false;
                return true;
            }
            meter.TryConsumeSurfaceComponentEdges(1);
            NavigationCellAddress destination = _volumeSeams.Current.Destination;
            if (_graph.TryGetStructuralMediumStateRef(destination, _medium, out _))
                AddVolumeNeighbor(destination);
        }
        return false;
    }

    private void AddVolumeNeighbor(NavigationCellAddress address)
    {
        if (!_visited.Add(address))
            return;
        if (!_domain.Contains(address))
        {
            throw new InvalidOperationException(
                "Medium-component closure omitted a positive-face neighbor.");
        }
        _queue[_queueWrite++] = address;
    }

    private bool AdvanceIncrementalPreparation(MaintenanceWorkMeter meter)
    {
        while (_affectedKeyOrdinal < _affectedKeys.Count || _removedComponent != null)
        {
            if (_removedComponent == null)
            {
                if (!_affectedKeyEnumerator.MoveNext())
                    break;
                _affectedKeyOrdinal++;
                NavigationSurfaceComponentKey key = _affectedKeyEnumerator.Current;
                if (!_previousGraph.SurfaceComponents.TryGet(key, out _removedComponent))
                    continue;
                _removedMembers = _removedComponent.Members.GetEnumerator();
                _removedRecord = false;
            }
            if (!_removedRecord)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _result = _result.RemoveComponentRecord(
                    _removedComponent,
                    out int copiedPersistentNodes);
                RecordPersistentCopies(copiedPersistentNodes);
                _removedRecord = true;
            }
            while (true)
            {
                if (!_hasPendingRemovedMember)
                {
                    if (!_removedMembers.MoveNext())
                        break;
                    _pendingRemovedMember = _removedMembers.Current;
                    _hasPendingRemovedMember = true;
                }
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _result = _result.RemoveMembership(
                    _pendingRemovedMember,
                    _removedComponent.Key,
                    out int copiedPersistentNodes);
                RecordPersistentCopies(copiedPersistentNodes);
                AddDomainRoot(_pendingRemovedMember);
                _hasPendingRemovedMember = false;
            }
            _removedComponent = null;
        }
        while (_seedOrdinal < _seedAddresses.Count)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            AddDomainRoot(_seedAddresses.GetAt(_seedOrdinal++));
        }
        _incrementalPreparationComplete = true;
        if (_rootCount == 0)
            IsComplete = true;
        return true;
    }

    private void AddDomainRoot(NavigationCellAddress address)
    {
        if (!_domain.Add(address))
            return;
        _rootScratch[_rootCount++] = address;
    }

    private void BeginSealComponent()
    {
        NavigationPagedSequence<NavigationCellAddress> sequence = _memberBuilder!.Seal();
        _memberBuilder = null;
        var key = new NavigationSurfaceComponentKey(_representative, _medium);
        _sealingComponent = new NavigationSurfaceComponent(
            key,
            _graph.GraphVersion,
            sequence,
            _certified);
        _sealingMembers = sequence.GetEnumerator();
        _sealingRecordAdded = false;
        _hasPendingSealingMember = false;
        _newComponentPayloadBytes = checked(
            _newComponentPayloadBytes + _sealingComponent.RetainedBytes);
        _newComponentPayloadPages = checked(
            _newComponentPayloadPages + _sealingComponent.PersistentPageCount);
    }

    private bool AdvanceSealComponent(MaintenanceWorkMeter meter)
    {
        if (!_sealingRecordAdded)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _result = _result.AddComponentRecord(
                _sealingComponent!,
                out int copiedPersistentNodes);
            RecordPersistentCopies(copiedPersistentNodes);
            _sealingRecordAdded = true;
        }
        while (true)
        {
            if (!_hasPendingSealingMember)
            {
                if (!_sealingMembers.MoveNext())
                    break;
                _pendingSealingMember = _sealingMembers.Current;
                _hasPendingSealingMember = true;
            }
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _result = _result.AddMembership(
                _pendingSealingMember,
                _sealingComponent!.Key,
                out int copiedPersistentNodes);
            RecordPersistentCopies(copiedPersistentNodes);
            _hasPendingSealingMember = false;
        }
        _sealingComponent = null;
        _componentActive = false;
        _nodeActive = false;
        _queueRead = 0;
        _queueWrite = 0;
        _memberCount = 0;
        return true;
    }

    private static long GetArrayBytes(int count, int elementBytes) => count == 0
        ? 0L
        : Align8(checked(24L + ((long)count * elementBytes)));

    private static long GetStampSetBytes(int capacity)
    {
        int tableSize = 1;
        int required = checked(capacity * 2);
        while (tableSize < required)
            tableSize = checked(tableSize * 2);
        return checked(
            48L
            + GetArrayBytes(tableSize, NavigationCellAddressBytes)
            + GetArrayBytes(tableSize, 8));
    }

    private static long Align8(long value) => checked((value + 7L) & ~7L);

    private void RecordPersistentCopies(int count)
    {
        _ownsResultRoot = true;
        if (count == 0)
            return;
        _copiedPersistentNodes = checked(_copiedPersistentNodes + count);
    }

    private int GetRetainedCopiedPersistentNodes() =>
        Math.Min(_copiedPersistentNodes, _result.PersistentMapNodeCount);
}
