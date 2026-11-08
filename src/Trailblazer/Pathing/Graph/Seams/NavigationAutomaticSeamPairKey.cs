//=======================================================================
// NavigationAutomaticSeamPairKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Orders one canonical unordered durable seam pair.</summary>
internal readonly struct NavigationAutomaticSeamPairKey : IComparable<NavigationAutomaticSeamPairKey>
{
    internal NavigationAutomaticSeamPairKey(
        NavigationCellAddress first,
        NavigationCellAddress second)
    {
        if (CompareAddress(first, second) > 0)
            (first, second) = (second, first);
        First = first;
        Second = second;
    }

    internal NavigationCellAddress First { get; }

    internal NavigationCellAddress Second { get; }

    public int CompareTo(NavigationAutomaticSeamPairKey other)
    {
        int comparison = CompareAddress(First, other.First);
        return comparison != 0 ? comparison : CompareAddress(Second, other.Second);
    }

    private static int CompareAddress(
        NavigationCellAddress left,
        NavigationCellAddress right)
    {
        int comparison = string.CompareOrdinal(left.MapId, right.MapId);
        if (comparison != 0)
            return comparison;
        comparison = left.Index.x < right.Index.x
            ? -1
            : left.Index.x > right.Index.x ? 1 : 0;
        if (comparison != 0)
            return comparison;
        comparison = left.Index.y < right.Index.y
            ? -1
            : left.Index.y > right.Index.y ? 1 : 0;
        if (comparison != 0)
            return comparison;
        return left.Index.z < right.Index.z
            ? -1
            : left.Index.z > right.Index.z ? 1 : 0;
    }
}
