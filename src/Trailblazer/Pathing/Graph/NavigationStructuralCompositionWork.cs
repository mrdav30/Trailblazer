//=======================================================================
// NavigationStructuralCompositionWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Retains the pre-change structural root and a canonical cursor while explicit dependency and
/// affected-component work is debited across fixed-step maintenance boundaries.
/// </summary>
internal sealed class NavigationStructuralCompositionWork
{
    private const long BaseRetainedBytes = 112L;

    private readonly GridWorld _world;
    private readonly int _batchChangeCount;
    private readonly NavigationWorldGraph _sourceGraph;
    private readonly NavigationOperationCandidate _candidate;
    private readonly NavigationOperationFrameChange[] _changes;
    private PersistentStringMap<bool> _changedMapIds = PersistentStringMap<bool>.Empty;
    private NavigationSurfaceComponentKeySet _affectedComponents =
        NavigationSurfaceComponentKeySet.Empty;
    private NavigationSurfaceComponentKeySet _publishedAffectedComponents =
        NavigationSurfaceComponentKeySet.Empty;
    private NavigationCellAddressSet _affectedAddresses = NavigationCellAddressSet.Empty;
    private NavigationSurfaceComponentKeySet _affectedMediumStates =
        NavigationSurfaceComponentKeySet.Empty;
    private int _affectedMemberCount;
    private PersistentStringMap<bool> _wholeMapIds = PersistentStringMap<bool>.Empty;
    private NavigationWorldGraph.StructuralPreparationWork? _preparation;
    private NavigationAutomaticSeamRefreshWork? _seamRefresh;
    private NavigationWorldGraph? _preparedGraph;
    private NavigationMaterializedComponentWork? _componentUpdate;
    private string? _pendingMapId;
    private NavigationSurfaceComponentKey? _pendingComponentKey;
    private TraversalMedium _pendingMedium = TraversalMedium.Solid;
    private NavigationCellAddress? _pendingExactAddress;
    private int _pendingMapComponentOrdinal;
    private int _explicitSourceIndex;
    private int _changeIndex;
    private int _overlayIndex;
    private int _overlayCellIndex;
    private int _capturePhase;
    private int _seamSourceIndex;
    private int _seamCapturePhase;
    private string? _pendingSeamMapId;
    private bool _seamCaptureComplete;
    private bool _exactCaptureComplete;
    private bool _pendingWholeMap;
    private int _explicitOwnerIndex;
    private int _explicitEndpointStage;
    private int _seamEndpointIndex;
    private int _preparedMapOrdinal;
    private int _preparedMapAddressOrdinal;
    private int _addressBakedCursor;
    private int _addressDynamicCursor;
    private readonly VoxelIndex[] _addressScratch = new VoxelIndex[1];
    private int _incidentAddressIndex;
    private TraversalMedium _incidentMedium = TraversalMedium.Gas;
    private bool _incidentNodeActive;
    private bool _incidentOutgoingComplete;
    private NavigationSurfaceEdgeEnumerator _incidentOutgoing;
    private NavigationIncomingSurfaceEdgeEnumerator _incidentIncoming;
    private NavigationCellAddress? _pendingIncidentAddress;
    private bool _allClosePublished;
    private bool _affectedClosurePublished;
    private bool _allCloseRepublishRequired;

    internal NavigationStructuralCompositionWork(
        GridWorld world,
        NavigationWorldGraph sourceGraph,
        NavigationOperationCandidate candidate,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        _world = world;
        _sourceGraph = sourceGraph;
        _candidate = candidate;
        _changes = changes;
        _batchChangeCount = changeCount;
    }

    internal int CapturedChangedMapCount => _changedMapIds.Count;

    internal bool IsChangedMapCaptureComplete => _capturePhase == 4
        && _seamCaptureComplete
        && _exactCaptureComplete;

    internal NavigationSurfaceComponentKeySet AffectedComponents => _affectedComponents;

    internal bool RequiresAffectedClosurePublication => _allClosePublished
        && IsChangedMapCaptureComplete
        && !_affectedClosurePublished;

    internal bool RequiresAllClosePublication => _allCloseRepublishRequired;

    internal string GetCapturedChangedMapIdAt(int ordinal) =>
        _changedMapIds.GetKeyAt(ordinal);

    internal NavigationWorldGraph PreparedGraph => _preparedGraph!;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _changedMapIds.RetainedBytes
        + _wholeMapIds.RetainedBytes
        + (_componentUpdate == null ? _affectedAddresses.RetainedBytes : 0L)
        + (_componentUpdate == null ? _affectedMediumStates.RetainedBytes : 0L)
        + (_componentUpdate == null
            && !ReferenceEquals(_publishedAffectedComponents, _affectedComponents)
                ? _affectedComponents.RetainedBytes
                : 0L)
        + (_preparation?.RetainedBytes ?? 0)
        + (_seamRefresh?.RetainedBytes ?? 0)
        + (_componentUpdate?.RetainedBytes ?? 0)
        );

    internal int PersistentPageCount => checked(
        1
        + _changedMapIds.PersistentNodeCount
        + _wholeMapIds.PersistentNodeCount
        + (_componentUpdate == null ? _affectedAddresses.PersistentPageCount : 0)
        + (_componentUpdate == null ? _affectedMediumStates.PersistentPageCount : 0)
        + (_componentUpdate == null
            && !ReferenceEquals(_publishedAffectedComponents, _affectedComponents)
                ? _affectedComponents.PersistentPageCount
                : 0)
        + (_preparation?.PersistentPageCount ?? 0)
        + (_seamRefresh?.PersistentPageCount ?? 0)
        + (_componentUpdate?.PersistentPageCount ?? 0));

    internal bool IsComplete => IsLifecycleComplete(
        IsChangedMapCaptureComplete,
        _preparation?.IsComplete,
        _seamRefresh?.IsComplete,
        _affectedComponents.Count,
        _affectedAddresses.Count,
        _componentUpdate?.IsComplete);

    internal static bool IsLifecycleComplete(
        bool captureComplete,
        bool? preparationComplete,
        bool? seamRefreshComplete,
        int affectedComponentCount,
        int affectedAddressCount,
        bool? componentUpdateComplete) => captureComplete
        && preparationComplete == true
        && seamRefreshComplete == true
        && (affectedComponentCount == 0 && affectedAddressCount == 0
            || componentUpdateComplete == true);

    internal NavigationWorldGraph Result => _componentUpdate?.Result
        ?? PreparedGraph.WithSurfaceComponents(_sourceGraph.SurfaceComponents);

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        return Advance(
            meter,
            long.MaxValue,
            int.MaxValue,
            out _);
    }

    internal bool Advance(
        MaintenanceWorkMeter meter,
        long maximumRetainedBytes,
        int maximumPersistentPages,
        out bool capacityExceeded)
    {
        capacityExceeded = false;
        if (_capturePhase != 4 && !AdvanceChangedMapCapture(meter))
            return false;
        _preparation ??= new NavigationWorldGraph.StructuralPreparationWork(
            _sourceGraph,
            _candidate,
            _changes,
            _batchChangeCount,
            _changedMapIds,
            _sourceGraph.GraphVersion + 1);
        // Topology-native edges, seams, cache invalidations, explicit edges, reverse dependencies,
        // and weak components all consume the same maintenance meter.
        if (!_preparation.IsComplete && !_preparation.Advance(meter))
            return false;
        _seamRefresh ??= new NavigationAutomaticSeamRefreshWork(
            _world,
            _sourceGraph,
            _preparation.Result,
            _changes,
            _batchChangeCount);
        if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
        {
            capacityExceeded = true;
            return false;
        }
        while (!_seamRefresh.IsComplete)
        {
            long seamRevision = _seamRefresh.Revision;
            NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus seamStatus =
                _seamRefresh.AdvanceOne(meter);
            if (_seamRefresh.Revision != seamRevision)
            {
                ResetSeamState();
                return false;
            }
            if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
            {
                capacityExceeded = true;
                return false;
            }
            if (seamStatus == NavigationAutomaticSeamRefreshWork.SeamAdvanceStatus.Blocked)
                return false;
        }
        if (!_seamCaptureComplete && !AdvanceSeamChangedMapCapture(meter))
            return false;
        _preparedGraph ??= _preparation.Result.WithAutomaticSeams(_seamRefresh.Result);
        if (!_exactCaptureComplete && !AdvanceExactAffectedCapture(meter))
            return false;
        if (RequiresAffectedClosurePublication || RequiresAllClosePublication)
            return false;
        if (_affectedComponents.Count == 0 && _affectedAddresses.Count == 0)
            return true;
        _componentUpdate ??= new NavigationMaterializedComponentWork(
            PreparedGraph,
            _affectedMediumStates,
            _affectedComponents,
            _affectedAddresses,
            checked(_affectedMemberCount + _affectedAddresses.Count),
            world: null,
            baselineCaptures: null,
            affectedMapOrdinals: null,
            affectedMapCount: 0,
            events: null,
            eventCount: 0);
        bool complete = _componentUpdate.Advance(meter);
        if (ExceedsCapacity(maximumRetainedBytes, maximumPersistentPages))
        {
            capacityExceeded = true;
            return false;
        }
        return complete;
    }

    private bool ExceedsCapacity(long maximumRetainedBytes, int maximumPersistentPages) =>
        RetainedBytes > maximumRetainedBytes
        || PersistentPageCount > maximumPersistentPages;

    internal void MarkAffectedClosurePublished()
    {
        _publishedAffectedComponents = _affectedComponents;
        _affectedClosurePublished = true;
    }

    internal void RecordAffectedClosurePublication(
        NavigationCandidatePublication publication)
    {
        if (publication == NavigationCandidatePublication.Published)
            MarkAffectedClosurePublished();
    }

    internal void MarkAllClosePublished()
    {
        _allClosePublished = true;
        _allCloseRepublishRequired = false;
    }

    internal void MarkInitialClosurePublished()
    {
        if (IsChangedMapCaptureComplete)
            MarkAffectedClosurePublished();
        else
            MarkAllClosePublished();
    }

    internal void MarkAllCloseRepublished()
    {
        _allClosePublished = true;
        _allCloseRepublishRequired = false;
        _affectedClosurePublished = false;
        _publishedAffectedComponents = NavigationSurfaceComponentKeySet.Empty;
    }

    internal void RecordAllClosePublication(
        NavigationCandidatePublication publication)
    {
        if (publication == NavigationCandidatePublication.Published)
            MarkAllCloseRepublished();
    }

    internal bool RevalidateAutomaticSeamsForPublication()
    {
        if (_seamRefresh == null || !_seamCaptureComplete)
            return true;
        long revision = _seamRefresh.Revision;
        if (_seamRefresh.RevalidateForPublication())
            return true;
        System.Diagnostics.Debug.Assert(_seamRefresh.Revision != revision,
            "Failed seam revalidation resets the refresh and advances its revision.");
        ResetSeamState();
        return false;
    }

    private bool AdvanceChangedMapCapture(MaintenanceWorkMeter meter)
    {
        while (true)
        {
            if (_capturePhase == 0)
            {
                if (!HasNextRawMapId())
                {
                    _capturePhase = 4;
                    return true;
                }
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                if (!TryTakeNextRawScope(
                        out _pendingMapId,
                        out _pendingExactAddress,
                        out _pendingWholeMap))
                {
                    _capturePhase = 4;
                    return true;
                }
                _pendingComponentKey = null;
                _pendingMedium = _pendingWholeMap
                    ? (TraversalMedium)((int)TraversalMedium.Liquid + 1)
                    : TraversalMedium.Solid;
                _pendingMapComponentOrdinal = 0;
                _addressBakedCursor = 0;
                _addressDynamicCursor = 0;
                _capturePhase = 1;
            }
            if (_capturePhase == 1)
            {
                if (!_changedMapIds.ContainsKey(_pendingMapId!))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _changedMapIds = _changedMapIds.Set(_pendingMapId!, true);
                }
                _capturePhase = 2;
            }
            if (_capturePhase == 2)
            {
                while (_pendingExactAddress.HasValue
                    && _pendingMedium <= TraversalMedium.Liquid)
                {
                    TraversalMedium medium = _pendingMedium++;
                    if (!HasMediumStateChanged(_pendingExactAddress.Value, medium)
                        || !_sourceGraph.TryGetSurfaceComponent(
                            _pendingExactAddress.Value,
                            medium,
                            out NavigationSurfaceComponentKey componentKey,
                            out _)
                        || _affectedComponents.Contains(componentKey))
                    {
                        continue;
                    }
                    if (!meter.TryConsumeDependencyEntries(1))
                    {
                        _pendingMedium--;
                        return false;
                    }
                    AddAffectedComponent(componentKey);
                }
                if (_pendingExactAddress.HasValue)
                {
                    if (!_affectedAddresses.Contains(_pendingExactAddress.Value))
                    {
                        if (!meter.TryConsumeDependencyEntries(1))
                            return false;
                        _affectedAddresses = _affectedAddresses.Add(
                            _pendingExactAddress.Value);
                    }
                }
                else if (_pendingWholeMap)
                {
                    if (_sourceGraph.TryGetMap(
                            _pendingMapId!,
                            out NavigationMapInstance? sourceInstance))
                    {
                        NavigationMapInstance instance = sourceInstance!;
                        while (_pendingComponentKey.HasValue
                            || _pendingMedium <= TraversalMedium.Liquid
                            || _pendingMapComponentOrdinal < instance.AddressCount)
                        {
                            if (_pendingComponentKey.HasValue)
                            {
                                NavigationSurfaceComponentKey pendingKey =
                                    _pendingComponentKey.Value;
                                if (!_affectedComponents.Contains(pendingKey))
                                {
                                    if (!meter.TryConsumeDependencyEntries(1))
                                        return false;
                                    AddAffectedComponent(pendingKey);
                                }
                                _pendingComponentKey = null;
                                continue;
                            }
                            if (_pendingMedium <= TraversalMedium.Liquid)
                            {
                                TraversalMedium medium = _pendingMedium++;
                                var currentAddress = new NavigationCellAddress(
                                    _pendingMapId!,
                                    _addressScratch[0]);
                                if (HasMediumStateChanged(currentAddress, medium)
                                    && _sourceGraph.TryGetSurfaceComponent(
                                        currentAddress,
                                        medium,
                                        out NavigationSurfaceComponentKey membershipKey,
                                        out _))
                                {
                                    _pendingComponentKey = membershipKey;
                                }
                                continue;
                            }
                            if (!meter.TryConsumeComponentNodes(1))
                                return false;
                            instance.CopyCanonicalAddressChunk(
                                ref _addressBakedCursor,
                                ref _addressDynamicCursor,
                                _addressScratch);
                            _pendingMapComponentOrdinal++;
                            _pendingMedium = TraversalMedium.Solid;
                        }
                    }
                    if (!_wholeMapIds.ContainsKey(_pendingMapId!))
                    {
                        if (!meter.TryConsumeDependencyEntries(1))
                            return false;
                        _wholeMapIds = _wholeMapIds.Set(_pendingMapId!, true);
                    }
                }
                _pendingMapId = null;
                _pendingComponentKey = null;
                _pendingMedium = TraversalMedium.Solid;
                _pendingExactAddress = null;
                _pendingMapComponentOrdinal = 0;
                _addressBakedCursor = 0;
                _addressDynamicCursor = 0;
                _pendingWholeMap = false;
                _capturePhase = 0;
            }
        }
    }

    private bool AdvanceSeamChangedMapCapture(MaintenanceWorkMeter meter)
    {
        while (_seamSourceIndex < _seamRefresh!.ChangedMapCount || _seamCapturePhase != 0)
        {
            if (_seamCapturePhase == 0)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _pendingSeamMapId = _seamRefresh.GetChangedMapIdAt(_seamSourceIndex++);
                _seamCapturePhase = 1;
            }
            System.Diagnostics.Debug.Assert(_seamCapturePhase == 1,
                "a metered seam-map read always advances directly into its ownership phase");
            if (!_changedMapIds.ContainsKey(_pendingSeamMapId!))
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _changedMapIds = _changedMapIds.Set(_pendingSeamMapId!, true);
            }
            _pendingSeamMapId = null;
            _seamCapturePhase = 0;
        }
        _seamCaptureComplete = true;
        return true;
    }

    private bool AdvanceExactAffectedCapture(MaintenanceWorkMeter meter)
    {
        while (_explicitOwnerIndex < _candidate.ExplicitChangedOwnerCount)
        {
            NavigationConnectionOwnerKey owner =
                _candidate.GetExplicitChangedOwnerAt(_explicitOwnerIndex);
            _sourceGraph.ExplicitConnections.TryGet(
                owner,
                out NavigationExplicitConnectionRecord prior);
            _candidate.ExplicitConnections.TryGet(
                owner,
                out NavigationExplicitConnectionRecord next);
            NavigationExplicitConnectionRecord? record = _explicitEndpointStage < 2
                ? prior
                : next;
            if (record != null && record.IsActive)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                CaptureAffectedAddress(
                    (_explicitEndpointStage & 1) == 0
                        ? record.Source
                        : record.Destination,
                    includeVolume: false);
            }
            _explicitEndpointStage++;
            if (_explicitEndpointStage == 4)
            {
                _explicitEndpointStage = 0;
                _explicitOwnerIndex++;
            }
        }

        while (_seamEndpointIndex < _seamRefresh!.ChangedStructuralEndpointCount)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            CaptureAffectedAddress(
                _seamRefresh.GetChangedStructuralEndpointAt(_seamEndpointIndex++),
                includeVolume: true);
        }

        while (_preparedMapOrdinal < PreparedGraph.MapCount)
        {
            NavigationMapInstance instance = PreparedGraph.GetInstance(_preparedMapOrdinal);
            if (!_wholeMapIds.ContainsKey(instance.MapId))
            {
                _preparedMapOrdinal++;
                continue;
            }
            while (_preparedMapAddressOrdinal < instance.AddressCount)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                instance.CopyCanonicalAddressChunk(
                    ref _addressBakedCursor,
                    ref _addressDynamicCursor,
                    _addressScratch);
                _preparedMapAddressOrdinal++;
                var address = new NavigationCellAddress(
                    instance.MapId,
                    _addressScratch[0]);
                if (PreparedGraph.HasEffectiveCell(address)
                    && !HasSameStructuralMedia(address))
                    _affectedAddresses = _affectedAddresses.Add(address);
            }
            _preparedMapOrdinal++;
            _preparedMapAddressOrdinal = 0;
            _addressBakedCursor = 0;
            _addressDynamicCursor = 0;
        }
        while (_incidentAddressIndex < _affectedAddresses.Count
            || _incidentNodeActive
            || _pendingIncidentAddress.HasValue)
        {
            if (_pendingIncidentAddress.HasValue)
            {
                NavigationCellAddress address = _pendingIncidentAddress.Value;
                if (_sourceGraph.TryGetSurfaceComponent(
                        address,
                        TraversalMedium.Solid,
                        out NavigationSurfaceComponentKey component,
                        out _)
                    && !_affectedComponents.Contains(component))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    AddAffectedComponent(component);
                }
                _pendingIncidentAddress = null;
                continue;
            }
            if (!_incidentNodeActive)
            {
                NavigationCellAddress address =
                    _affectedAddresses.GetAt(_incidentAddressIndex);
                while (_incidentMedium <= TraversalMedium.Liquid)
                {
                    TraversalMedium medium = _incidentMedium++;
                    if (!HasMediumStateChanged(address, medium))
                        continue;
                    var affectedState = new NavigationSurfaceComponentKey(address, medium);
                    System.Diagnostics.Debug.Assert(
                        !_affectedMediumStates.Contains(affectedState),
                        "the canonical affected-address cursor visits each exact medium once");
                    if (!meter.TryConsumeDependencyEntries(1))
                    {
                        _incidentMedium--;
                        return false;
                    }
                    _affectedMediumStates = _affectedMediumStates.Add(affectedState);
                }
                if (!PreparedGraph.TryGetStructuralMediumStateRef(
                        address,
                        TraversalMedium.Solid,
                        out NavigationMediumStateRef solidState))
                {
                    _incidentMedium = TraversalMedium.Gas;
                    _incidentAddressIndex++;
                    continue;
                }
                NavigationNodeRef node = solidState.Node;
                _incidentOutgoing = PreparedGraph.EnumerateStructuralSurfaceEdges(node);
                _incidentIncoming =
                    PreparedGraph.EnumerateIncomingStructuralSurfaceEdges(node);
                _incidentOutgoingComplete = false;
                _incidentNodeActive = true;
            }
            int remainingEdges = meter.RemainingSurfaceComponentEdges;
            if (!_incidentOutgoingComplete)
            {
                NavigationSurfaceEdgeAdvanceStatus status =
                    _incidentOutgoing.AdvanceOne(meter, ref remainingEdges);
                if (status == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                    return false;
                if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                    continue;
                if (status == NavigationSurfaceEdgeAdvanceStatus.Edge)
                {
                    bool found = PreparedGraph.TryGetNodeAddress(
                        _incidentOutgoing.Current.Target,
                        out NavigationCellAddress address);
                    System.Diagnostics.Debug.Assert(found,
                        "A structural outgoing edge targets a node in the prepared graph.");
                    _pendingIncidentAddress = address;
                    continue;
                }
                _incidentOutgoingComplete = true;
            }
            NavigationSurfaceEdgeAdvanceStatus incomingStatus =
                _incidentIncoming.AdvanceOne(meter, ref remainingEdges);
            if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                return false;
            if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Edge)
            {
                bool found = PreparedGraph.TryGetNodeAddress(
                    _incidentIncoming.Current.Predecessor,
                    out NavigationCellAddress address);
                System.Diagnostics.Debug.Assert(found,
                    "A structural incoming edge originates at a node in the prepared graph.");
                _pendingIncidentAddress = address;
                continue;
            }
            _incidentOutgoing = default;
            _incidentIncoming = default;
            _incidentNodeActive = false;
            _incidentMedium = TraversalMedium.Gas;
            _incidentAddressIndex++;
        }
        _exactCaptureComplete = true;
        return true;
    }

    private void CaptureAffectedAddress(
        NavigationCellAddress address,
        bool includeVolume)
    {
        _affectedAddresses = _affectedAddresses.Add(address);
        TraversalMedium maximum = includeVolume
            ? TraversalMedium.Liquid
            : TraversalMedium.Solid;
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= maximum;
             medium++)
        {
            if (_sourceGraph.TryGetSurfaceComponent(
                    address,
                    medium,
                    out NavigationSurfaceComponentKey component,
                    out _))
            {
                AddAffectedComponent(component);
            }
        }
    }

    private void AddAffectedComponent(NavigationSurfaceComponentKey key)
    {
        if (_affectedComponents.Contains(key))
            return;
        _affectedComponents = _affectedComponents.Add(key);
        _sourceGraph.SurfaceComponents.TryGet(key, out NavigationSurfaceComponent component);
        System.Diagnostics.Debug.Assert(
            component != null,
            "Affected keys originate from resolved source-graph components.");
        _affectedMemberCount = checked(_affectedMemberCount + component.Members.Count);
    }

    private void ResetSeamState()
    {
        _changedMapIds = PersistentStringMap<bool>.Empty;
        _affectedComponents = NavigationSurfaceComponentKeySet.Empty;
        _affectedAddresses = NavigationCellAddressSet.Empty;
        _affectedMediumStates = NavigationSurfaceComponentKeySet.Empty;
        _affectedMemberCount = 0;
        _wholeMapIds = PersistentStringMap<bool>.Empty;
        _pendingMapId = null;
        _pendingComponentKey = null;
        _pendingMedium = TraversalMedium.Solid;
        _pendingExactAddress = null;
        _pendingMapComponentOrdinal = 0;
        _pendingWholeMap = false;
        _explicitSourceIndex = 0;
        _changeIndex = 0;
        _overlayIndex = 0;
        _overlayCellIndex = 0;
        _capturePhase = 0;
        _seamSourceIndex = 0;
        _seamCapturePhase = 0;
        _pendingSeamMapId = null;
        _seamCaptureComplete = false;
        _exactCaptureComplete = false;
        _explicitOwnerIndex = 0;
        _explicitEndpointStage = 0;
        _seamEndpointIndex = 0;
        _preparedMapOrdinal = 0;
        _preparedMapAddressOrdinal = 0;
        _addressBakedCursor = 0;
        _addressDynamicCursor = 0;
        _incidentAddressIndex = 0;
        _incidentMedium = TraversalMedium.Gas;
        _incidentNodeActive = false;
        _incidentOutgoingComplete = false;
        _incidentOutgoing = default;
        _incidentIncoming = default;
        _pendingIncidentAddress = null;
        _preparation = null;
        _seamRefresh = null;
        _preparedGraph = null;
        _componentUpdate = null;
        if (_affectedClosurePublished)
            _allCloseRepublishRequired = true;
        _affectedClosurePublished = false;
        _publishedAffectedComponents = NavigationSurfaceComponentKeySet.Empty;
    }

    private bool HasNextRawMapId() =>
        _explicitSourceIndex < _candidate.ExplicitChangedSourceCount
        || _changeIndex < _batchChangeCount;

    private bool TryTakeNextRawScope(
        out string mapId,
        out NavigationCellAddress? exactAddress,
        out bool wholeMap)
    {
        if (_explicitSourceIndex < _candidate.ExplicitChangedSourceCount)
        {
            mapId = _candidate.GetExplicitChangedSourceAt(_explicitSourceIndex++);
            exactAddress = null;
            wholeMap = false;
            return true;
        }
        while (true)
        {
            NavigationOperationFrameChange change = _changes[_changeIndex];
            if (change.Kind != NavigationOperationFrameChangeKind.Overlay)
            {
                _changeIndex++;
                mapId = change.MapId!;
                exactAddress = null;
                wholeMap = true;
                return true;
            }
            ReadOnlySpan<NavigationMapOverlayDelta> maps =
                change.PreparedOverlay!.Transaction.MapSpan;
            NavigationMapOverlayDelta map = maps[_overlayIndex];
            if (_overlayCellIndex < map.CellSpan.Length)
            {
                mapId = map.MapId;
                var address = new NavigationCellAddress(
                    map.MapId,
                    map.CellSpan[_overlayCellIndex++].Index);
                exactAddress = HasSameStructuralMedia(address) ? null : address;
                wholeMap = false;
                return true;
            }
            bool hasNonCellStructuralDelta = !map.ConnectionSpan.IsEmpty
                || !map.TransitionSpan.IsEmpty;
            if (hasNonCellStructuralDelta && _overlayCellIndex == map.CellSpan.Length)
            {
                _overlayCellIndex++;
                mapId = map.MapId;
                exactAddress = null;
                wholeMap = false;
                return true;
            }
            _overlayIndex++;
            _overlayCellIndex = 0;
            if (_overlayIndex == maps.Length)
            {
                _overlayIndex = 0;
                _changeIndex++;
                if (_changeIndex == _batchChangeCount)
                {
                    mapId = null!;
                    exactAddress = null;
                    wholeMap = false;
                    return false;
                }
            }
        }
    }

    private bool HasSameStructuralMedia(NavigationCellAddress address)
    {
        bool priorAddressed = _sourceGraph.TryGetSemanticState(
            address,
            out NavigationCellSemanticSource priorSource,
            out bool priorHasCell,
            out NavigationCell priorCell);
        bool nextAddressed = _candidate.TryGetSemanticState(
            address,
            out NavigationCellSemanticSource nextSource,
            out bool nextHasCell,
            out NavigationCell nextCell);
        TraversalMedia priorMedia = priorAddressed && priorHasCell
            ? priorCell.Media
            : TraversalMedia.None;
        TraversalMedia nextMedia = nextAddressed && nextHasCell
            ? nextCell.Media
            : TraversalMedia.None;
        return priorMedia == nextMedia;
    }

    private bool HasMediumStateChanged(
        NavigationCellAddress address,
        TraversalMedium medium)
    {
        _sourceGraph.TryGetSemanticState(
            address,
            out _,
            out bool priorHasCell,
            out NavigationCell priorCell);
        _candidate.TryGetSemanticState(
            address,
            out _,
            out bool nextHasCell,
            out NavigationCell nextCell);
        bool prior = priorHasCell && priorCell.SupportsMedium(medium);
        bool next = nextHasCell && nextCell.SupportsMedium(medium);
        return prior != next;
    }

    internal static long GetMinimumScratchBytes(
        int sourceMapCount,
        int candidateMapCount,
        int changedMapCount,
        long overlayCellCount)
    {
        long semanticPages = overlayCellCount == 0 ? 0 : ((overlayCellCount + 63) / 64) + 1;
        return checked(
            NavigationAutomaticSeamRefreshWork.FixedRetainedBytes
            + 256L
            + ((long)candidateMapCount * 256L)
            + (semanticPages * 4_504L));
    }

    internal static int GetMinimumScratchPages(
        int candidateMapCount,
        long overlayCellCount)
    {
        long semanticPages = overlayCellCount == 0 ? 0 : ((overlayCellCount + 63) / 64) + 1;
        return checked(
            2 + 4
            + (candidateMapCount * 2)
            + checked((int)Math.Min(int.MaxValue, semanticPages * 2L)));
    }

}
