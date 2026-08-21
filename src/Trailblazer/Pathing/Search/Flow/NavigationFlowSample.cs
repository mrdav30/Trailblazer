//=======================================================================
// NavigationFlowSample.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one medium-specific movement sample or semantic action.</summary>
public readonly struct NavigationFlowSample
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

    /// <summary>Gets the deterministic ordinary movement heading, or zero for a pending action.</summary>
    public Vector3d Heading { get; }

    /// <summary>Gets the exact movement or action-approach target.</summary>
    public Vector3d Target { get; }

    /// <summary>Gets the exact traversal medium before this sample completes.</summary>
    public TraversalMedium Medium { get; }

    /// <summary>Gets the semantic action when <see cref="HasTransition"/> is true.</summary>
    public NavigationTransitionInstruction Transition { get; }

    /// <summary>Gets whether this sample requires explicit semantic-action completion.</summary>
    public bool HasTransition { get; }
}
