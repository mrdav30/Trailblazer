//=======================================================================
// NavigatorOccupancyTracker.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;

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

        bool voxelFound = TryResolveVoxel(
            world,
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

        bool lastVoxelFound = TryResolveVoxel(
            world,
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
        bool oldVoxelFound = TryResolveVoxel(
            world,
            oldPosition,
            out VoxelGrid? oldGrid,
            out Voxel? oldVoxel);
        bool newVoxelFound = TryResolveVoxel(
            world,
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

    internal static bool TryResolveVoxel(
        GridWorld world,
        Vector3d position,
        out VoxelGrid? grid,
        out Voxel? voxel)
    {
        voxel = null;
        if (world.TryGetGrid(position, out grid))
        {
            if (!grid!.TryGetClosestVoxel(position, out voxel))
            {
                grid = null;
                return false;
            }
        }
        else if (!world.TryGetClosestGridAndVoxel(position, out grid, out voxel))
        {
            return false;
        }

        if (!grid!.Configuration.TryNormalize(out NormalizedGridConfiguration binding)
            || !binding.TryGetCellPrism(voxel!.Index, out GridCellPrism prism)
            || !prism.Contains(position))
        {
            grid = null;
            voxel = null;
            return false;
        }

        return true;
    }
}
