using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Navigation;

internal static class NavigatorOccupancyTracker
{
    public static void Update(
        GridWorld world,
        Navigator navigator,
        Vector3d position,
        Vector3d lastPosition,
        bool init)
    {
        if (!init && position == lastPosition)
            return;

        bool voxelFound = world.TryGetGridAndVoxel(
            position,
            out VoxelGrid? curGrid,
            out Voxel? curVoxel);
        if (!voxelFound)
            return;

        if (curGrid!.TryAddVoxelOccupant(curVoxel!, navigator) == false)
        {
            TrailblazerLogger.Channel.Warn($"Navigator {navigator.GlobalId} failed to register occupancy in voxel {curVoxel!.Index} of grid {curGrid} at position {position}.");
            return;
        }

        bool lastVoxelFound = world.TryGetGridAndVoxel(
            lastPosition,
            out VoxelGrid? lastGrid,
            out Voxel? lastVoxel);

        if (!lastVoxelFound || curVoxel == lastVoxel)
            return;

        lastGrid!.TryRemoveVoxelOccupant(lastVoxel!, navigator);
    }

    public static void UpdateAfterRootProjection(
        GridWorld world,
        Navigator navigator,
        Vector3d oldPosition,
        Vector3d newPosition)
    {
        bool oldVoxelFound = world.TryGetGridAndVoxel(
            oldPosition,
            out VoxelGrid? oldGrid,
            out Voxel? oldVoxel);
        bool newVoxelFound = world.TryGetGridAndVoxel(
            newPosition,
            out VoxelGrid? newGrid,
            out Voxel? newVoxel);

        if (oldVoxelFound && newVoxelFound && oldVoxel == newVoxel)
            return;

        if (oldVoxelFound)
            oldGrid!.TryRemoveVoxelOccupant(oldVoxel!, navigator);

        if (newVoxelFound && newGrid!.TryAddVoxelOccupant(newVoxel!, navigator) == false)
            TrailblazerLogger.Channel.Warn($"Navigator {navigator.GlobalId} failed to register occupancy in voxel {newVoxel!.Index} of grid {newGrid} at position {newPosition}.");
    }
}
