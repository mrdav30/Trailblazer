//=======================================================================
// NavigationOperationCandidate.ExplicitConnections.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;
using SwiftCollections;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationOperationCandidate
{
    internal NavigationExplicitConnectionIndex ExplicitConnections => _explicitConnections;

    internal int ExplicitChangedSourceCount => _explicitChangedSources.Count;

    internal string GetExplicitChangedSourceAt(int ordinal) =>
        _explicitChangedSources.GetKeyAt(ordinal);

    internal ExplicitConnectionRefreshWork BeginExplicitConnectionRefresh(
        string mapId,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints) => new(
            this,
            mapId,
            transaction: null,
            corridorPrisms,
            corridorWaypoints);

    internal ExplicitConnectionRefreshWork BeginExplicitConnectionRefresh(
        NavigationOverlayTransaction transaction,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints) => new(
            this,
            mapId: null,
            transaction,
            corridorPrisms,
            corridorWaypoints);

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
            Array.Empty<Vector3d>());

    private static bool IsKnownSuppressed(MapState state, GridForge.Spatial.VoxelIndex index) =>
        state.Overlay.TryGetCell(index, out NavigationCellOverlayOperation operation)
        && operation.Kind == NavigationCellOverlayOperationKind.Suppress;

    internal sealed class ExplicitConnectionRefreshWork
    {
        private readonly NavigationOperationCandidate _candidate;
        private readonly string? _mapId;
        private readonly NavigationOverlayTransaction? _transaction;
        private readonly GridCellPrism[] _corridorPrisms;
        private readonly Vector3d[] _corridorWaypoints;
        private PersistentStringMap<PersistentStringMap<bool>> _owners =
            PersistentStringMap<PersistentStringMap<bool>>.Empty;
        private int _stage;
        private int _mapIndex;
        private int _itemIndex;
        private int _ownerIndex;
        private int _compileMapIndex;
        private int _compileOwnerIndex;
        private NavigationConnectionOwnerKey _currentOwner;
        private MapState? _currentSource;
        private NavigationConnection? _currentDefinition;
        private NavigationExplicitConnectionRecord? _priorRecord;
        private NavigationExplicitConnectionRecord? _preparedRecord;
        private NavigationCell _destinationCell;
        private int _semanticIndex;
        private int _oldIncidenceIndex;
        private int _newIncidenceIndex;
        private bool _currentInitialized;
        private bool _selectionCharged;
        private bool _isDormant;
        private bool _ownerUpdated;
        private long _innerOwnerBytes;
        private int _innerOwnerPages;

        internal ExplicitConnectionRefreshWork(
            NavigationOperationCandidate candidate,
            string? mapId,
            NavigationOverlayTransaction? transaction,
            GridCellPrism[] corridorPrisms,
            Vector3d[] corridorWaypoints)
        {
            _candidate = candidate;
            _mapId = mapId;
            _transaction = transaction;
            _corridorPrisms = corridorPrisms;
            _corridorWaypoints = corridorWaypoints;
        }

        internal bool IsValid { get; private set; } = true;

        internal long RetainedBytes => checked(
            128L
            + _owners.RetainedBytes
            + _innerOwnerBytes
            + (_ownerUpdated ? 0 : _preparedRecord?.RetainedBytes ?? 0));

        internal int PersistentPageCount => checked(
            1 + _owners.PersistentNodeCount + _innerOwnerPages);

        internal bool Advance(MaintenanceWorkMeter meter)
        {
            if (_stage == 0 && !AdvanceGather(meter))
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
                    if (!AdvanceOldIncidence(meter))
                        return false;
                    UpdateOwner();
                    if (!AdvanceNewIncidence(meter))
                        return false;
                    _compileOwnerIndex++;
                    ResetCurrent();
                }
                _compileMapIndex++;
                _compileOwnerIndex = 0;
            }
            return true;
        }

        private void InitializeCurrent(PersistentStringMap<bool> map)
        {
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
            // Admission caps this one GridForge certificate primitive at MaxCorridorCells;
            // the semantic cells it walks were each captured under the explicit-edge meter.
            if (!GridCellGeometry.TryValidateNavigationCorridor(
                    _corridorPrisms.AsSpan(0, prismCount),
                    _currentDefinition.EntryAnchor,
                    _currentDefinition.ExitAnchor,
                    _currentDefinition.PortalRadiusClearance,
                    _currentDefinition.PortalHeightClearance,
                    _corridorWaypoints.AsSpan(0, (prismCount - 1) * 2),
                    out int waypointCount,
                    out Fixed64 corridorCost)
                || (_currentDefinition.IsLowerBoundCertified
                    && !ValidateLowerBound(
                        _corridorPrisms[0],
                        _corridorPrisms[prismCount - 1],
                        _destinationCell,
                        _currentDefinition,
                        corridorCost)))
            {
                IsValid = false;
                return true;
            }
            var waypoints = new Vector3d[waypointCount];
            _corridorWaypoints.AsSpan(0, waypointCount).CopyTo(waypoints);
            _preparedRecord = new NavigationExplicitConnectionRecord(
                _currentOwner,
                _currentDefinition,
                isActive: true,
                corridorCost,
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

        private bool AdvanceOldIncidence(MaintenanceWorkMeter meter)
        {
            if (_priorRecord == null)
                return true;
            int nextIndex = _oldIncidenceIndex;
            while (TryGetNextDistinctAddress(
                _priorRecord.Owner,
                _priorRecord.Definition,
                ref nextIndex,
                out NavigationCellAddress address))
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _oldIncidenceIndex = nextIndex;
                _candidate._explicitConnections = _candidate._explicitConnections.UpdateIncidence(
                    address,
                    _currentOwner,
                    add: false,
                    out int copiedNodes);
                _candidate.RecordPersistentCopies(copiedNodes);
            }
            return true;
        }

        private void UpdateOwner()
        {
            if (_ownerUpdated)
                return;
            if (_preparedRecord != null)
            {
                _candidate._explicitConnections = _candidate._explicitConnections.SetOwner(
                    _preparedRecord,
                    out int copiedNodes);
                _candidate.RecordPersistentCopies(copiedNodes);
            }
            else
            {
                _candidate._explicitConnections = _candidate._explicitConnections.RemoveOwner(
                    _currentOwner,
                    out _,
                    out int copiedNodes);
                _candidate.RecordPersistentCopies(copiedNodes);
            }
            _ownerUpdated = true;
        }

        private bool AdvanceNewIncidence(MaintenanceWorkMeter meter)
        {
            if (_preparedRecord == null)
                return true;
            int nextIndex = _newIncidenceIndex;
            while (TryGetNextDistinctAddress(
                _preparedRecord.Owner,
                _preparedRecord.Definition,
                ref nextIndex,
                out NavigationCellAddress address))
            {
                if (!meter.TryConsumeDependencyEntries(1))
                    return false;
                _newIncidenceIndex = nextIndex;
                _candidate._explicitConnections = _candidate._explicitConnections.UpdateIncidence(
                    address,
                    _currentOwner,
                    add: true,
                    out int copiedNodes);
                _candidate.RecordPersistentCopies(copiedNodes);
            }
            return true;
        }

        private static bool TryGetNextDistinctAddress(
            NavigationConnectionOwnerKey owner,
            NavigationConnection connection,
            ref int rawIndex,
            out NavigationCellAddress address)
        {
            int rawCount = connection.Witnesses.Count + 2;
            while (rawIndex < rawCount)
            {
                int current = rawIndex++;
                address = GetRawAddress(owner, connection, current);
                bool duplicate = false;
                for (int prior = 0; prior < current; prior++)
                    duplicate |= address.Equals(GetRawAddress(owner, connection, prior));
                if (!duplicate)
                    return true;
            }
            address = default;
            return false;
        }

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
            _oldIncidenceIndex = 0;
            _newIncidenceIndex = 0;
            _currentInitialized = false;
            _selectionCharged = false;
            _isDormant = false;
            _ownerUpdated = false;
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
                        ReadOnlySpan<NavigationConnectionOwnerKey> incident =
                            _candidate._explicitConnections.GetIncidentOwners(address);
                        while (_ownerIndex < incident.Length)
                        {
                            if (!AddOwner(incident[_ownerIndex], meter))
                                return false;
                            _ownerIndex++;
                        }
                        _ownerIndex = 0;
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
                        ReadOnlySpan<NavigationConnectionOwnerKey> incident =
                            _candidate._explicitConnections.GetIncidentOwners(
                                new NavigationCellAddress(map.MapId, cells[_itemIndex].Index));
                        while (_ownerIndex < incident.Length)
                        {
                            if (!AddOwner(incident[_ownerIndex], meter))
                                return false;
                            _ownerIndex++;
                        }
                        _ownerIndex = 0;
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
    }
}
