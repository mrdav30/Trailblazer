//=======================================================================
// NavigationOperationCandidate.cs
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
    private readonly int _navigationAreaCount;
    private PersistentStringMap<MapState> _maps = PersistentStringMap<MapState>.Empty;
    private PersistentStringMap<long> _bakeVersionHighWater = PersistentStringMap<long>.Empty;
    private PersistentStringMap<PersistentStringMap<bool>> _incomingSources =
        PersistentStringMap<PersistentStringMap<bool>>.Empty;
    private PersistentGridConfigurationMap<string> _gridBindings = PersistentGridConfigurationMap<string>.Empty;
    private NavigationExplicitConnectionIndex _explicitConnections =
        NavigationExplicitConnectionIndex.Empty;
    private PersistentStringMap<bool> _explicitChangedSources =
        PersistentStringMap<bool>.Empty;
    private long _overlaySlotCount;
    private long _overlayConnectionCount;
    private long _overlayTransitionCount;
    private long _seamCandidateCount;
    private long _explicitEdgeCount;
    private int _dynamicCellCount;
    private long _mapStateRetainedBytes;
    private int _mapStatePersistentPages;
    private long _incomingSetRetainedBytes;
    private int _incomingSetPersistentPages;
    private long _workCopiedPersistentBytes;
    private int _workCopiedPersistentPages;

    internal NavigationOperationCandidate(int navigationAreaCount = ushort.MaxValue + 1)
    {
        _navigationAreaCount = navigationAreaCount;
    }

    internal int MapCount => _maps.Count;

    internal long OverlayCellCount => _overlaySlotCount;

    internal long OverlayConnectionCount => _overlayConnectionCount;

    internal long OverlayTransitionCount => _overlayTransitionCount;

    internal int NavigationAreaCount => _navigationAreaCount;

    internal long RetainedBytes => checked(
        96L
        + _maps.RetainedBytes
        + _bakeVersionHighWater.RetainedBytes
        + _incomingSources.RetainedBytes
        + _gridBindings.RetainedBytes
        + _explicitConnections.RetainedBytes
        + _explicitChangedSources.RetainedBytes
        + _mapStateRetainedBytes
        + _incomingSetRetainedBytes);

    internal int PersistentPageCount => checked(
        4
        + _maps.PersistentNodeCount
        + _bakeVersionHighWater.PersistentNodeCount
        + _incomingSources.PersistentNodeCount
        + _gridBindings.Count
        + _explicitConnections.PersistentPageCount
        + _explicitChangedSources.PersistentNodeCount
        + _mapStatePersistentPages
        + _incomingSetPersistentPages);

    internal long WorkCopiedPersistentBytes => _workCopiedPersistentBytes;

    internal int WorkCopiedPersistentPages => _workCopiedPersistentPages;

    internal void ResetWorkCopiedPersistentOwnership()
    {
        _workCopiedPersistentBytes = 0;
        _workCopiedPersistentPages = 0;
        _explicitChangedSources = PersistentStringMap<bool>.Empty;
    }

    internal void RecordPersistentCopies(int copiedNodes, long bytesPerNode = 64L)
    {
        _workCopiedPersistentPages = checked(_workCopiedPersistentPages + copiedNodes);
        _workCopiedPersistentBytes = checked(
            _workCopiedPersistentBytes + (copiedNodes * bytesPerNode));
    }

    internal NavigationOperationRejection ReplaceOverlayState(
        MapState current,
        MapState next,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        if (next.Overlay.CellCount > limits.MaxOverlayCellsPerMap
            || next.Overlay.ConnectionCount > limits.MaxOverlayConnectionsPerMap
            || next.Overlay.TransitionCount > limits.MaxOverlayTransitionsPerMap)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }
        long candidateCells = _overlaySlotCount
            - current.Overlay.CellCount + next.Overlay.CellCount;
        long candidateConnections = _overlayConnectionCount
            - current.Overlay.ConnectionCount + next.Overlay.ConnectionCount;
        long candidateTransitions = _overlayTransitionCount
            - current.Overlay.TransitionCount + next.Overlay.TransitionCount;
        if (candidateCells > limits.MaxOverlayCells
            || candidateConnections > limits.MaxOverlayConnections
            || candidateTransitions > limits.MaxOverlayTransitions)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }
        ReplaceTotals(current, next);
        SetMapState(current, next);
        return NavigationOperationRejection.None;
    }

    internal NavigationOperationRejection ValidateOverlayCandidate(
        NavigationOverlayTransaction transaction,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        ReadOnlySpan<NavigationMapOverlayDelta> deltas = transaction.MapSpan;
        var ids = new string[deltas.Length];
        var states = new MapState[deltas.Length];
        for (int i = 0; i < deltas.Length; i++)
        {
            ids[i] = deltas[i].MapId;
            if (!_maps.TryGetValue(ids[i], out states[i]!))
                return NavigationOperationRejection.MissingMap;
        }
        return ValidateCandidate(
            ids,
            states,
            corridorPrisms,
            corridorWaypoints,
            allowDormantEndpoints: true)
            ? NavigationOperationRejection.None
            : NavigationOperationRejection.ValidationFailed;
    }

    internal NavigationOperationCandidate Clone()
    {
        return new NavigationOperationCandidate(_navigationAreaCount)
        {
            _maps = _maps,
            _bakeVersionHighWater = _bakeVersionHighWater,
            _incomingSources = _incomingSources,
            _gridBindings = _gridBindings,
            _explicitConnections = _explicitConnections,
            _explicitChangedSources = _explicitChangedSources,
            _overlaySlotCount = _overlaySlotCount,
            _overlayConnectionCount = _overlayConnectionCount,
            _overlayTransitionCount = _overlayTransitionCount,
            _seamCandidateCount = _seamCandidateCount,
            _explicitEdgeCount = _explicitEdgeCount,
            _dynamicCellCount = _dynamicCellCount,
            _mapStateRetainedBytes = _mapStateRetainedBytes,
            _mapStatePersistentPages = _mapStatePersistentPages,
            _incomingSetRetainedBytes = _incomingSetRetainedBytes,
            _incomingSetPersistentPages = _incomingSetPersistentPages,
            _workCopiedPersistentBytes = _workCopiedPersistentBytes,
            _workCopiedPersistentPages = _workCopiedPersistentPages
        };
    }

    internal int GetIncomingSourceCount(string mapId) =>
        _incomingSources.TryGetValue(mapId, out PersistentStringMap<bool> sources)
            ? sources.Count
            : 0;

    internal string GetIncomingSource(string mapId, int ordinal)
    {
        _incomingSources.TryGetValue(mapId, out PersistentStringMap<bool> sources);
        return sources.GetKeyAt(ordinal);
    }

    internal void UpdateIncomingSourceForWork(
        string destinationMapId,
        string sourceMapId,
        bool remove) => UpdateIncomingSource(destinationMapId, sourceMapId, remove);

    internal bool ValidateStateEdgeForWork(
        MapState state,
        int stage,
        int index,
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        bool allowDormantEndpoints)
    {
        switch (stage)
        {
            case 0:
            case 1:
                return true;
            case 2:
                TraversalTransitionDefinition transition = state.Map.TransitionSpan[index];
                return state.Overlay.TryGetTransition(transition.Id, out _)
                    || ValidateTransition(
                        state,
                        transition,
                        changedMapIds,
                        changedStates,
                        allowDormantEndpoints);
            default:
                TraversalTransitionOverlayOperation transitionOverlay =
                    state.Overlay.GetTransitionAt(index);
                return transitionOverlay.Kind != TraversalTransitionOverlayOperationKind.Upsert
                    || ValidateTransition(
                        state,
                        transitionOverlay.Transition,
                        changedMapIds,
                        changedStates,
                        allowDormantEndpoints);
        }
    }

    internal bool ValidateAuthoredStateEdgeForWork(
        MapState state,
        bool connection,
        int index,
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        bool allowDormantEndpoints) => connection
            ? !RequiresAuthoredConnectionValidation(state, index)
                || ValidateConnection(
                state,
                state.Map.ConnectionSpan[index],
                changedMapIds,
                changedStates,
                corridorPrisms,
                corridorWaypoints,
                allowDormantEndpoints,
                useAuthoredFallback: true)
            : ValidateTransition(
                state,
                state.Map.TransitionSpan[index],
                changedMapIds,
                changedStates,
                allowDormantEndpoints,
                useAuthoredFallback: true);

    internal static bool RequiresAuthoredConnectionValidation(MapState state, int index) =>
        state.Overlay.TryGetConnection(state.Map.ConnectionSpan[index].Id, out _);

    internal MapState[] CaptureStates()
    {
        var states = new MapState[_maps.Count];
        _maps.CopyValuesTo(states);
        return states;
    }

    internal int GetTotalDynamicCellCandidateCount()
    {
        return _dynamicCellCount;
    }

    internal bool TryGetMap(string mapId, out NavigationMap map)
    {
        if (_maps.TryGetValue(mapId, out MapState? state))
        {
            map = state.Map;
            return true;
        }

        map = null!;
        return false;
    }

    internal bool TryGetState(string mapId, out MapState? state) =>
        _maps.TryGetValue(mapId, out state);

    internal bool TryGetOverlay(string mapId, out NavigationMapOverlayState overlay)
    {
        if (_maps.TryGetValue(mapId, out MapState? state))
        {
            overlay = state.Overlay;
            return true;
        }

        overlay = NavigationMapOverlayState.Empty;
        return false;
    }

    internal NavigationOperationRejection ApplyMap(
        PreparedNavigationMap prepared,
        OverlayReplacementPolicy replacementPolicy,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        string mapId = prepared.Map.MapId;
        bool replacing = _maps.TryGetValue(mapId, out MapState? current);
        if (_bakeVersionHighWater.TryGetValue(mapId, out long bakeVersionHighWater)
            && prepared.BakeVersion <= bakeVersionHighWater)
        {
            return NavigationOperationRejection.Stale;
        }
        if (!_bakeVersionHighWater.ContainsKey(mapId)
            && _bakeVersionHighWater.Count >= limits.MaxRetainedMapIdentities)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }
        if (!replacing && _maps.Count >= limits.MaxMaps)
            return NavigationOperationRejection.CapacityExceeded;

        for (int cellIndex = 0; cellIndex < prepared.Map.Cells.Count; cellIndex++)
        {
            if (prepared.Map.Cells[cellIndex].Cell.Area.Value >= _navigationAreaCount)
                return NavigationOperationRejection.ValidationFailed;
        }

        if (_gridBindings.TryGetValue(prepared.Map.GridBinding.Key, out string boundMapId)
            && !string.Equals(boundMapId, mapId, StringComparison.Ordinal))
        {
            return NavigationOperationRejection.ValidationFailed;
        }

        NavigationMapOverlayState overlay = replacementPolicy == OverlayReplacementPolicy.Clear || !replacing
            ? NavigationMapOverlayState.Empty
            : current!.Overlay;
        PersistentVoxelIndexMap<byte> dynamicAddresses = replacing
            && replacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                ? current!.DynamicAddresses
                : PersistentVoxelIndexMap<byte>.Empty;
        for (int dynamicIndex = 0; dynamicIndex < dynamicAddresses.Count; dynamicIndex++)
        {
            if (prepared.Map.ContainsCell(dynamicAddresses.GetKeyAt(dynamicIndex)))
                return NavigationOperationRejection.ValidationFailed;
        }

        if (prepared.CheckpointStamp.HasValue)
        {
            NavigationMapCheckpointStamp stamp = prepared.CheckpointStamp.Value;
            if (!replacing
                || current!.BakeVersion != stamp.BakeVersion
                || current.Overlay.HighWaterSequence != stamp.OverlayHighWaterSequence)
            {
                return NavigationOperationRejection.Stale;
            }
        }

        if (HasOversizedCorridor(prepared.Map, overlay, limits.MaxCorridorCells))
            return NavigationOperationRejection.CapacityExceeded;

        var next = new MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            overlay,
            replacing && replacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate
                ? current!.DynamicSlotGeneration
                : replacing
                    ? checked(current!.DynamicSlotGeneration + 1)
                    : 0,
            dynamicAddresses,
            prepared.BakedCellLookup);
        string[] changedMapIds = new[] { mapId };
        MapState[] changedStates = new[] { next };
        for (int connectionIndex = 0; connectionIndex < prepared.Map.Connections.Count; connectionIndex++)
        {
            if (!ValidateConnection(
                    next,
                    prepared.Map.Connections[connectionIndex],
                    changedMapIds,
                    changedStates,
                    corridorPrisms,
                    corridorWaypoints,
                    allowDormantEndpoints: false,
                    useAuthoredFallback: true))
                return NavigationOperationRejection.ValidationFailed;
        }
        for (int transitionIndex = 0; transitionIndex < prepared.Map.Transitions.Count; transitionIndex++)
        {
            if (!ValidateTransition(
                    next,
                    prepared.Map.Transitions[transitionIndex],
                    changedMapIds,
                    changedStates,
                    allowDormantEndpoints: false,
                    useAuthoredFallback: true))
                return NavigationOperationRejection.ValidationFailed;
        }
        if (!ValidateCandidate(
                changedMapIds,
                changedStates,
                corridorPrisms,
                corridorWaypoints,
                allowDormantEndpoints: replacing
                    && replacementPolicy == OverlayReplacementPolicy.PreserveAndRevalidate))
            return NavigationOperationRejection.ValidationFailed;

        SetMapState(replacing ? current : null, next);
        if (replacing && !current!.Map.GridBinding.Key.Equals(prepared.Map.GridBinding.Key))
            _gridBindings = _gridBindings.Remove(current.Map.GridBinding.Key);
        _gridBindings = _gridBindings.Set(prepared.Map.GridBinding.Key, mapId);
        UpdateIncomingDependencies(replacing ? current : null, next);
        ReplaceTotals(replacing ? current : null, next);
        _bakeVersionHighWater = _bakeVersionHighWater.Set(mapId, prepared.BakeVersion);
        return NavigationOperationRejection.None;
    }

    internal NavigationOperationRejection RemoveMap(string mapId)
    {
        _maps.TryGetValue(mapId, out MapState? current);
        _maps = _maps.Remove(mapId, out bool removed);
        if (removed)
        {
            RemoveMapStateTotals(current!);
            _gridBindings = _gridBindings.Remove(current!.Map.GridBinding.Key);
            UpdateIncomingDependencies(current, next: null);
            ReplaceTotals(current, next: null);
        }
        return removed ? NavigationOperationRejection.None : NavigationOperationRejection.MissingMap;
    }

    internal NavigationOperationRejection ApplyOverlay(
        NavigationOverlayTransaction transaction,
        long operationSequence,
        NavigationOperationLimits limits,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        ReadOnlySpan<NavigationMapOverlayDelta> deltas = transaction.MapSpan;
        var nextStates = new MapState[deltas.Length];
        var nextMapIds = new string[deltas.Length];
        long candidateCellCount = _overlaySlotCount;
        long candidateConnectionCount = _overlayConnectionCount;
        long candidateTransitionCount = _overlayTransitionCount;

        for (int i = 0; i < deltas.Length; i++)
        {
            NavigationMapOverlayDelta delta = deltas[i];
            if (!_maps.TryGetValue(delta.MapId, out MapState? current))
                return NavigationOperationRejection.MissingMap;

            for (int cellIndex = 0; cellIndex < delta.Cells.Count; cellIndex++)
            {
                NavigationCellOverlayOperation operation = delta.Cells[cellIndex];
                if (operation.Kind == NavigationCellOverlayOperationKind.Set
                    && operation.Cell.Area.Value >= _navigationAreaCount)
                {
                    return NavigationOperationRejection.ValidationFailed;
                }
            }

            NavigationMapOverlayState nextOverlay = current.Overlay.Apply(delta, operationSequence);
            PersistentVoxelIndexMap<byte> dynamicAddresses = current.DynamicAddresses;
            for (int cellIndex = 0; cellIndex < delta.Cells.Count; cellIndex++)
            {
                NavigationCellOverlayOperation operation = delta.Cells[cellIndex];
                if (operation.Kind == NavigationCellOverlayOperationKind.Set
                    && !current.Map.ContainsCell(operation.Index))
                {
                    dynamicAddresses = dynamicAddresses.Set(operation.Index, 0);
                }
            }
            if (HasOversizedCorridor(current.Map, nextOverlay, limits.MaxCorridorCells))
                return NavigationOperationRejection.CapacityExceeded;
            if (nextOverlay.CellCount > limits.MaxOverlayCellsPerMap
                || nextOverlay.ConnectionCount > limits.MaxOverlayConnectionsPerMap
                || nextOverlay.TransitionCount > limits.MaxOverlayTransitionsPerMap)
            {
                return NavigationOperationRejection.CapacityExceeded;
            }

            candidateCellCount += nextOverlay.CellCount - current.Overlay.CellCount;
            candidateConnectionCount += nextOverlay.ConnectionCount - current.Overlay.ConnectionCount;
            candidateTransitionCount += nextOverlay.TransitionCount - current.Overlay.TransitionCount;
            nextMapIds[i] = delta.MapId;
            nextStates[i] = new MapState(
                current.Map,
                current.BakeVersion,
                current.PreparedMapRetainedBytes,
                nextOverlay,
                current.DynamicSlotGeneration,
                dynamicAddresses,
                current.BakedCellLookup);
        }

        if (candidateCellCount > limits.MaxOverlayCells
            || candidateConnectionCount > limits.MaxOverlayConnections
            || candidateTransitionCount > limits.MaxOverlayTransitions)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }

        for (int i = 0; i < deltas.Length; i++)
        {
            ReadOnlySpan<NavigationConnectionOverlayOperation> connections = deltas[i].ConnectionSpan;
            for (int connectionIndex = 0; connectionIndex < connections.Length; connectionIndex++)
            {
                NavigationConnectionOverlayOperation operation = connections[connectionIndex];
                if (operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
                    && !ValidateConnection(
                        nextStates[i],
                        operation.Connection!,
                        nextMapIds,
                        nextStates,
                        corridorPrisms,
                        corridorWaypoints,
                        allowDormantEndpoints: false))
                {
                    return NavigationOperationRejection.ValidationFailed;
                }
            }

            ReadOnlySpan<TraversalTransitionOverlayOperation> transitions = deltas[i].TransitionSpan;
            for (int transitionIndex = 0; transitionIndex < transitions.Length; transitionIndex++)
            {
                TraversalTransitionOverlayOperation operation = transitions[transitionIndex];
                if (operation.Kind == TraversalTransitionOverlayOperationKind.Upsert
                    && !ValidateTransition(
                        nextStates[i],
                        operation.Transition,
                        nextMapIds,
                        nextStates,
                        allowDormantEndpoints: false))
                {
                    return NavigationOperationRejection.ValidationFailed;
                }
            }
        }

        if (!ValidateCandidate(
                nextMapIds,
                nextStates,
                corridorPrisms,
                corridorWaypoints,
                allowDormantEndpoints: true))
            return NavigationOperationRejection.ValidationFailed;

        for (int i = 0; i < deltas.Length; i++)
        {
            _maps.TryGetValue(deltas[i].MapId, out MapState? current);
            UpdateIncomingDependencies(current, nextStates[i]);
            ReplaceTotals(current, nextStates[i]);
            SetMapState(current, nextStates[i]);
        }

        return NavigationOperationRejection.None;
    }

    private void SetMapState(MapState? previous, MapState next)
    {
        if (previous != null)
            RemoveMapStateTotals(previous);
        AddMapStateTotals(next);
        _maps = _maps.Set(next.Map.MapId, next, out int copiedNodes);
        RecordPersistentCopies(copiedNodes);
    }

    private void AddMapStateTotals(MapState state)
    {
        _mapStateRetainedBytes = checked(
            _mapStateRetainedBytes
            + state.PreparedMapRetainedBytes
            + state.Overlay.RetainedBytes
            + state.DynamicAddresses.RetainedBytes);
        _mapStatePersistentPages = checked(
            _mapStatePersistentPages
            + state.Overlay.PersistentNodeCount
            + state.DynamicAddresses.PersistentNodeCount);
    }

    private void RemoveMapStateTotals(MapState state)
    {
        _mapStateRetainedBytes = checked(
            _mapStateRetainedBytes
            - state.PreparedMapRetainedBytes
            - state.Overlay.RetainedBytes
            - state.DynamicAddresses.RetainedBytes);
        _mapStatePersistentPages = checked(
            _mapStatePersistentPages
            - state.Overlay.PersistentNodeCount
            - state.DynamicAddresses.PersistentNodeCount);
    }

    private void ReplaceTotals(MapState? previous, MapState? next)
    {
        if (previous != null)
        {
            _overlaySlotCount -= previous.Overlay.CellCount;
            _overlayConnectionCount -= previous.Overlay.ConnectionCount;
            _overlayTransitionCount -= previous.Overlay.TransitionCount;
            long previousConnections = previous.Map.ConnectionSpan.Length
                + previous.Overlay.ConnectionCount;
            _seamCandidateCount -= previousConnections;
            _explicitEdgeCount -= previousConnections
                + previous.Map.TransitionSpan.Length
                + previous.Overlay.TransitionCount;
            _dynamicCellCount = checked(_dynamicCellCount - previous.DynamicAddresses.Count);
        }

        if (next != null)
        {
            _overlaySlotCount += next.Overlay.CellCount;
            _overlayConnectionCount += next.Overlay.ConnectionCount;
            _overlayTransitionCount += next.Overlay.TransitionCount;
            long nextConnections = next.Map.ConnectionSpan.Length
                + next.Overlay.ConnectionCount;
            _seamCandidateCount += nextConnections;
            _explicitEdgeCount += nextConnections
                + next.Map.TransitionSpan.Length
                + next.Overlay.TransitionCount;
            _dynamicCellCount = checked(_dynamicCellCount + next.DynamicAddresses.Count);
        }
    }

    private static bool HasOversizedCorridor(
        NavigationMap map,
        NavigationMapOverlayState overlay,
        int maxCorridorCells)
    {
        for (int i = 0; i < map.Connections.Count; i++)
        {
            if (map.Connections[i].Witnesses.Count > maxCorridorCells - 2)
                return true;
        }
        for (int i = 0; i < overlay.ConnectionCount; i++)
        {
            NavigationConnectionOverlayOperation operation = overlay.GetConnectionAt(i);
            if (operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
                && operation.Connection!.Witnesses.Count > maxCorridorCells - 2)
            {
                return true;
            }
        }

        return false;
    }

    private bool ValidateCandidate(
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        bool allowDormantEndpoints)
    {
        for (int changedIndex = 0; changedIndex < changedMapIds.Length; changedIndex++)
        {
            MapState state = changedStates[changedIndex];
            if (!ValidateState(
                    state,
                    changedMapIds,
                    changedStates,
                    corridorPrisms,
                    corridorWaypoints,
                    allowDormantEndpoints))
                return false;

            if (!_incomingSources.TryGetValue(
                    changedMapIds[changedIndex],
                    out PersistentStringMap<bool> sources))
                continue;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                string sourceId = sources.GetKeyAt(sourceIndex);
                MapState? source = FindChangedState(sourceId, changedMapIds, changedStates);
                if (source == null && !_maps.TryGetValue(sourceId, out source))
                    continue;
                if (!ValidateState(
                        source,
                        changedMapIds,
                        changedStates,
                        corridorPrisms,
                        corridorWaypoints,
                        allowDormantEndpoints))
                    return false;
            }
        }

        return true;
    }

    private void UpdateIncomingDependencies(MapState? previous, MapState? next)
    {
        if (previous != null)
            VisitDestinationMapIds(previous, remove: true);
        if (next != null)
            VisitDestinationMapIds(next, remove: false);
    }

    private void VisitDestinationMapIds(MapState state, bool remove)
    {
        for (int i = 0; i < state.Map.Transitions.Count; i++)
            UpdateIncomingSource(state.Map.Transitions[i].Destination.MapId, state.Map.MapId, remove);
        for (int i = 0; i < state.Overlay.TransitionCount; i++)
        {
            TraversalTransitionOverlayOperation operation = state.Overlay.GetTransitionAt(i);
            if (operation.Kind == TraversalTransitionOverlayOperationKind.Upsert)
                UpdateIncomingSource(operation.Transition.Destination.MapId, state.Map.MapId, remove);
        }
    }

    private void UpdateIncomingSource(string destinationMapId, string sourceMapId, bool remove)
    {
        bool hadSources = _incomingSources.TryGetValue(
            destinationMapId,
            out PersistentStringMap<bool> sources);
        sources ??= PersistentStringMap<bool>.Empty;
        long previousBytes = hadSources ? sources.RetainedBytes : 0;
        int previousPages = hadSources ? sources.PersistentNodeCount : 0;
        if (remove)
        {
            sources = sources.Remove(sourceMapId, out bool removed, out int copiedNodes);
            if (!removed)
                return;
            RecordPersistentCopies(copiedNodes);
            if (sources.Count == 0)
            {
                _incomingSources = _incomingSources.Remove(
                    destinationMapId,
                    out _,
                    out copiedNodes);
            }
            else
            {
                _incomingSources = _incomingSources.Set(
                    destinationMapId,
                    sources,
                    out copiedNodes);
            }
            RecordPersistentCopies(copiedNodes);
        }
        else
        {
            if (sources.ContainsKey(sourceMapId))
                return;
            sources = sources.Set(sourceMapId, true, out int copiedNodes);
            RecordPersistentCopies(copiedNodes);
            _incomingSources = _incomingSources.Set(
                destinationMapId,
                sources,
                out copiedNodes);
            RecordPersistentCopies(copiedNodes);
        }
        long nextBytes = sources.Count == 0 ? 0 : sources.RetainedBytes;
        int nextPages = sources.Count == 0 ? 0 : sources.PersistentNodeCount;
        _incomingSetRetainedBytes = checked(
            _incomingSetRetainedBytes - previousBytes + nextBytes);
        _incomingSetPersistentPages = checked(
            _incomingSetPersistentPages - previousPages + nextPages);
    }

    private bool ValidateState(
        MapState state,
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        bool allowDormantEndpoints)
    {
        NavigationMap map = state.Map;
        NavigationMapOverlayState overlay = state.Overlay;
        for (int i = 0; i < overlay.CellCount; i++)
        {
            if (!map.GridBinding.IsValidIndex(overlay.GetCellAt(i).Index))
                return false;
        }

        for (int i = 0; i < map.Connections.Count; i++)
        {
            NavigationConnection connection = map.Connections[i];
            if (TryFindConnectionOverlay(overlay, connection.Id, out NavigationConnectionOverlayOperation change))
            {
                if (change.Kind != NavigationConnectionOverlayOperationKind.RevertToBake)
                    continue;
            }

            if (!ValidateConnection(
                    state,
                    connection,
                    changedMapIds,
                    changedStates,
                    corridorPrisms,
                    corridorWaypoints,
                    allowDormantEndpoints))
                return false;
        }

        for (int i = 0; i < overlay.ConnectionCount; i++)
        {
            NavigationConnectionOverlayOperation operation = overlay.GetConnectionAt(i);
            if (operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
                && !ValidateConnection(
                    state,
                    operation.Connection!,
                    changedMapIds,
                    changedStates,
                    corridorPrisms,
                    corridorWaypoints,
                    allowDormantEndpoints))
            {
                return false;
            }
        }

        for (int i = 0; i < map.Transitions.Count; i++)
        {
            TraversalTransitionDefinition transition = map.Transitions[i];
            if (TryFindTransitionOverlay(overlay, transition.Id, out TraversalTransitionOverlayOperation change))
            {
                if (change.Kind != TraversalTransitionOverlayOperationKind.RevertToBake)
                    continue;
            }

            if (!ValidateTransition(
                    state,
                    transition,
                    changedMapIds,
                    changedStates,
                    allowDormantEndpoints))
                return false;
        }

        for (int i = 0; i < overlay.TransitionCount; i++)
        {
            TraversalTransitionOverlayOperation operation = overlay.GetTransitionAt(i);
            if (operation.Kind == TraversalTransitionOverlayOperationKind.Upsert
                && !ValidateTransition(
                    state,
                    operation.Transition,
                    changedMapIds,
                    changedStates,
                    allowDormantEndpoints))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateConnection(
        MapState source,
        NavigationConnection connection,
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints,
        bool allowDormantEndpoints,
        bool useAuthoredFallback = false)
    {
        if (!TryGetValidationCell(
                source,
                connection.SourceIndex,
                useAuthoredFallback,
                out NavigationCell sourceCell))
        {
            return allowDormantEndpoints
                && IsKnownSuppressed(source, connection.SourceIndex);
        }
        if (connection.PortalRadiusClearance > sourceCell.RadiusClearance
            || connection.PortalHeightClearance > sourceCell.HeightClearance
            || !source.Map.GridBinding.TryGetCellPrism(connection.SourceIndex, out GridForge.Grids.Topology.GridCellPrism sourcePrism)
            || !sourcePrism.Contains(connection.EntryAnchor))
        {
            return false;
        }

        if (!TryValidateConnectionAddress(
                connection.Destination,
                connection.PortalRadiusClearance,
                connection.PortalHeightClearance,
                connection.ExitAnchor,
                validateAnchor: true,
                changedMapIds,
                changedStates,
                allowDormantEndpoints,
                useAuthoredFallback,
                out bool destinationDormant))
        {
            return false;
        }
        if (destinationDormant)
            return true;

        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            if (!TryValidateConnectionAddress(
                    connection.Witnesses[i],
                    connection.PortalRadiusClearance,
                    connection.PortalHeightClearance,
                    anchor: default,
                    validateAnchor: false,
                    changedMapIds,
                    changedStates,
                    allowDormantEndpoints,
                    useAuthoredFallback,
                    out bool witnessDormant))
            {
                return false;
            }
            if (witnessDormant)
                return true;
        }

        return TryValidateCorridor(
            source,
            connection,
            changedMapIds,
            changedStates,
            corridorPrisms,
            corridorWaypoints);
    }

    private bool TryValidateCorridor(
        MapState source,
        NavigationConnection connection,
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        int prismCount = connection.Witnesses.Count + 2;
        if (prismCount > corridorPrisms.Length)
            return false;
        if (!source.Map.GridBinding.TryGetCellPrism(connection.SourceIndex, out corridorPrisms[0]))
            return false;

        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            NavigationCellAddress witness = connection.Witnesses[i];
            MapState? witnessMap = FindChangedState(witness.MapId, changedMapIds, changedStates);
            if (witnessMap == null && !_maps.TryGetValue(witness.MapId, out witnessMap))
                return true;
            if (!witnessMap.Map.GridBinding.TryGetCellPrism(witness.Index, out corridorPrisms[i + 1]))
                return false;
        }

        MapState? destination = FindChangedState(connection.Destination.MapId, changedMapIds, changedStates);
        if (destination == null && !_maps.TryGetValue(connection.Destination.MapId, out destination))
            return true;
        if (!destination.Map.GridBinding.TryGetCellPrism(connection.Destination.Index, out corridorPrisms[prismCount - 1]))
            return false;

        bool valid = GridCellGeometry.TryValidateNavigationCorridor(
            corridorPrisms.AsSpan(0, prismCount),
            connection.EntryAnchor,
            connection.ExitAnchor,
            connection.PortalRadiusClearance,
            connection.PortalHeightClearance,
            corridorWaypoints.AsSpan(0, (prismCount - 1) * 2),
            out _,
            out Fixed64 corridorCost);
        if (!valid || !connection.IsLowerBoundCertified)
            return valid;

        if (!TryGetValidationCell(
                destination,
                connection.Destination.Index,
                useAuthoredFallback: true,
                out NavigationCell destinationCell))
            return false;

        Vector3d sourceAnchor = GetFootAnchor(corridorPrisms[0]);
        Vector3d destinationAnchor = GetFootAnchor(corridorPrisms[prismCount - 1]);
        return Vector3d.TryGetDistance(sourceAnchor, connection.EntryAnchor, out Fixed64 approachCost)
            && Vector3d.TryGetDistance(connection.ExitAnchor, destinationAnchor, out Fixed64 departureCost)
            && Fixed64.TryAdd(approachCost, corridorCost, out Fixed64 traversalCost)
            && Fixed64.TryAdd(traversalCost, departureCost, out traversalCost)
            && Fixed64.TryAdd(traversalCost, connection.AdditionalCost, out traversalCost)
            && Fixed64.TryAdd(traversalCost, destinationCell.EnterCost, out traversalCost)
            && Vector3d.TryGetDistance(sourceAnchor, destinationAnchor, out Fixed64 directCost)
            && traversalCost >= directCost;
    }

    private static Vector3d GetFootAnchor(in GridCellPrism prism) =>
        new(prism.Center.X, prism.VerticalMin, prism.Center.Z);

    private bool ValidateTransition(
        MapState source,
        TraversalTransitionDefinition transition,
        string[] changedMapIds,
        MapState[] changedStates,
        bool allowDormantEndpoints,
        bool useAuthoredFallback = false)
    {
        if (!TryGetValidationCell(
                source,
                transition.SourceIndex,
                useAuthoredFallback,
                out NavigationCell sourceCell))
            return allowDormantEndpoints;
        if (!SupportsMedium(sourceCell, transition.SourceMedium))
            return false;

        if (transition.HasSourcePointOverride
            && (!source.Map.GridBinding.TryGetCellPrism(transition.SourceIndex, out GridForge.Grids.Topology.GridCellPrism sourcePrism)
                || !sourcePrism.Contains(transition.SourcePointOverride)))
        {
            return false;
        }

        MapState? destination = FindChangedState(transition.Destination.MapId, changedMapIds, changedStates);
        if (destination == null && !_maps.TryGetValue(transition.Destination.MapId, out destination))
            return true;
        if (!TryGetValidationCell(
                destination,
                transition.Destination.Index,
                useAuthoredFallback,
                out NavigationCell destinationCell))
            return allowDormantEndpoints;
        if (!SupportsMedium(destinationCell, transition.DestinationMedium))
            return false;

        return !transition.HasDestinationPointOverride
            || (destination.Map.GridBinding.TryGetCellPrism(transition.Destination.Index, out GridForge.Grids.Topology.GridCellPrism destinationPrism)
                && destinationPrism.Contains(transition.DestinationPointOverride));
    }

    private bool TryValidateConnectionAddress(
        NavigationCellAddress address,
        FixedMathSharp.Fixed64 radius,
        FixedMathSharp.Fixed64 height,
        FixedMathSharp.Vector3d anchor,
        bool validateAnchor,
        string[] changedMapIds,
        MapState[] changedStates,
        bool allowDormantEndpoints,
        bool useAuthoredFallback,
        out bool dormant)
    {
        dormant = false;
        MapState? target = FindChangedState(address.MapId, changedMapIds, changedStates);
        if (target == null && !_maps.TryGetValue(address.MapId, out target))
        {
            dormant = true;
            return true;
        }

        if (!TryGetValidationCell(target, address.Index, useAuthoredFallback, out NavigationCell cell))
        {
            dormant = allowDormantEndpoints
                && IsKnownSuppressed(target, address.Index);
            return dormant;
        }
        if (radius > cell.RadiusClearance
            || height > cell.HeightClearance)
        {
            return false;
        }

        return !validateAnchor
            || (target.Map.GridBinding.TryGetCellPrism(address.Index, out GridForge.Grids.Topology.GridCellPrism prism)
                && prism.Contains(anchor));
    }

    private static bool TryGetEffectiveCell(
        MapState state,
        GridForge.Spatial.VoxelIndex index,
        out NavigationCell cell)
    {
        if (TryFindCellOverlay(state.Overlay, index, out NavigationCellOverlayOperation operation))
        {
            cell = operation.Cell;
            return operation.Kind == NavigationCellOverlayOperationKind.Set;
        }

        int low = 0;
        int high = state.Map.Cells.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = state.Map.Cells[middle].Index.CompareTo(index);
            if (comparison == 0)
            {
                cell = state.Map.Cells[middle].Cell;
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        cell = default;
        return false;
    }

    private static bool TryGetValidationCell(
        MapState state,
        GridForge.Spatial.VoxelIndex index,
        bool useAuthoredFallback,
        out NavigationCell cell)
    {
        if (TryGetEffectiveCell(state, index, out cell))
            return true;
        if (useAuthoredFallback)
        {
            int baked = state.Map.FindCellIndex(index);
            if (baked >= 0)
            {
                cell = state.Map.Cells[baked].Cell;
                return true;
            }
        }
        cell = default;
        return false;
    }

    private static bool SupportsMedium(NavigationCell cell, TraversalMedium medium) => medium switch
    {
        TraversalMedium.Solid => (cell.Media & TraversalMedia.Solid) != 0,
        TraversalMedium.Gas => (cell.Media & TraversalMedia.Gas) != 0,
        TraversalMedium.Liquid => (cell.Media & TraversalMedia.Liquid) != 0,
        _ => false
    };

    private static MapState? FindChangedState(
        string mapId,
        string[] changedMapIds,
        MapState[] changedStates)
    {
        int low = 0;
        int high = changedMapIds.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(changedMapIds[middle], mapId);
            if (comparison == 0)
                return changedStates[middle];
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return null;
    }

    private static bool TryFindCellOverlay(
        NavigationMapOverlayState overlay,
        GridForge.Spatial.VoxelIndex index,
        out NavigationCellOverlayOperation operation)
        => overlay.TryGetCell(index, out operation);

    private static bool TryFindConnectionOverlay(
        NavigationMapOverlayState overlay,
        string id,
        out NavigationConnectionOverlayOperation operation)
        => overlay.TryGetConnection(id, out operation);

    private static bool TryFindTransitionOverlay(
        NavigationMapOverlayState overlay,
        string id,
        out TraversalTransitionOverlayOperation operation)
        => overlay.TryGetTransition(id, out operation);

    internal sealed class MapState
    {
        internal MapState(
            NavigationMap map,
            long bakeVersion,
            long preparedMapRetainedBytes,
            NavigationMapOverlayState overlay,
            long dynamicSlotGeneration,
            PersistentVoxelIndexMap<byte>? dynamicAddresses = null,
            NavigationBakedCellLookup? bakedCellLookup = null)
        {
            Map = map;
            BakeVersion = bakeVersion;
            PreparedMapRetainedBytes = preparedMapRetainedBytes;
            Overlay = overlay;
            DynamicSlotGeneration = dynamicSlotGeneration;
            DynamicAddresses = dynamicAddresses ?? PersistentVoxelIndexMap<byte>.Empty;
            BakedCellLookup = bakedCellLookup ?? NavigationBakedCellLookup.Create(map);
        }

        internal NavigationMap Map { get; }

        internal long BakeVersion { get; }

        internal long PreparedMapRetainedBytes { get; }

        internal NavigationMapOverlayState Overlay { get; }

        internal long DynamicSlotGeneration { get; }

        internal PersistentVoxelIndexMap<byte> DynamicAddresses { get; }

        internal NavigationBakedCellLookup BakedCellLookup { get; }
    }
}
