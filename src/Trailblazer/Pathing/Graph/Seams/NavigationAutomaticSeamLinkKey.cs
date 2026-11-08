//=======================================================================
// NavigationAutomaticSeamLinkKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

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
