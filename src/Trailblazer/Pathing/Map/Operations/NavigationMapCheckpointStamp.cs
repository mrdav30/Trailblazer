//=======================================================================
// NavigationMapCheckpointStamp.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections.Diagnostics;
using SwiftCollections.Utility;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Identifies the exact bake and overlay prefix captured by a checkpoint rebake.
/// </summary>
public readonly struct NavigationMapCheckpointStamp : IEquatable<NavigationMapCheckpointStamp>
{
    /// <summary>Initializes an immutable checkpoint base stamp.</summary>
    public NavigationMapCheckpointStamp(string mapId, long bakeVersion, long overlayHighWaterSequence)
    {
        SwiftThrowHelper.ThrowIfNull(mapId, nameof(mapId));
        SwiftThrowHelper.ThrowIfArgument(string.IsNullOrWhiteSpace(mapId), nameof(mapId));
        SwiftThrowHelper.ThrowIfArgument(bakeVersion <= 0, nameof(bakeVersion));
        SwiftThrowHelper.ThrowIfArgument(overlayHighWaterSequence < 0, nameof(overlayHighWaterSequence));

        MapId = mapId;
        BakeVersion = bakeVersion;
        OverlayHighWaterSequence = overlayHighWaterSequence;
    }

    /// <summary>Gets the stable map identifier.</summary>
    public string MapId { get; }

    /// <summary>Gets the exact immutable bake version captured for the checkpoint.</summary>
    public long BakeVersion { get; }

    /// <summary>Gets the captured overlay operation high-water sequence.</summary>
    public long OverlayHighWaterSequence { get; }

    /// <inheritdoc/>
    public bool Equals(NavigationMapCheckpointStamp other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && BakeVersion == other.BakeVersion
        && OverlayHighWaterSequence == other.OverlayHighWaterSequence;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationMapCheckpointStamp other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int mapHash = MapId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(MapId);
        int hash = SwiftHashTools.CombineHashCodes(mapHash, BakeVersion.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, OverlayHighWaterSequence.GetHashCode());
    }

    /// <inheritdoc/>
    public static bool operator ==(NavigationMapCheckpointStamp left, NavigationMapCheckpointStamp right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(NavigationMapCheckpointStamp left, NavigationMapCheckpointStamp right) => !left.Equals(right);
}
