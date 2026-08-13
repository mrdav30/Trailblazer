//=======================================================================
// PreparedNavigationMap.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Wraps an inert validated map bake before deterministic runtime admission.</summary>
public sealed class PreparedNavigationMap
{
    /// <summary>Creates an inert prepared-map descriptor from a validated immutable map.</summary>
    public PreparedNavigationMap(
        NavigationMap map,
        long bakeVersion,
        NavigationMapCheckpointStamp? checkpointStamp = null)
    {
        SwiftThrowHelper.ThrowIfNull(map, nameof(map));
        SwiftThrowHelper.ThrowIfArgument(bakeVersion <= 0, nameof(bakeVersion));
        SwiftThrowHelper.ThrowIfArgument(
            checkpointStamp.HasValue
            && !string.Equals(checkpointStamp.Value.MapId, map.MapId, System.StringComparison.Ordinal),
            nameof(checkpointStamp),
            "Checkpoint map id must match the prepared map.");

        Map = map;
        BakeVersion = bakeVersion;
        BakedCellLookup = NavigationBakedCellLookup.Create(map);
        try
        {
            RetainedBytes = checked(EstimateRetainedBytes(map) + BakedCellLookup.RetainedBytes);
        }
        catch (OverflowException)
        {
            SwiftThrowHelper.ThrowIfArgument(
                true,
                nameof(map),
                "Prepared map retained-byte accounting overflowed.");
        }
        CheckpointStamp = checkpointStamp;
    }

    /// <summary>Gets the immutable prepared map asset.</summary>
    public NavigationMap Map { get; }

    /// <summary>Gets the immutable bake identity assigned during preparation.</summary>
    public long BakeVersion { get; }

    /// <summary>Gets the deterministic retained-byte reservation for the prepared bake.</summary>
    public long RetainedBytes { get; }

    /// <summary>Gets the checkpoint base stamp, when this preparation absorbed overlay state.</summary>
    public NavigationMapCheckpointStamp? CheckpointStamp { get; }

    internal NavigationBakedCellLookup BakedCellLookup { get; }

    private static long EstimateRetainedBytes(NavigationMap map)
    {
        long bytes = checked(128L + (map.MapId.Length * sizeof(char)));
        bytes = checked(bytes + ((long)map.Cells.Count * 96L));
        for (int i = 0; i < map.Connections.Count; i++)
        {
            NavigationConnection connection = map.Connections[i];
            bytes = checked(
                bytes
                + 160L
                + (connection.Id.Length * sizeof(char))
                + (connection.Destination.MapId.Length * sizeof(char)));
            for (int witness = 0; witness < connection.Witnesses.Count; witness++)
            {
                bytes = checked(
                    bytes
                    + 32L
                    + (connection.Witnesses[witness].MapId.Length * sizeof(char)));
            }
        }

        for (int i = 0; i < map.Transitions.Count; i++)
        {
            TraversalTransitionDefinition transition = map.Transitions[i];
            bytes = checked(
                bytes
                + 144L
                + (transition.Id.Length * sizeof(char))
                + (transition.Destination.MapId.Length * sizeof(char)));
        }

        return bytes;
    }
}
