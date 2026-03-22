using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// A registered transition together with the voxel endpoints it resolved to.
/// </summary>
internal readonly struct RegisteredTraversalTransition
{
    public RegisteredTraversalTransition(
        TraversalTransition transition,
        GlobalVoxelIndex sourceVoxelIndex,
        GlobalVoxelIndex destinationVoxelIndex)
    {
        Transition = transition;
        SourceVoxelIndex = sourceVoxelIndex;
        DestinationVoxelIndex = destinationVoxelIndex;
    }

    public TraversalTransition Transition { get; }

    public GlobalVoxelIndex SourceVoxelIndex { get; }

    public GlobalVoxelIndex DestinationVoxelIndex { get; }
}
