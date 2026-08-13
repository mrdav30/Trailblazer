//=======================================================================
// RegisteredTraversalTransition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// A registered transition together with the voxel endpoints it resolved to.
/// </summary>
internal readonly struct RegisteredTraversalTransition : IEquatable<RegisteredTraversalTransition>
{
    public RegisteredTraversalTransition(
        TraversalTransition transition,
        TraversalTransitionOwnershipKind ownershipKind,
        int priority,
        int registrationOrder)
    {
        Transition = transition;
        SourceVoxelIndex = transition.Source.VoxelIndex;
        SourcePosition = transition.Source.Position;
        DestinationVoxelIndex = transition.Destination.VoxelIndex;
        DestinationPosition = transition.Destination.Position;
        OwnershipKind = ownershipKind;
        Priority = priority;
        RegistrationOrder = registrationOrder;
    }

    public TraversalTransition Transition { get; }

    public WorldVoxelIndex SourceVoxelIndex { get; }

    public Vector3d SourcePosition { get; }

    public WorldVoxelIndex DestinationVoxelIndex { get; }

    public Vector3d DestinationPosition { get; }

    public TraversalTransitionOwnershipKind OwnershipKind { get; }

    public int Priority { get; }

    public int RegistrationOrder { get; }

    public bool Equals(RegisteredTraversalTransition other)
    {
        return Transition.Type == other.Transition.Type
            && Transition.PathCostModifier == other.Transition.PathCostModifier
            && Transition.IsBidirectional == other.Transition.IsBidirectional
            && Transition.Source.Medium == other.Transition.Source.Medium
            && SourceVoxelIndex.Equals(other.SourceVoxelIndex)
            && SourcePosition.Equals(other.SourcePosition)
            && Transition.Destination.Medium == other.Transition.Destination.Medium
            && DestinationVoxelIndex.Equals(other.DestinationVoxelIndex)
            && DestinationPosition.Equals(other.DestinationPosition);
    }

    public override bool Equals(object? obj) =>
        obj is RegisteredTraversalTransition other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Transition.Type.GetHashCode();
            hash = hash * 31 + Transition.PathCostModifier;
            hash = hash * 31 + Transition.IsBidirectional.GetHashCode();
            hash = hash * 31 + Transition.Source.Medium.GetHashCode();
            hash = hash * 31 + SourceVoxelIndex.GetHashCode();
            hash = hash * 31 + SourcePosition.GetHashCode();
            hash = hash * 31 + Transition.Destination.Medium.GetHashCode();
            hash = hash * 31 + DestinationVoxelIndex.GetHashCode();
            hash = hash * 31 + DestinationPosition.GetHashCode();
            return hash;
        }
    }
}
