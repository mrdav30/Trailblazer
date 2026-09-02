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
    private const long DefaultCellRetainedBytes = 64L;
    private const long TransitionRuleRetainedBytes = 80L;

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
        RetainedBytes = NavigationByteCount.SaturatingAdd(
            EstimateRetainedBytes(map),
            BakedCellLookup.RetainedBytes);
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
        long bytes = NavigationByteCount.SaturatingAdd(
            128L,
            (long)map.MapId.Length * sizeof(char));
        bytes = NavigationByteCount.SaturatingAdd(bytes, map.NativePortalTemplateRetainedBytes);
        bytes = NavigationByteCount.SaturatingAdd(bytes, (long)map.Cells.Count * 96L);
        if (map.DefaultCell.HasValue)
            bytes = NavigationByteCount.SaturatingAdd(bytes, DefaultCellRetainedBytes);
        ReadOnlySpan<TraversalTransitionRule> rules = map.TransitionRuleSpan;
        for (int i = 0; i < rules.Length; i++)
        {
            bytes = NavigationByteCount.SaturatingAdd(bytes, TransitionRuleRetainedBytes);
            bytes = NavigationByteCount.SaturatingAdd(
                bytes,
                (long)rules[i].Id.Length * sizeof(char));
        }
        for (int i = 0; i < map.Connections.Count; i++)
        {
            NavigationConnection connection = map.Connections[i];
            bytes = NavigationByteCount.SaturatingAdd(bytes, 160L);
            bytes = NavigationByteCount.SaturatingAdd(
                bytes,
                (long)connection.Id.Length * sizeof(char));
            bytes = NavigationByteCount.SaturatingAdd(
                bytes,
                (long)connection.Destination.MapId.Length * sizeof(char));
            for (int witness = 0; witness < connection.Witnesses.Count; witness++)
            {
                bytes = NavigationByteCount.SaturatingAdd(bytes, 32L);
                bytes = NavigationByteCount.SaturatingAdd(
                    bytes,
                    (long)connection.Witnesses[witness].MapId.Length * sizeof(char));
            }
        }

        for (int i = 0; i < map.Transitions.Count; i++)
        {
            TraversalTransitionDefinition transition = map.Transitions[i];
            bytes = NavigationByteCount.SaturatingAdd(bytes, 144L);
            bytes = NavigationByteCount.SaturatingAdd(
                bytes,
                (long)transition.Id.Length * sizeof(char));
            bytes = NavigationByteCount.SaturatingAdd(
                bytes,
                (long)transition.Destination.MapId.Length * sizeof(char));
        }

        return bytes;
    }
}
