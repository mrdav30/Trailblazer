//=======================================================================
// NavigationOperationCandidate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;
using SwiftCollections.Utility;
using System;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

internal sealed class NavigationOperationCandidate
{
    private readonly SwiftDictionary<string, MapState> _maps = new(
        SwiftDictionary<string, MapState>.DefaultCapacity,
        SwiftHashTools.GetDeterministicStringEqualityComparer());
    private readonly SwiftDictionary<string, long> _bakeVersionHighWater = new(
        SwiftDictionary<string, long>.DefaultCapacity,
        SwiftHashTools.GetDeterministicStringEqualityComparer());

    internal int MapCount => _maps.Count;

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

        foreach (MapState state in _maps.Values)
        {
            if (!string.Equals(state.Map.MapId, mapId, StringComparison.Ordinal)
                && state.Map.GridBinding.Key.Equals(prepared.Map.GridBinding.Key))
            {
                return NavigationOperationRejection.ValidationFailed;
            }
        }

        NavigationMapOverlayState overlay = replacementPolicy == OverlayReplacementPolicy.Clear || !replacing
            ? NavigationMapOverlayState.Empty
            : current!.Overlay;

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

        var next = new MapState(prepared.Map, prepared.BakeVersion, overlay);
        if (!ValidateCandidate(
                new[] { mapId },
                new[] { next },
                corridorPrisms,
                corridorWaypoints))
            return NavigationOperationRejection.ValidationFailed;

        _maps[mapId] = next;
        _bakeVersionHighWater[mapId] = prepared.BakeVersion;
        return NavigationOperationRejection.None;
    }

    internal NavigationOperationRejection RemoveMap(string mapId)
    {
        return _maps.Remove(mapId)
            ? NavigationOperationRejection.None
            : NavigationOperationRejection.MissingMap;
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
        long candidateCellCount = 0;
        long candidateConnectionCount = 0;
        long candidateTransitionCount = 0;

        foreach (MapState state in _maps.Values)
        {
            candidateCellCount += state.Overlay.Cells.Length;
            candidateConnectionCount += state.Overlay.Connections.Length;
            candidateTransitionCount += state.Overlay.Transitions.Length;
        }

        for (int i = 0; i < deltas.Length; i++)
        {
            NavigationMapOverlayDelta delta = deltas[i];
            if (!_maps.TryGetValue(delta.MapId, out MapState? current))
                return NavigationOperationRejection.MissingMap;

            NavigationMapOverlayState nextOverlay = current.Overlay.Apply(delta, operationSequence);
            if (HasOversizedCorridor(current.Map, nextOverlay, limits.MaxCorridorCells))
                return NavigationOperationRejection.CapacityExceeded;
            if (nextOverlay.Cells.Length > limits.MaxOverlayCellsPerMap
                || nextOverlay.Connections.Length > limits.MaxOverlayConnectionsPerMap
                || nextOverlay.Transitions.Length > limits.MaxOverlayTransitionsPerMap)
            {
                return NavigationOperationRejection.CapacityExceeded;
            }

            candidateCellCount += nextOverlay.Cells.Length - current.Overlay.Cells.Length;
            candidateConnectionCount += nextOverlay.Connections.Length - current.Overlay.Connections.Length;
            candidateTransitionCount += nextOverlay.Transitions.Length - current.Overlay.Transitions.Length;
            nextMapIds[i] = delta.MapId;
            nextStates[i] = new MapState(current.Map, current.BakeVersion, nextOverlay);
        }

        if (candidateCellCount > limits.MaxOverlayCells
            || candidateConnectionCount > limits.MaxOverlayConnections
            || candidateTransitionCount > limits.MaxOverlayTransitions)
        {
            return NavigationOperationRejection.CapacityExceeded;
        }

        if (!ValidateCandidate(nextMapIds, nextStates, corridorPrisms, corridorWaypoints))
            return NavigationOperationRejection.ValidationFailed;

        for (int i = 0; i < deltas.Length; i++)
            _maps[deltas[i].MapId] = nextStates[i];

        return NavigationOperationRejection.None;
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
        for (int i = 0; i < overlay.Connections.Length; i++)
        {
            NavigationConnectionOverlayOperation operation = overlay.Connections[i];
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
        Vector3d[] corridorWaypoints)
    {
        bool appendedNewMap = false;
        foreach (MapState published in _maps.Values)
        {
            MapState state = FindChangedState(published.Map.MapId, changedMapIds, changedStates) ?? published;
            if (!ValidateState(
                    state,
                    changedMapIds,
                    changedStates,
                    corridorPrisms,
                    corridorWaypoints))
                return false;

            if (string.Equals(state.Map.MapId, changedMapIds[0], StringComparison.Ordinal))
                appendedNewMap = true;
        }

        if (!appendedNewMap && changedMapIds.Length == 1)
            return ValidateState(
                changedStates[0],
                changedMapIds,
                changedStates,
                corridorPrisms,
                corridorWaypoints);

        return true;
    }

    private bool ValidateState(
        MapState state,
        string[] changedMapIds,
        MapState[] changedStates,
        GridCellPrism[] corridorPrisms,
        Vector3d[] corridorWaypoints)
    {
        NavigationMap map = state.Map;
        NavigationMapOverlayState overlay = state.Overlay;
        for (int i = 0; i < overlay.Cells.Length; i++)
        {
            if (!map.GridBinding.IsValidIndex(overlay.Cells[i].Index))
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
                    corridorWaypoints))
                return false;
        }

        for (int i = 0; i < overlay.Connections.Length; i++)
        {
            NavigationConnectionOverlayOperation operation = overlay.Connections[i];
            if (operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
                && !ValidateConnection(
                    state,
                    operation.Connection!,
                    changedMapIds,
                    changedStates,
                    corridorPrisms,
                    corridorWaypoints))
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

            if (!ValidateTransition(state, transition, changedMapIds, changedStates))
                return false;
        }

        for (int i = 0; i < overlay.Transitions.Length; i++)
        {
            TraversalTransitionOverlayOperation operation = overlay.Transitions[i];
            if (operation.Kind == TraversalTransitionOverlayOperationKind.Upsert
                && !ValidateTransition(state, operation.Transition, changedMapIds, changedStates))
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
        Vector3d[] corridorWaypoints)
    {
        if (!TryGetEffectiveCell(source, connection.SourceIndex, out NavigationCell sourceCell)
            || connection.PortalRadiusClearance > sourceCell.RadiusClearance
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
                changedStates))
        {
            return false;
        }

        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            if (!TryValidateConnectionAddress(
                    connection.Witnesses[i],
                    connection.PortalRadiusClearance,
                    connection.PortalHeightClearance,
                    anchor: default,
                    validateAnchor: false,
                    changedMapIds,
                    changedStates))
            {
                return false;
            }
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

        if (!TryGetEffectiveCell(destination, connection.Destination.Index, out NavigationCell destinationCell))
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
        MapState[] changedStates)
    {
        if (!TryGetEffectiveCell(source, transition.SourceIndex, out NavigationCell sourceCell)
            || !SupportsMedium(sourceCell, transition.SourceMedium))
        {
            return false;
        }

        if (transition.HasSourcePointOverride
            && (!source.Map.GridBinding.TryGetCellPrism(transition.SourceIndex, out GridForge.Grids.Topology.GridCellPrism sourcePrism)
                || !sourcePrism.Contains(transition.SourcePointOverride)))
        {
            return false;
        }

        MapState? destination = FindChangedState(transition.Destination.MapId, changedMapIds, changedStates);
        if (destination == null && !_maps.TryGetValue(transition.Destination.MapId, out destination))
            return true;
        if (!TryGetEffectiveCell(destination, transition.Destination.Index, out NavigationCell destinationCell)
            || !SupportsMedium(destinationCell, transition.DestinationMedium))
        {
            return false;
        }

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
        MapState[] changedStates)
    {
        MapState? target = FindChangedState(address.MapId, changedMapIds, changedStates);
        if (target == null && !_maps.TryGetValue(address.MapId, out target))
            return true;

        if (!TryGetEffectiveCell(target, address.Index, out NavigationCell cell)
            || radius > cell.RadiusClearance
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
    {
        int low = 0;
        int high = overlay.Cells.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = overlay.Cells[middle].Index.CompareTo(index);
            if (comparison == 0)
            {
                operation = overlay.Cells[middle];
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        operation = default;
        return false;
    }

    private static bool TryFindConnectionOverlay(
        NavigationMapOverlayState overlay,
        string id,
        out NavigationConnectionOverlayOperation operation)
    {
        int low = 0;
        int high = overlay.Connections.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(overlay.Connections[middle].Id, id);
            if (comparison == 0)
            {
                operation = overlay.Connections[middle];
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        operation = default;
        return false;
    }

    private static bool TryFindTransitionOverlay(
        NavigationMapOverlayState overlay,
        string id,
        out TraversalTransitionOverlayOperation operation)
    {
        int low = 0;
        int high = overlay.Transitions.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(overlay.Transitions[middle].Id, id);
            if (comparison == 0)
            {
                operation = overlay.Transitions[middle];
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        operation = default;
        return false;
    }

    private sealed class MapState
    {
        internal MapState(NavigationMap map, long bakeVersion, NavigationMapOverlayState overlay)
        {
            Map = map;
            BakeVersion = bakeVersion;
            Overlay = overlay;
        }

        internal NavigationMap Map { get; }

        internal long BakeVersion { get; }

        internal NavigationMapOverlayState Overlay { get; }
    }
}
