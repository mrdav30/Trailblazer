//=======================================================================
// NavigationGuideStep.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one medium-specific movement target or semantic action.</summary>
internal readonly struct NavigationGuideStep
{
    internal NavigationGuideStep(
        NavigationCellAddress address,
        Vector3d position,
        TraversalMedium medium,
        NavigationTransitionInstruction transition,
        bool hasTransition)
    {
        Address = address;
        Position = position;
        Medium = medium;
        Transition = transition;
        HasTransition = hasTransition;
    }

    internal NavigationCellAddress Address { get; }

    internal Vector3d Position { get; }

    internal TraversalMedium Medium { get; }

    internal NavigationTransitionInstruction Transition { get; }

    internal bool HasTransition { get; }
}
