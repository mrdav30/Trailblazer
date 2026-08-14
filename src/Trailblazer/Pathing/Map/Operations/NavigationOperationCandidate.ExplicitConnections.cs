//=======================================================================
// NavigationOperationCandidate.ExplicitConnections.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationOperationCandidate
{
    internal NavigationExplicitConnectionIndex ExplicitConnections => _explicitConnections;

    internal int ExplicitChangedSourceCount => _explicitChangedSources.Count;

    internal string GetExplicitChangedSourceAt(int ordinal) =>
        _explicitChangedSources.GetKeyAt(ordinal);

    internal ExplicitConnectionRefreshWork BeginExplicitConnectionRefresh(
        string mapId,
        NavigationExplicitConnectionIndex foldSource,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            foldSource,
            mapId,
            transaction: null,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    internal ExplicitConnectionRefreshWork BeginExplicitConnectionRefresh(
        NavigationOverlayTransaction transaction,
        NavigationExplicitConnectionIndex foldSource,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            foldSource,
            mapId: null,
            transaction,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    private bool TrySelectConnection(
        NavigationConnectionOwnerKey owner,
        out MapState source,
        out NavigationConnection definition)
    {
        if (!_maps.TryGetValue(owner.MapId, out source!))
        {
            definition = null!;
            return false;
        }
        if (source.Overlay.TryGetConnection(
                owner.ConnectionId,
                out NavigationConnectionOverlayOperation overlay))
        {
            if (overlay.Kind == NavigationConnectionOverlayOperationKind.Upsert)
            {
                definition = overlay.Connection!;
                return true;
            }
            if (overlay.Kind == NavigationConnectionOverlayOperationKind.Suppress)
            {
                definition = null!;
                return false;
            }
        }
        ReadOnlySpan<NavigationConnection> baked = source.Map.ConnectionSpan;
        int low = 0;
        int high = baked.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(baked[middle].Id, owner.ConnectionId);
            if (comparison == 0)
            {
                definition = baked[middle];
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        definition = null!;
        return false;
    }

    private bool TryGetSemanticPrism(
        NavigationCellAddress address,
        NavigationConnection connection,
        bool validateAnchor,
        Vector3d anchor,
        out GridCellPrism prism,
        out NavigationCell cell,
        out bool dormant)
    {
        dormant = false;
        if (!_maps.TryGetValue(address.MapId, out MapState target))
        {
            prism = default;
            cell = default;
            dormant = true;
            return true;
        }
        if (!TryGetEffectiveCell(target, address.Index, out cell))
        {
            prism = default;
            if (IsKnownSuppressed(target, address.Index))
            {
                dormant = true;
                return true;
            }
            return false;
        }
        if (connection.PortalRadiusClearance > cell.RadiusClearance
            || connection.PortalHeightClearance > cell.HeightClearance
            || !target.Map.GridBinding.TryGetCellPrism(address.Index, out prism)
            || (validateAnchor && !prism.Contains(anchor)))
        {
            prism = default;
            return false;
        }
        return true;
    }

    private static bool ValidateLowerBound(
        in GridCellPrism source,
        in GridCellPrism destination,
        NavigationCell destinationCell,
        NavigationConnection connection,
        Fixed64 corridorCost)
    {
        Vector3d sourceFoot = new(source.Center.X, source.VerticalMin, source.Center.Z);
        Vector3d destinationFoot = new(
            destination.Center.X,
            destination.VerticalMin,
            destination.Center.Z);
        return Vector3d.TryGetDistance(sourceFoot, connection.EntryAnchor, out Fixed64 approach)
            && Vector3d.TryGetDistance(connection.ExitAnchor, destinationFoot, out Fixed64 departure)
            && Fixed64.TryAdd(approach, corridorCost, out Fixed64 total)
            && Fixed64.TryAdd(total, departure, out total)
            && Fixed64.TryAdd(total, connection.AdditionalCost, out total)
            && Fixed64.TryAdd(total, destinationCell.EnterCost, out total)
            && Vector3d.TryGetDistance(sourceFoot, destinationFoot, out Fixed64 direct)
            && total >= direct;
    }

    private static NavigationExplicitConnectionRecord Dormant(
        NavigationConnectionOwnerKey owner,
        NavigationConnection connection) => new(
            owner,
            connection,
            isActive: false,
            Fixed64.Zero,
            NavigationPagedSequence<Vector3d>.Empty);

    private static bool IsKnownSuppressed(MapState state, GridForge.Spatial.VoxelIndex index) =>
        state.Overlay.TryGetCell(index, out NavigationCellOverlayOperation operation)
        && operation.Kind == NavigationCellOverlayOperationKind.Suppress;

    internal sealed class ExplicitConnectionRefreshWork
    {
        private const long BaseRetainedBytes = 688L;

        private readonly NavigationOperationCandidate _candidate;
        private readonly NavigationExplicitConnectionIndex _foldSource;
        private readonly string? _mapId;
        private readonly NavigationOverlayTransaction? _transaction;
        private readonly GridCellPrism[] _corridorPrisms;
        private readonly Vector3d[] _corridorWaypoints;
        private readonly NavigationCellAddress[] _corridorAddresses;
        private readonly NavigationAddressStampSet _corridorAddressSet;
        private PersistentStringMap<PersistentStringMap<bool>> _owners =
            PersistentStringMap<PersistentStringMap<bool>>.Empty;
        private int _stage;
        private int _mapIndex;
        private int _itemIndex;
        private NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator
            _incidentOwnerEnumerator;
        private NavigationConnectionOwnerKey _pendingIncidentOwner;
        private bool _incidentOwnerEnumerationStarted;
        private bool _hasPendingIncidentOwner;
        private int _compileMapIndex;
        private int _compileOwnerIndex;
        private NavigationConnectionOwnerKey _currentOwner;
        private MapState? _currentSource;
        private NavigationConnection? _currentDefinition;
        private NavigationExplicitConnectionRecord? _priorRecord;
        private NavigationExplicitConnectionRecord? _preparedRecord;
        private NavigationCell _destinationCell;
        private int _semanticIndex;
        private int _rawAddressIndex;
        private int _distinctAddressCount;
        private int _touchAddressIndex;
        private bool _currentInitialized;
        private bool _selectionCharged;
        private bool _isDormant;
        private bool _incidenceUnchanged;
        private bool _ownerUpdated;
        private bool _ownerJournaled;
        private bool _corridorStarted;
        private GridNavigationCorridorValidationCursor _corridorCursor;
        private NavigationPagedSequence<Vector3d>.Builder? _waypointBuilder;
        private int _waypointCopyIndex;
        private PersistentStringMap<PersistentVoxelIndexMap<IncidenceRowDelta>> _rowDeltas =
            PersistentStringMap<PersistentVoxelIndexMap<IncidenceRowDelta>>.Empty;
        private IncidenceOwnerTree _finalOwners;
        private IncidenceOwnerTree.Node? _additionOwner;
        private NavigationConnection? _additionDefinition;
        private int _additionAddressIndex;
        private bool _additionOwnerInitialized;
        private int _rowMapIndex;
        private int _rowAddressIndex;
        private NavigationCellAddress _rowAddress;
        private IncidenceRowDelta? _rowDelta;
        private NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator _priorRowOwners;
        private NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator _additionRowOwners;
        private NavigationConnectionOwnerKey _priorRowOwner;
        private NavigationConnectionOwnerKey _additionRowOwner;
        private int _priorRowRemaining;
        private int _additionRowRemaining;
        private bool _priorRowOwnerReady;
        private bool _additionRowOwnerReady;
        private NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder? _finalRowBuilder;
        private NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder? _endpointRowBuilder;
        private NavigationConnectionOwnerKey _pendingEndpointOwner;
        private bool _endpointOwnerPending;
        private bool _incidentRowCommitted;
        private bool _rowInitialized;
        private long _rowDeltaInnerBytes;
        private int _rowDeltaInnerPages;
        private long _rowDeltaValueBytes;
        private int _rowDeltaValuePages;
        private long _innerOwnerBytes;
        private int _innerOwnerPages;
        private long _displacedSourcePayloadBytes;
        private int _displacedSourcePayloadPages;

        internal ExplicitConnectionRefreshWork(
            NavigationOperationCandidate candidate,
            NavigationExplicitConnectionIndex foldSource,
            string? mapId,
            NavigationOverlayTransaction? transaction,
            GridCellPrism[] corridorPrisms,
            Vector3d[] corridorWaypoints,
            NavigationCellAddress[] corridorAddresses,
            NavigationAddressStampSet corridorAddressSet)
        {
            _candidate = candidate;
            _foldSource = foldSource;
            _mapId = mapId;
            _transaction = transaction;
            _corridorPrisms = corridorPrisms;
            _corridorWaypoints = corridorWaypoints;
            _corridorAddresses = corridorAddresses;
            _corridorAddressSet = corridorAddressSet;
        }

        internal bool IsValid { get; private set; } = true;

        internal bool IsGatherComplete => _stage == 4;

        internal long DisplacedSourcePayloadBytes => _displacedSourcePayloadBytes;

        internal int DisplacedSourcePayloadPages => _displacedSourcePayloadPages;

        internal long RetainedBytes => checked(
            BaseRetainedBytes
            + _owners.RetainedBytes
            + _innerOwnerBytes
            + _rowDeltas.RetainedBytes
            + _rowDeltaInnerBytes
            + _rowDeltaValueBytes
            + _finalOwners.RetainedBytes
            + (_finalRowBuilder?.RetainedBytes ?? 0)
            + (_endpointRowBuilder?.RetainedBytes ?? 0)
            + (_waypointBuilder?.RetainedBytes ?? 0)
            + (_ownerUpdated ? 0 : _preparedRecord?.RetainedBytes ?? 0));

        internal int PersistentPageCount => checked(
            1
            + _owners.PersistentNodeCount
            + _innerOwnerPages
            + _rowDeltas.PersistentNodeCount
            + _rowDeltaInnerPages
            + _rowDeltaValuePages
            + _finalOwners.PersistentPageCount
            + (_finalRowBuilder?.PersistentPageCount ?? 0)
            + (_endpointRowBuilder?.PersistentPageCount ?? 0)
            + (_waypointBuilder?.PersistentPageCount ?? 0)
            + (_ownerUpdated ? 0 : _preparedRecord?.PersistentPageCount ?? 0));

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            if (_stage < 4 && !AdvanceGather(meter))
                return false;
            while (_compileMapIndex < _owners.Count)
            {
                PersistentStringMap<bool> map = _owners.GetValueAt(_compileMapIndex);
                while (_compileOwnerIndex < map.Count)
                {
                    if (!_currentInitialized)
                        InitializeCurrent(map);
                    if (!AdvanceCompilation(meter))
                        return false;
                    if (!IsValid)
                        return true;
                    _incidenceUnchanged = _priorRecord != null
                        && _preparedRecord != null
                        && ReferenceEquals(
                            _priorRecord.Definition,
                            _preparedRecord.Definition);
                    if (_incidenceUnchanged)
                        UpdateOwner();
                    if (!_ownerUpdated)
                    {
                        if (!AdvanceDistinctAddresses(_priorRecord?.Definition, meter))
                            return false;
                        if (!AdvanceOldIncidenceTouches(meter))
                            return false;
                        UpdateOwner();
                    }
                    if (!_incidenceUnchanged && !_ownerJournaled)
                    {
                        if (_preparedRecord != null)
                        {
                            if (!meter.TryConsumeDependencyEntries(1))
                                return false;
                            _finalOwners.Add(
                                _currentOwner,
                                _candidate._explicitConnections);
                        }
                        _ownerJournaled = true;
                    }
                    _compileOwnerIndex++;
                    ResetCurrent();
                }
                _compileMapIndex++;
                _compileOwnerIndex = 0;
            }
            if (!AdvanceFinalAdditions(meter))
                return false;
            return AdvanceIncidenceRows(meter);
        }

        private void InitializeCurrent(PersistentStringMap<bool> map)
        {
            _corridorAddressSet.Reset();
            _currentOwner = new NavigationConnectionOwnerKey(
                _owners.GetKeyAt(_compileMapIndex),
                map.GetKeyAt(_compileOwnerIndex));
            if (_candidate.TrySelectConnection(
                    _currentOwner,
                    out MapState source,
                    out NavigationConnection definition))
            {
                _currentSource = source;
                _currentDefinition = definition;
            }
            _candidate._explicitConnections.TryGet(_currentOwner, out _priorRecord!);
            _currentInitialized = true;
        }

        private bool AdvanceCompilation(MaintenanceWorkMeter meter)
        {
            if (_preparedRecord != null)
                return true;
            if (_currentDefinition == null)
            {
                if (_selectionCharged)
                    return true;
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                _selectionCharged = true;
                return true;
            }

            int semanticCount = _currentDefinition.Witnesses.Count + 2;
            if (semanticCount > _corridorPrisms.Length
                || semanticCount - 1 > _corridorWaypoints.Length / 2)
            {
                IsValid = false;
                return true;
            }
            while (_semanticIndex < semanticCount)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                if (!CaptureSemanticPrism(_semanticIndex))
                {
                    IsValid = false;
                    return true;
                }
                _semanticIndex++;
            }
            if (_isDormant)
            {
                _preparedRecord = Dormant(_currentOwner, _currentDefinition);
                return true;
            }

            int prismCount = semanticCount;
            if (!_corridorStarted)
            {
                _corridorCursor = new GridNavigationCorridorValidationCursor(
                    prismCount,
                    _currentDefinition.EntryAnchor,
                    _currentDefinition.ExitAnchor,
                    _currentDefinition.PortalRadiusClearance,
                    _currentDefinition.PortalHeightClearance);
                _corridorStarted = true;
            }
            while (_corridorCursor.Status == GridNavigationCorridorValidationStatus.InProgress)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                _corridorCursor.Advance(
                    _corridorPrisms.AsSpan(0, prismCount),
                    _corridorWaypoints.AsSpan(0, (prismCount - 1) * 2),
                    maxWork: 1);
            }
            if (_corridorCursor.Status != GridNavigationCorridorValidationStatus.Complete
                || (_currentDefinition.IsLowerBoundCertified
                    && !ValidateLowerBound(
                        _corridorPrisms[0],
                        _corridorPrisms[prismCount - 1],
                        _destinationCell,
                        _currentDefinition,
                        _corridorCursor.GeometricCost)))
            {
                IsValid = false;
                return true;
            }
            int waypointCount = _corridorCursor.PortalWaypointCount;
            while (_waypointCopyIndex < waypointCount)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                _waypointBuilder ??= new NavigationPagedSequence<Vector3d>.Builder(
                    elementBytes: 24);
                _waypointBuilder.Append(_corridorWaypoints[_waypointCopyIndex++]);
            }
            NavigationPagedSequence<Vector3d> waypoints = _waypointBuilder?.Seal()
                ?? NavigationPagedSequence<Vector3d>.Empty;
            _waypointBuilder = null;
            _preparedRecord = new NavigationExplicitConnectionRecord(
                _currentOwner,
                _currentDefinition,
                isActive: true,
                _corridorCursor.GeometricCost,
                waypoints,
                isLowerBoundCertified: _currentDefinition.IsLowerBoundCertified);
            return true;
        }

        private bool CaptureSemanticPrism(int semanticIndex)
        {
            NavigationConnection connection = _currentDefinition!;
            if (semanticIndex == 0)
            {
                if (!TryGetEffectiveCell(
                        _currentSource!,
                        connection.SourceIndex,
                        out NavigationCell sourceCell))
                {
                    if (IsKnownSuppressed(_currentSource!, connection.SourceIndex))
                    {
                        _isDormant = true;
                        _corridorPrisms[0] = default;
                        return true;
                    }
                    return false;
                }
                if (connection.PortalRadiusClearance > sourceCell.RadiusClearance
                    || connection.PortalHeightClearance > sourceCell.HeightClearance
                    || !_currentSource!.Map.GridBinding.TryGetCellPrism(
                        connection.SourceIndex,
                        out _corridorPrisms[0])
                    || !_corridorPrisms[0].Contains(connection.EntryAnchor))
                {
                    return false;
                }
                return true;
            }

            bool isDestination = semanticIndex == connection.Witnesses.Count + 1;
            NavigationCellAddress address = isDestination
                ? connection.Destination
                : connection.Witnesses[semanticIndex - 1];
            if (!_candidate.TryGetSemanticPrism(
                    address,
                    connection,
                    validateAnchor: isDestination,
                    isDestination ? connection.ExitAnchor : default,
                    out _corridorPrisms[semanticIndex],
                    out NavigationCell cell,
                    out bool dormant))
            {
                return false;
            }
            _isDormant |= dormant;
            if (isDestination && !dormant)
                _destinationCell = cell;
            return true;
        }

        private bool AdvanceDistinctAddresses(
            NavigationConnection? definition,
            MaintenanceWorkMeter meter)
        {
            if (definition == null)
                return true;
            int rawCount = definition.Witnesses.Count + 2;
            while (_rawAddressIndex < rawCount)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                NavigationCellAddress address = GetRawAddress(
                    _currentOwner,
                    definition,
                    _rawAddressIndex++);
                if (_corridorAddressSet.Add(address))
                    _corridorAddresses[_distinctAddressCount++] = address;
            }
            return true;
        }

        private bool AdvanceOldIncidenceTouches(MaintenanceWorkMeter meter)
        {
            if (_priorRecord == null)
                return true;
            while (_touchAddressIndex < _distinctAddressCount)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                NavigationCellAddress address = _corridorAddresses[_touchAddressIndex++];
                IncidenceRowDelta row = GetOrCreateRowDelta(address);
                if (IsEndpoint(address, _currentOwner, _priorRecord.Definition))
                    row.MarkEndpointTouched();
            }
            return true;
        }

        private void UpdateOwner()
        {
            if (_ownerUpdated)
                return;
            if (_preparedRecord != null)
            {
                _candidate.RecordExplicitRecordOwnership(
                    _currentOwner,
                    _preparedRecord,
                    _foldSource,
                    ref _displacedSourcePayloadBytes,
                    ref _displacedSourcePayloadPages);
                _candidate._explicitConnections = _candidate._explicitConnections.SetOwner(
                    _preparedRecord,
                    out int copiedNodes);
                _candidate.RecordPersistentCopies(copiedNodes);
            }
            else
            {
                _candidate.RecordExplicitRecordOwnership(
                    _currentOwner,
                    next: null,
                    _foldSource,
                    ref _displacedSourcePayloadBytes,
                    ref _displacedSourcePayloadPages);
                _candidate._explicitConnections = _candidate._explicitConnections.RemoveOwner(
                    _currentOwner,
                    out _,
                    out int copiedNodes);
                _candidate.RecordPersistentCopies(copiedNodes);
            }
            _ownerUpdated = true;
        }

        private bool AdvanceFinalAdditions(MaintenanceWorkMeter meter)
        {
            while (true)
            {
                if (!_additionOwnerInitialized)
                {
                    _additionOwner = _finalOwners.GetSuccessor(_additionOwner);
                    if (_additionOwner == null)
                    {
                        _finalOwners.Clear();
                        return true;
                    }
                    _currentOwner = _additionOwner.Owner;
                    _candidate._explicitConnections.TryGet(
                        _currentOwner,
                        out NavigationExplicitConnectionRecord record);
                    _additionDefinition = record.Definition;
                    _corridorAddressSet.Reset();
                    _rawAddressIndex = 0;
                    _distinctAddressCount = 0;
                    _additionAddressIndex = 0;
                    _additionOwnerInitialized = true;
                }
                if (!AdvanceDistinctAddresses(_additionDefinition, meter))
                    return false;
                while (_additionAddressIndex < _distinctAddressCount)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    IncidenceRowDelta row = GetOrCreateRowDelta(
                        _corridorAddresses[_additionAddressIndex++]);
                    long priorBytes = row.RetainedBytes;
                    int priorPages = row.PersistentPageCount;
                    row.AppendAddition(_currentOwner);
                    NavigationCellAddress address =
                        _corridorAddresses[_additionAddressIndex - 1];
                    if (IsEndpoint(address, _currentOwner, _additionDefinition!))
                        row.MarkEndpointTouched();
                    _rowDeltaValueBytes = checked(
                        _rowDeltaValueBytes - priorBytes + row.RetainedBytes);
                    _rowDeltaValuePages = checked(
                        _rowDeltaValuePages - priorPages + row.PersistentPageCount);
                }
                _additionOwnerInitialized = false;
            }
        }

        private bool AdvanceIncidenceRows(MaintenanceWorkMeter meter)
        {
            while (_rowMapIndex < _rowDeltas.Count)
            {
                PersistentVoxelIndexMap<IncidenceRowDelta> map =
                    _rowDeltas.GetValueAt(_rowMapIndex);
                while (_rowAddressIndex < map.Count)
                {
                    if (!_rowInitialized)
                        InitializeRow(map);
                    if (!AdvancePriorRowFilter(meter))
                        return false;
                    if (!AdvanceRowMerge(meter))
                        return false;
                    if (!_incidentRowCommitted)
                    {
                        if (!meter.TryConsumeDependencyEntries(1))
                            return false;
                        NavigationPagedSequence<NavigationConnectionOwnerKey> next =
                            _finalRowBuilder?.Seal()
                                ?? NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty;
                        _finalRowBuilder = null;
                        NavigationPagedSequence<NavigationConnectionOwnerKey> prior =
                            _candidate._explicitConnections.GetIncidentOwnerRow(_rowAddress);
                        _candidate.RecordExplicitIncidenceOwnership(
                            _rowAddress,
                            next,
                            _foldSource,
                            ref _displacedSourcePayloadBytes,
                            ref _displacedSourcePayloadPages);
                        _candidate._explicitConnections =
                            _candidate._explicitConnections.SetIncidentRow(
                                _rowAddress,
                                prior,
                                next,
                                out int copiedNodes);
                        _candidate.RecordPersistentCopies(copiedNodes);
                        _incidentRowCommitted = true;
                    }
                    if (_rowDelta!.EndpointTouched)
                    {
                        if (!meter.TryConsumeDependencyEntries(1))
                            return false;
                        NavigationPagedSequence<NavigationConnectionOwnerKey> next =
                            _endpointRowBuilder?.Seal()
                                ?? NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty;
                        _endpointRowBuilder = null;
                        NavigationPagedSequence<NavigationConnectionOwnerKey> prior =
                            _candidate._explicitConnections.GetEndpointOwnerRow(_rowAddress);
                        _candidate.RecordExplicitEndpointOwnership(
                            _rowAddress,
                            next,
                            _foldSource,
                            ref _displacedSourcePayloadBytes,
                            ref _displacedSourcePayloadPages);
                        _candidate._explicitConnections =
                            _candidate._explicitConnections.SetEndpointRow(
                                _rowAddress,
                                prior,
                                next,
                                out int copiedNodes);
                        _candidate.RecordPersistentCopies(copiedNodes);
                    }
                    ReleaseCurrentRow();
                    _rowAddressIndex++;
                }
                _rowMapIndex++;
                _rowAddressIndex = 0;
            }
            _rowDeltas =
                PersistentStringMap<PersistentVoxelIndexMap<IncidenceRowDelta>>.Empty;
            _rowDeltaInnerBytes = 0;
            _rowDeltaInnerPages = 0;
            _rowDeltaValueBytes = 0;
            _rowDeltaValuePages = 0;
            return true;
        }

        private void InitializeRow(PersistentVoxelIndexMap<IncidenceRowDelta> map)
        {
            _rowAddress = new NavigationCellAddress(
                _rowDeltas.GetKeyAt(_rowMapIndex),
                map.GetKeyAt(_rowAddressIndex));
            _rowDelta = map.GetValueAt(_rowAddressIndex);
            long priorBytes = _rowDelta.RetainedBytes;
            int priorPages = _rowDelta.PersistentPageCount;
            NavigationPagedSequence<NavigationConnectionOwnerKey> additions =
                _rowDelta.SealAdditions();
            _rowDeltaValueBytes = checked(
                _rowDeltaValueBytes - priorBytes + _rowDelta.RetainedBytes);
            _rowDeltaValuePages = checked(
                _rowDeltaValuePages - priorPages + _rowDelta.PersistentPageCount);
            NavigationPagedSequence<NavigationConnectionOwnerKey> prior =
                _candidate._explicitConnections.GetIncidentOwnerRow(_rowAddress);
            _priorRowOwners = prior.GetEnumerator();
            _priorRowRemaining = prior.Count;
            _additionRowOwners = additions.GetEnumerator();
            _additionRowRemaining = additions.Count;
            _rowInitialized = true;
        }

        private bool AdvancePriorRowFilter(MaintenanceWorkMeter meter)
        {
            while (!_priorRowOwnerReady && _priorRowRemaining != 0)
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _priorRowOwners.MoveNext();
                _priorRowOwner = _priorRowOwners.Current;
                _priorRowRemaining--;
                if (!IsIncidenceChanged(_priorRowOwner))
                    _priorRowOwnerReady = true;
            }
            return true;
        }

        private bool AdvanceRowMerge(MaintenanceWorkMeter meter)
        {
            while (_priorRowOwnerReady || _priorRowRemaining != 0
                || _additionRowOwnerReady || _additionRowRemaining != 0
                || _endpointOwnerPending)
            {
                if (_endpointOwnerPending)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _endpointRowBuilder ??=
                        new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
                    _endpointRowBuilder.Append(_pendingEndpointOwner);
                    _endpointOwnerPending = false;
                    continue;
                }
                if (!_priorRowOwnerReady && !AdvancePriorRowFilter(meter))
                    return false;
                if (!_additionRowOwnerReady && _additionRowRemaining != 0)
                {
                    _additionRowOwners.MoveNext();
                    _additionRowOwner = _additionRowOwners.Current;
                    _additionRowRemaining--;
                    _additionRowOwnerReady = true;
                }
                if (!_priorRowOwnerReady && !_additionRowOwnerReady)
                    return true;
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                NavigationConnectionOwnerKey next;
                if (!_priorRowOwnerReady)
                {
                    next = _additionRowOwner;
                    _additionRowOwnerReady = false;
                }
                else if (!_additionRowOwnerReady)
                {
                    next = _priorRowOwner;
                    _priorRowOwnerReady = false;
                }
                else if (_candidate._explicitConnections.CompareOwners(
                             _priorRowOwner,
                             _additionRowOwner) <= 0)
                {
                    next = _priorRowOwner;
                    _priorRowOwnerReady = false;
                }
                else
                {
                    next = _additionRowOwner;
                    _additionRowOwnerReady = false;
                }
                _finalRowBuilder ??=
                    new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
                _finalRowBuilder.Append(next);
                if (_rowDelta!.EndpointTouched
                    && _candidate._explicitConnections.TryGet(
                        next,
                        out NavigationExplicitConnectionRecord record)
                    && IsEndpoint(_rowAddress, next, record.Definition))
                {
                    _pendingEndpointOwner = next;
                    _endpointOwnerPending = true;
                }
            }
            return true;
        }

        private bool IsIncidenceChanged(NavigationConnectionOwnerKey owner)
        {
            if (!_owners.TryGetValue(owner.MapId, out PersistentStringMap<bool> map)
                || !map.ContainsKey(owner.ConnectionId))
            {
                return false;
            }
            bool hadPrior = _foldSource.TryGet(
                owner,
                out NavigationExplicitConnectionRecord prior);
            bool hasFinal = _candidate._explicitConnections.TryGet(
                owner,
                out NavigationExplicitConnectionRecord final);
            return hadPrior != hasFinal
                || (hadPrior && !ReferenceEquals(prior.Definition, final.Definition));
        }

        private IncidenceRowDelta GetOrCreateRowDelta(NavigationCellAddress address)
        {
            bool hadMap = _rowDeltas.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<IncidenceRowDelta> existing);
            PersistentVoxelIndexMap<IncidenceRowDelta> map = hadMap
                ? existing
                : PersistentVoxelIndexMap<IncidenceRowDelta>.Empty;
            if (map.TryGetValue(address.Index, out IncidenceRowDelta delta))
                return delta;
            delta = new IncidenceRowDelta();
            long priorMapBytes = hadMap ? map.RetainedBytes : 0;
            int priorMapPages = hadMap ? map.PersistentNodeCount : 0;
            map = map.Set(address.Index, delta);
            _rowDeltas = _rowDeltas.Set(address.MapId, map);
            _rowDeltaInnerBytes = checked(
                _rowDeltaInnerBytes - priorMapBytes + map.RetainedBytes);
            _rowDeltaInnerPages = checked(
                _rowDeltaInnerPages - priorMapPages + map.PersistentNodeCount);
            _rowDeltaValueBytes = checked(_rowDeltaValueBytes + delta.RetainedBytes);
            _rowDeltaValuePages = checked(
                _rowDeltaValuePages + delta.PersistentPageCount);
            return delta;
        }

        private void ReleaseCurrentRow()
        {
            long priorBytes = _rowDelta!.RetainedBytes;
            int priorPages = _rowDelta.PersistentPageCount;
            _rowDelta.ReleaseAdditions();
            _rowDeltaValueBytes = checked(
                _rowDeltaValueBytes - priorBytes + _rowDelta.RetainedBytes);
            _rowDeltaValuePages = checked(
                _rowDeltaValuePages - priorPages + _rowDelta.PersistentPageCount);
            _rowDelta = null;
            _priorRowOwners = default;
            _additionRowOwners = default;
            _priorRowRemaining = 0;
            _additionRowRemaining = 0;
            _priorRowOwnerReady = false;
            _additionRowOwnerReady = false;
            _endpointRowBuilder = null;
            _pendingEndpointOwner = default;
            _endpointOwnerPending = false;
            _incidentRowCommitted = false;
            _rowInitialized = false;
        }

        private static bool IsEndpoint(
            NavigationCellAddress address,
            NavigationConnectionOwnerKey owner,
            NavigationConnection definition) =>
                address.Equals(new NavigationCellAddress(owner.MapId, definition.SourceIndex))
                || address.Equals(definition.Destination);

        private static NavigationCellAddress GetRawAddress(
            NavigationConnectionOwnerKey owner,
            NavigationConnection connection,
            int rawIndex)
        {
            if (rawIndex == 0)
                return new NavigationCellAddress(owner.MapId, connection.SourceIndex);
            if (rawIndex == connection.Witnesses.Count + 1)
                return connection.Destination;
            return connection.Witnesses[rawIndex - 1];
        }

        private void ResetCurrent()
        {
            _currentOwner = default;
            _currentSource = null;
            _currentDefinition = null;
            _priorRecord = null;
            _preparedRecord = null;
            _destinationCell = default;
            _semanticIndex = 0;
            _rawAddressIndex = 0;
            _distinctAddressCount = 0;
            _touchAddressIndex = 0;
            _currentInitialized = false;
            _selectionCharged = false;
            _isDormant = false;
            _incidenceUnchanged = false;
            _ownerUpdated = false;
            _ownerJournaled = false;
            _corridorStarted = false;
            _corridorCursor = default;
            _waypointBuilder = null;
            _waypointCopyIndex = 0;
        }

        private bool AdvanceGather(MaintenanceWorkMeter meter)
        {
            if (_transaction != null)
                return AdvanceOverlayGather(meter);
            while (_stage < 4)
            {
                _candidate._maps.TryGetValue(_mapId!, out MapState state);
                int count = _stage switch
                {
                    0 => _candidate._explicitConnections.GetSourceOwnerCount(_mapId!),
                    1 => _candidate._explicitConnections.GetIncidentAddressCount(_mapId!),
                    2 => state == null ? 0 : state.Map.ConnectionSpan.Length,
                    _ => state == null ? 0 : state.Overlay.ConnectionCount
                };
                while (_itemIndex < count)
                {
                    if (_stage == 1)
                    {
                        NavigationCellAddress address = _candidate._explicitConnections
                            .GetIncidentAddressAt(_mapId!, _itemIndex);
                        if (!AdvanceIncidentOwners(address, meter))
                            return false;
                        _itemIndex++;
                        continue;
                    }
                    NavigationConnectionOwnerKey owner = _stage switch
                    {
                        0 => _candidate._explicitConnections
                            .GetSourceOwnerAt(_mapId!, _itemIndex).Owner,
                        2 => new NavigationConnectionOwnerKey(
                            _mapId!,
                            state!.Map.ConnectionSpan[_itemIndex].Id),
                        _ => new NavigationConnectionOwnerKey(
                            _mapId!,
                            state!.Overlay.GetConnectionAt(_itemIndex).Id)
                    };
                    if (!AddOwner(owner, meter))
                        return false;
                    _itemIndex++;
                }
                _stage++;
                _itemIndex = 0;
            }
            return true;
        }

        private bool AdvanceOverlayGather(MaintenanceWorkMeter meter)
        {
            ReadOnlySpan<NavigationMapOverlayDelta> maps = _transaction!.MapSpan;
            while (_mapIndex < maps.Length)
            {
                NavigationMapOverlayDelta map = maps[_mapIndex];
                if (_stage == 0)
                {
                    ReadOnlySpan<NavigationCellOverlayOperation> cells = map.CellSpan;
                    while (_itemIndex < cells.Length)
                    {
                        if (!AdvanceIncidentOwners(
                                new NavigationCellAddress(map.MapId, cells[_itemIndex].Index),
                                meter))
                        {
                            return false;
                        }
                        _itemIndex++;
                    }
                    _stage = 1;
                    _itemIndex = 0;
                }
                ReadOnlySpan<NavigationConnectionOverlayOperation> connections = map.ConnectionSpan;
                while (_itemIndex < connections.Length)
                {
                    if (!AddOwner(
                            new NavigationConnectionOwnerKey(map.MapId, connections[_itemIndex].Id),
                            meter))
                        return false;
                    _itemIndex++;
                }
                _mapIndex++;
                _stage = 0;
                _itemIndex = 0;
            }
            _stage = 4;
            return true;
        }

        private bool AddOwner(NavigationConnectionOwnerKey owner, MaintenanceWorkMeter meter)
        {
            if (!meter.TryConsumeDependencyEntries(1))
                return false;
            PersistentStringMap<bool> map = _owners.TryGetValue(
                owner.MapId,
                out PersistentStringMap<bool> existing)
                    ? existing
                    : PersistentStringMap<bool>.Empty;
            if (!map.ContainsKey(owner.ConnectionId))
            {
                long priorBytes = map.RetainedBytes;
                int priorPages = map.PersistentNodeCount;
                map = map.Set(owner.ConnectionId, true);
                _owners = _owners.Set(owner.MapId, map);
                _innerOwnerBytes = checked(
                    _innerOwnerBytes - priorBytes + map.RetainedBytes);
                _innerOwnerPages = checked(
                    _innerOwnerPages - priorPages + map.PersistentNodeCount);
            }
            if (!_candidate._explicitChangedSources.ContainsKey(owner.MapId))
            {
                _candidate._explicitChangedSources =
                    _candidate._explicitChangedSources.Set(owner.MapId, true);
            }
            return true;
        }

        private bool AdvanceIncidentOwners(
            NavigationCellAddress address,
            MaintenanceWorkMeter meter)
        {
            if (!_incidentOwnerEnumerationStarted)
            {
                _incidentOwnerEnumerator =
                    _candidate._explicitConnections.GetIncidentOwnerEnumerator(address);
                _incidentOwnerEnumerationStarted = true;
            }
            while (true)
            {
                if (!_hasPendingIncidentOwner)
                {
                    if (!_incidentOwnerEnumerator.MoveNext())
                    {
                        _incidentOwnerEnumerationStarted = false;
                        return true;
                    }
                    _pendingIncidentOwner = _incidentOwnerEnumerator.Current;
                    _hasPendingIncidentOwner = true;
                }
                if (!AddOwner(_pendingIncidentOwner, meter))
                    return false;
                _hasPendingIncidentOwner = false;
            }
        }

        private sealed class IncidenceRowDelta
        {
            private NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder?
                _additionBuilder;
            private NavigationPagedSequence<NavigationConnectionOwnerKey>? _additions;

            internal long RetainedBytes => checked(
                40L
                + (_additionBuilder?.RetainedBytes ?? _additions?.RetainedBytes ?? 0));

            internal int PersistentPageCount => checked(
                1
                + (_additionBuilder?.PersistentPageCount
                    ?? _additions?.PersistentPageCount
                    ?? 0));

            internal void AppendAddition(NavigationConnectionOwnerKey owner)
            {
                _additionBuilder ??=
                    new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
                _additionBuilder.Append(owner);
            }

            internal bool EndpointTouched { get; private set; }

            internal void MarkEndpointTouched() => EndpointTouched = true;

            internal NavigationPagedSequence<NavigationConnectionOwnerKey> SealAdditions()
            {
                if (_additions != null)
                    return _additions;
                _additions = _additionBuilder?.Seal()
                    ?? NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty;
                _additionBuilder = null;
                return _additions;
            }

            internal void ReleaseAdditions()
            {
                _additionBuilder = null;
                _additions = null;
            }
        }

        private struct IncidenceOwnerTree
        {
            private Node? _root;
            private int _count;

            internal long RetainedBytes => checked((long)_count * 64L);

            internal int PersistentPageCount => _count;

            internal void Add(
                NavigationConnectionOwnerKey owner,
                NavigationExplicitConnectionIndex index)
            {
                if (_root == null)
                {
                    _root = new Node(owner, null);
                    _count = 1;
                    return;
                }
                Node current = _root;
                while (true)
                {
                    int comparison = index.CompareOwners(owner, current.Owner);
                    if (comparison == 0)
                        return;
                    if (comparison < 0)
                    {
                        if (current.Left != null)
                        {
                            current = current.Left;
                            continue;
                        }
                        current.Left = new Node(owner, current);
                        _count++;
                        Rebalance(current);
                        return;
                    }
                    if (current.Right != null)
                    {
                        current = current.Right;
                        continue;
                    }
                    current.Right = new Node(owner, current);
                    _count++;
                    Rebalance(current);
                    return;
                }
            }

            internal Node? GetSuccessor(Node? current)
            {
                if (current == null)
                    return Minimum(_root);
                if (current.Right != null)
                    return Minimum(current.Right);
                Node child = current;
                Node? parent = current.Parent;
                while (parent != null && ReferenceEquals(parent.Right, child))
                {
                    child = parent;
                    parent = parent.Parent;
                }
                return parent;
            }

            internal void Clear()
            {
                _root = null;
                _count = 0;
            }

            private static Node? Minimum(Node? node)
            {
                while (node?.Left != null)
                    node = node.Left;
                return node;
            }

            private void Rebalance(Node? node)
            {
                while (node != null)
                {
                    UpdateHeight(node);
                    int balance = Height(node.Left) - Height(node.Right);
                    if (balance > 1)
                    {
                        if (Height(node.Left!.Left) < Height(node.Left.Right))
                            RotateLeft(node.Left);
                        node = RotateRight(node);
                    }
                    else if (balance < -1)
                    {
                        if (Height(node.Right!.Right) < Height(node.Right.Left))
                            RotateRight(node.Right);
                        node = RotateLeft(node);
                    }
                    node = node.Parent;
                }
            }

            private Node RotateLeft(Node node)
            {
                Node pivot = node.Right!;
                ReplaceParentLink(node, pivot);
                node.Right = pivot.Left;
                if (node.Right != null)
                    node.Right.Parent = node;
                pivot.Left = node;
                node.Parent = pivot;
                UpdateHeight(node);
                UpdateHeight(pivot);
                return pivot;
            }

            private Node RotateRight(Node node)
            {
                Node pivot = node.Left!;
                ReplaceParentLink(node, pivot);
                node.Left = pivot.Right;
                if (node.Left != null)
                    node.Left.Parent = node;
                pivot.Right = node;
                node.Parent = pivot;
                UpdateHeight(node);
                UpdateHeight(pivot);
                return pivot;
            }

            private void ReplaceParentLink(Node node, Node replacement)
            {
                Node? parent = node.Parent;
                replacement.Parent = parent;
                if (parent == null)
                    _root = replacement;
                else if (ReferenceEquals(parent.Left, node))
                    parent.Left = replacement;
                else
                    parent.Right = replacement;
            }

            private static int Height(Node? node) => node?.Height ?? 0;

            private static void UpdateHeight(Node node) =>
                node.Height = 1 + Math.Max(Height(node.Left), Height(node.Right));

            internal sealed class Node
            {
                internal Node(NavigationConnectionOwnerKey owner, Node? parent)
                {
                    Owner = owner;
                    Parent = parent;
                }

                internal NavigationConnectionOwnerKey Owner { get; }

                internal Node? Parent { get; set; }

                internal Node? Left { get; set; }

                internal Node? Right { get; set; }

                internal int Height { get; set; } = 1;
            }
        }
    }
}
