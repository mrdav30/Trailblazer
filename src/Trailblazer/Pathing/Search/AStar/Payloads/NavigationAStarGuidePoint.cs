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
        Vector3d position,
        TraversalMedium medium,
        int transitionOrdinal = -1)
    {
        Address = address;
        Position = position;
        Medium = medium;
        TransitionOrdinal = transitionOrdinal;
    }

    internal NavigationCellAddress Address { get; }

    internal Vector3d Position { get; }

    internal TraversalMedium Medium { get; }

    internal int TransitionOrdinal { get; }

    internal bool HasTransition => TransitionOrdinal >= 0;
}
