//=======================================================================
// NavigationAutomaticSeamPair.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Owns one canonical absolute portal shared by both seam directions.</summary>
internal sealed class NavigationAutomaticSeamPair
{
    internal const long RetainedSize = 168L;

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
