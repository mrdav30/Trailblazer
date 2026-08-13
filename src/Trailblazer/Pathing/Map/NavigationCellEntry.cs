//=======================================================================
// NavigationCellEntry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Associates one topology-local address with its immutable authored cell payload.
/// </summary>
public readonly struct NavigationCellEntry : IEquatable<NavigationCellEntry>
{
    /// <summary>
    /// The topology-local cell index.
    /// </summary>
    public VoxelIndex Index { get; }

    /// <summary>
    /// The complete authored cell payload.
    /// </summary>
    public NavigationCell Cell { get; }

    /// <summary>
    /// Creates an authored cell entry.
    /// </summary>
    public NavigationCellEntry(VoxelIndex index, NavigationCell cell)
    {
        Index = index;
        Cell = cell;
    }

    /// <inheritdoc/>
    public bool Equals(NavigationCellEntry other) =>
        Index.Equals(other.Index) && Cell.Equals(other.Cell);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationCellEntry other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Index.GetHashCode(), Cell.GetHashCode());

    /// <summary>
    /// Tests two entries for value equality.
    /// </summary>
    public static bool operator ==(NavigationCellEntry left, NavigationCellEntry right) => left.Equals(right);

    /// <summary>
    /// Tests two entries for value inequality.
    /// </summary>
    public static bool operator !=(NavigationCellEntry left, NavigationCellEntry right) => !left.Equals(right);
}
