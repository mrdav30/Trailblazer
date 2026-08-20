//=======================================================================
// NavigationAStarGuidePoint.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable addressed waypoint in a certified A* guide.</summary>
internal readonly struct NavigationAStarGuidePoint
{
    internal NavigationAStarGuidePoint(
        NavigationCellAddress address,
        Vector3d position)
    {
        Address = address;
        Position = position;
    }

    internal NavigationCellAddress Address { get; }

    internal Vector3d Position { get; }
}
