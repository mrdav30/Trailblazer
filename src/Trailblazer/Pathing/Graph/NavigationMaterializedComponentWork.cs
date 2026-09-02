//=======================================================================
// NavigationMaterializedComponentWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Captures exact components incident to newly materialized medium states, then delegates the
/// bounded rebuild to the ordinary component builder.
/// </summary>
internal sealed class NavigationMaterializedComponentWork
{
    internal const long BaseRetainedBytes = 3_856L;

    private readonly NavigationWorldGraph _candidate;
    private readonly GridWorld? _world;
    private readonly NavigationGridBaselineCapture[]? _baselineCaptures;
    private readonly int[]? _affectedMapOrdinals;
    private readonly int _affectedMapCount;
    private readonly GridEventInfo[]? _events;
    private readonly int _eventCount;
    private readonly NavigationOperationCandidate? _transitionCandidate;
    private readonly NavigationSurfaceComponentKeySet _initialAffectedKeys;
    private readonly NavigationCellAddressSet _initialSeeds;
    private readonly int _initialAffectedMemberCount;
    private NavigationSurfaceComponentKeySet _changedStates;
    private PersistentStringMap<bool> _seamDiscoveryMaps = PersistentStringMap<bool>.Empty;
    private PersistentStringMap<bool> _transitionChangedMaps = PersistentStringMap<bool>.Empty;
    private NavigationTransitionRefreshWork? _transitionRefresh;
    private NavigationWorldGraph? _transitionGraph;
    private NavigationAutomaticSeamRefreshWork? _seamRefresh;
    private NavigationSurfaceComponentKeySet _affectedKeys =
        NavigationSurfaceComponentKeySet.Empty;
    private NavigationCellAddressSet _seeds = NavigationCellAddressSet.Empty;
    private NavigationSurfaceComponentBuildWork? _build;
    private NavigationWorldGraph? _componentGraph;
    private NavigationSurfaceComponentKeySet.Enumerator _changedStateEnumerator;
    private NavigationSurfaceComponentKey _state;
    private NavigationCellAddress? _pendingNeighbor;
    private NavigationSurfaceEdgeEnumerator _outgoing;
    private NavigationIncomingSurfaceEdgeEnumerator _incoming;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _volumeSeams;
    private NavigationMediumStateRef _volumeSource;
    private int _stateOrdinal;
    private int _statePhase;
    private int _primaryOrdinal;
    private int _primaryCount;
    private int _affectedMemberCount;
    private int _frontAffectedOrdinal;
    private int _frontStateOrdinal;
    private int _frontEventOrdinal;
    private long _seamRevision = -1;
    private NavigationSurfaceComponentKeySet.Enumerator _frontStateEnumerator;
    private bool _frontMapActive;
    private bool _frontComplete;
    private bool _hasNoChanges;
    private bool _outgoingComplete;
    private bool _requiresSnapshotRestart;

    internal NavigationMaterializedComponentWork(
        NavigationWorldGraph candidate,
        NavigationSurfaceComponentKeySet changedStates,
        NavigationSurfaceComponentKeySet affectedKeys,
        NavigationCellAddressSet seeds,
        int affectedMemberCount,
        GridWorld? world,
        NavigationGridBaselineCapture[]? baselineCaptures,
        int[]? affectedMapOrdinals,
        int affectedMapCount,
        GridEventInfo[]? events,
        int eventCount,
        NavigationOperationCandidate? transitionCandidate = null)
    {
        _candidate = candidate;
        _world = world;
        _baselineCaptures = baselineCaptures;
        _affectedMapOrdinals = affectedMapOrdinals;
        _affectedMapCount = affectedMapCount;
        _events = events;
        _eventCount = eventCount;
        _transitionCandidate = transitionCandidate;
        _changedStates = changedStates;
        _affectedKeys = affectedKeys;
        _seeds = seeds;
        _affectedMemberCount = affectedMemberCount;
        _initialAffectedKeys = affectedKeys;
        _initialSeeds = seeds;
        _initialAffectedMemberCount = affectedMemberCount;
        _frontComplete = world == null;
        if (_frontComplete)
        {
            _changedStateEnumerator = changedStates.GetEnumerator();
            _componentGraph = candidate;
            _hasNoChanges = changedStates.Count == 0
                && affectedKeys.Count == 0
                && seeds.Count == 0;
        }
    }

    internal bool IsComplete => IsLifecycleComplete(
        _frontComplete,
        _transitionRefresh?.IsComplete,
        _hasNoChanges,
        _seamRefresh?.IsComplete,
        _build?.IsComplete);

    internal static bool IsLifecycleComplete(
        bool frontComplete,
        bool? transitionRefreshComplete,
        bool hasNoChanges,
        bool? seamRefreshComplete,
        bool? buildComplete) => frontComplete
        && transitionRefreshComplete != false
        && (hasNoChanges
            || (seamRefreshComplete != false && buildComplete == true));

    internal NavigationWorldGraph Result
    {
        get
        {
            System.Diagnostics.Debug.Assert(_componentGraph != null,
                "Completed materialized work owns an explicit component graph.");
            return _build == null
                ? _componentGraph!
                : _componentGraph!.WithSurfaceComponents(_build.Result);
        }
    }

    internal bool RevalidateForPublication()
    {
        if (_seamRefresh == null)
            return true;
        long revision = _seamRefresh.Revision;
        if (_seamRefresh.RevalidateForPublication())
            return true;
        System.Diagnostics.Debug.Assert(_seamRefresh.Revision != revision,
            "Failed seam revalidation resets the refresh and advances its revision.");
        ResetComponentState();
        _requiresSnapshotRestart = true;
        return false;
    }

    internal bool RequiresSnapshotRestart => _requiresSnapshotRestart;

    internal int RetainedEventCount => _eventCount;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _candidate.RetainedBytes
        + (_transitionGraph == null || ReferenceEquals(_transitionGraph, _candidate)
            ? 0L
            : NavigationWorldGraph.BaseRetainedBytes)
        + (_componentGraph == null
            || ReferenceEquals(_componentGraph, _candidate)
            || ReferenceEquals(_componentGraph, _transitionGraph)
                ? 0L
                : NavigationWorldGraph.BaseRetainedBytes)
        + _changedStates.RetainedBytes
        + (_seamRefresh == null ? _seamDiscoveryMaps.RetainedBytes : 0L)
        + (_world == null ? 0L : _transitionChangedMaps.RetainedBytes)
        + (_transitionRefresh?.RetainedBytes ?? 0L)
        + _affectedKeys.RetainedBytes
        + (ReferenceEquals(_initialAffectedKeys, _affectedKeys)
            ? 0L
            : _initialAffectedKeys.RetainedBytes)
        + _seeds.RetainedBytes
        + (ReferenceEquals(_initialSeeds, _seeds) ? 0L : _initialSeeds.RetainedBytes)
        + (_seamRefresh?.RetainedBytes ?? 0L)
        + (_build?.RetainedBytes ?? 0L));

    internal int PersistentPageCount => checked(
        1
        + _candidate.PersistentPageCount
        + _changedStates.PersistentPageCount
        + (_seamRefresh == null ? 1 + _seamDiscoveryMaps.PersistentNodeCount : 0)
        + (_world == null ? 0 : 1 + _transitionChangedMaps.PersistentNodeCount)
        + (_transitionRefresh?.PersistentPageCount ?? 0)
        + _affectedKeys.PersistentPageCount
        + (ReferenceEquals(_initialAffectedKeys, _affectedKeys)
            ? 0
            : _initialAffectedKeys.PersistentPageCount)
        + _seeds.PersistentPageCount
        + (ReferenceEquals(_initialSeeds, _seeds)
            ? 0
            : _initialSeeds.PersistentPageCount)
        + (_seamRefresh?.PersistentPageCount ?? 0)
        + (_build?.PersistentPageCount ?? 0));

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        if (!_frontComplete && !AdvanceFront(meter))
            return false;
        if (_world != null)
        {
            _transitionRefresh ??= new NavigationTransitionRefreshWork(
                _candidate,
                _candidate,
                _transitionCandidate,
                _transitionChangedMaps,
                rebuildRules: false,
                _candidate.GraphVersion);
            if (!_transitionRefresh.Advance(meter))
                return false;
            if (_transitionGraph == null)
            {
                _transitionGraph = _candidate.WithTransitionPublication(
                    _transitionRefresh.Pages,
                    _transitionRefresh.Rules);
            }
            if (_seamRefresh == null)
                _componentGraph = _transitionGraph;
        }
        if (_hasNoChanges)
            return true;
        if (_seamRefresh != null)
        {
            if (_seamRevision != _seamRefresh.Revision)
                ResetComponentState();
            while (!_seamRefresh.IsComplete)
            {
                NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus seamStatus =
                    _seamRefresh.AdvanceOne(meter);
                if (_seamRevision != _seamRefresh.Revision)
                {
                    ResetComponentState();
                    _requiresSnapshotRestart = true;
                    return false;
                }
                if (seamStatus == NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus.Blocked)
                    return false;
            }
            if (_componentGraph == null)
            {
                _seamRevision = _seamRefresh.Revision;
                System.Diagnostics.Debug.Assert(_transitionGraph != null,
                    "Seam refresh follows transition publication for materialized work.");
                _componentGraph = _transitionGraph!
                    .WithAutomaticSeams(_seamRefresh.Result);
            }
        }
        if (_build != null)
            return _build.Advance(meter);
        if (!AdvanceAffectedCapture(meter))
            return false;
        _build = new NavigationSurfaceComponentBuildWork(
            _componentGraph!,
            _candidate,
            _affectedKeys,
            _seeds,
            checked(_affectedMemberCount + _seeds.Count));
        return _build.Advance(meter);
    }

    private bool AdvanceFront(MaintenanceWorkMeter meter)
    {
        while (_frontAffectedOrdinal < _affectedMapCount)
        {
            int mapOrdinal = _affectedMapOrdinals![_frontAffectedOrdinal];
            NavigationGridBaselineCapture capture = _baselineCaptures![mapOrdinal];
            NavigationSurfaceComponentKeySet? states = capture.StructuralChangedStates;
            if (!_frontMapActive)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                string changedMapId = _candidate.GetInstance(mapOrdinal).MapId;
                _transitionChangedMaps = _transitionChangedMaps.Set(changedMapId, true);
                if (capture.DefaultPhysicalAddressSetChanged)
                {
                    _seamDiscoveryMaps = _seamDiscoveryMaps.Set(
                        _candidate.GetInstance(mapOrdinal).MapId,
                        true);
                }
                _frontStateOrdinal = 0;
                _frontStateEnumerator = states?.GetEnumerator() ?? default;
                _frontMapActive = true;
            }
            if (states != null && _frontStateOrdinal < states.Count)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _frontStateEnumerator.MoveNext();
                _changedStates = _changedStates.Add(_frontStateEnumerator.Current);
                _frontStateOrdinal++;
                continue;
            }
            GridNavigationBaseline? baseline = states == null ? capture.Baseline : null;
            if (baseline != null && _frontStateOrdinal < baseline.VoxelStates.Length)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                AddChangedMedia(
                    _candidate.GetInstance(mapOrdinal),
                    baseline.VoxelStates[_frontStateOrdinal++].VoxelIndex);
                continue;
            }
            _frontMapActive = false;
            _frontAffectedOrdinal++;
        }

        while (_frontEventOrdinal < _eventCount)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            GridEventInfo eventInfo = _events![_frontEventOrdinal++];
            if (!_candidate.TryGetMapId(
                    eventInfo.Configuration.ToGridKey(),
                    out string mapId))
                continue;
            bool foundMap = _candidate.TryGetMap(mapId, out NavigationMapInstance next);
            System.Diagnostics.Debug.Assert(foundMap,
                "The immutable configuration index and map directory share one candidate graph.");
            _transitionChangedMaps = _transitionChangedMaps.Set(mapId, true);
            if (eventInfo.HasVoxelState)
                AddChangedMedia(next, eventInfo.VoxelIndex);
        }

        _frontComplete = true;
        if (_changedStates.Count == 0)
        {
            _componentGraph = _candidate;
            _hasNoChanges = true;
            return true;
        }
        _seamRefresh = _seamDiscoveryMaps.Count == 0
            ? null
            : new NavigationAutomaticSeamRefreshWork(
                _world!,
                _candidate,
                _candidate,
                _seamDiscoveryMaps);
        _changedStateEnumerator = _changedStates.GetEnumerator();
        return true;
    }

    private void AddChangedMedia(NavigationMapInstance next, VoxelIndex index)
    {
        var address = new NavigationCellAddress(next.MapId, index);
        TraversalMedia nextMedia = next.GetEffectiveMedia(index);
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            TraversalMedia bit = (TraversalMedia)NavigationMediumSlots<byte>.GetBit(medium);
            bool priorHasState = _candidate.SurfaceComponents.TryGet(address, medium, out _);
            bool nextHasState = (nextMedia & bit) != 0;
            if (priorHasState != nextHasState)
            {
                _changedStates = _changedStates.Add(
                    new NavigationSurfaceComponentKey(address, medium));
            }
        }
    }

    private bool AdvanceAffectedCapture(MaintenanceWorkMeter meter)
    {
        while (_stateOrdinal < _changedStates.Count || _statePhase != 0)
        {
            if (_pendingNeighbor.HasValue)
            {
                if (!CapturePriorComponent(_pendingNeighbor.Value, _state.Medium, meter))
                    return false;
                _pendingNeighbor = null;
                continue;
            }
            if (_statePhase == 0)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _changedStateEnumerator.MoveNext();
                _state = _changedStateEnumerator.Current;
                _stateOrdinal++;
                _statePhase = 1;
            }
            if (_statePhase == 1)
            {
                if (!_seeds.Contains(_state.Representative))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _seeds = _seeds.Add(_state.Representative);
                }
                _pendingNeighbor = _state.Representative;
                _statePhase = 2;
                continue;
            }
            if (_statePhase == 2)
            {
                if (_state.Medium == TraversalMedium.Solid)
                {
                    if (!_componentGraph!.TryGetStructuralMediumStateRef(
                            _state.Representative,
                            TraversalMedium.Solid,
                            out NavigationMediumStateRef solidState))
                    {
                        CompleteState();
                        continue;
                    }
                    NavigationNodeRef node = solidState.Node;
                    _outgoing = _componentGraph.EnumerateStructuralSurfaceEdges(node);
                    _incoming = _componentGraph.EnumerateIncomingStructuralSurfaceEdges(node);
                    _outgoingComplete = false;
                }
                else
                {
                    if (!_componentGraph!.TryGetStructuralMediumStateRef(
                            _state.Representative,
                            _state.Medium,
                            out _volumeSource))
                    {
                        CompleteState();
                        continue;
                    }
                    _primaryOrdinal = 0;
                    _primaryCount = _componentGraph.GetPrimaryDirectionCount(_volumeSource.Node);
                    _volumeSeams = _componentGraph.AutomaticSeams.GetActiveEndpointEnumerator(
                        _state.Representative);
                }
                _statePhase = 3;
            }
            if (_state.Medium == TraversalMedium.Solid)
            {
                if (!AdvanceSolidAdjacency(meter))
                    return false;
            }
            else if (!AdvanceVolumeAdjacency(meter))
            {
                return false;
            }
        }
        return true;
    }

    private bool AdvanceSolidAdjacency(MaintenanceWorkMeter meter)
    {
        int remaining = meter.RemainingSurfaceComponentEdges;
        if (!_outgoingComplete)
        {
            NavigationSurfaceEdgeAdvanceStatus status = _outgoing.AdvanceOne(
                meter,
                ref remaining);
            if (status == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                return false;
            if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                return true;
            if (status == NavigationSurfaceEdgeAdvanceStatus.Edge)
            {
                bool found = _componentGraph!.TryGetNodeAddress(
                    _outgoing.Current.Target,
                    out NavigationCellAddress address);
                System.Diagnostics.Debug.Assert(found,
                    "A published outgoing surface edge targets a node in the same graph.");
                _pendingNeighbor = address;
                return true;
            }
            _outgoingComplete = true;
        }
        NavigationSurfaceEdgeAdvanceStatus incoming = _incoming.AdvanceOne(
            meter,
            ref remaining);
        if (incoming == NavigationSurfaceEdgeAdvanceStatus.Blocked)
            return false;
        if (incoming == NavigationSurfaceEdgeAdvanceStatus.Pending)
            return true;
        if (incoming == NavigationSurfaceEdgeAdvanceStatus.Edge)
        {
            bool found = _componentGraph!.TryGetNodeAddress(
                _incoming.Current.Predecessor,
                out NavigationCellAddress address);
            System.Diagnostics.Debug.Assert(found,
                "A published incoming surface edge originates at a node in the same graph.");
            _pendingNeighbor = address;
            return true;
        }
        CompleteState();
        return true;
    }

    private bool AdvanceVolumeAdjacency(MaintenanceWorkMeter meter)
    {
        while (_primaryOrdinal < _primaryCount)
        {
            if (!meter.TryConsumeSurfaceComponentEdges(1))
                return false;
            if (_componentGraph!.TryGetStructuralPrimaryMediumNeighbor(
                    _volumeSource,
                    _primaryOrdinal++,
                    out NavigationMediumStateRef neighbor)
                && _componentGraph.TryGetNodeAddress(
                    neighbor.Node,
                    out NavigationCellAddress address))
            {
                _pendingNeighbor = address;
                return true;
            }
        }
        while (meter.RemainingSurfaceComponentEdges > 0)
        {
            if (!_volumeSeams.MoveNext())
            {
                CompleteState();
                return true;
            }
            meter.TryConsumeSurfaceComponentEdges(1);
            NavigationCellAddress destination = _volumeSeams.Current.Destination;
            if (_componentGraph!.TryGetStructuralMediumStateRef(
                    destination,
                    _state.Medium,
                    out _))
            {
                _pendingNeighbor = destination;
                return true;
            }
        }
        return false;
    }

    private bool CapturePriorComponent(
        NavigationCellAddress address,
        TraversalMedium medium,
        MaintenanceWorkMeter meter)
    {
        if (!_candidate.SurfaceComponents.TryGet(
                address,
                medium,
                out NavigationSurfaceComponent component)
            || _affectedKeys.Contains(component.Key))
        {
            return true;
        }
        if (!meter.TryConsumeDependencyEntries(1))
            return false;
        _affectedKeys = _affectedKeys.Add(component.Key);
        _affectedMemberCount = checked(_affectedMemberCount + component.Members.Count);
        return true;
    }

    private void CompleteState()
    {
        _statePhase = 0;
        _pendingNeighbor = null;
        _outgoing = default;
        _incoming = default;
        _volumeSeams = default;
        _volumeSource = default;
        _primaryOrdinal = 0;
        _primaryCount = 0;
        _outgoingComplete = false;
    }

    private void ResetComponentState()
    {
        System.Diagnostics.Debug.Assert(_seamRefresh != null,
            "Only seam revision changes restart component discovery.");
        _affectedKeys = _initialAffectedKeys;
        _seeds = _initialSeeds;
        _affectedMemberCount = _initialAffectedMemberCount;
        _changedStateEnumerator = _changedStates.GetEnumerator();
        _state = default;
        _stateOrdinal = 0;
        _statePhase = 0;
        _pendingNeighbor = null;
        _build = null;
        _componentGraph = null;
        _seamRevision = _seamRefresh!.Revision;
        CompleteState();
    }
}
