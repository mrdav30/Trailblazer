//=======================================================================
// NavigationFlowSample.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one medium-specific movement sample or semantic action.</summary>
internal readonly struct NavigationFlowSample
{
    internal NavigationFlowSample(
        Vector3d heading,
        Vector3d target,
        TraversalMedium medium,
        NavigationTransitionInstruction transition,
        bool hasTransition)
    {
        Heading = heading;
        Target = target;
        Medium = medium;
        Transition = transition;
        HasTransition = hasTransition;
    }

    internal Vector3d Heading { get; }

    internal Vector3d Target { get; }

    internal TraversalMedium Medium { get; }

    internal NavigationTransitionInstruction Transition { get; }

    internal bool HasTransition { get; }
}
