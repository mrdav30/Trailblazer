//=======================================================================
// NavigationAutomaticSeamPair.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;
using System;

namespace Trailblazer.Pathing;

/// <summary>Owns one canonical absolute portal shared by both seam directions.</summary>
internal sealed class NavigationAutomaticSeamPair
{
    internal const long RetainedSize = 136L;

    internal NavigationAutomaticSeamPair(
        NavigationCellAddress first,
        NavigationCellAddress second,
        GridNavigationPortal portal)
    {
        First = first;
        Second = second;
        Portal = portal;
    }

    internal NavigationCellAddress First { get; }

    internal NavigationCellAddress Second { get; }

    internal GridNavigationPortal Portal { get; }
}

/// <summary>Stores snapshot-local active state without duplicating canonical portal geometry.</summary>
internal sealed class NavigationAutomaticSeamPairRecord
{
    internal const long RetainedSize = 32L;

    internal NavigationAutomaticSeamPairRecord(
        NavigationAutomaticSeamPair pair,
        bool isActive)
    {
        Pair = pair;
        IsActive = isActive;
    }

    internal NavigationAutomaticSeamPair Pair { get; }

    internal bool IsActive { get; }
}

/// <summary>Identifies one directed use of a canonical automatic seam.</summary>
internal readonly struct NavigationAutomaticSeamRef
{
    internal NavigationAutomaticSeamRef(NavigationAutomaticSeamPair pair, bool reverse)
    {
        Pair = pair;
        IsReverse = reverse;
    }

    internal NavigationAutomaticSeamPair Pair { get; }

    internal bool IsReverse { get; }

    internal NavigationCellAddress Source => IsReverse ? Pair.Second : Pair.First;

    internal NavigationCellAddress Destination => IsReverse ? Pair.First : Pair.Second;

    internal GridNavigationPortal Portal => Pair.Portal;
}

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

/// <summary>Provides an ordinal value key for one structural-link row.</summary>
internal readonly struct NavigationAutomaticSeamMapKey : IComparable<NavigationAutomaticSeamMapKey>
{
    internal NavigationAutomaticSeamMapKey(string mapId) => MapId = mapId;

    internal string MapId { get; }

    public int CompareTo(NavigationAutomaticSeamMapKey other) =>
        string.CompareOrdinal(MapId, other.MapId);
}

/// <summary>Orders one directed structural seam dependency.</summary>
internal readonly struct NavigationAutomaticSeamLinkKey : IComparable<NavigationAutomaticSeamLinkKey>
{
    internal NavigationAutomaticSeamLinkKey(string sourceMapId, string destinationMapId)
    {
        SourceMapId = sourceMapId;
        DestinationMapId = destinationMapId;
    }

    internal string SourceMapId { get; }

    internal string DestinationMapId { get; }

    public int CompareTo(NavigationAutomaticSeamLinkKey other)
    {
        int comparison = string.CompareOrdinal(SourceMapId, other.SourceMapId);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(DestinationMapId, other.DestinationMapId);
    }
}
