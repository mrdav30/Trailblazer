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
    private readonly bool _updateComposition;
    private PersistentStringMap<bool> _changedMapIds = PersistentStringMap<bool>.Empty;
    private NavigationSurfaceComponentKeySet _affectedComponents =
        NavigationSurfaceComponentKeySet.Empty;
    private NavigationSurfaceComponentKeySet _publishedAffectedComponents =
        NavigationSurfaceComponentKeySet.Empty;
    private NavigationCellAddressSet _affectedAddresses = NavigationCellAddressSet.Empty;
    private int _affectedMemberCount;
    private PersistentStringMap<bool> _wholeMapIds = PersistentStringMap<bool>.Empty;
    private NavigationWorldGraph.StructuralPreparationWork? _preparation;
    private NavigationAutomaticSeamRefreshWork? _seamRefresh;
    private NavigationWorldGraph? _preparedGraph;
    private NavigationSurfaceComponentBuildWork? _componentUpdate;
    private string? _pendingMapId;
    private NavigationSurfaceComponentKey? _pendingComponentKey;
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
    private NavigationSurfaceComponentKey? _pendingSeamComponentKey;
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
        int changeCount,
        bool updateComposition)
    {
        _world = world;
        _sourceGraph = sourceGraph;
        _candidate = candidate;
        _changes = changes;
        _updateComposition = updateComposition;
        _batchChangeCount = changeCount;
    }

    internal int CapturedChangedMapCount => _changedMapIds.Count;

    internal bool IsChangedMapCaptureComplete => _capturePhase == 4
        && _seamCaptureComplete
        && _exactCaptureComplete;

    internal NavigationSurfaceComponentKeySet AffectedComponents => _affectedComponents;

    internal bool RequiresAffectedClosurePublication => _updateComposition
        && _allClosePublished
        && IsChangedMapCaptureComplete
        && (!_affectedClosurePublished
            || !ReferenceEquals(_publishedAffectedComponents, _affectedComponents));

    internal bool RequiresAllClosePublication => _allCloseRepublishRequired;

    internal string GetCapturedChangedMapIdAt(int ordinal) =>
        _changedMapIds.GetKeyAt(ordinal);

    internal bool UpdatesComposition => _updateComposition;

    internal NavigationWorldGraph PreparedGraph => _preparedGraph!;

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _changedMapIds.RetainedBytes
        + _wholeMapIds.RetainedBytes
        + _affectedAddresses.RetainedBytes
        + (ReferenceEquals(_publishedAffectedComponents, _affectedComponents)
            ? 0L
            : _affectedComponents.RetainedBytes)
        + (_preparation?.RetainedBytes ?? 0)
        + (_seamRefresh?.RetainedBytes ?? 0)
        + (_componentUpdate?.RetainedBytes ?? 0)
        );

    internal int PersistentPageCount => checked(
        1
        + _changedMapIds.PersistentNodeCount
        + _wholeMapIds.PersistentNodeCount
        + _affectedAddresses.PersistentPageCount
        + (ReferenceEquals(_publishedAffectedComponents, _affectedComponents)
            ? 0
            : _affectedComponents.PersistentPageCount)
        + (_preparation?.PersistentPageCount ?? 0)
        + (_seamRefresh?.PersistentPageCount ?? 0)
        + (_componentUpdate?.PersistentPageCount ?? 0));

    internal bool IsComplete => IsChangedMapCaptureComplete
        && (_preparation?.IsComplete ?? false)
        && (_seamRefresh?.IsComplete ?? false)
        && (!_updateComposition
            || (_affectedComponents.Count == 0 && _affectedAddresses.Count == 0
                || (_componentUpdate?.IsComplete ?? false)));

    internal NavigationWorldGraph Result => _updateComposition
        ? PreparedGraph.WithSurfaceComponents(
            _componentUpdate?.Result ?? _sourceGraph.SurfaceComponents)
        : PreparedGraph;

    internal bool Matches(NavigationOperationFrameChange[] changes, int changeCount)
    {
        if (changeCount != _batchChangeCount)
            return false;
        return ReferenceEquals(changes, _changes);
    }

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
        // Topology-native edges, seams, and cache invalidations enter the same meter in Phases 3
        // and 4; Phase 2 owns only explicit edges, reverse dependencies, and weak components.
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
        if (!_updateComposition)
            return true;
        if (_affectedComponents.Count == 0 && _affectedAddresses.Count == 0)
            return true;
        _componentUpdate ??= new NavigationSurfaceComponentBuildWork(
            PreparedGraph,
            _sourceGraph,
            _affectedComponents,
            _affectedAddresses,
            checked(_affectedMemberCount + _affectedAddresses.Count));
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

    internal void MarkAllClosePublished()
    {
        _allClosePublished = true;
        _allCloseRepublishRequired = false;
    }

    internal void MarkAllCloseRepublished()
    {
        _allClosePublished = true;
        _allCloseRepublishRequired = false;
        _affectedClosurePublished = false;
        _publishedAffectedComponents = NavigationSurfaceComponentKeySet.Empty;
    }

    internal bool RevalidateAutomaticSeamsForPublication()
    {
        if (_seamRefresh == null || !_seamCaptureComplete)
            return true;
        long revision = _seamRefresh.Revision;
        if (_seamRefresh.RevalidateForPublication())
            return true;
        if (_seamRefresh.Revision != revision)
            ResetSeamState();
        return false;
    }

    private bool AdvanceChangedMapCapture(MaintenanceWorkMeter meter)
    {
        while (_capturePhase < 4)
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
                TakeNextRawScope(
                    out _pendingMapId,
                    out _pendingExactAddress,
                    out _pendingWholeMap);
                string pendingMapId = _pendingMapId!;
                _pendingComponentKey = _pendingExactAddress.HasValue
                    && _sourceGraph.TryGetSurfaceComponent(
                        _pendingExactAddress.Value,
                        out NavigationSurfaceComponentKey componentKey,
                        out _)
                    ? componentKey
                    : null;
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
                if (_pendingExactAddress.HasValue && _pendingComponentKey.HasValue)
                {
                    if (!_affectedComponents.Contains(_pendingComponentKey.Value))
                    {
                        if (!meter.TryConsumeDependencyEntries(1))
                            return false;
                        AddAffectedComponent(_pendingComponentKey.Value);
                    }
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
                            if (!meter.TryConsumeComponentNodes(1))
                                return false;
                            instance.CopyCanonicalAddressChunk(
                                ref _addressBakedCursor,
                                ref _addressDynamicCursor,
                                _addressScratch);
                            _pendingMapComponentOrdinal++;
                            var address = new NavigationCellAddress(
                                _pendingMapId!,
                                _addressScratch[0]);
                            if (_sourceGraph.TryGetSurfaceComponent(
                                    address,
                                    out NavigationSurfaceComponentKey membershipKey,
                                    out _))
                            {
                                _pendingComponentKey = membershipKey;
                            }
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
                _pendingExactAddress = null;
                _pendingMapComponentOrdinal = 0;
                _addressBakedCursor = 0;
                _addressDynamicCursor = 0;
                _pendingWholeMap = false;
                _capturePhase = 0;
            }
        }
        return true;
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
                _pendingSeamComponentKey = null;
                _seamCapturePhase = 1;
            }
            if (_seamCapturePhase == 1)
            {
                if (!_changedMapIds.ContainsKey(_pendingSeamMapId!))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _changedMapIds = _changedMapIds.Set(_pendingSeamMapId!, true);
                }
                _seamCapturePhase = 2;
            }
            if (_seamCapturePhase == 2)
            {
                if (_pendingSeamComponentKey.HasValue
                    && !_affectedComponents.Contains(_pendingSeamComponentKey.Value))
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    AddAffectedComponent(_pendingSeamComponentKey.Value);
                }
                _pendingSeamMapId = null;
                _pendingSeamComponentKey = null;
                _seamCapturePhase = 0;
            }
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
                        : record.Destination);
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
                _seamRefresh.GetChangedStructuralEndpointAt(_seamEndpointIndex++));
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
                if (PreparedGraph.HasEffectiveCell(address))
                    CaptureAffectedAddress(address);
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
                if (!PreparedGraph.TryGetNodeRef(address, out NavigationNodeRef node)
                    || !PreparedGraph.HasEffectiveCell(address))
                {
                    _incidentAddressIndex++;
                    continue;
                }
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
                    if (PreparedGraph.TryGetNodeAddress(
                            _incidentOutgoing.Current.Target,
                            out NavigationCellAddress address))
                    {
                        _pendingIncidentAddress = address;
                    }
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
                if (PreparedGraph.TryGetNodeAddress(
                        _incidentIncoming.Current.Predecessor,
                        out NavigationCellAddress address))
                {
                    _pendingIncidentAddress = address;
                }
                continue;
            }
            _incidentOutgoing = default;
            _incidentIncoming = default;
            _incidentNodeActive = false;
            _incidentAddressIndex++;
        }
        _exactCaptureComplete = true;
        return true;
    }

    private void CaptureAffectedAddress(NavigationCellAddress address)
    {
        _affectedAddresses = _affectedAddresses.Add(address);
        if (_sourceGraph.TryGetSurfaceComponent(
                address,
                out NavigationSurfaceComponentKey component,
                out _))
        {
            AddAffectedComponent(component);
        }
    }

    private void AddAffectedComponent(NavigationSurfaceComponentKey key)
    {
        if (_affectedComponents.Contains(key))
            return;
        _affectedComponents = _affectedComponents.Add(key);
        if (_sourceGraph.SurfaceComponents.TryGet(
                key,
                out NavigationSurfaceComponent component))
        {
            _affectedMemberCount = checked(
                _affectedMemberCount + component.Members.Count);
        }
    }

    private void AddAffectedComponent(NavigationSurfaceComponent component)
    {
        if (_affectedComponents.Contains(component.Key))
            return;
        _affectedComponents = _affectedComponents.Add(component.Key);
        _affectedMemberCount = checked(
            _affectedMemberCount + component.Members.Count);
    }

    private void ResetSeamState()
    {
        _changedMapIds = PersistentStringMap<bool>.Empty;
        _affectedComponents = NavigationSurfaceComponentKeySet.Empty;
        _affectedAddresses = NavigationCellAddressSet.Empty;
        _affectedMemberCount = 0;
        _wholeMapIds = PersistentStringMap<bool>.Empty;
        _pendingMapId = null;
        _pendingComponentKey = null;
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
        _pendingSeamComponentKey = null;
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

    private void TakeNextRawScope(
        out string mapId,
        out NavigationCellAddress? exactAddress,
        out bool wholeMap)
    {
        if (_explicitSourceIndex < _candidate.ExplicitChangedSourceCount)
        {
            mapId = _candidate.GetExplicitChangedSourceAt(_explicitSourceIndex++);
            exactAddress = null;
            wholeMap = false;
            return;
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
                return;
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
                exactAddress = HasSameSemanticState(address) ? null : address;
                wholeMap = false;
                return;
            }
            bool hasNonCellStructuralDelta = !map.ConnectionSpan.IsEmpty
                || !map.TransitionSpan.IsEmpty;
            if (hasNonCellStructuralDelta && _overlayCellIndex == map.CellSpan.Length)
            {
                _overlayCellIndex++;
                mapId = map.MapId;
                exactAddress = null;
                wholeMap = false;
                return;
            }
            _overlayIndex++;
            _overlayCellIndex = 0;
            if (_overlayIndex == maps.Length)
            {
                _overlayIndex = 0;
                _changeIndex++;
            }
        }
    }

    private bool HasSameSemanticState(NavigationCellAddress address)
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
        return priorAddressed == nextAddressed
            && (!priorAddressed
                || priorSource == nextSource
                    && priorHasCell == nextHasCell
                    && (!priorHasCell || priorCell.Equals(nextCell)));
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
