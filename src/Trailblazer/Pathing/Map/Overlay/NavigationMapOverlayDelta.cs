//=======================================================================
// NavigationMapOverlayDelta.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Contains the canonically ordered semantic overlay operations for one source map.
/// </summary>
public sealed class NavigationMapOverlayDelta
{
    private static readonly IComparer<NavigationCellOverlayOperation> CellComparer = new CellOperationComparer();
    private static readonly IComparer<NavigationConnectionOverlayOperation> ConnectionComparer = new ConnectionOperationComparer();
    private static readonly IComparer<TraversalTransitionOverlayOperation> TransitionComparer = new TransitionOperationComparer();

    private readonly NavigationCellOverlayOperation[] _cells;
    private readonly NavigationConnectionOverlayOperation[] _connections;
    private readonly TraversalTransitionOverlayOperation[] _transitions;
    private readonly ReadOnlyCollection<NavigationCellOverlayOperation> _cellView;
    private readonly ReadOnlyCollection<NavigationConnectionOverlayOperation> _connectionView;
    private readonly ReadOnlyCollection<TraversalTransitionOverlayOperation> _transitionView;

    /// <summary>
    /// Initializes an immutable delta and normalizes every operation collection into canonical key order.
    /// </summary>
    public NavigationMapOverlayDelta(
        string mapId,
        ReadOnlySpan<NavigationCellOverlayOperation> cells = default,
        ReadOnlySpan<NavigationConnectionOverlayOperation> connections = default,
        ReadOnlySpan<TraversalTransitionOverlayOperation> transitions = default)
    {
        SwiftThrowHelper.ThrowIfNull(mapId, nameof(mapId));
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(mapId),
            nameof(mapId),
            "Map id cannot be empty or whitespace.");
        SwiftThrowHelper.ThrowIfArgument(
            cells.IsEmpty && connections.IsEmpty && transitions.IsEmpty,
            nameof(cells),
            "A map overlay delta must contain at least one operation.");

        MapId = mapId;
        _cells = cells.ToArray();
        _connections = connections.ToArray();
        _transitions = transitions.ToArray();

        Array.Sort(_cells, CellComparer);
        Array.Sort(_connections, ConnectionComparer);
        Array.Sort(_transitions, TransitionComparer);

        ValidateCells(_cells);
        ValidateConnections(_connections);
        ValidateTransitions(_transitions);
        _cellView = Array.AsReadOnly(_cells);
        _connectionView = Array.AsReadOnly(_connections);
        _transitionView = Array.AsReadOnly(_transitions);
        try
        {
            EstimatedDescriptorBytes = EstimateDescriptorBytes();
        }
        catch (OverflowException)
        {
            SwiftThrowHelper.ThrowIfArgument(
                true,
                nameof(cells),
                "Overlay descriptor-byte accounting overflowed.");
        }
    }

    /// <summary>Gets the stable source map identifier.</summary>
    public string MapId { get; }

    /// <summary>Gets canonically ordered, unique cell operations.</summary>
    public IReadOnlyList<NavigationCellOverlayOperation> Cells => _cellView;

    /// <summary>Gets canonically ordered, unique connection operations.</summary>
    public IReadOnlyList<NavigationConnectionOverlayOperation> Connections => _connectionView;

    /// <summary>Gets canonically ordered, unique transition operations.</summary>
    public IReadOnlyList<TraversalTransitionOverlayOperation> Transitions => _transitionView;

    internal ReadOnlySpan<NavigationCellOverlayOperation> CellSpan => _cells;

    internal ReadOnlySpan<NavigationConnectionOverlayOperation> ConnectionSpan => _connections;

    internal ReadOnlySpan<TraversalTransitionOverlayOperation> TransitionSpan => _transitions;

    /// <summary>
    /// Gets a deterministic conservative byte count for admission of this submitted descriptor.
    /// It is not the retained runtime-overlay size.
    /// </summary>
    public long EstimatedDescriptorBytes { get; }

    private static void ValidateCells(NavigationCellOverlayOperation[] operations)
    {
        for (int i = 0; i < operations.Length; i++)
        {
            NavigationCellOverlayOperation.ValidateKind(operations[i].Kind);
            NavigationCell cell = operations[i].Cell;
            SwiftThrowHelper.ThrowIfArgument(
                operations[i].Kind == NavigationCellOverlayOperationKind.Set
                && (cell.Media == TraversalMedia.None
                    || (cell.Media & ~NavigationCell.KnownMedia) != 0
                    || (cell.RequiredCapabilities & ~NavigationCell.KnownCapabilities) != 0
                    || cell.EnterCost < FixedMathSharp.Fixed64.Zero
                    || cell.RadiusClearance < FixedMathSharp.Fixed64.Zero
                    || cell.HeightClearance < FixedMathSharp.Fixed64.Zero
                    || (cell.Flags & ~NavigationCell.KnownFlags) != 0),
                nameof(operations),
                "Cell Set requires one complete valid navigation-cell payload.");
            SwiftThrowHelper.ThrowIfArgument(
                i > 0 && operations[i - 1].Index.CompareTo(operations[i].Index) == 0,
                nameof(operations),
                "Cell overlay addresses must be unique.");
        }
    }

    private static void ValidateConnections(NavigationConnectionOverlayOperation[] operations)
    {
        for (int i = 0; i < operations.Length; i++)
        {
            NavigationConnectionOverlayOperation operation = operations[i];
            NavigationConnectionOverlayOperation.ValidateKind(operation.Kind);
            SwiftThrowHelper.ThrowIfArgument(
                string.IsNullOrWhiteSpace(operation.Id),
                nameof(operations),
                "Connection overlay ids cannot be empty.");
            SwiftThrowHelper.ThrowIfArgument(
                operation.Kind == NavigationConnectionOverlayOperationKind.Upsert
                && !string.Equals(operation.Id, operation.Connection?.Id, StringComparison.Ordinal),
                nameof(operations),
                "Connection Upsert key must match the complete definition id.");
            if (operation.Kind == NavigationConnectionOverlayOperationKind.Upsert)
            {
                SwiftThrowHelper.ThrowIfArgument(
                    string.IsNullOrWhiteSpace(operation.Connection!.Destination.MapId),
                    nameof(operations),
                    "Connection Upsert destination map id cannot be empty.");
                ValidateConnectionWitnesses(operation.Connection!, operations);
            }
            SwiftThrowHelper.ThrowIfArgument(
                i > 0 && string.Equals(operations[i - 1].Id, operations[i].Id, StringComparison.Ordinal),
                nameof(operations),
                "Connection overlay ids must be unique.");
        }
    }

    private static void ValidateConnectionWitnesses(
        NavigationConnection connection,
        NavigationConnectionOverlayOperation[] operations)
    {
        var witnesses = new SwiftHashSet<NavigationCellAddress>(connection.Witnesses.Count);
        for (int witness = 0; witness < connection.Witnesses.Count; witness++)
        {
            SwiftThrowHelper.ThrowIfArgument(
                string.IsNullOrWhiteSpace(connection.Witnesses[witness].MapId),
                nameof(operations),
                "Connection witness map ids cannot be empty.");
            SwiftThrowHelper.ThrowIfArgument(
                !witnesses.Add(connection.Witnesses[witness]),
                nameof(operations),
                "Connection witnesses must be unique.");
        }
    }

    private static void ValidateTransitions(TraversalTransitionOverlayOperation[] operations)
    {
        for (int i = 0; i < operations.Length; i++)
        {
            TraversalTransitionOverlayOperation.ValidateKind(operations[i].Kind);
            SwiftThrowHelper.ThrowIfArgument(
                string.IsNullOrWhiteSpace(operations[i].Id),
                nameof(operations),
                "Transition overlay ids cannot be empty.");
            SwiftThrowHelper.ThrowIfArgument(
                operations[i].Kind == TraversalTransitionOverlayOperationKind.Upsert
                && !string.Equals(operations[i].Id, operations[i].Transition.Id, StringComparison.Ordinal),
                nameof(operations),
                "Transition Upsert key must match the complete definition id.");
            SwiftThrowHelper.ThrowIfArgument(
                operations[i].Kind == TraversalTransitionOverlayOperationKind.Upsert
                && string.IsNullOrWhiteSpace(operations[i].Transition.Destination.MapId),
                nameof(operations),
                "Transition Upsert destination map id cannot be empty.");
            SwiftThrowHelper.ThrowIfArgument(
                i > 0 && string.Equals(operations[i - 1].Id, operations[i].Id, StringComparison.Ordinal),
                nameof(operations),
                "Transition overlay ids must be unique.");
        }
    }

    private long EstimateDescriptorBytes()
    {
        return checked(
            32L
            + (MapId.Length * sizeof(char))
            + ((long)_cells.Length * 64L)
            + EstimateIdOperations(_connections)
            + EstimateIdOperations(_transitions));
    }

    internal static long EstimateRetainedPayload(NavigationCellOverlayOperation operation) => 64L;

    private static long EstimateIdOperations(NavigationConnectionOverlayOperation[] operations)
    {
        long bytes = 0;
        for (int i = 0; i < operations.Length; i++)
            bytes = checked(bytes + EstimateRetainedPayload(operations[i]));
        return bytes;
    }

    private static long EstimateIdOperations(TraversalTransitionOverlayOperation[] operations)
    {
        long bytes = 0;
        for (int i = 0; i < operations.Length; i++)
            bytes = checked(bytes + EstimateRetainedPayload(operations[i]));
        return bytes;
    }

    internal static long EstimateRetainedPayload(NavigationConnectionOverlayOperation operation)
    {
        long bytes = checked(96L + (operation.Id.Length * sizeof(char)));
        if (operation.Kind != NavigationConnectionOverlayOperationKind.Upsert)
            return bytes;

        NavigationConnection connection = operation.Connection!;
        bytes = checked(bytes + 96L + (connection.Destination.MapId.Length * sizeof(char)));
        for (int witness = 0; witness < connection.Witnesses.Count; witness++)
        {
            bytes = checked(
                bytes
                + 32L
                + (connection.Witnesses[witness].MapId.Length * sizeof(char)));
        }
        return bytes;
    }

    internal static long EstimateRetainedPayload(TraversalTransitionOverlayOperation operation)
    {
        long bytes = checked(96L + (operation.Id.Length * sizeof(char)));
        if (operation.Kind == TraversalTransitionOverlayOperationKind.Upsert)
        {
            bytes = checked(
                bytes
                + 64L
                + (operation.Transition.Destination.MapId.Length * sizeof(char)));
        }
        return bytes;
    }

    private sealed class CellOperationComparer : IComparer<NavigationCellOverlayOperation>
    {
        public int Compare(NavigationCellOverlayOperation left, NavigationCellOverlayOperation right) =>
            left.Index.CompareTo(right.Index);
    }

    private sealed class ConnectionOperationComparer : IComparer<NavigationConnectionOverlayOperation>
    {
        public int Compare(NavigationConnectionOverlayOperation left, NavigationConnectionOverlayOperation right) =>
            string.CompareOrdinal(left.Id, right.Id);
    }

    private sealed class TransitionOperationComparer : IComparer<TraversalTransitionOverlayOperation>
    {
        public int Compare(TraversalTransitionOverlayOperation left, TraversalTransitionOverlayOperation right) =>
            string.CompareOrdinal(left.Id, right.Id);
    }
}
