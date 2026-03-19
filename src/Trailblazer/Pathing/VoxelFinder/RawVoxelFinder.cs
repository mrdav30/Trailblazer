using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Resolves and validates raw voxel volumes without requiring navigation chart partitions.
/// </summary>
internal static class RawVoxelFinder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetPathEdgeVoxels(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        out Voxel targetVoxel,
        Fixed64 unitSize,
        bool allowUnwalkable = false)
    {
        targetVoxel = null;
        if (!GetStartVoxel(origin, target, out originVoxel, allowUnwalkable, unitSize))
            return false;

        if (!GetEndVoxel(origin, target, out targetVoxel, allowUnwalkable, unitSize))
            return false;

        return true;
    }

    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        bool allowUnwalkable = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            origin,
            target,
            out originVoxel,
            allowUnwalkable,
            unitSize ?? GlobalGridManager.VoxelSize);
    }

    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel targetVoxel,
        bool allowUnwalkable = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            target,
            origin,
            out targetVoxel,
            allowUnwalkable,
            unitSize ?? GlobalGridManager.VoxelSize);
    }

    public static bool IsDirectPathClear(
        Vector3d start,
        Vector3d end,
        Fixed64 unitSize,
        bool allowUnwalkable,
        Voxel startNode = null,
        Voxel endNode = null)
    {
        bool foundAny = false;

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(start, end))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                foundAny = true;

                bool isRelaxedEndpoint = allowUnwalkable
                    && ((startNode != null && voxel.GlobalIndex == startNode.GlobalIndex)
                    || (endNode != null && voxel.GlobalIndex == endNode.GlobalIndex));
                if (isRelaxedEndpoint)
                    continue;

                if (!IsTraversable(voxel, unitSize))
                    return false;
            }
        }

        return foundAny;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTraversable(Voxel voxel, Fixed64 unitSize)
    {
        if (voxel == null || voxel.IsBlocked)
            return false;

        if (voxel.TryGetPartition(out PathPartition partition))
            return !partition.IsImpassable(unitSize);

        return HasClearance(voxel, unitSize);
    }

    public static bool TryGetClosestTraversableVoxel(
        Voxel voxel,
        out Voxel closestNeighbor,
        Fixed64 unitSize)
    {
        closestNeighbor = null;

        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !IsTraversable(closestNeighbor, unitSize))
            {
                continue;
            }

            return true;
        }

        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !IsTraversable(closestNeighbor, unitSize))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetEndpointVoxel(
        Vector3d position,
        Vector3d traceToward,
        out Voxel voxel,
        bool allowUnwalkable,
        Fixed64 unitSize)
    {
        voxel = null;

        if (GlobalGridManager.TryGetVoxel(position, out voxel))
        {
            if (allowUnwalkable || IsTraversable(voxel, unitSize))
                return true;

            if (TryGetClosestTraversableVoxel(voxel, out Voxel closestNeighbor, unitSize))
            {
                voxel = closestNeighbor;
                return true;
            }
        }

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(position, traceToward))
        {
            foreach (Voxel current in gridVoxelSet.Voxels)
            {
                if (!IsTraversable(current, unitSize))
                    continue;

                voxel = current;
                return true;
            }
        }

        return false;
    }

    private static bool HasClearance(Voxel origin, Fixed64 unitSize)
    {
        if (unitSize <= GlobalGridManager.VoxelSize)
            return true;

        int requiredRadius = (unitSize / GlobalGridManager.VoxelSize).CeilToInt() - 1;
        if (requiredRadius <= 0)
            return true;

        for (int x = -requiredRadius; x <= requiredRadius; x++)
        {
            for (int y = -requiredRadius; y <= requiredRadius; y++)
            {
                for (int z = -requiredRadius; z <= requiredRadius; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;

                    if (!origin.TryGetNeighborFromOffset((x, y, z), out Voxel neighbor)
                        || neighbor.IsBlocked)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
