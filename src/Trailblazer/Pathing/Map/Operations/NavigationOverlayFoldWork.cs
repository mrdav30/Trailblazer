//=======================================================================
// NavigationOverlayFoldWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Folds one atomic overlay transaction into persistent map roots across frames.</summary>
internal sealed class NavigationOverlayFoldWork
{
    private readonly NavigationOperationCandidate _source;
    private readonly NavigationOperationCandidate _working;
    private readonly NavigationOverlayTransaction _transaction;
    private readonly long _operationSequence;
    private readonly NavigationOperationLimits _limits;
    private readonly GridCellPrism[] _corridorPrisms;
    private readonly FixedMathSharp.Vector3d[] _corridorWaypoints;
    private readonly NavigationOperationCandidate.MapState[] _priorStates;
    private readonly NavigationOperationCandidate.MapState[] _nextStates;
    private readonly string[] _changedMapIds;
    private int _mapIndex;
    private int _cellIndex;
    private int _connectionIndex;
    private int _transitionIndex;
    private NavigationOperationCandidate.MapState? _current;
    private NavigationMapOverlayState? _overlay;
    private PersistentVoxelIndexMap<byte>? _dynamicAddresses;
    private int _dependencyMapIndex;
    private int _dependencyStage;
    private int _dependencyIndex;
    private bool _removeDependencies = true;
    private int _validationMapIndex;
    private int _validationSourceIndex = -1;
    private int _validationStage;
    private int _validationEdgeIndex;
    private int _validationWorkIndex;
    private Stage _stage;
    private NavigationOperationCandidate.ExplicitConnectionRefreshWork? _explicitRefresh;

    internal NavigationOverlayFoldWork(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        long operationSequence,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        FixedMathSharp.Vector3d[] corridorWaypoints)
    {
        _source = source;
        _working = source.Clone();
        _transaction = transaction;
        _operationSequence = operationSequence;
        _limits = limits;
        _corridorPrisms = corridorPrisms;
        _corridorWaypoints = corridorWaypoints;
        int mapCount = transaction.MapSpan.Length;
        _priorStates = new NavigationOperationCandidate.MapState[mapCount];
        _nextStates = new NavigationOperationCandidate.MapState[mapCount];
        _changedMapIds = new string[mapCount];
    }

    internal NavigationOperationCandidate Candidate => _working;

    internal long SourceRetainedBytes => _source.RetainedBytes;

    internal int SourcePersistentPageCount => _source.PersistentPageCount;

    internal long RetainedBytes => checked(
        160L
        + _transaction.EstimatedDescriptorBytes
        + ((long)(_priorStates.Length + _nextStates.Length + _changedMapIds.Length)
            * IntPtr.Size)
        + _working.WorkCopiedPersistentBytes
        + (_explicitRefresh?.RetainedBytes ?? 0)
        + Math.Max(0L, _working.RetainedBytes - _source.RetainedBytes));

    internal int PersistentPageCount => checked(
        1
        + _working.WorkCopiedPersistentPages
        + (_explicitRefresh?.PersistentPageCount ?? 0)
        + Math.Max(0, _working.PersistentPageCount - _source.PersistentPageCount));

    internal bool Advance(
        MaintenanceWorkMeter meter,
        out NavigationOperationRejection rejection)
    {
        if (_stage == Stage.Fold)
        {
            ReadOnlySpan<NavigationMapOverlayDelta> maps = _transaction.MapSpan;
            while (_mapIndex < maps.Length)
            {
                NavigationMapOverlayDelta delta = maps[_mapIndex];
                if (_current == null)
                {
                    if (!_source.TryGetState(delta.MapId, out _current) || _current == null)
                    {
                        rejection = NavigationOperationRejection.MissingMap;
                        return true;
                    }
                    _overlay = _current.Overlay;
                    _dynamicAddresses = _current.DynamicAddresses;
                }
                ReadOnlySpan<NavigationCellOverlayOperation> cells = delta.CellSpan;
                while (_cellIndex < cells.Length)
                {
                    if (!meter.TryConsumeOverlaySlots(1))
                    {
                        rejection = NavigationOperationRejection.None;
                        return false;
                    }
                    NavigationCellOverlayOperation change = cells[_cellIndex++];
                    if (!_current.Map.GridBinding.IsValidIndex(change.Index)
                        || (change.Kind == NavigationCellOverlayOperationKind.Set
                            && change.Cell.Area.Value >= _working.NavigationAreaCount))
                    {
                        rejection = NavigationOperationRejection.ValidationFailed;
                        return true;
                    }
                    _overlay = _overlay!.Apply(change, _operationSequence);
                    _working.RecordPersistentCopies(_overlay.LastApplyCopiedNodeCount);
                    if (change.Kind == NavigationCellOverlayOperationKind.Set
                        && !_current.Map.ContainsCell(change.Index))
                    {
                        _dynamicAddresses = _dynamicAddresses!.Set(
                            change.Index,
                            0,
                            out int copiedNodes);
                        _working.RecordPersistentCopies(copiedNodes);
                    }
                }
                ReadOnlySpan<NavigationConnectionOverlayOperation> connections = delta.ConnectionSpan;
                while (_connectionIndex < connections.Length)
                {
                    if (!meter.TryConsumeOverlaySlots(1))
                    {
                        rejection = NavigationOperationRejection.None;
                        return false;
                    }
                    NavigationConnectionOverlayOperation change = connections[_connectionIndex++];
                    if (change.Kind == NavigationConnectionOverlayOperationKind.Upsert
                        && change.Connection!.Witnesses.Count > _limits.MaxCorridorCells - 2)
                    {
                        rejection = NavigationOperationRejection.CapacityExceeded;
                        return true;
                    }
                    _overlay = _overlay!.Apply(change, _operationSequence);
                    _working.RecordPersistentCopies(_overlay.LastApplyCopiedNodeCount);
                }
                ReadOnlySpan<TraversalTransitionOverlayOperation> transitions = delta.TransitionSpan;
                while (_transitionIndex < transitions.Length)
                {
                    if (!meter.TryConsumeOverlaySlots(1))
                    {
                        rejection = NavigationOperationRejection.None;
                        return false;
                    }
                    _overlay = _overlay!.Apply(transitions[_transitionIndex++], _operationSequence);
                    _working.RecordPersistentCopies(_overlay.LastApplyCopiedNodeCount);
                }

                NavigationOperationCandidate.MapState next = new(
                    _current.Map,
                    _current.BakeVersion,
                    _current.PreparedMapRetainedBytes,
                    _overlay!,
                    _current.DynamicSlotGeneration,
                    _dynamicAddresses!,
                    _current.BakedCellLookup);
                rejection = _working.ReplaceOverlayState(
                    _current,
                    next,
                    _limits,
                    _corridorPrisms,
                    _corridorWaypoints);
                if (rejection != NavigationOperationRejection.None)
                    return true;
                _priorStates[_mapIndex] = _current;
                _nextStates[_mapIndex] = next;
                _changedMapIds[_mapIndex] = delta.MapId;
                _mapIndex++;
                _cellIndex = 0;
                _connectionIndex = 0;
                _transitionIndex = 0;
                _current = null;
                _overlay = null;
                _dynamicAddresses = null;
            }
            _stage = Stage.Dependencies;
        }

        if (_stage == Stage.Dependencies)
        {
            while (_dependencyMapIndex < _priorStates.Length)
            {
                NavigationOperationCandidate.MapState state = _removeDependencies
                    ? _priorStates[_dependencyMapIndex]
                    : _nextStates[_dependencyMapIndex];
                if (!AdvanceDependencies(state, _removeDependencies, meter))
                {
                    rejection = NavigationOperationRejection.None;
                    return false;
                }
                if (_removeDependencies)
                {
                    _removeDependencies = false;
                    ResetDependencyCursor();
                    continue;
                }
                _removeDependencies = true;
                _dependencyMapIndex++;
                ResetDependencyCursor();
            }
            _stage = Stage.Validation;
        }

        while (_validationMapIndex < _nextStates.Length)
        {
            NavigationOperationCandidate.MapState state;
            if (_validationSourceIndex < 0)
                state = _nextStates[_validationMapIndex];
            else
            {
                string target = _changedMapIds[_validationMapIndex];
                int sourceCount = _working.GetIncomingSourceCount(target);
                if (_validationSourceIndex >= sourceCount)
                {
                    _validationMapIndex++;
                    _validationSourceIndex = -1;
                    continue;
                }
                string sourceId = _working.GetIncomingSource(target, _validationSourceIndex);
                if (!_working.TryGetState(sourceId, out NavigationOperationCandidate.MapState? source)
                    || source == null)
                {
                    _validationSourceIndex++;
                    continue;
                }
                state = source;
            }
            if (!AdvanceValidation(state, meter, out bool valid))
            {
                rejection = NavigationOperationRejection.None;
                return false;
            }
            if (!valid)
            {
                rejection = NavigationOperationRejection.ValidationFailed;
                return true;
            }
            if (_validationSourceIndex < 0)
                _validationSourceIndex = 0;
            else
                _validationSourceIndex++;
        }
        _explicitRefresh ??= _working.BeginExplicitConnectionRefresh(
            _transaction,
            _corridorPrisms,
            _corridorWaypoints);
        if (!_explicitRefresh.Advance(meter))
        {
            rejection = NavigationOperationRejection.None;
            return false;
        }
        if (!_explicitRefresh.IsValid)
        {
            rejection = NavigationOperationRejection.ValidationFailed;
            return true;
        }
        _stage = Stage.Complete;
        rejection = NavigationOperationRejection.None;
        return true;
    }

    private bool AdvanceDependencies(
        NavigationOperationCandidate.MapState state,
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
                _working.UpdateIncomingSourceForWork(destination, state.Map.MapId, remove);
                _dependencyIndex++;
            }
            _dependencyStage++;
            _dependencyIndex = 0;
        }
        return true;
    }

    private bool AdvanceValidation(
        NavigationOperationCandidate.MapState state,
        MaintenanceWorkMeter meter,
        out bool valid)
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
            while (_validationEdgeIndex < count)
            {
                if (_validationStage < 2)
                {
                    _validationEdgeIndex++;
                    continue;
                }
                int workUnits = GetValidationWorkUnits(
                    state,
                    _validationStage,
                    _validationEdgeIndex);
                while (_validationWorkIndex < workUnits)
                {
                    if (!meter.TryConsumeExplicitEdges(1))
                    {
                        valid = true;
                        return false;
                    }
                    _validationWorkIndex++;
                }
                _validationWorkIndex = 0;
                if (!_working.ValidateStateEdgeForWork(
                        state,
                        _validationStage,
                        _validationEdgeIndex++,
                        _changedMapIds,
                        _nextStates,
                        _corridorPrisms,
                        _corridorWaypoints,
                        allowDormantEndpoints: true))
                {
                    valid = false;
                    return true;
                }
            }
            _validationStage++;
            _validationEdgeIndex = 0;
        }
        _validationStage = 0;
        valid = true;
        return true;
    }

    private static int GetValidationWorkUnits(
        NavigationOperationCandidate.MapState state,
        int stage,
        int index)
    {
        if (stage == 0)
            return state.Map.ConnectionSpan[index].Witnesses.Count + 2;
        if (stage != 1)
            return 1;
        NavigationConnectionOverlayOperation operation = state.Overlay.GetConnectionAt(index);
        return operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
            ? operation.Connection!.Witnesses.Count + 2
            : 1;
    }

    private void ResetDependencyCursor()
    {
        _dependencyStage = 0;
        _dependencyIndex = 0;
    }

    private enum Stage
    {
        Fold,
        Dependencies,
        Validation,
        Complete
    }
}
