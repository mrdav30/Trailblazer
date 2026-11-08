//=======================================================================
// NavigationMapOverlayState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Stores one map's immutable overlay roots in canonical key order.</summary>
internal sealed class NavigationMapOverlayState
{
    internal static readonly NavigationMapOverlayState Empty = new(
        PersistentVoxelIndexMap<NavigationCellOverlayOperation>.Empty,
        PersistentStringMap<NavigationConnectionOverlayOperation>.Empty,
        PersistentStringMap<TraversalTransitionOverlayOperation>.Empty,
        highWaterSequence: 0,
        lastApplyCopiedNodeCount: 0,
        retainedPayloadBytes: 0);

    private readonly PersistentVoxelIndexMap<NavigationCellOverlayOperation> _cells;
    private readonly PersistentStringMap<NavigationConnectionOverlayOperation> _connections;
    private readonly PersistentStringMap<TraversalTransitionOverlayOperation> _transitions;

    private NavigationMapOverlayState(
        PersistentVoxelIndexMap<NavigationCellOverlayOperation> cells,
        PersistentStringMap<NavigationConnectionOverlayOperation> connections,
        PersistentStringMap<TraversalTransitionOverlayOperation> transitions,
        long highWaterSequence,
        int lastApplyCopiedNodeCount,
        long retainedPayloadBytes)
    {
        _cells = cells;
        _connections = connections;
        _transitions = transitions;
        HighWaterSequence = highWaterSequence;
        LastApplyCopiedNodeCount = lastApplyCopiedNodeCount;
        RetainedPayloadBytes = retainedPayloadBytes;
    }

    internal int CellCount => _cells.Count;

    internal int ConnectionCount => _connections.Count;

    internal int TransitionCount => _transitions.Count;

    internal int PersistentNodeCount => checked(
        _cells.PersistentNodeCount
        + _connections.PersistentNodeCount
        + _transitions.PersistentNodeCount);

    internal long RetainedBytes => checked(
        48L
        + _cells.RetainedBytes
        + _connections.RetainedBytes
        + _transitions.RetainedBytes
        + RetainedPayloadBytes);

    internal long RetainedPayloadBytes { get; }

    internal int LastApplyCopiedNodeCount { get; }

    internal long HighWaterSequence { get; }

    internal NavigationCellOverlayOperation GetCellAt(int ordinal) =>
        _cells.GetValueAt(ordinal);

    internal NavigationConnectionOverlayOperation GetConnectionAt(int ordinal) =>
        _connections.GetValueAt(ordinal);

    internal TraversalTransitionOverlayOperation GetTransitionAt(int ordinal) =>
        _transitions.GetValueAt(ordinal);

    internal bool TryGetCell(VoxelIndex index, out NavigationCellOverlayOperation operation) =>
        _cells.TryGetValue(index, out operation);

    internal bool TryGetConnection(
        string id,
        out NavigationConnectionOverlayOperation operation) =>
        _connections.TryGetValue(id, out operation);

    internal bool TryGetTransition(
        string id,
        out TraversalTransitionOverlayOperation operation) =>
        _transitions.TryGetValue(id, out operation);

    internal NavigationMapOverlayState Apply(NavigationMapOverlayDelta delta, long operationSequence)
    {
        PersistentVoxelIndexMap<NavigationCellOverlayOperation> cells = _cells;
        PersistentStringMap<NavigationConnectionOverlayOperation> connections = _connections;
        PersistentStringMap<TraversalTransitionOverlayOperation> transitions = _transitions;
        int copiedNodeCount = 0;
        long retainedPayloadBytes = RetainedPayloadBytes;

        ReadOnlySpan<NavigationCellOverlayOperation> cellChanges = delta.CellSpan;
        for (int i = 0; i < cellChanges.Length; i++)
        {
            NavigationCellOverlayOperation change = cellChanges[i];
            if (cells.TryGetValue(change.Index, out NavigationCellOverlayOperation prior))
            {
                retainedPayloadBytes = checked(
                    retainedPayloadBytes
                    - NavigationMapOverlayDelta.EstimateRetainedPayload(prior));
            }
            int copied;
            if (change.Kind == NavigationCellOverlayOperationKind.RevertToBake)
                cells = cells.Remove(change.Index, out _, out copied);
            else
            {
                cells = cells.Set(change.Index, change, out copied);
                retainedPayloadBytes = checked(
                    retainedPayloadBytes
                    + NavigationMapOverlayDelta.EstimateRetainedPayload(change));
            }
            copiedNodeCount = checked(copiedNodeCount + copied);
        }

        ReadOnlySpan<NavigationConnectionOverlayOperation> connectionChanges = delta.ConnectionSpan;
        for (int i = 0; i < connectionChanges.Length; i++)
        {
            NavigationConnectionOverlayOperation change = connectionChanges[i];
            if (connections.TryGetValue(change.Id, out NavigationConnectionOverlayOperation prior))
            {
                retainedPayloadBytes = checked(
                    retainedPayloadBytes
                    - NavigationMapOverlayDelta.EstimateRetainedPayload(prior));
            }
            int copied;
            if (change.Kind == NavigationConnectionOverlayOperationKind.RevertToBake)
                connections = connections.Remove(change.Id, out _, out copied);
            else
            {
                connections = connections.Set(change.Id, change, out copied);
                retainedPayloadBytes = checked(
                    retainedPayloadBytes
                    + NavigationMapOverlayDelta.EstimateRetainedPayload(change));
            }
            copiedNodeCount = checked(copiedNodeCount + copied);
        }

        ReadOnlySpan<TraversalTransitionOverlayOperation> transitionChanges = delta.TransitionSpan;
        for (int i = 0; i < transitionChanges.Length; i++)
        {
            TraversalTransitionOverlayOperation change = transitionChanges[i];
            if (transitions.TryGetValue(change.Id, out TraversalTransitionOverlayOperation prior))
            {
                retainedPayloadBytes = checked(
                    retainedPayloadBytes
                    - NavigationMapOverlayDelta.EstimateRetainedPayload(prior));
            }
            int copied;
            if (change.Kind == TraversalTransitionOverlayOperationKind.RevertToBake)
                transitions = transitions.Remove(change.Id, out _, out copied);
            else
            {
                transitions = transitions.Set(change.Id, change, out copied);
                retainedPayloadBytes = checked(
                    retainedPayloadBytes
                    + NavigationMapOverlayDelta.EstimateRetainedPayload(change));
            }
            copiedNodeCount = checked(copiedNodeCount + copied);
        }

        return new NavigationMapOverlayState(
            cells,
            connections,
            transitions,
            operationSequence,
            copiedNodeCount,
            retainedPayloadBytes);
    }

    internal NavigationMapOverlayState Apply(
        NavigationCellOverlayOperation change,
        long operationSequence)
    {
        long retainedPayloadBytes = RetainedPayloadBytes;
        if (_cells.TryGetValue(change.Index, out NavigationCellOverlayOperation prior))
            retainedPayloadBytes -= NavigationMapOverlayDelta.EstimateRetainedPayload(prior);
        int copied;
        PersistentVoxelIndexMap<NavigationCellOverlayOperation> cells;
        if (change.Kind == NavigationCellOverlayOperationKind.RevertToBake)
            cells = _cells.Remove(change.Index, out _, out copied);
        else
        {
            cells = _cells.Set(change.Index, change, out copied);
            retainedPayloadBytes = checked(
                retainedPayloadBytes + NavigationMapOverlayDelta.EstimateRetainedPayload(change));
        }
        return new NavigationMapOverlayState(
            cells,
            _connections,
            _transitions,
            operationSequence,
            copied,
            retainedPayloadBytes);
    }

    internal NavigationMapOverlayState Apply(
        NavigationConnectionOverlayOperation change,
        long operationSequence)
    {
        long retainedPayloadBytes = RetainedPayloadBytes;
        if (_connections.TryGetValue(change.Id, out NavigationConnectionOverlayOperation prior))
            retainedPayloadBytes -= NavigationMapOverlayDelta.EstimateRetainedPayload(prior);
        int copied;
        PersistentStringMap<NavigationConnectionOverlayOperation> connections;
        if (change.Kind == NavigationConnectionOverlayOperationKind.RevertToBake)
            connections = _connections.Remove(change.Id, out _, out copied);
        else
        {
            connections = _connections.Set(change.Id, change, out copied);
            retainedPayloadBytes = checked(
                retainedPayloadBytes + NavigationMapOverlayDelta.EstimateRetainedPayload(change));
        }
        return new NavigationMapOverlayState(
            _cells,
            connections,
            _transitions,
            operationSequence,
            copied,
            retainedPayloadBytes);
    }

    internal NavigationMapOverlayState Apply(
        TraversalTransitionOverlayOperation change,
        long operationSequence)
    {
        long retainedPayloadBytes = RetainedPayloadBytes;
        if (_transitions.TryGetValue(change.Id, out TraversalTransitionOverlayOperation prior))
            retainedPayloadBytes -= NavigationMapOverlayDelta.EstimateRetainedPayload(prior);
        int copied;
        PersistentStringMap<TraversalTransitionOverlayOperation> transitions;
        if (change.Kind == TraversalTransitionOverlayOperationKind.RevertToBake)
            transitions = _transitions.Remove(change.Id, out _, out copied);
        else
        {
            transitions = _transitions.Set(change.Id, change, out copied);
            retainedPayloadBytes = checked(
                retainedPayloadBytes + NavigationMapOverlayDelta.EstimateRetainedPayload(change));
        }
        return new NavigationMapOverlayState(
            _cells,
            _connections,
            transitions,
            operationSequence,
            copied,
            retainedPayloadBytes);
    }
}
