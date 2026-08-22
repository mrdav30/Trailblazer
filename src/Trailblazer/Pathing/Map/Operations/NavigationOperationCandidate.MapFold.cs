//=======================================================================
// NavigationOperationCandidate.MapFold.cs
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
    internal MapFoldCursor BeginMapFold(
        PreparedNavigationMap prepared,
        OverlayReplacementPolicy replacementPolicy,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            prepared,
            replacementPolicy,
            limits,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    internal MapFoldCursor BeginMapRemovalFold(
        string mapId,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet) => new(
            this,
            mapId,
            corridorPrisms,
            corridorWaypoints,
            corridorAddresses,
            corridorAddressSet);

    internal sealed class MapFoldCursor
    {
        private readonly NavigationOperationCandidate _working;
        private readonly NavigationOperationCandidate _foldSource;
        private readonly PreparedNavigationMap? _prepared;
        private readonly OverlayReplacementPolicy _replacementPolicy;
        private readonly NavigationOperationLimits _limits;
        private readonly GridCellPrism[]? _corridorPrisms;
        private readonly Vector3d[]? _corridorWaypoints;
        private readonly NavigationCellAddress[] _corridorAddresses;
        private readonly NavigationAddressStampSet _corridorAddressSet;
        private readonly string _mapId;
        private readonly MapState? _current;
        private readonly NavigationExplicitConnectionIndex _sourceExplicitConnections;
        private MapState? _next;
        private readonly string[] _changedMapIds = new string[1];
        private readonly MapState[] _changedStates = new MapState[1];
        private NavigationOperationRejection _rejection;
        private bool _defaultCellValidated;
        private int _cellIndex;
        private int _overlayCellIndex;
        private int _dynamicIndex;
        private int _authoredIndex;
        private int _ruleValidationMapIndex;
        private int _ruleValidationSourceIndex;
        private int _ruleValidationTargetIndex;
        private bool _ruleValidationMapDebited;
        private int _validationSourceIndex = -1;
        private int _validationStage;
        private int _validationIndex;
        private int _dependencyStage;
        private int _dependencyIndex;
        private bool _removeDependencies = true;
        private ExplicitConnectionRefreshWork? _explicitRefresh;
        private long _displacedMapStatePayloadBytes;
        private int _displacedMapStatePayloadPages;
        private Stage _stage;

        internal MapFoldCursor(
            NavigationOperationCandidate source,
            PreparedNavigationMap prepared,
            OverlayReplacementPolicy replacementPolicy,
            NavigationOperationLimits limits,
            GridCellPrism[] corridorPrisms,
            Vector3d[] corridorWaypoints,
            NavigationCellAddress[] corridorAddresses,
            NavigationAddressStampSet corridorAddressSet)
        {
            _working = source.Clone();
            _foldSource = source;
            _sourceExplicitConnections = source.ExplicitConnections;
            _prepared = prepared;
            _replacementPolicy = replacementPolicy;
            _limits = limits;
            _corridorPrisms = corridorPrisms;
            _corridorWaypoints = corridorWaypoints;
            _corridorAddresses = corridorAddresses;
            _corridorAddressSet = corridorAddressSet;
            _mapId = prepared.Map.MapId;
            _working._maps.TryGetValue(_mapId, out _current);
            _changedMapIds[0] = _mapId;
            PrepareCommit();
        }

        internal MapFoldCursor(
            NavigationOperationCandidate source,
            string mapId,
            GridCellPrism[] corridorPrisms,
            Vector3d[] corridorWaypoints,
            NavigationCellAddress[] corridorAddresses,
            NavigationAddressStampSet corridorAddressSet)
        {
            _working = source.Clone();
            _foldSource = source;
            _sourceExplicitConnections = source.ExplicitConnections;
            _prepared = null;
            _replacementPolicy = OverlayReplacementPolicy.Clear;
            _limits = default;
            _mapId = mapId;
            _corridorPrisms = corridorPrisms;
            _corridorWaypoints = corridorWaypoints;
            _corridorAddresses = corridorAddresses;
            _corridorAddressSet = corridorAddressSet;
            _working._maps.TryGetValue(mapId, out _current);
            _changedMapIds[0] = mapId;
            _stage = _current == null ? Stage.Complete : Stage.Dependencies;
            _rejection = _current == null
                ? NavigationOperationRejection.MissingMap
                : NavigationOperationRejection.None;
        }

        internal NavigationOperationCandidate Candidate => _working;

        internal long DisplacedExplicitPayloadBytes =>
            _explicitRefresh?.DisplacedSourcePayloadBytes ?? 0L;

        internal int DisplacedExplicitPayloadPages =>
            _explicitRefresh?.DisplacedSourcePayloadPages ?? 0;

        internal long DisplacedMapStatePayloadBytes => _displacedMapStatePayloadBytes;

        internal int DisplacedMapStatePayloadPages => _displacedMapStatePayloadPages;

        internal long RetainedBytes => checked(
            128L
            + ((long)(_changedMapIds.Length + _changedStates.Length) * System.IntPtr.Size)
            + _working.WorkCopiedPersistentBytes
            + (_explicitRefresh?.RetainedBytes ?? 0)
            + GetPendingStateGrowthBytes());

        internal int PersistentPageCount => checked(
            1
            + _working.WorkCopiedPersistentPages
            + (_explicitRefresh?.PersistentPageCount ?? 0)
            + GetPendingStateGrowthPages());

        private long GetPendingStateGrowthBytes()
        {
            if (_next == null || _stage == Stage.Complete)
                return 0;
            GetAdditionalMapStatePayload(_next, _current, out long bytes, out _);
            return bytes;
        }

        private int GetPendingStateGrowthPages()
        {
            if (_next == null || _stage == Stage.Complete)
                return 0;
            GetAdditionalMapStatePayload(_next, _current, out _, out int pages);
            return pages;
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
            if (_stage == Stage.ExplicitConnections)
            {
                if (!_explicitRefresh!.Advance(meter))
                {
                    rejection = NavigationOperationRejection.None;
                    return false;
                }
                if (!_explicitRefresh.IsValid)
                    _rejection = NavigationOperationRejection.ValidationFailed;
                _stage = Stage.Complete;
            }
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
            long retainedRuleCount = checked(
                _working.TransitionRuleCount
                - (_current?.Map.TransitionRuleSpan.Length ?? 0));
            if (map.TransitionRuleSpan.Length > _limits.MaxTransitionRulesPerMap
                || map.TransitionRuleSpan.Length > _limits.MaxTransitionRules - retainedRuleCount)
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
            if (!_defaultCellValidated && _next!.Map.DefaultCell.HasValue)
            {
                if (!meter.TryConsumeOverlaySlots(1))
                    return false;
                _defaultCellValidated = true;
                if (_next.Map.DefaultCell.Value.Area.Value >= _working._navigationAreaCount)
                {
                    _rejection = NavigationOperationRejection.ValidationFailed;
                    _stage = Stage.Complete;
                    return true;
                }
            }
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
            while (_authoredIndex < _next!.Map.ConnectionSpan.Length)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                    return false;
                if (_next.Map.ConnectionSpan[_authoredIndex++].Witnesses.Count
                    > _limits.MaxCorridorCells - 2)
                {
                    _rejection = NavigationOperationRejection.CapacityExceeded;
                    _stage = Stage.Complete;
                    return true;
                }
            }
            if (!AdvanceRuleOwnershipValidation(meter))
                return false;
            if (_rejection != NavigationOperationRejection.None)
                return true;
            _stage = Stage.Validation;
            return true;
        }

        private bool AdvanceRuleOwnershipValidation(MaintenanceWorkMeter meter)
        {
            ReadOnlySpan<TraversalTransitionRule> sourceRules = _next!.Map.TransitionRuleSpan;
            if (sourceRules.IsEmpty)
                return true;
            while (_ruleValidationMapIndex < _working._maps.Count)
            {
                if (!_ruleValidationMapDebited)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    _ruleValidationMapDebited = true;
                }
                MapState target = _working._maps.GetValueAt(_ruleValidationMapIndex);
                if (string.Equals(target.Map.MapId, _mapId, System.StringComparison.Ordinal))
                {
                    _ruleValidationMapIndex++;
                    _ruleValidationMapDebited = false;
                    continue;
                }
                ReadOnlySpan<TraversalTransitionRule> targetRules = target.Map.TransitionRuleSpan;
                while (_ruleValidationSourceIndex < sourceRules.Length
                    && _ruleValidationTargetIndex < targetRules.Length)
                {
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    int comparison = string.CompareOrdinal(
                        sourceRules[_ruleValidationSourceIndex].Id,
                        targetRules[_ruleValidationTargetIndex].Id);
                    if (comparison == 0)
                    {
                        _rejection = NavigationOperationRejection.ValidationFailed;
                        _stage = Stage.Complete;
                        return true;
                    }
                    if (comparison < 0)
                        _ruleValidationSourceIndex++;
                    else
                        _ruleValidationTargetIndex++;
                }
                _ruleValidationMapIndex++;
                _ruleValidationSourceIndex = 0;
                _ruleValidationTargetIndex = 0;
                _ruleValidationMapDebited = false;
            }
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
            while (_validationStage < 2)
            {
                int count = _validationStage == 0
                    ? state.Map.TransitionSpan.Length
                    : state.Overlay.TransitionCount;
                while (_validationIndex < count)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                        return false;
                    if (!_working.ValidateTransitionForWork(
                            state,
                            overlay: _validationStage == 1,
                            _validationIndex++,
                            _changedMapIds,
                            _changedStates,
                            allowDormantEndpoints: true))
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
            while (_dependencyStage < 2)
            {
                int count = _dependencyStage == 0
                    ? state.Map.TransitionSpan.Length
                    : state.Overlay.TransitionCount;
                while (_dependencyIndex < count)
                {
                    TraversalTransitionOverlayOperation overlay = _dependencyStage == 1
                        ? state.Overlay.GetTransitionAt(_dependencyIndex)
                        : default;
                    if (_dependencyStage == 1
                        && overlay.Kind != TraversalTransitionOverlayOperationKind.Upsert)
                    {
                        _dependencyIndex++;
                        continue;
                    }
                    if (!meter.TryConsumeDependencyEntries(1))
                        return false;
                    string destination = _dependencyStage == 0
                        ? state.Map.TransitionSpan[_dependencyIndex].Destination.MapId
                        : overlay.Transition.Destination.MapId;
                    _working.UpdateIncomingSource(destination, state.Map.MapId, remove);
                    _dependencyIndex++;
                }
                _dependencyStage++;
                _dependencyIndex = 0;
            }
            return true;
        }

        private void Commit()
        {
            _working.RecordMapStateOwnership(
                _mapId,
                _next,
                _foldSource,
                ref _displacedMapStatePayloadBytes,
                ref _displacedMapStatePayloadPages);
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
            _explicitRefresh = _working.BeginExplicitConnectionRefresh(
                _mapId,
                _sourceExplicitConnections,
                _corridorPrisms!,
                _corridorWaypoints!,
                _corridorAddresses,
                _corridorAddressSet);
            _stage = Stage.ExplicitConnections;
        }

        private void ResetDependencyCursor()
        {
            _dependencyStage = 0;
            _dependencyIndex = 0;
        }

        private enum Stage
        {
            Cells,
            Dynamic,
            AuthoredValidation,
            Validation,
            Dependencies,
            Commit,
            ExplicitConnections,
            Complete
        }
    }
}
