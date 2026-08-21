//=======================================================================
// NavigationGuideStep.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one medium-specific movement target or semantic action.</summary>
public readonly struct NavigationGuideStep
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

    /// <summary>Gets the stable address selected for this movement or action step.</summary>
    public NavigationCellAddress Address { get; }

    /// <summary>Gets the exact world position selected for this step.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the exact traversal medium before this step completes.</summary>
    public TraversalMedium Medium { get; }

    /// <summary>Gets the semantic action when <see cref="HasTransition"/> is true.</summary>
    public NavigationTransitionInstruction Transition { get; }

    /// <summary>Gets whether this step requires explicit semantic-action completion.</summary>
    public bool HasTransition { get; }
}
