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
        TraversalTransitionRegistrationSource registrationSource,
        int registrationOrder)
    {
        Transition = transition;
        SourceVoxelIndex = transition.Source.VoxelIndex;
        SourcePosition = transition.Source.Position;
        DestinationVoxelIndex = transition.Destination.VoxelIndex;
        DestinationPosition = transition.Destination.Position;
        RegistrationSource = registrationSource;
        RegistrationOrder = registrationOrder;
    }

    public TraversalTransition Transition { get; }

    public GlobalVoxelIndex SourceVoxelIndex { get; }

    public Vector3d SourcePosition { get; }

    public GlobalVoxelIndex DestinationVoxelIndex { get; }

    public Vector3d DestinationPosition { get; }

    public TraversalTransitionRegistrationSource RegistrationSource { get; }

    public int RegistrationOrder { get; }

    public bool Equals(RegisteredTraversalTransition other)
    {
        return Transition.Type == other.Transition.Type
            && Transition.PathCostModifier == other.Transition.PathCostModifier
            && Transition.IsBidirectional == other.Transition.IsBidirectional
            && Transition.Source.Space == other.Transition.Source.Space
            && SourceVoxelIndex.Equals(other.SourceVoxelIndex)
            && SourcePosition.Equals(other.SourcePosition)
            && Transition.Destination.Space == other.Transition.Destination.Space
            && DestinationVoxelIndex.Equals(other.DestinationVoxelIndex)
            && DestinationPosition.Equals(other.DestinationPosition);
    }

    public override bool Equals(object obj) =>
        obj is RegisteredTraversalTransition other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Transition.Type.GetHashCode();
            hash = hash * 31 + Transition.PathCostModifier;
            hash = hash * 31 + Transition.IsBidirectional.GetHashCode();
            hash = hash * 31 + Transition.Source.Space.GetHashCode();
            hash = hash * 31 + SourceVoxelIndex.GetHashCode();
            hash = hash * 31 + SourcePosition.GetHashCode();
            hash = hash * 31 + Transition.Destination.Space.GetHashCode();
            hash = hash * 31 + DestinationVoxelIndex.GetHashCode();
            hash = hash * 31 + DestinationPosition.GetHashCode();
            return hash;
        }
    }
}
