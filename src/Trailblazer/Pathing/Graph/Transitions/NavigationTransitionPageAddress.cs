//=======================================================================
// NavigationTransitionPageAddress.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one exact map-local transition page.</summary>
internal readonly struct NavigationTransitionPageAddress : IEquatable<NavigationTransitionPageAddress>
{
    internal NavigationTransitionPageAddress(string mapId, int pageIndex)
    {
        MapId = mapId;
        PageIndex = pageIndex;
    }

    internal string MapId { get; }

    internal int PageIndex { get; }

    public bool Equals(NavigationTransitionPageAddress other) =>
        PageIndex == other.PageIndex
        && string.Equals(MapId, other.MapId, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var comparer = SwiftHashTools.GetDeterministicStringEqualityComparer();
        return SwiftHashTools.CombineHashCodes(comparer.GetHashCode(MapId), PageIndex);
    }
}
