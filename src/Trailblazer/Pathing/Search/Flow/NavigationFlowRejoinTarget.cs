//=======================================================================
// NavigationFlowRejoinTarget.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Pairs the position for a flow-field rejoin with the exact ray traversal constraint.</summary>
internal readonly struct NavigationFlowRejoinTarget
{
    internal NavigationFlowRejoinTarget(
        Vector3d position,
        NavigationRayChainConstraint constraint)
    {
        Position = position;
        Constraint = constraint;
    }

    internal Vector3d Position { get; }

    internal NavigationRayChainConstraint Constraint { get; }
}
