using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Utility for resolving valid start and end voxels for pathfinding based on world positions, 
/// with optional size consideration and walkability fallback.
/// </summary>
public static class VoxelFinder
{
    // set to the highest height or width value of any game object
    private const int _maxTestDistance = 3;

    /// <summary>
    /// Attempts to get valid start and end voxels based on provided world positions.
    /// Falls back to the closest walkable neighbor if necessary.
    /// </summary>
    /// <param name="origin">The start position in world space.</param>
    /// <param name="target">The end position in world space.</param>
    /// <param name="originVoxel">Resolved start voxel.</param>
    /// <param name="targetVoxel">Resolved end voxel.</param>
    /// <param name="unitSize">The size of the unit in voxels</param>
    /// <returns>True if both voxels were resolved successfully; otherwise, false.</returns>
    public static bool TryGetPathEdgeVoxels(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        out Voxel targetVoxel,
        Fixed64? unitSize = null)
    {
        targetVoxel = default;
        bool checkPassable = unitSize.HasValue && unitSize.Value != GlobalGridManager.VoxelSize;

        if (!GlobalGridManager.TryGetVoxel(origin, out originVoxel))
        {
            Console.WriteLine($"Unable to find a valid start voxel for {origin}");
            return false;
        }

        if (originVoxel.IsBlocked
            || !originVoxel.TryGetPartition(out PathPartition originPart)
            || checkPassable && originPart.IsImpassable(unitSize.Value))
        {
            if (!TryGetClosestWalkableVoxel(originVoxel, out Voxel closestNeighbor, unitSize))
                return false;
            originVoxel = closestNeighbor;
        }

        if (!GlobalGridManager.TryGetVoxel(target, out targetVoxel))
        {
            Console.WriteLine($"Unable to find a valid end voxel for {target}");
            return false;
        }

        if (targetVoxel.IsBlocked
            || !targetVoxel.TryGetPartition(out PathPartition targetPart)
            || checkPassable && targetPart.IsImpassable(unitSize.Value))
        {
            if (!TryGetClosestWalkableVoxel(targetVoxel, out Voxel closestNeighbor, unitSize))
                return false;
            targetVoxel = closestNeighbor;
        }

        return true;
    }

    public static bool TryGetClosestWalkableVoxel(
        Voxel voxel,
        out Voxel closestNeighbor,
        Fixed64? unitSize = null)
    {
        closestNeighbor = null;
        bool checkPassable = unitSize.HasValue && unitSize.Value != GlobalGridManager.VoxelSize;

        // prefer straight neighbors since they cost less
        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !closestNeighbor.TryGetPartition(out PathPartition part)
                || checkPassable && part.IsImpassable(unitSize.Value)) continue;
            return true;
        }

        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !closestNeighbor.TryGetPartition(out PathPartition part)
                || checkPassable && part.IsImpassable(unitSize.Value)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds closest next-best-voxel also when destination is off invalid
    /// </summary>
    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel targetVoxel,
        bool allowUnwalkable = false,
        Fixed64? unitSize = null)
    {
        // if size requires consideration, use next-best-voxel system
        if (unitSize.HasValue && unitSize.Value != GlobalGridManager.VoxelSize)
            return GetClosestVoxelForSize(origin, target, unitSize.Value, out targetVoxel, allowUnwalkable);

        if (!GlobalGridManager.TryGetVoxel(target, out targetVoxel))
        {
            // If null, it is off the grid. Raycast back onto grid for closest viable voxel to the destination.
            foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(target, origin))
            {
                foreach (Voxel voxel in gridVoxelSet.Voxels)
                {
                    // A path is required if a voxel doesn't exist in the traced line
                    if (!allowUnwalkable && voxel.IsBlocked || !voxel.HasPartition<PathPartition>())
                        continue;

                    targetVoxel = voxel;
                    return true;
                }
            }

            return false;
        }

        if (targetVoxel.IsBlocked)
        {
            if (allowUnwalkable && TryGetClosestWalkableVoxel(targetVoxel, out _))
                return true;

            return StarCast(target, out targetVoxel);
        }

        return true;
    }

    /// <summary>
    /// Finds closest next-best-voxel
    /// </summary>
    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        bool allowUnwalkable = false,
        Fixed64? unitSize = null)
    {
        // if size requires consideration, use next-best-voxel system
        if (unitSize.HasValue && unitSize.Value != GlobalGridManager.VoxelSize)
            return GetClosestVoxelForSize(origin, target, unitSize.Value, out originVoxel, allowUnwalkable);

        if (!GlobalGridManager.TryGetVoxel(origin, out originVoxel))
        {
            // If null, it is off the grid. Raycast back onto grid for closest viable voxel to the destination.
            foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(origin, target))
            {
                foreach (Voxel voxel in gridVoxelSet.Voxels)
                {
                    // A path is required if a voxel doesn't exist in the traced line
                    if (!allowUnwalkable && voxel.IsBlocked || !voxel.HasPartition<PathPartition>())
                        continue;

                    originVoxel = voxel;
                    return true;
                }
            }

            return false;
        }

        if (originVoxel.IsBlocked)
        {
            if (allowUnwalkable && TryGetClosestWalkableVoxel(originVoxel, out _))
                return true;

            return StarCast(origin, out originVoxel);
        }

        return true;
    }

    public static bool StarCast(Vector3d target, out Voxel targetVoxel)
    {
        targetVoxel = null;
        if (!GlobalGridManager.TryGetGrid(target, out VoxelGrid outGrid))
            return false; // no grid found at this position!

        AlternativeVoxelFinder.Instance.SetQuery(target, outGrid.BoundsMin, _maxTestDistance);

        if (!AlternativeVoxelFinder.Instance.GetVoxel(out targetVoxel))
            return false;

        return true;
    }

    public static bool GetClosestVoxelForSize(
        Vector3d origin,
        Vector3d target,
        Fixed64 unitSize,
        out Voxel targetVoxel,
        bool allowUnwalkable = false)
    {
        if (GlobalGridManager.TryGetVoxel(origin, out targetVoxel)
            && (!targetVoxel.IsBlocked || allowUnwalkable)
            && targetVoxel.TryGetPartition(out PathPartition retPart)
            && !retPart.IsImpassable(unitSize))
        {
            return true;
        }

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(origin, target))
        {
            foreach (Voxel current in gridVoxelSet.Voxels)
            {
                // A path is required if a voxel doesn't exist in the traced line
                if (!allowUnwalkable && current.IsBlocked || !current.TryGetPartition(out PathPartition curPart))
                    continue;

                if (!curPart.IsImpassable(unitSize))
                {
                    targetVoxel = current;
                    return true;
                }
            }
        }

        return false;
    }
}