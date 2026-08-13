//=======================================================================
// NavigationCellAddress.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Identifies one durable navigation cell by ordinal map ID and topology-local index.
/// </summary>
public readonly struct NavigationCellAddress :
    IEquatable<NavigationCellAddress>,
    IComparable<NavigationCellAddress>
{
    /// <summary>
    /// The stable host-owned map identifier.
    /// </summary>
    public string MapId { get; }

    /// <summary>
    /// The topology-local X/Q, Y/layer, Z/R cell index.
    /// </summary>
    public VoxelIndex Index { get; }

    /// <summary>
    /// Creates a durable cell address.
    /// </summary>
    public NavigationCellAddress(string mapId, VoxelIndex index)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(mapId),
            nameof(mapId),
            "Map ID cannot be null, empty, or whitespace.");

        MapId = mapId;
        Index = index;
    }

    /// <inheritdoc/>
    public int CompareTo(NavigationCellAddress other)
    {
        int result = StringComparer.Ordinal.Compare(MapId, other.MapId);
        return result != 0 ? result : Index.CompareTo(other.Index);
    }

    /// <inheritdoc/>
    public bool Equals(NavigationCellAddress other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && Index.Equals(other.Index);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is NavigationCellAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int mapHash = MapId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(MapId);
        return SwiftHashTools.CombineHashCodes(mapHash, Index.GetHashCode());
    }

    /// <inheritdoc/>
    public override string ToString() => $"{MapId}:{Index}";

    /// <summary>
    /// Tests two addresses for value equality.
    /// </summary>
    public static bool operator ==(NavigationCellAddress left, NavigationCellAddress right) =>
        left.Equals(right);

    /// <summary>
    /// Tests two addresses for value inequality.
    /// </summary>
    public static bool operator !=(NavigationCellAddress left, NavigationCellAddress right) =>
        !left.Equals(right);
}
