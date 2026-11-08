//=======================================================================
// NavigationAutomaticSeamMapKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Provides an ordinal value key for one structural-link row.</summary>
internal readonly struct NavigationAutomaticSeamMapKey : IComparable<NavigationAutomaticSeamMapKey>
{
    internal NavigationAutomaticSeamMapKey(string mapId) => MapId = mapId;

    internal string MapId { get; }

    public int CompareTo(NavigationAutomaticSeamMapKey other) =>
        string.CompareOrdinal(MapId, other.MapId);
}
