//=======================================================================
// NavigationAutomaticSeamRefreshWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Applies bounded incident automatic-seam changes to one unpublished graph candidate.</summary>
internal sealed class NavigationAutomaticSeamRefreshWork
{
    internal const long FixedRetainedBytes = 3_464L;

    private const int CursorHeight = 64;
    private const int PairDeltaNodeBytes = 104;
    private const int AddressDeltaNodeBytes = 80;
    private const int LinkDeltaNodeBytes = 72;
    private const long PairDeltaBytes = 40L;
    private const long AddressDeltaBytes = 56L;
    private const long LinkDeltaBytes = 24L;

    private static readonly NavigationSeamEditTree<NavigationAutomaticSeamPairKey, PairDelta>
        EmptyPairDeltas = new(PairDeltaNodeBytes);
    private static readonly NavigationSeamEditTree<NavigationCellAddress, AddressDelta>
        EmptyAddressDeltas = new(AddressDeltaNodeBytes);
    private static readonly NavigationSeamEditTree<NavigationAutomaticSeamLinkKey, LinkDelta>
        EmptyLinkDeltas = new(LinkDeltaNodeBytes);

    private readonly GridWorld _world;
    private readonly NavigationWorldGraph _sourceGraph;
    private readonly NavigationWorldGraph _preparedGraph;
    private readonly NavigationOperationFrameChange[] _changes;
    private readonly int _changeCount;
    private readonly GridEventInfo[] _gridEvents;
    private readonly int _gridEventCount;
    private readonly bool _fullRebuild;
    private NavigationSeamEditToken _ownershipToken;
    private GridBoundaryContactCursor _cursor = new();
    private GridBoundaryContactCursor _validatorCursor = new();
    private readonly GridBoundaryContact[] _contact = new GridBoundaryContact[1];
    private NavigationAutomaticSeamIndex _working;
    private PersistentStringMap<bool> _changedMapIds = PersistentStringMap<bool>.Empty;
    private NavigationCellAddressSet _changedStructuralEndpoints =
        NavigationCellAddressSet.Empty;

    private NavigationSeamEditTree<NavigationAutomaticSeamPairKey, PairDelta>.Editor? _pairEditor;
    private NavigationSeamEditTree<NavigationAutomaticSeamPairKey, PairDelta>? _pairDeltas;
    private NavigationSeamEditTree<NavigationAutomaticSeamPairKey, PairDelta>.Cursor? _pairCursor;
    private NavigationSeamEditTree<NavigationCellAddress, AddressDelta>.Editor? _addressEditor;
    private NavigationSeamEditTree<NavigationCellAddress, AddressDelta>? _addressDeltas;
    private NavigationSeamEditTree<NavigationCellAddress, AddressDelta>.Cursor? _addressCursor;
    private NavigationSeamEditTree<NavigationAutomaticSeamLinkKey, LinkDelta>.Editor? _linkEditor;
    private NavigationSeamEditTree<NavigationAutomaticSeamLinkKey, LinkDelta>? _linkDeltas;
    private NavigationSeamEditTree<NavigationAutomaticSeamLinkKey, LinkDelta>.Cursor? _linkCursor;
    private NavigationAutomaticSeamIndex.EditSession? _indexEditor;

    private NavigationSeamEditTree<NavigationCellAddress, NavigationPagedSequence<NavigationAutomaticSeamPair>>.Cursor?
        _dependencyCursor;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _dependencyRow;
    private NavigationCellAddress _dependencyAddress;
    private int _dependencyRowRemaining;

    private NavigationAutomaticSeamPair? _pendingPair;
    private MutationKind _pendingMutation;
    private int _mutationStage;
    private bool _pendingPairOwned;

    private PairDelta? _currentPairDelta;
    private NavigationAutomaticSeamPairKey _currentPairKey;
    private int _pairApplyStage;
    private bool _currentGeometryChanged;
    private bool _currentActiveChanged;
    private bool _currentActiveRowChanged;
    private bool _currentStructuralLinkChanged;

    private AddressDelta? _currentAddressDelta;
    private NavigationCellAddress _currentAddress;
    private int _addressApplyStage;
    private RowKind _rowKind;
    private NavigationPagedSequence<NavigationAutomaticSeamPair>.Enumerator _sourceRow;
    private NavigationPagedSequence<NavigationAutomaticSeamPair>.Enumerator _additionRow;
    private bool _sourceRowHasNext;
    private bool _sourceRowComplete;
    private bool _additionRowHasNext;
    private bool _additionRowComplete;
    private NavigationAutomaticSeamPair? _sourceRowPair;
    private NavigationAutomaticSeamPair? _additionRowPair;
    private NavigationAutomaticSeamPair? _pendingRowOutput;
    private NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder? _rowBuilder;

    private NavigationAutomaticSeamLinkKey _currentLinkKey;
    private LinkDelta? _currentLinkDelta;
    private bool _linkDeltaReady;
    private bool _linkDeltaComplete;
    private string? _linkSourceMapId;
    private NavigationPagedSequence<NavigationStructuralLink>.Enumerator _sourceLinks;
    private bool _sourceLinkReady;
    private bool _sourceLinksComplete;
    private NavigationStructuralLink _sourceLink;
    private NavigationStructuralLink? _pendingLinkOutput;
    private NavigationPagedSequence<NavigationStructuralLink>.Builder? _linkBuilder;

    private long _pairDeltaPayloadBytes;
    private int _pairDeltaPayloadPages;
    private long _addressDeltaPayloadBytes;
    private int _addressDeltaPayloadPages;
    private long _linkDeltaPayloadBytes;
    private int _linkDeltaPayloadPages;
    private long _sealedIndexBytes;
    private int _sealedIndexPages;

    private string? _modeMapId;
    private GridConfigurationKey _pendingDiscoveryKey;
    private int _changeIndex;
    private int _overlayMapIndex;
    private int _overlayCellIndex;
    private int _gridEventIndex;
    private int _worldResetMapIndex;
    private int _fullRebuildRemoveMapIndex;
    private int _fullRebuildDiscoverMapIndex;
    private bool _worldResetPending;
    private bool _overlayMapInspected;
    private WorkMode _mode;
    private WorkPhase _phase;
    private GridBoundaryContactRunStamp _runStamp;
    private bool _hasRunStamp;
    private bool _cursorBegun;
    private bool _hasValidatorCursor;
    private bool _complete;

    internal NavigationAutomaticSeamRefreshWork(
        GridWorld world,
        NavigationWorldGraph sourceGraph,
        NavigationWorldGraph preparedGraph,
        NavigationOperationFrameChange[] changes,
        int changeCount)
    {
        _world = world;
        _sourceGraph = sourceGraph;
        _preparedGraph = preparedGraph;
        _changes = changes;
        _changeCount = changeCount;
        _gridEvents = Array.Empty<GridEventInfo>();
        _gridEventCount = 0;
        _fullRebuild = false;
        _working = sourceGraph.AutomaticSeams;
        _ownershipToken = NavigationSeamEditToken.Create();
    }

    internal NavigationAutomaticSeamRefreshWork(
        GridWorld world,
        NavigationWorldGraph sourceGraph,
        NavigationWorldGraph preparedGraph,
        GridEventInfo[] gridEvents,
        int gridEventCount,
        bool fullRebuild = false)
    {
        _world = world;
        _sourceGraph = sourceGraph;
        _preparedGraph = preparedGraph;
        _changes = Array.Empty<NavigationOperationFrameChange>();
        _changeCount = 0;
        _gridEvents = gridEvents;
        _gridEventCount = gridEventCount;
        _fullRebuild = fullRebuild;
        _working = sourceGraph.AutomaticSeams;
        _ownershipToken = NavigationSeamEditToken.Create();
    }

    internal NavigationAutomaticSeamIndex Result => _working;

    internal bool IsComplete => _complete;

    internal bool GeometryChanged { get; private set; }

    internal bool StructuralLinksChanged { get; private set; }

    internal long Revision { get; private set; }

    internal int ChangedMapCount => _changedMapIds.Count;

    internal PersistentStringMap<bool> ChangedMapIds => _changedMapIds;

    internal string GetChangedMapIdAt(int ordinal) => _changedMapIds.GetKeyAt(ordinal);

    internal int ChangedStructuralEndpointCount => _changedStructuralEndpoints.Count;

    internal NavigationCellAddress GetChangedStructuralEndpointAt(int ordinal) =>
        _changedStructuralEndpoints.GetAt(ordinal);

    internal bool RevalidateForPublication() => RevalidateCompletedCursor();

    internal long RetainedBytes => checked(
        FixedRetainedBytes
        + (ReferenceEquals(_changedMapIds, PersistentStringMap<bool>.Empty)
            ? 0L
            : _changedMapIds.RetainedBytes)
        + (ReferenceEquals(_changedStructuralEndpoints, NavigationCellAddressSet.Empty)
            ? 0L
            : _changedStructuralEndpoints.RetainedBytes)
        + (_pendingPairOwned ? NavigationAutomaticSeamPair.RetainedSize : 0L)
        + GetPairJournalBytes()
        + GetAddressJournalBytes()
        + GetLinkJournalBytes()
        + (_dependencyCursor?.RetainedBytes ?? 0L)
        + (_indexEditor?.RetainedBytes ?? _sealedIndexBytes)
        + (_rowBuilder?.RetainedBytes ?? 0L)
        + (_linkBuilder?.RetainedBytes ?? 0L));

    internal int PersistentPageCount => checked(
        4
        + (ReferenceEquals(_changedMapIds, PersistentStringMap<bool>.Empty)
            ? 0
            : 1 + _changedMapIds.PersistentNodeCount)
        + (ReferenceEquals(_changedStructuralEndpoints, NavigationCellAddressSet.Empty)
            ? 0
            : _changedStructuralEndpoints.PersistentPageCount)
        + (_pendingPairOwned ? 1 : 0)
        + GetPairJournalPages()
        + GetAddressJournalPages()
        + GetLinkJournalPages()
        + (_dependencyCursor?.PersistentPageCount ?? 0)
        + (_indexEditor?.PersistentPageCount ?? _sealedIndexPages)
        + (_rowBuilder?.PersistentPageCount ?? 0)
        + (_linkBuilder?.PersistentPageCount ?? 0));

    internal bool Advance(MaintenanceWorkMeter meter)
    {
        while (true)
        {
            SeamAdvanceStatus status = AdvanceOne(meter);
            if (status == SeamAdvanceStatus.Complete)
                return true;
            if (status == SeamAdvanceStatus.Blocked)
                return false;
        }
    }

    internal SeamAdvanceStatus AdvanceOne(MaintenanceWorkMeter meter)
    {
        if (_complete)
            return RevalidateCompletedCursor()
                ? SeamAdvanceStatus.Complete
                : SeamAdvanceStatus.Blocked;
        bool progressed = _phase switch
        {
            WorkPhase.Gather => AdvanceGather(meter),
            WorkPhase.ApplyPairs => AdvancePairApplication(meter),
            WorkPhase.ApplyAddresses => AdvanceAddressApplication(meter),
            WorkPhase.ApplyLinks => AdvanceLinkApplication(meter),
            WorkPhase.Seal => SealResult(),
            _ => false
        };
        if (_complete)
            return SeamAdvanceStatus.Complete;
        return progressed ? SeamAdvanceStatus.Progressed : SeamAdvanceStatus.Blocked;
    }

    private bool AdvanceGather(MaintenanceWorkMeter meter)
    {
        if (_pendingPair != null)
            return AdvanceQueuedMutation(meter);
        if (_mode != WorkMode.None)
            return AdvanceMode(meter);
        if (TrySetupNextMode(meter))
            return true;
        if ((!_fullRebuild && _worldResetPending)
            || (_fullRebuild
                && (_fullRebuildRemoveMapIndex < _sourceGraph.MapCount
                    || _fullRebuildDiscoverMapIndex < _preparedGraph.MapCount))
            || (!_fullRebuild && _gridEventIndex < _gridEventCount)
            || _changeIndex < _changeCount)
            return false;
        if (!RevalidateCompletedCursor())
            return false;
        BeginPairApplication();
        return true;
    }

    private bool TrySetupNextMode(MaintenanceWorkMeter meter)
    {
        if (_fullRebuild)
        {
            if (TrySetupFullRebuildMode(meter))
                return true;
            if (_fullRebuildRemoveMapIndex < _sourceGraph.MapCount
                || _fullRebuildDiscoverMapIndex < _preparedGraph.MapCount)
            {
                return false;
            }
        }
        else if (TrySetupNextGridEventMode(meter))
        {
            return true;
        }
        while (_changeIndex < _changeCount)
        {
            NavigationOperationFrameChange change = _changes[_changeIndex];
            if (change.Kind == NavigationOperationFrameChangeKind.MapCommit)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _changeIndex++;
                string mapId = change.MapId!;
                _sourceGraph.TryGetMap(mapId, out NavigationMapInstance? prior);
                _preparedGraph.TryGetMap(mapId, out NavigationMapInstance? next);
                if (next == null)
                    continue;
                if (prior == null)
                {
                    BeginDiscovery(next.Map.GridBinding.Key);
                    return true;
                }
                if (!prior.Map.GridBinding.Key.Equals(next.Map.GridBinding.Key))
                {
                    _pendingDiscoveryKey = next.Map.GridBinding.Key;
                    BeginMapMode(mapId, WorkMode.RemoveMap);
                    return true;
                }
                BeginMapMode(mapId, WorkMode.RevalidateMap);
                return true;
            }
            if (change.Kind == NavigationOperationFrameChangeKind.MapRemove)
            {
                if (!meter.TryConsumeComponentNodes(1))
                    return false;
                _changeIndex++;
                BeginMapMode(change.MapId!, WorkMode.RemoveMap);
                return true;
            }

            ReadOnlySpan<NavigationMapOverlayDelta> maps =
                change.PreparedOverlay!.Transaction.MapSpan;
            while (_overlayMapIndex < maps.Length)
            {
                if (!_overlayMapInspected)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    _overlayMapInspected = true;
                }
                ReadOnlySpan<NavigationCellOverlayOperation> cells = maps[_overlayMapIndex].CellSpan;
                if (_overlayCellIndex < cells.Length)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    NavigationCellAddress address = new(
                        maps[_overlayMapIndex].MapId,
                        cells[_overlayCellIndex++].Index);
                    BeginAddressMode(address);
                    return true;
                }
                _overlayMapIndex++;
                _overlayCellIndex = 0;
                _overlayMapInspected = false;
            }
            _overlayMapIndex = 0;
            _overlayMapInspected = false;
            _changeIndex++;
        }
        return false;
    }

    private bool TrySetupFullRebuildMode(MaintenanceWorkMeter meter)
    {
        if (_fullRebuildRemoveMapIndex < _sourceGraph.MapCount)
        {
            if (!meter.TryConsumeComponentNodes(1))
                return false;
            BeginMapMode(
                _sourceGraph.GetInstance(_fullRebuildRemoveMapIndex++).MapId,
                WorkMode.RemoveMap);
            return true;
        }
        if (_fullRebuildDiscoverMapIndex < _preparedGraph.MapCount)
        {
            if (!meter.TryConsumeComponentNodes(1))
                return false;
            BeginDiscovery(
                _preparedGraph.GetInstance(_fullRebuildDiscoverMapIndex++)
                    .Map.GridBinding.Key);
            return true;
        }
        return false;
    }

    private bool TrySetupNextGridEventMode(MaintenanceWorkMeter meter)
    {
        while (_worldResetPending || _gridEventIndex < _gridEventCount)
        {
            if (_worldResetPending)
            {
                if (_worldResetMapIndex < _sourceGraph.MapCount)
                {
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    BeginMapMode(
                        _sourceGraph.GetInstance(_worldResetMapIndex++).MapId,
                        WorkMode.RemoveMap);
                    return true;
                }
                _worldResetPending = false;
                _worldResetMapIndex = 0;
                _gridEventIndex++;
                return true;
            }

            if (!meter.TryConsumeComponentNodes(1))
                return false;
            GridEventInfo eventInfo = _gridEvents[_gridEventIndex];
            GridConfigurationKey key = eventInfo.Configuration.ToGridKey();
            switch (eventInfo.ChangeKind)
            {
                case GridEventKind.GridRemoved:
                    _gridEventIndex++;
                    if (_sourceGraph.TryGetMapId(key, out string removedMapId))
                    {
                        BeginMapMode(removedMapId, WorkMode.RemoveMap);
                        return true;
                    }
                    break;
                case GridEventKind.GridAdded:
                    _gridEventIndex++;
                    if (_preparedGraph.TryGetMapId(key, out _))
                    {
                        BeginDiscovery(key);
                        return true;
                    }
                    break;
                case GridEventKind.WorldReset:
                    _worldResetPending = true;
                    _worldResetMapIndex = 0;
                    return true;
                default:
                    _gridEventIndex++;
                    break;
            }
            return true;
        }
        return false;
    }

    private bool AdvanceMode(MaintenanceWorkMeter meter)
    {
        if (_mode == WorkMode.Discover)
            return AdvanceDiscovery(meter);
        if (_dependencyRowRemaining > 0)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _dependencyRow.MoveNext();
            NavigationAutomaticSeamPair pair = _dependencyRow.Current.Pair;
            _dependencyRowRemaining--;
            NavigationCellAddress ownerAddress = pair.First.MapId.Equals(
                _modeMapId,
                StringComparison.Ordinal)
                ? pair.First
                : pair.Second;
            if (!_dependencyAddress.Equals(ownerAddress))
            {
                return true;
            }
            QueueMutation(
                pair,
                _mode == WorkMode.RemoveMap ? MutationKind.Remove : MutationKind.Revalidate);
            return true;
        }
        if (_mode == WorkMode.RevalidateAddress)
        {
            FinishMode();
            return true;
        }
        if (_dependencyCursor == null || !_dependencyCursor.HasNext)
            return CompleteMapMode();
        if (!meter.TryConsumeDependencyEntries(1))
            return false;
        _dependencyCursor.MoveNext();
        NavigationCellAddress address = _dependencyCursor.CurrentKey;
        if (!address.MapId.Equals(_modeMapId, StringComparison.Ordinal))
            return CompleteMapMode();
        _dependencyAddress = address;
        NavigationPagedSequence<NavigationAutomaticSeamPair> row =
            _sourceGraph.AutomaticSeams.GetDependencyRow(address);
        _dependencyRow = new NavigationAutomaticSeamIndex.EndpointEnumerator(address, row);
        _dependencyRowRemaining = row.Count;
        return true;
    }

    private bool CompleteMapMode()
    {
        WorkMode completedMode = _mode;
        FinishMode();
        if (completedMode == WorkMode.RemoveMap
            && !_pendingDiscoveryKey.Equals(default(GridConfigurationKey)))
        {
            GridConfigurationKey key = _pendingDiscoveryKey;
            _pendingDiscoveryKey = default;
            BeginDiscovery(key);
        }
        return true;
    }

    private bool AdvanceDiscovery(MaintenanceWorkMeter meter)
    {
        if (!_cursorBegun)
        {
            if (!RevalidateCompletedCursor())
                return false;
            if (!_world.TryBeginBoundaryContacts(_pendingDiscoveryKey, _cursor))
            {
                if (!RevalidateCompletedCursor())
                    return false;
                _pendingDiscoveryKey = default;
                FinishMode();
                return true;
            }
            if (_hasRunStamp && _cursor.RunStamp != _runStamp)
            {
                ResetToSource();
                return false;
            }
            _runStamp = _cursor.RunStamp;
            _hasRunStamp = true;
            _cursorBegun = true;
        }
        GridBoundaryContactCursorStatus status = _world.AdvanceBoundaryContacts(
            _cursor,
            _contact,
            meter.RemainingSeamCandidateProbes,
            outputLimit: 1,
            out int consumed,
            out int outputCount);
        meter.TryConsumeSeamCandidateProbes(consumed);
        if (status == GridBoundaryContactCursorStatus.Stale
            || _cursor.RunStamp != _runStamp)
        {
            ResetToSource();
            return false;
        }
        if (outputCount != 0)
            PrepareDiscoveredPair(_contact[0]);
        if (status == GridBoundaryContactCursorStatus.Complete && outputCount == 0)
        {
            GridBoundaryContactCursor previousValidator = _validatorCursor;
            _validatorCursor = _cursor;
            _cursor = previousValidator;
            _hasValidatorCursor = true;
            _cursorBegun = false;
            _pendingDiscoveryKey = default;
            FinishMode();
        }
        return outputCount != 0 || consumed != 0
            || status == GridBoundaryContactCursorStatus.Complete;
    }

    private void PrepareDiscoveredPair(GridBoundaryContact contact)
    {
        if (!_preparedGraph.TryGetMapId(contact.SourceConfigurationKey, out string sourceMapId)
            || !_preparedGraph.TryGetMapId(contact.TargetConfigurationKey, out string targetMapId))
        {
            return;
        }
        NavigationCellAddress first = new(sourceMapId, contact.Contact.Source.VoxelIndex);
        NavigationCellAddress second = new(targetMapId, contact.Contact.Target.VoxelIndex);
        var key = new NavigationAutomaticSeamPairKey(first, second);
        if (TryGetCurrentPair(key, out NavigationAutomaticSeamPair existing))
        {
            QueueMutation(existing, MutationKind.Revalidate);
            return;
        }
        if (!_preparedGraph.TryGetSeamPrism(key.First, out GridCellPrism firstPrism)
            || !_preparedGraph.TryGetSeamPrism(key.Second, out GridCellPrism secondPrism)
            || !GridCellGeometry.TryCreateNavigationPortal(
                firstPrism,
                secondPrism,
                out GridNavigationPortal portal))
        {
            return;
        }
        QueueMutation(
            new NavigationAutomaticSeamPair(key.First, key.Second, portal),
            MutationKind.Add,
            ownsPair: true);
    }

    private bool AdvanceQueuedMutation(MaintenanceWorkMeter meter)
    {
        if (_mutationStage < 2)
        {
            if (!meter.TryConsumeExplicitEdges(1))
                return false;
            _mutationStage++;
            return true;
        }
        if (_mutationStage < 4)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _mutationStage++;
            if (_mutationStage == 4)
                CoalescePendingMutation();
            return true;
        }
        throw new InvalidOperationException("The queued seam mutation stage is invalid.");
    }

    private void CoalescePendingMutation()
    {
        NavigationAutomaticSeamPair pair = _pendingPair!;
        var key = new NavigationAutomaticSeamPairKey(pair.First, pair.Second);
        EnsurePairEditor();
        if (!_pairEditor!.TryGetValue(key, out PairDelta delta))
        {
            _sourceGraph.AutomaticSeams.TryGetPairRecord(key, out NavigationAutomaticSeamPairRecord source);
            delta = new PairDelta(source);
            _pairEditor.Set(key, delta);
            _pairDeltaPayloadBytes = checked(_pairDeltaPayloadBytes + PairDeltaBytes);
            _pairDeltaPayloadPages++;
        }
        NavigationAutomaticSeamPair? prior = delta.FinalPair;
        if (_pendingMutation == MutationKind.Remove)
            delta.FinalPair = null;
        else if (_pendingMutation == MutationKind.Add)
            delta.FinalPair ??= pair;
        if (_pendingPairOwned && ReferenceEquals(delta.FinalPair, pair))
        {
            if (!ReferenceEquals(prior, pair))
            {
                _pairDeltaPayloadBytes = checked(
                    _pairDeltaPayloadBytes + NavigationAutomaticSeamPair.RetainedSize);
                _pairDeltaPayloadPages++;
                delta.OwnsFinalPair = true;
            }
            _pendingPairOwned = false;
        }
        if (delta.OwnsFinalPair && delta.FinalPair == null)
        {
            _pairDeltaPayloadBytes -= NavigationAutomaticSeamPair.RetainedSize;
            _pairDeltaPayloadPages--;
            delta.OwnsFinalPair = false;
        }
        delta.FinalActive = delta.FinalPair != null
            && _preparedGraph.HasEffectiveCell(delta.FinalPair.First)
            && _preparedGraph.HasEffectiveCell(delta.FinalPair.Second);
        _pendingPair = null;
        _pendingPairOwned = false;
        _pendingMutation = MutationKind.None;
        _mutationStage = 0;
    }

    private void BeginPairApplication()
    {
        if (_pairEditor == null)
        {
            _complete = true;
            return;
        }
        _pairDeltas = _pairEditor.Seal();
        _pairEditor = null;
        _pairCursor = _pairDeltas.CreateCursor(
            CursorHeight,
            NavigationAutomaticSeamIndex.PairCursorShellBytes);
        _pairCursor.BeginAll(_pairDeltas);
        _indexEditor = _sourceGraph.AutomaticSeams.Edit(_ownershipToken);
        _phase = WorkPhase.ApplyPairs;
    }

    private bool AdvancePairApplication(MaintenanceWorkMeter meter)
    {
        if (_currentPairDelta == null)
        {
            if (!_pairCursor!.HasNext)
            {
                _pairCursor = null;
                SealSecondaryJournals();
                return true;
            }
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _pairCursor.MoveNext();
            _currentPairKey = _pairCursor.CurrentKey;
            _currentPairDelta = _pairCursor.Current;
            NormalizeCurrentPairDelta();
            _pairApplyStage = 0;
            return true;
        }

        PairDelta delta = _currentPairDelta;
        while (_pairApplyStage < 13)
        {
            switch (_pairApplyStage)
            {
                case 0:
                    if (!_currentGeometryChanged && !_currentActiveChanged)
                    {
                        _pairApplyStage = 13;
                        continue;
                    }
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    NavigationAutomaticSeamPairRecord? record = delta.FinalPair == null
                        ? null
                        : new NavigationAutomaticSeamPairRecord(delta.FinalPair, delta.FinalActive);
                    _indexEditor!.SetPair(_currentPairKey, record);
                    if (delta.OwnsFinalPair)
                    {
                        _pairDeltaPayloadBytes -= NavigationAutomaticSeamPair.RetainedSize;
                        _pairDeltaPayloadPages--;
                        delta.OwnsFinalPair = false;
                    }
                    _pairApplyStage++;
                    return true;
                case 1:
                case 3:
                    if (!_currentGeometryChanged)
                    {
                        _pairApplyStage = 5;
                        continue;
                    }
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    TouchAddress(
                        _pairApplyStage == 1 ? _currentPairKey.First : _currentPairKey.Second,
                        dependency: true,
                        active: false);
                    _pairApplyStage++;
                    return true;
                case 2:
                case 4:
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    if (delta.FinalPair != null)
                        AppendAddressPair(
                            _pairApplyStage == 2 ? _currentPairKey.First : _currentPairKey.Second,
                            delta.FinalPair,
                            active: false);
                    _pairApplyStage++;
                    return true;
                case 5:
                case 7:
                    if (!_currentActiveRowChanged)
                    {
                        _pairApplyStage = 9;
                        continue;
                    }
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    TouchAddress(
                        _pairApplyStage == 5 ? _currentPairKey.First : _currentPairKey.Second,
                        dependency: false,
                        active: true);
                    _pairApplyStage++;
                    return true;
                case 6:
                case 8:
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    if (delta.FinalActive)
                        AppendAddressPair(
                            _pairApplyStage == 6 ? _currentPairKey.First : _currentPairKey.Second,
                            delta.FinalPair!,
                            active: true);
                    _pairApplyStage++;
                    return true;
                case 9:
                case 10:
                    if (!_currentActiveRowChanged && !_currentStructuralLinkChanged)
                    {
                        _pairApplyStage = 11;
                        continue;
                    }
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    bool reverse = _pairApplyStage == 10;
                    NavigationCellAddress endpoint = reverse
                        ? _currentPairKey.Second
                        : _currentPairKey.First;
                    _changedStructuralEndpoints =
                        _changedStructuralEndpoints.Add(endpoint);
                    if (!_currentStructuralLinkChanged)
                    {
                        _pairApplyStage++;
                        return true;
                    }
                    string source = reverse ? _currentPairKey.Second.MapId : _currentPairKey.First.MapId;
                    string destination = reverse ? _currentPairKey.First.MapId : _currentPairKey.Second.MapId;
                    NavigationAutomaticSeamPair? sourcePair = delta.SourceRecord?.Pair;
                    bool sourceActive = delta.SourceRecord?.IsActive ?? false;
                    AddLinkDelta(
                        source,
                        destination,
                        (delta.FinalActive ? 1 : 0) - (sourceActive ? 1 : 0),
                        GetUncertifiedCount(delta.FinalPair, delta.FinalActive)
                            - GetUncertifiedCount(sourcePair, sourceActive));
                    _pairApplyStage++;
                    return true;
                case 11:
                case 12:
                    if (!meter.TryConsumeComponentNodes(1))
                        return false;
                    AddChangedMapId(_pairApplyStage == 11
                        ? _currentPairKey.First.MapId
                        : _currentPairKey.Second.MapId);
                    _pairApplyStage++;
                    return true;
            }
        }
        _currentPairDelta = null;
        return true;
    }

    private void NormalizeCurrentPairDelta()
    {
        PairDelta delta = _currentPairDelta!;
        if (delta.FinalPair != null
            && (!_preparedGraph.TryGetMap(delta.FinalPair.First.MapId, out _)
                || !_preparedGraph.TryGetMap(delta.FinalPair.Second.MapId, out _)))
        {
            if (delta.OwnsFinalPair)
            {
                _pairDeltaPayloadBytes -= NavigationAutomaticSeamPair.RetainedSize;
                _pairDeltaPayloadPages--;
                delta.OwnsFinalPair = false;
            }
            delta.FinalPair = null;
            delta.FinalActive = false;
        }
        NavigationAutomaticSeamPair? sourcePair = delta.SourceRecord?.Pair;
        _currentGeometryChanged = !ReferenceEquals(sourcePair, delta.FinalPair);
        bool sourceActive = delta.SourceRecord?.IsActive ?? false;
        _currentActiveChanged = sourceActive != delta.FinalActive;
        _currentActiveRowChanged = _currentActiveChanged
            || (_currentGeometryChanged && (sourceActive || delta.FinalActive));
        _currentStructuralLinkChanged = _currentActiveChanged
            || GetUncertifiedCount(sourcePair, sourceActive)
                != GetUncertifiedCount(delta.FinalPair, delta.FinalActive);
        GeometryChanged |= _currentGeometryChanged;
        StructuralLinksChanged |= _currentStructuralLinkChanged;
    }

    private static int GetUncertifiedCount(
        NavigationAutomaticSeamPair? pair,
        bool active) => active
            && pair!.Portal.FaceKind != VoxelContactFaceKind.Vertical
                ? 1
                : 0;

    private void TouchAddress(NavigationCellAddress address, bool dependency, bool active)
    {
        EnsureAddressEditor();
        if (!_addressEditor!.TryGetValue(address, out AddressDelta delta))
        {
            delta = new AddressDelta();
            _addressEditor.Set(address, delta);
            _addressDeltaPayloadBytes = checked(_addressDeltaPayloadBytes + AddressDeltaBytes);
            _addressDeltaPayloadPages++;
        }
        delta.DependencyChanged |= dependency;
        delta.ActiveChanged |= active;
    }

    private void AppendAddressPair(
        NavigationCellAddress address,
        NavigationAutomaticSeamPair pair,
        bool active)
    {
        _addressEditor!.TryGetValue(address, out AddressDelta delta);
        long beforeBytes = delta.RetainedBytes;
        int beforePages = delta.PersistentPageCount;
        if (active)
            delta.AppendActive(pair);
        else
            delta.AppendDependency(pair);
        RecordAddressPayloadChange(delta, beforeBytes, beforePages);
    }

    private void AddLinkDelta(
        string sourceMapId,
        string destinationMapId,
        int countChange,
        int uncertifiedCountChange)
    {
        EnsureLinkEditor();
        var key = new NavigationAutomaticSeamLinkKey(sourceMapId, destinationMapId);
        if (!_linkEditor!.TryGetValue(key, out LinkDelta delta))
        {
            delta = new LinkDelta();
            _linkEditor.Set(key, delta);
            _linkDeltaPayloadBytes = checked(_linkDeltaPayloadBytes + LinkDeltaBytes);
            _linkDeltaPayloadPages++;
        }
        delta.Count = checked(delta.Count + countChange);
        delta.UncertifiedCount = checked(
            delta.UncertifiedCount + uncertifiedCountChange);
        if (delta.Count != 0 || delta.UncertifiedCount != 0)
            return;
        _linkEditor.Remove(key);
        _linkDeltaPayloadBytes -= LinkDeltaBytes;
        _linkDeltaPayloadPages--;
    }

    private void SealSecondaryJournals()
    {
        if (_addressEditor != null)
        {
            _addressDeltas = _addressEditor.Seal();
            _addressEditor = null;
            _addressCursor = _addressDeltas.CreateCursor(
                CursorHeight,
                NavigationAutomaticSeamIndex.AddressCursorShellBytes);
            _addressCursor.BeginAll(_addressDeltas);
        }
        if (_linkEditor != null)
        {
            _linkDeltas = _linkEditor.Seal();
            _linkEditor = null;
            _linkCursor = _linkDeltas.CreateCursor(
                CursorHeight,
                NavigationAutomaticSeamIndex.LinkCursorShellBytes);
            _linkCursor.BeginAll(_linkDeltas);
        }
        _phase = WorkPhase.ApplyAddresses;
    }

    private bool AdvanceAddressApplication(MaintenanceWorkMeter meter)
    {
        if (_addressCursor == null)
        {
            _phase = WorkPhase.ApplyLinks;
            return true;
        }
        if (_currentAddressDelta == null)
        {
            if (!_addressCursor.HasNext)
            {
                _addressCursor = null;
                _addressDeltas = null;
                _addressDeltaPayloadBytes = 0;
                _addressDeltaPayloadPages = 0;
                _phase = WorkPhase.ApplyLinks;
                return true;
            }
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _addressCursor.MoveNext();
            _currentAddress = _addressCursor.CurrentKey;
            _currentAddressDelta = _addressCursor.Current;
            long beforeBytes = _currentAddressDelta.RetainedBytes;
            int beforePages = _currentAddressDelta.PersistentPageCount;
            _currentAddressDelta.SealAdditions();
            RecordAddressPayloadChange(_currentAddressDelta, beforeBytes, beforePages);
            _addressApplyStage = 0;
            return true;
        }
        if (_addressApplyStage == 0)
        {
            if (!_currentAddressDelta.DependencyChanged)
            {
                _addressApplyStage = 1;
                return true;
            }
            if (_rowKind == RowKind.None)
                BeginRow(RowKind.Dependency, _currentAddressDelta.DependencyAdditions);
            if (!AdvanceRow(meter))
                return false;
            if (_rowKind != RowKind.None)
                return true;
            _addressApplyStage = 1;
            return true;
        }
        if (_addressApplyStage == 1)
        {
            if (!_currentAddressDelta.ActiveChanged)
            {
                _addressApplyStage = 2;
                return true;
            }
            if (_rowKind == RowKind.None)
                BeginRow(RowKind.Active, _currentAddressDelta.ActiveAdditions);
            if (!AdvanceRow(meter))
                return false;
            if (_rowKind != RowKind.None)
                return true;
            _addressApplyStage = 2;
            return true;
        }
        long retained = _currentAddressDelta.RetainedBytes;
        int pages = _currentAddressDelta.PersistentPageCount;
        _currentAddressDelta.ReleaseAdditions();
        RecordAddressPayloadChange(_currentAddressDelta, retained, pages);
        _currentAddressDelta = null;
        return true;
    }

    private void BeginRow(
        RowKind kind,
        NavigationPagedSequence<NavigationAutomaticSeamPair> additions)
    {
        ClearRowEnumeration();
        _rowKind = kind;
        NavigationPagedSequence<NavigationAutomaticSeamPair> source = kind == RowKind.Dependency
            ? _sourceGraph.AutomaticSeams.GetDependencyRow(_currentAddress)
            : _sourceGraph.AutomaticSeams.GetActiveRow(_currentAddress);
        _sourceRow = source.GetEnumerator();
        _additionRow = additions.GetEnumerator();
        _sourceRowComplete = source.Count == 0;
        _additionRowComplete = additions.Count == 0;
        _sourceRowHasNext = false;
        _additionRowHasNext = false;
        _pendingRowOutput = null;
    }

    private bool AdvanceRow(MaintenanceWorkMeter meter)
    {
        if (_pendingRowOutput != null)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _rowBuilder ??= new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder(8);
            _rowBuilder.Append(_pendingRowOutput);
            _pendingRowOutput = null;
            return true;
        }
        if (!_sourceRowHasNext && !_sourceRowComplete)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            if (!_sourceRow.MoveNext())
            {
                _sourceRowComplete = true;
                return true;
            }
            NavigationAutomaticSeamPair pair = _sourceRow.Current;
            if (ShouldFilterSourcePair(pair, _rowKind))
                return true;
            _sourceRowPair = pair;
            _sourceRowHasNext = true;
            return true;
        }
        if (!_additionRowHasNext && !_additionRowComplete)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            if (!_additionRow.MoveNext())
            {
                _additionRowComplete = true;
                return true;
            }
            _additionRowPair = _additionRow.Current;
            _additionRowHasNext = true;
            return true;
        }
        if (!_sourceRowHasNext && _sourceRowComplete
            && !_additionRowHasNext && _additionRowComplete)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            NavigationPagedSequence<NavigationAutomaticSeamPair> result =
                _rowBuilder?.Seal() ?? NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
            if (_rowKind == RowKind.Dependency)
                _indexEditor!.SetDependencyRow(_currentAddress, result);
            else
                _indexEditor!.SetActiveRow(_currentAddress, result);
            _rowBuilder = null;
            ClearRowEnumeration();
            _rowKind = RowKind.None;
            return true;
        }
        if (!_sourceRowHasNext)
        {
            _pendingRowOutput = _additionRowPair;
            _additionRowHasNext = false;
            return true;
        }
        if (!_additionRowHasNext)
        {
            _pendingRowOutput = _sourceRowPair;
            _sourceRowHasNext = false;
            return true;
        }
        int comparison = ComparePairs(_sourceRowPair!, _additionRowPair!);
        if (comparison <= 0)
        {
            _pendingRowOutput = _sourceRowPair;
            _sourceRowHasNext = false;
        }
        else
        {
            _pendingRowOutput = _additionRowPair;
            _additionRowHasNext = false;
        }
        return true;
    }

    private bool ShouldFilterSourcePair(NavigationAutomaticSeamPair pair, RowKind kind)
    {
        if (_pairDeltas == null
            || !_pairDeltas.TryGetValue(
                new NavigationAutomaticSeamPairKey(pair.First, pair.Second),
                out PairDelta delta))
        {
            return false;
        }
        bool geometryChanged = !ReferenceEquals(delta.SourceRecord?.Pair, delta.FinalPair);
        return kind == RowKind.Dependency
            ? geometryChanged
            : geometryChanged || (delta.SourceRecord?.IsActive ?? false) != delta.FinalActive;
    }

    private bool AdvanceLinkApplication(MaintenanceWorkMeter meter)
    {
        if (_linkCursor == null)
        {
            _phase = WorkPhase.Seal;
            return true;
        }
        if (!_linkDeltaReady && !_linkDeltaComplete)
        {
            if (!_linkCursor.HasNext)
            {
                _linkDeltaComplete = true;
            }
            else
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _linkCursor.MoveNext();
                _currentLinkKey = _linkCursor.CurrentKey;
                _currentLinkDelta = _linkCursor.Current;
                _linkDeltaReady = true;
                return true;
            }
        }
        if (_linkSourceMapId == null)
        {
            if (_linkDeltaComplete)
            {
                _linkCursor = null;
                _linkDeltas = null;
                _currentLinkDelta = null;
                _linkDeltaPayloadBytes = 0;
                _linkDeltaPayloadPages = 0;
                _phase = WorkPhase.Seal;
                return true;
            }
            _linkSourceMapId = _currentLinkKey.SourceMapId;
            NavigationPagedSequence<NavigationStructuralLink> source =
                _sourceGraph.AutomaticSeams.GetStructuralLinks(_linkSourceMapId);
            _sourceLinks = source.GetEnumerator();
            _sourceLinksComplete = source.Count == 0;
            return true;
        }
        if (_pendingLinkOutput.HasValue)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            _linkBuilder ??= new NavigationPagedSequence<NavigationStructuralLink>.Builder(16);
            _linkBuilder.Append(_pendingLinkOutput.Value);
            _pendingLinkOutput = null;
            return true;
        }
        if (!_sourceLinkReady && !_sourceLinksComplete)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            if (!_sourceLinks.MoveNext())
            {
                _sourceLinksComplete = true;
                return true;
            }
            _sourceLink = _sourceLinks.Current;
            _sourceLinkReady = true;
            return true;
        }
        bool deltaInGroup = _linkDeltaReady
            && _currentLinkKey.SourceMapId.Equals(_linkSourceMapId, StringComparison.Ordinal);
        if (!_sourceLinkReady && _sourceLinksComplete && !deltaInGroup)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            NavigationPagedSequence<NavigationStructuralLink> result =
                _linkBuilder?.Seal() ?? NavigationPagedSequence<NavigationStructuralLink>.Empty;
            _indexEditor!.SetStructuralLinks(_linkSourceMapId, result);
            _linkBuilder = null;
            _linkSourceMapId = null;
            _sourceLinks = default;
            _sourceLink = default;
            return true;
        }
        if (!_sourceLinkReady)
        {
            if (_currentLinkDelta!.Count > 0)
            {
                _pendingLinkOutput = new NavigationStructuralLink(
                    _currentLinkKey.DestinationMapId,
                    _currentLinkDelta.Count,
                    _currentLinkDelta.UncertifiedCount);
            }
            ConsumeCurrentLinkDelta();
            return true;
        }
        if (!deltaInGroup)
        {
            _pendingLinkOutput = _sourceLink;
            _sourceLinkReady = false;
            _sourceLink = default;
            return true;
        }
        int comparison = string.CompareOrdinal(
            _sourceLink.DestinationMapId,
            _currentLinkKey.DestinationMapId);
        if (comparison < 0)
        {
            _pendingLinkOutput = _sourceLink;
            _sourceLinkReady = false;
            return true;
        }
        if (comparison > 0)
        {
            if (_currentLinkDelta!.Count > 0)
            {
                _pendingLinkOutput = new NavigationStructuralLink(
                    _currentLinkKey.DestinationMapId,
                    _currentLinkDelta.Count,
                    _currentLinkDelta.UncertifiedCount);
            }
            ConsumeCurrentLinkDelta();
            return true;
        }
        int count = checked(_sourceLink.Count + _currentLinkDelta!.Count);
        int uncertifiedCount = checked(
            _sourceLink.UncertifiedCount + _currentLinkDelta.UncertifiedCount);
        if (count > 0)
        {
            _pendingLinkOutput = new NavigationStructuralLink(
                _sourceLink.DestinationMapId,
                count,
                uncertifiedCount);
        }
        _sourceLinkReady = false;
        _sourceLink = default;
        ConsumeCurrentLinkDelta();
        return true;
    }

    private void ConsumeCurrentLinkDelta()
    {
        _currentLinkDelta = null;
        _linkDeltaReady = false;
    }

    private void ClearRowEnumeration()
    {
        _sourceRow = default;
        _additionRow = default;
        _sourceRowPair = null;
        _additionRowPair = null;
        _pendingRowOutput = null;
        _sourceRowHasNext = false;
        _sourceRowComplete = false;
        _additionRowHasNext = false;
        _additionRowComplete = false;
    }

    private bool SealResult()
    {
        if (!RevalidateCompletedCursor())
            return false;
        _sealedIndexBytes = _indexEditor!.SealedAdditionalRetainedBytes;
        _sealedIndexPages = _indexEditor.SealedAdditionalPersistentPages;
        _working = _indexEditor.Seal();
        _indexEditor = null;
        _pairDeltas = null;
        _pairDeltaPayloadBytes = 0;
        _pairDeltaPayloadPages = 0;
        _complete = true;
        return true;
    }

    private bool RevalidateCompletedCursor()
    {
        if (!_hasValidatorCursor)
            return true;
        GridBoundaryContactCursorStatus status = _world.AdvanceBoundaryContacts(
            _validatorCursor,
            _contact.AsSpan(0, 0),
            0,
            0,
            out _,
            out _);
        if (status != GridBoundaryContactCursorStatus.Stale
            && _validatorCursor.RunStamp == _runStamp)
        {
            return true;
        }
        ResetToSource();
        return false;
    }

    private void BeginMapMode(string mapId, WorkMode mode)
    {
        _modeMapId = mapId;
        _dependencyCursor = _sourceGraph.AutomaticSeams.CreateDependencyCursor(CursorHeight);
        _sourceGraph.AutomaticSeams.BeginDependencyRange(_dependencyCursor, mapId);
        _dependencyRowRemaining = 0;
        _mode = mode;
    }

    private void BeginAddressMode(NavigationCellAddress address)
    {
        _modeMapId = address.MapId;
        _dependencyAddress = address;
        NavigationPagedSequence<NavigationAutomaticSeamPair> row =
            _sourceGraph.AutomaticSeams.GetDependencyRow(address);
        _dependencyRow = new NavigationAutomaticSeamIndex.EndpointEnumerator(address, row);
        _dependencyRowRemaining = row.Count;
        _mode = WorkMode.RevalidateAddress;
    }

    private void BeginDiscovery(GridConfigurationKey key)
    {
        _pendingDiscoveryKey = key;
        _cursorBegun = false;
        _mode = WorkMode.Discover;
    }

    private void QueueMutation(
        NavigationAutomaticSeamPair pair,
        MutationKind mutation,
        bool ownsPair = false)
    {
        _pendingPair = pair;
        _pendingMutation = mutation;
        _pendingPairOwned = ownsPair;
        _mutationStage = 0;
    }

    private void FinishMode()
    {
        _mode = WorkMode.None;
        _modeMapId = null;
        _dependencyCursor = null;
        _dependencyRow = default;
        _dependencyRowRemaining = 0;
    }

    private bool TryGetCurrentPair(
        NavigationAutomaticSeamPairKey key,
        out NavigationAutomaticSeamPair pair)
    {
        if (_pairEditor != null && _pairEditor.TryGetValue(key, out PairDelta delta))
        {
            if (delta.FinalPair != null)
            {
                pair = delta.FinalPair;
                return true;
            }
            pair = null!;
            return false;
        }
        return _sourceGraph.AutomaticSeams.TryGetPair(key.First, key.Second, out pair);
    }

    private void EnsurePairEditor() =>
        _pairEditor ??= EmptyPairDeltas.Edit(_ownershipToken);

    private void EnsureAddressEditor() =>
        _addressEditor ??= EmptyAddressDeltas.Edit(_ownershipToken);

    private void EnsureLinkEditor() =>
        _linkEditor ??= EmptyLinkDeltas.Edit(_ownershipToken);

    private void AddChangedMapId(string mapId)
    {
        if (!_changedMapIds.ContainsKey(mapId))
            _changedMapIds = _changedMapIds.Set(mapId, true);
    }

    private void RecordAddressPayloadChange(
        AddressDelta delta,
        long beforeBytes,
        int beforePages)
    {
        _addressDeltaPayloadBytes = checked(
            _addressDeltaPayloadBytes - beforeBytes + delta.RetainedBytes);
        _addressDeltaPayloadPages = checked(
            _addressDeltaPayloadPages - beforePages + delta.PersistentPageCount);
    }

    private long GetPairJournalBytes() => checked(
        (_pairEditor?.RetainedBytes ?? _pairDeltas?.RetainedBytes ?? 0L)
        + (_pairCursor?.RetainedBytes ?? 0L)
        + _pairDeltaPayloadBytes);

    private int GetPairJournalPages() => checked(
        (_pairEditor?.PersistentPageCount ?? _pairDeltas?.PersistentPageCount ?? 0)
        + (_pairCursor?.PersistentPageCount ?? 0)
        + _pairDeltaPayloadPages);

    private long GetAddressJournalBytes() => checked(
        (_addressEditor?.RetainedBytes ?? _addressDeltas?.RetainedBytes ?? 0L)
        + (_addressCursor?.RetainedBytes ?? 0L)
        + _addressDeltaPayloadBytes);

    private int GetAddressJournalPages() => checked(
        (_addressEditor?.PersistentPageCount ?? _addressDeltas?.PersistentPageCount ?? 0)
        + (_addressCursor?.PersistentPageCount ?? 0)
        + _addressDeltaPayloadPages);

    private long GetLinkJournalBytes() => checked(
        (_linkEditor?.RetainedBytes ?? _linkDeltas?.RetainedBytes ?? 0L)
        + (_linkCursor?.RetainedBytes ?? 0L)
        + _linkDeltaPayloadBytes);

    private int GetLinkJournalPages() => checked(
        (_linkEditor?.PersistentPageCount ?? _linkDeltas?.PersistentPageCount ?? 0)
        + (_linkCursor?.PersistentPageCount ?? 0)
        + _linkDeltaPayloadPages);

    private void ResetToSource()
    {
        _working = _sourceGraph.AutomaticSeams;
        _changedMapIds = PersistentStringMap<bool>.Empty;
        _changedStructuralEndpoints = NavigationCellAddressSet.Empty;
        _pairEditor = null;
        _pairDeltas = null;
        _pairCursor = null;
        _addressEditor = null;
        _addressDeltas = null;
        _addressCursor = null;
        _linkEditor = null;
        _linkDeltas = null;
        _linkCursor = null;
        _indexEditor = null;
        _dependencyCursor = null;
        _dependencyRow = default;
        _pendingPair = null;
        _pendingPairOwned = false;
        _pendingMutation = MutationKind.None;
        _mutationStage = 0;
        _currentPairDelta = null;
        _currentStructuralLinkChanged = false;
        _currentAddressDelta = null;
        _rowBuilder = null;
        _linkBuilder = null;
        _rowKind = RowKind.None;
        ClearRowEnumeration();
        _pendingLinkOutput = null;
        _linkSourceMapId = null;
        _currentLinkDelta = null;
        _linkDeltaReady = false;
        _linkDeltaComplete = false;
        _sourceLinks = default;
        _sourceLink = default;
        _sourceLinkReady = false;
        _sourceLinksComplete = false;
        _modeMapId = null;
        _pendingDiscoveryKey = default;
        _dependencyRowRemaining = 0;
        _changeIndex = 0;
        _overlayMapIndex = 0;
        _overlayCellIndex = 0;
        _overlayMapInspected = false;
        _gridEventIndex = 0;
        _worldResetMapIndex = 0;
        _fullRebuildRemoveMapIndex = 0;
        _fullRebuildDiscoverMapIndex = 0;
        _worldResetPending = false;
        _mode = WorkMode.None;
        _phase = WorkPhase.Gather;
        _runStamp = default;
        _hasRunStamp = false;
        _cursorBegun = false;
        _hasValidatorCursor = false;
        _complete = false;
        _pairDeltaPayloadBytes = 0;
        _pairDeltaPayloadPages = 0;
        _addressDeltaPayloadBytes = 0;
        _addressDeltaPayloadPages = 0;
        _linkDeltaPayloadBytes = 0;
        _linkDeltaPayloadPages = 0;
        _sealedIndexBytes = 0;
        _sealedIndexPages = 0;
        GeometryChanged = false;
        StructuralLinksChanged = false;
        Revision++;
        _ownershipToken = NavigationSeamEditToken.Create();
    }

    private static int ComparePairs(
        NavigationAutomaticSeamPair first,
        NavigationAutomaticSeamPair second) =>
        new NavigationAutomaticSeamPairKey(first.First, first.Second).CompareTo(
            new NavigationAutomaticSeamPairKey(second.First, second.Second));

    private sealed class PairDelta
    {
        internal PairDelta(NavigationAutomaticSeamPairRecord? sourceRecord)
        {
            SourceRecord = sourceRecord;
            FinalPair = sourceRecord?.Pair;
            FinalActive = sourceRecord?.IsActive ?? false;
        }

        internal NavigationAutomaticSeamPairRecord? SourceRecord;
        internal NavigationAutomaticSeamPair? FinalPair;
        internal bool FinalActive;
        internal bool OwnsFinalPair;
    }

    private sealed class AddressDelta
    {
        private NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder? _dependencyBuilder;
        private NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder? _activeBuilder;

        internal bool DependencyChanged;
        internal bool ActiveChanged;
        internal NavigationPagedSequence<NavigationAutomaticSeamPair> DependencyAdditions =
            NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
        internal NavigationPagedSequence<NavigationAutomaticSeamPair> ActiveAdditions =
            NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;

        internal long RetainedBytes => checked(
            AddressDeltaBytes
            + (_dependencyBuilder?.RetainedBytes ?? DependencyAdditions.RetainedBytes)
            + (_activeBuilder?.RetainedBytes ?? ActiveAdditions.RetainedBytes));

        internal int PersistentPageCount => checked(
            1
            + (_dependencyBuilder?.PersistentPageCount ?? DependencyAdditions.PersistentPageCount)
            + (_activeBuilder?.PersistentPageCount ?? ActiveAdditions.PersistentPageCount));

        internal void AppendDependency(NavigationAutomaticSeamPair pair)
        {
            _dependencyBuilder ??= new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder(8);
            _dependencyBuilder.Append(pair);
        }

        internal void AppendActive(NavigationAutomaticSeamPair pair)
        {
            _activeBuilder ??= new NavigationPagedSequence<NavigationAutomaticSeamPair>.Builder(8);
            _activeBuilder.Append(pair);
        }

        internal void SealAdditions()
        {
            if (_dependencyBuilder != null)
            {
                DependencyAdditions = _dependencyBuilder.Seal();
                _dependencyBuilder = null;
            }
            if (_activeBuilder != null)
            {
                ActiveAdditions = _activeBuilder.Seal();
                _activeBuilder = null;
            }
        }

        internal void ReleaseAdditions()
        {
            DependencyAdditions = NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
            ActiveAdditions = NavigationPagedSequence<NavigationAutomaticSeamPair>.Empty;
        }
    }

    private sealed class LinkDelta
    {
        internal int Count;
        internal int UncertifiedCount;
    }

    private enum WorkPhase : byte
    {
        Gather,
        ApplyPairs,
        ApplyAddresses,
        ApplyLinks,
        Seal
    }

    private enum WorkMode : byte
    {
        None,
        RemoveMap,
        RevalidateMap,
        RevalidateAddress,
        Discover
    }

    private enum MutationKind : byte
    {
        None,
        Add,
        Remove,
        Revalidate
    }

    private enum RowKind : byte
    {
        None,
        Dependency,
        Active
    }

    internal enum SeamAdvanceStatus : byte
    {
        Blocked,
        Progressed,
        Complete
    }
}
