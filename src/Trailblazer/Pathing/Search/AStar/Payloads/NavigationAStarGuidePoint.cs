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
    // Negative ordinals encode ordinary anchors and bends without widening retained spatial points.
    internal const int OrdinaryOrdinal = -1;
    internal const int SideViewStepBendOrdinal = -2;
    internal const int SideViewGroundedBendOrdinal = -3;

    internal NavigationAStarGuidePoint(
        NavigationCellAddress address,
        Vector3d position,
        TraversalMedium medium,
        int transitionOrdinal = OrdinaryOrdinal)
    {
        Address = address;
        Position = position;
        Medium = medium;
        TransitionOrdinal = transitionOrdinal;
    }

    internal NavigationCellAddress Address { get; }

    internal Vector3d Position { get; }

    internal TraversalMedium Medium { get; }

    internal bool IsSideViewStepBend => TransitionOrdinal == SideViewStepBendOrdinal;

    internal bool IsSideViewGroundedBend => TransitionOrdinal == SideViewGroundedBendOrdinal;

    internal int TransitionOrdinal { get; }

    internal bool HasTransition => TransitionOrdinal >= 0;
}
