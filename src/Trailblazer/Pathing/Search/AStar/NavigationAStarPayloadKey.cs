//=======================================================================
// NavigationAStarPayloadKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one exact addressed A* request independently of a graph generation.</summary>
internal readonly struct NavigationAStarPayloadKey : IEquatable<NavigationAStarPayloadKey>
{
    internal NavigationAStarPayloadKey(
        PathQuery query,
        NavigationCellAddress start,
        NavigationCellAddress end)
    {
        Query = query;
        Start = start;
        End = end;
    }

    internal PathQuery Query { get; }

    internal NavigationCellAddress Start { get; }

    internal NavigationCellAddress End { get; }

    public bool Equals(NavigationAStarPayloadKey other) =>
        Query == other.Query
        && Start == other.Start
        && End == other.End;

    public override bool Equals(object? obj) =>
        obj is NavigationAStarPayloadKey other && Equals(other);

    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(Query.GetHashCode(), Start.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, End.GetHashCode());
    }

    public static bool operator ==(
        NavigationAStarPayloadKey left,
        NavigationAStarPayloadKey right) => left.Equals(right);

    public static bool operator !=(
        NavigationAStarPayloadKey left,
        NavigationAStarPayloadKey right) => !left.Equals(right);
}
