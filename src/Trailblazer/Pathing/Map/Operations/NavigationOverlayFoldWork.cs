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
    private readonly NavigationCellAddress[] _corridorAddresses;
    private readonly NavigationAddressStampSet _corridorAddressSet;
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
    private Stage _stage;
    private NavigationOperationCandidate.ExplicitConnectionRefreshWork? _explicitRefresh;
    private long _displacedMapStatePayloadBytes;
    private int _displacedMapStatePayloadPages;

    internal NavigationOverlayFoldWork(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        long operationSequence,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        FixedMathSharp.Vector3d[] corridorWaypoints,
        NavigationCellAddress[] corridorAddresses,
        NavigationAddressStampSet corridorAddressSet)
    {
        _source = source;
        _working = source.Clone();
        _transaction = transaction;
        _operationSequence = operationSequence;
        _limits = limits;
        _corridorPrisms = corridorPrisms;
        _corridorWaypoints = corridorWaypoints;
        _corridorAddresses = corridorAddresses;
        _corridorAddressSet = corridorAddressSet;
        int mapCount = transaction.MapSpan.Length;
        _priorStates = new NavigationOperationCandidate.MapState[mapCount];
        _nextStates = new NavigationOperationCandidate.MapState[mapCount];
        _changedMapIds = new string[mapCount];
    }

    internal NavigationOperationCandidate Candidate => _working;

    internal bool ExplicitGatherComplete => _explicitRefresh?.IsGatherComplete == true;

    internal bool MayChangeExplicitConnections => _transaction.MayChangeExplicitConnections;

    internal long DisplacedExplicitPayloadBytes =>
        _explicitRefresh?.DisplacedSourcePayloadBytes ?? 0L;

    internal int DisplacedExplicitPayloadPages =>
        _explicitRefresh?.DisplacedSourcePayloadPages ?? 0;

    internal long DisplacedMapStatePayloadBytes => _displacedMapStatePayloadBytes;

    internal int DisplacedMapStatePayloadPages => _displacedMapStatePayloadPages;

    internal long RetainedBytes => checked(
        160L
        + _transaction.EstimatedDescriptorBytes
        + ((long)(_priorStates.Length + _nextStates.Length + _changedMapIds.Length)
            * IntPtr.Size)
        + _working.WorkCopiedPersistentBytes
        + (_explicitRefresh?.RetainedBytes ?? 0)
        + GetPendingMapStatePayloadBytes());

    internal int PersistentPageCount => checked(
        1
        + _working.WorkCopiedPersistentPages
        + (_explicitRefresh?.PersistentPageCount ?? 0)
        + GetPendingMapStatePayloadPages());

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
                _working.RecordMapStateOwnership(
                    delta.MapId,
                    next,
                    _source,
                    ref _displacedMapStatePayloadBytes,
                    ref _displacedMapStatePayloadPages);
                rejection = _working.ReplaceOverlayState(
                    _current,
                    next,
                    _limits);
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
            _source.ExplicitConnections,
            _corridorPrisms,
            _corridorWaypoints,
            _corridorAddresses,
            _corridorAddressSet);
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

    private long GetPendingMapStatePayloadBytes()
    {
        long bytes = 0;
        if (_current == null)
            return bytes;
        if (_overlay != null && !ReferenceEquals(_overlay, _current.Overlay))
            bytes = checked(bytes + _overlay.RetainedBytes);
        if (_dynamicAddresses != null
            && !ReferenceEquals(_dynamicAddresses, _current.DynamicAddresses))
        {
            bytes = checked(bytes + _dynamicAddresses.RetainedBytes);
        }
        return bytes;
    }

    private int GetPendingMapStatePayloadPages()
    {
        int pages = 0;
        if (_current == null)
            return pages;
        if (_overlay != null && !ReferenceEquals(_overlay, _current.Overlay))
            pages = checked(pages + _overlay.PersistentNodeCount);
        if (_dynamicAddresses != null
            && !ReferenceEquals(_dynamicAddresses, _current.DynamicAddresses))
        {
            pages = checked(pages + _dynamicAddresses.PersistentNodeCount);
        }
        return pages;
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
        while (_validationStage < 2)
        {
            int count = _validationStage == 0
                ? state.Map.TransitionSpan.Length
                : state.Overlay.TransitionCount;
            while (_validationEdgeIndex < count)
            {
                if (!meter.TryConsumeExplicitEdges(1))
                {
                    valid = true;
                    return false;
                }
                if (!_working.ValidateTransitionForWork(
                        state,
                        overlay: _validationStage == 1,
                        _validationEdgeIndex++,
                        _changedMapIds,
                        _nextStates,
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
