//=======================================================================
// NavigationAutomaticSeamRef.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

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
