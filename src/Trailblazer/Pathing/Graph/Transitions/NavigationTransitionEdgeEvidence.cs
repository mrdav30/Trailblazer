//=======================================================================
// NavigationTransitionEdgeEvidence.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Preserves the exact cost and action endpoints selected for a transition.</summary>
internal readonly struct NavigationTransitionEdgeEvidence
{
    internal NavigationTransitionEdgeEvidence(
        Fixed64 cost,
        Vector3d sourceAction,
        Vector3d destinationAction)
    {
        Cost = cost;
        SourceAction = sourceAction;
        DestinationAction = destinationAction;
    }

    internal Fixed64 Cost { get; }

    internal Vector3d SourceAction { get; }

    internal Vector3d DestinationAction { get; }
}
