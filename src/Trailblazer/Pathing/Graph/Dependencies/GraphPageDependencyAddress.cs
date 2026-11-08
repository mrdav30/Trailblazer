//=======================================================================
// GraphPageDependencyAddress.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Names one exact map-local page whose versions a query consumed.</summary>
internal readonly struct GraphPageDependencyAddress
{
    internal GraphPageDependencyAddress(string mapId, int pageIndex)
    {
        MapId = mapId;
        PageIndex = pageIndex;
    }

    internal string MapId { get; }

    internal int PageIndex { get; }
}
