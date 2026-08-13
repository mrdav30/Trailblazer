//=======================================================================
// NavigationOperationCandidate.MapFold.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationOperationCandidate
{
    internal MapFoldCursor BeginMapFold(
        PreparedNavigationMap prepared,
        OverlayReplacementPolicy replacementPolicy,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints) => new(
            this,
            prepared,
            replacementPolicy,
            limits,
            corridorPrisms,
            corridorWaypoints);

    internal MapFoldCursor BeginMapRemovalFold(string mapId) => new(this, mapId);

    internal sealed class MapFoldCursor
    {
        private readonly NavigationOperationCandidate _working;
        private readonly PreparedNavigationMap? _prepared;
        private readonly OverlayReplacementPolicy _replacementPolicy;
        private readonly NavigationOperationLimits _limits;
        private readonly GridCellPrism[]? _corridorPrisms;
        private readonly Vector3d[]? _corridorWaypoints;
        private readonly string _mapId;
        private readonly MapState? _current;
        private readonly long _sourceRetainedBytes;
        private readonly int _sourcePersistentPageCount;
        private MapState? _next;
        private readonly string[] _changedMapIds = new string[1];
        private readonly MapState[] _changedStates = new MapState[1];
        private NavigationOperationRejection _rejection;
        private int _cellIndex;
        private int _overlayCellIndex;
        private int _dynamicIndex;
        private int _authoredStage;
        private int _authoredIndex;
        private int _authoredWorkIndex;
        private int _validationSourceIndex = -1;
        private int _validationStage;
        private int _validationIndex;
        private int _validationWorkIndex;
        private int _dependencyStage;
        private int _dependencyIndex;
        private int _dependencyAddressIndex;
        private bool _removeDependencies = true;
        private Stage _stage;

        internal MapFoldCursor(
            NavigationOperationCandidate source,
            PreparedNavigationMap prepared,
            OverlayReplacementPolicy replacementPolicy,
            NavigationOperationLimits limits,
            GridCellPrism[] corridorPrisms,
            Vector3d[] corridorWaypoints)
        {
            _working = source.Clone();
            _sourceRetainedBytes = source.RetainedBytes;
            _sourcePersistentPageCount = source.PersistentPageCount;
            _prepared = prepared;
            _replacementPolicy = replacementPolicy;
            _limits = limits;
            _corridorPrisms = corridorPrisms;
            _corridorWaypoints = corridorWaypoints;
            _mapId = prepared.Map.MapId;
            _working._maps.TryGetValue(_mapId, out _current);
            _changedMapIds[0] = _mapId;
            PrepareCommit();
        }

        internal MapFoldCursor(NavigationOperationCandidate source, string mapId)
        {
            _working = source.Clone();
            _sourceRetainedBytes = source.RetainedBytes;
            _sourcePersistentPageCount = source.PersistentPageCount;
            _prepared = null;
            _replacementPolicy = OverlayReplacementPolicy.Clear;
            _limits = default;
            _mapId = mapId;
            _working._maps.TryGetValue(mapId, out _current);
            _changedMapIds[0] = mapId;
            _stage = _current == null ? Stage.Complete : Stage.Dependencies;
            _rejection = _current == null
                ? NavigationOperationRejection.MissingMap
                : NavigationOperationRejection.None;
        }

        internal NavigationOperationCandidate Candidate => _working;

        internal long SourceRetainedBytes => _sourceRetainedBytes;

        internal int SourcePersistentPageCount => _sourcePersistentPageCount;

        internal long RetainedBytes => checked(
            128L
            + ((long)(_changedMapIds.Length + _changedStates.Length) * System.IntPtr.Size)
            + System.Math.Max(0L, _working.RetainedBytes - _sourceRetainedBytes)
            + _working.WorkCopiedPersistentBytes
            + GetPendingStateGrowthBytes());

        internal int PersistentPageCount => checked(
            1
            + System.Math.Max(0, _working.PersistentPageCount - _sourcePersistentPageCount)
            + _working.WorkCopiedPersistentPages
            + GetPendingStateGrowthPages());

        private long GetPendingStateGrowthBytes()
        {
            if (_next == null || _stage == Stage.Complete)
                return 0;
            long currentBytes = _current == null
                ? 0
                : checked(
                    _current.PreparedMapRetainedBytes
                    + _current.Overlay.RetainedBytes
                    + _current.DynamicAddresses.RetainedBytes);
            long nextBytes = checked(
                _next.PreparedMapRetainedBytes
                + _next.Overlay.RetainedBytes
                + _next.DynamicAddresses.RetainedBytes);
            return System.Math.Max(0L, nextBytes - currentBytes);
        }

        private int GetPendingStateGrowthPages()
        {
            if (_next == null || _stage == Stage.Complete)
                return 0;
            int currentPages = _current == null
                ? 0
                : checked(_current.Overlay.PersistentNodeCount + _current.DynamicAddresses.PersistentNodeCount);
            int nextPages = checked(_next.Overlay.PersistentNodeCount + _next.DynamicAddresses.PersistentNodeCount);
            return System.Math.Max(0, nextPages - currentPages);
        }

        internal bool Advance(
            MaintenanceWorkMeter meter,
            out NavigationOperationRejection rejection)
        {
            if (_rejection != NavigationOperationRejection.None)
            {
                rejection = _rejection;
                return true;
            }
            if (_stage == Stage.Cells && !AdvanceCells(meter))
            {
                rejection = NavigationOperationRejection.None;
                return false;
            }
            if (_stage == Stage.Dynamic && !AdvanceDynamic(meter))
            {
                rejection = NavigationOperationRejection.None;
                return false;
            }
            if (_stage == Stage.AuthoredValidation && !AdvanceAuthoredValidation(meter))
            {
                rejection = NavigationOperationRejection.None;
                return false;
            }
            if (_stage == Stage.Validation && !AdvanceValidation(meter))
            {
                rejection = NavigationOperationRejection.None;
                return false;
            }
            if (_rejection != NavigationOperationRejection.None)
            {
                rejection = _rejection;
                return true;
            }
            if (_stage == Stage.Dependencies && !AdvanceDependencies(meter))
            {
                rejection = NavigationOperationRejection.None;
                return false;
            }
            if (_stage == Stage.Commit)
                Commit();
            rejection = _rejection;
            return true;
        }

        private void PrepareCommit()
        {
            NavigationMap map = _prepared!.Map;
            bool replacing = _current != null;
            if (_working._bakeVersionHighWater.TryGetValue(_mapId, out long highWater)
                && _prepared.BakeVersion <= highWater)
            {
                _rejection = NavigationOperationRejection.Stale;
                _stage = Stage.Complete;
                return;
            }
            if ((!_working._bakeVersionHighWater.ContainsKey(_mapId)
                    && _working._bakeVersionHighWater.Count >= _limits.MaxRetainedMapIdentities)
                || (!replacing && _working._maps.Count >= _limits.MaxMaps))
            {
                _rejection = NavigationOperationRejection.CapacityExceeded;
                _stage = Stage.Complete;
                return;
            }
            if (_working._gridBindings.TryGetValue(map.GridBinding.Key, out string bound)
                && !string.Equals(bound, _mapId, System.StringComparison.Ordinal))
            {
                _rejection = NavigationOperationRejection.ValidationFailed;
                _stage = Stage.Complete;
                return;
            }
            if (_prepared.CheckpointStamp.HasValue)
            {
                NavigationMapCheckpointStamp stamp = _prepared.CheckpointStamp.Value;
                if (!replacing
                    || _current!.BakeVersion != stamp.BakeVersion
                    || _current.Overlay.HighWaterSequence != stamp.OverlayHighWaterSequence)
                {
                    _rejection = NavigationOperationRejection.Stale;
                    _stage = Stage.Complete;
                    return;
                }
            }
            NavigationMapOverlayState overlay = !replacing
                || _replacementPolicy == OverlayReplacementPolicy.Clear
                    ? NavigationMapOverlayState.Empty
                    : _current!.Overlay;
            PersistentVoxelIndexMap<byte> dynamicAddresses = replacing
                && _replacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                    ? _current!.DynamicAddresses
                    : PersistentVoxelIndexMap<byte>.Empty;
            _next = new MapState(
                map,
                _prepared.BakeVersion,
                _prepared.RetainedBytes,
                overlay,
                replacing && _replacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                    ? _current!.DynamicSlotGeneration
                    : replacing ? checked(_current!.DynamicSlotGeneration + 1) : 0,
                dynamicAddresses,
                _prepared.BakedCellLookup);
            _changedStates[0] = _next;
            _stage = Stage.Cells;
        }

        private bool AdvanceCells(MaintenanceWorkMeter meter)
        {
            while (_cellIndex < _next!.Map.CellSpan.Length)
            {
                if (!meter.TryConsumeOverlaySlots(1))
                    return false;
                if (_next.Map.CellSpan[_cellIndex++].Cell.Area.Value >= _working._navigationAreaCount)
                {
                    _rejection = NavigationOperationRejection.ValidationFailed;
                    _stage = Stage.Complete;
                    return true;
                }
            }
            while (_overlayCellIndex < _next!.Overlay.CellCount)
            {
                if (!meter.TryConsumeOverlaySlots(1))
                    return false;
                if (!_next.Map.GridBinding.IsValidIndex(
                        _next.Overlay.GetCellAt(_overlayCellIndex++).Index))
                {
                    _rejection = NavigationOperationRejection.ValidationFailed;
                    _stage = Stage.Complete;
                    return true;
                }
            }
            _stage = Stage.Dynamic;
            return true;
        }

        private bool AdvanceDynamic(MaintenanceWorkMeter meter)
        {
            while (_dynamicIndex < _next!.DynamicAddresses.Count)
            {
                if (!meter.TryConsumeOverlaySlots(1))
                    return false;
                if (_next.Map.ContainsCell(_next.DynamicAddresses.GetKeyAt(_dynamicIndex++)))
                {
                    _rejection = NavigationOperationRejection.ValidationFailed;
                    _stage = Stage.Complete;
                    return true;
                }
            }
            _stage = Stage.AuthoredValidation;
            return true;
        }

        private bool AdvanceAuthoredValidation(MaintenanceWorkMeter meter)
        {
            while (_authoredStage < 2)
            {
                int count = _authoredStage == 0
                    ? _next!.Map.ConnectionSpan.Length
                    : _next!.Map.TransitionSpan.Length;
                while (_authoredIndex < count)
                {
                    int workUnits = _authoredStage == 0
                        ? _next!.Map.ConnectionSpan[_authoredIndex].Witnesses.Count + 2
                        : 1;
                    while (_authoredWorkIndex < workUnits)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        _authoredWorkIndex++;
                    }
                    int edgeIndex = _authoredIndex++;
                    _authoredWorkIndex = 0;
                    if (_authoredStage == 0
                        && _next.Map.ConnectionSpan[edgeIndex].Witnesses.Count
                            > _limits.MaxCorridorCells - 2)
                    {
                        _rejection = NavigationOperationRejection.CapacityExceeded;
                        _stage = Stage.Complete;
                        return true;
                    }
                    if (!_working.ValidateAuthoredStateEdgeForWork(
                            _next,
                            connection: _authoredStage == 0,
                            edgeIndex,
                            _changedMapIds,
                            _changedStates,
                            _corridorPrisms!,
                            _corridorWaypoints!,
                            allowDormantEndpoints: false))
                    {
                        _rejection = NavigationOperationRejection.ValidationFailed;
                        _stage = Stage.Complete;
                        return true;
                    }
                }
                _authoredStage++;
                _authoredIndex = 0;
            }
            _stage = Stage.Validation;
            return true;
        }

        private bool AdvanceValidation(MaintenanceWorkMeter meter)
        {
            while (true)
            {
                MapState? state = GetValidationState();
                if (state == null)
                {
                    _stage = Stage.Dependencies;
                    return true;
                }
                if (!AdvanceStateValidation(state, meter))
                    return false;
                if (_rejection != NavigationOperationRejection.None)
                    return true;
                _validationSourceIndex++;
                _validationStage = 0;
                _validationIndex = 0;
            }
        }

        private MapState? GetValidationState()
        {
            if (_validationSourceIndex < 0)
                return _next;
            int sourceCount = _working.GetIncomingSourceCount(_mapId);
            while (_validationSourceIndex < sourceCount)
            {
                string sourceId = _working.GetIncomingSource(_mapId, _validationSourceIndex);
                if (string.Equals(sourceId, _mapId, System.StringComparison.Ordinal))
                {
                    _validationSourceIndex++;
                    continue;
                }
                if (_working._maps.TryGetValue(sourceId, out MapState source))
                    return source;
                _validationSourceIndex++;
            }
            return null;
        }

        private bool AdvanceStateValidation(MapState state, MaintenanceWorkMeter meter)
        {
            while (_validationStage < 4)
            {
                int count = _validationStage switch
                {
                    0 => state.Map.ConnectionSpan.Length,
                    1 => state.Overlay.ConnectionCount,
                    2 => state.Map.TransitionSpan.Length,
                    _ => state.Overlay.TransitionCount
                };
                while (_validationIndex < count)
                {
                    int workUnits = GetWitnessCount(state, _validationStage, _validationIndex) + 1;
                    while (_validationWorkIndex < workUnits)
                    {
                        if (!meter.TryConsumeExplicitEdges(1))
                            return false;
                        _validationWorkIndex++;
                    }
                    int edgeIndex = _validationIndex++;
                    _validationWorkIndex = 0;
                    if (GetWitnessCount(state, _validationStage, edgeIndex)
                        > _limits.MaxCorridorCells - 2)
                    {
                        _rejection = NavigationOperationRejection.CapacityExceeded;
                        _stage = Stage.Complete;
                        return true;
                    }
                    if (!_working.ValidateStateEdgeForWork(
                            state,
                            _validationStage,
                            edgeIndex,
                            _changedMapIds,
                            _changedStates,
                            _corridorPrisms!,
                            _corridorWaypoints!,
                            allowDormantEndpoints: _current != null
                                && _replacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate))
                    {
                        _rejection = NavigationOperationRejection.ValidationFailed;
                        _stage = Stage.Complete;
                        return true;
                    }
                }
                _validationStage++;
                _validationIndex = 0;
            }
            return true;
        }

        private static int GetWitnessCount(MapState state, int stage, int index)
        {
            if (stage == 0)
                return state.Map.ConnectionSpan[index].Witnesses.Count;
            if (stage != 1)
                return 0;
            NavigationConnectionOverlayOperation operation = state.Overlay.GetConnectionAt(index);
            return operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
                ? operation.Connection!.Witnesses.Count
                : 0;
        }

        private bool AdvanceDependencies(MaintenanceWorkMeter meter)
        {
            while (true)
            {
                MapState? state = _removeDependencies ? _current : _next;
                if (state != null && !AdvanceStateDependencies(state, _removeDependencies, meter))
                    return false;
                if (_removeDependencies && _next != null)
                {
                    _removeDependencies = false;
                    ResetDependencyCursor();
                    continue;
                }
                _stage = Stage.Commit;
                return true;
            }
        }

        private bool AdvanceStateDependencies(
            MapState state,
            bool remove,
            MaintenanceWorkMeter meter)
        {
            while (_dependencyStage < 4)
            {
                int count = _dependencyStage switch
                {
                    0 => state.Map.ConnectionSpan.Length,
                    1 => state.Map.TransitionSpan.Length,
                    2 => state.Overlay.ConnectionCount,
                    _ => state.Overlay.TransitionCount
                };
                while (_dependencyIndex < count)
                {
                    if (_dependencyStage is 0 or 2)
                    {
                        NavigationConnection? connection = _dependencyStage == 0
                            ? state.Map.ConnectionSpan[_dependencyIndex]
                            : state.Overlay.GetConnectionAt(_dependencyIndex).Kind
                                == NavigationConnectionOverlayOperationKind.Upsert
                                    ? state.Overlay.GetConnectionAt(_dependencyIndex).Connection
                                    : null;
                        if (connection == null)
                        {
                            _dependencyIndex++;
                            continue;
                        }
                        int addresses = connection.Witnesses.Count + 1;
                        while (_dependencyAddressIndex < addresses)
                        {
                            if (!meter.TryConsumeDependencyEntries(1))
                                return false;
                            string destination = _dependencyAddressIndex++ == 0
                                ? connection.Destination.MapId
                                : connection.Witnesses[_dependencyAddressIndex - 2].MapId;
                            _working.UpdateIncomingSource(destination, state.Map.MapId, remove);
                        }
                    }
                    else
                    {
                        TraversalTransitionOverlayOperation overlay = _dependencyStage == 3
                            ? state.Overlay.GetTransitionAt(_dependencyIndex)
                            : default;
                        if (_dependencyStage == 3
                            && overlay.Kind != TraversalTransitionOverlayOperationKind.Upsert)
                        {
                            _dependencyIndex++;
                            continue;
                        }
                        if (!meter.TryConsumeDependencyEntries(1))
                            return false;
                        string destination = _dependencyStage == 1
                            ? state.Map.TransitionSpan[_dependencyIndex].Destination.MapId
                            : overlay.Transition.Destination.MapId;
                        _working.UpdateIncomingSource(destination, state.Map.MapId, remove);
                    }
                    _dependencyIndex++;
                    _dependencyAddressIndex = 0;
                }
                _dependencyStage++;
                _dependencyIndex = 0;
            }
            return true;
        }

        private void Commit()
        {
            if (_next == null)
            {
                _working._maps = _working._maps.Remove(
                    _mapId,
                    out _,
                    out int copiedNodes);
                _working.RecordPersistentCopies(copiedNodes);
                _working.RemoveMapStateTotals(_current!);
                _working._gridBindings = _working._gridBindings.Remove(
                    _current!.Map.GridBinding.Key,
                    out _,
                    out copiedNodes);
                _working.RecordPersistentCopies(copiedNodes, 144L);
                _working.ReplaceTotals(_current, next: null);
            }
            else
            {
                _working.SetMapState(_current, _next);
                if (_current != null && !_current.Map.GridBinding.Key.Equals(_next.Map.GridBinding.Key))
                {
                    _working._gridBindings = _working._gridBindings.Remove(
                        _current.Map.GridBinding.Key,
                        out _,
                        out int removedCopies);
                    _working.RecordPersistentCopies(removedCopies, 144L);
                }
                _working._gridBindings = _working._gridBindings.Set(
                    _next.Map.GridBinding.Key,
                    _mapId,
                    out int bindingCopies);
                _working.RecordPersistentCopies(bindingCopies, 144L);
                _working.ReplaceTotals(_current, _next);
                _working._bakeVersionHighWater = _working._bakeVersionHighWater.Set(
                    _mapId,
                    _prepared!.BakeVersion,
                    out int versionCopies);
                _working.RecordPersistentCopies(versionCopies);
            }
            _stage = Stage.Complete;
        }

        private void ResetDependencyCursor()
        {
            _dependencyStage = 0;
            _dependencyIndex = 0;
            _dependencyAddressIndex = 0;
        }

        private enum Stage
        {
            Cells,
            Dynamic,
            AuthoredValidation,
            Validation,
            Dependencies,
            Commit,
            Complete
        }
    }
}
