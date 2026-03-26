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
        bool allowUnwalkableEndNode = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        if (!VolumeMediumRules.IsConfigured(medium))
        {
            originVoxel = null;
            targetVoxel = null;
            return false;
        }

        targetVoxel = null;
        if (!GetStartVoxel(origin, target, out originVoxel, allowUnwalkableEndNode, unitSize, medium))
            return false;

        if (!GetEndVoxel(origin, target, out targetVoxel, allowUnwalkableEndNode, unitSize, medium))
            return false;

        return true;
    }

    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        bool allowUnwalkableEndNode = false,
        Fixed64? unitSize = null,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return TryGetEndpointVoxel(
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndNode,
            unitSize ?? GlobalGridManager.VoxelSize,
            medium);
    }

    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel targetVoxel,
        bool allowUnwalkableEndNode = false,
        Fixed64? unitSize = null,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return TryGetEndpointVoxel(
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndNode,
            unitSize ?? GlobalGridManager.VoxelSize,
            medium);
    }

    public static bool IsDirectPathClear(
        Vector3d start,
        Vector3d end,
        Fixed64 unitSize,
        bool allowUnwalkableEndNode,
        TraversalMedium medium = TraversalMedium.Gas,
        Voxel startNode = null,
        Voxel endNode = null)
    {
        if (!VolumeMediumRules.IsConfigured(medium))
            return false;

        bool foundAny = false;

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(start, end))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                foundAny = true;

                bool isRelaxedEndpoint = allowUnwalkableEndNode
                    && ((startNode != null && voxel.GlobalIndex == startNode.GlobalIndex)
                    || (endNode != null && voxel.GlobalIndex == endNode.GlobalIndex));
                if (isRelaxedEndpoint)
                {
                    if (!PassesMedium(voxel, medium))
                        return false;

                    continue;
                }

                if (!IsTraversable(voxel, unitSize, medium))
                    return false;
            }
        }

        return foundAny;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTraversable(
        Voxel voxel,
        Fixed64 unitSize,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return IsBaseTraversable(voxel, unitSize)
            && PassesMedium(voxel, medium);
    }

    public static bool TryGetClosestTraversableVoxel(
        Voxel voxel,
        out Voxel closestNeighbor,
        Fixed64 unitSize,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        closestNeighbor = null;

        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !IsTraversable(closestNeighbor, unitSize, medium))
            {
                continue;
            }

            return true;
        }

        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !IsTraversable(closestNeighbor, unitSize, medium))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBaseTraversable(Voxel voxel, Fixed64 unitSize)
    {
        if (voxel == null || voxel.IsBlocked)
            return false;

        if (voxel.TryGetPartition(out VolumeChartPartition volumePartition))
            return !volumePartition.IsImpassable(unitSize);

        if (voxel.TryGetPartition(out SolidChartPartition partition))
            return !partition.IsImpassable(unitSize);

        return false;
    }

    private static bool TryGetEndpointVoxel(
        Vector3d position,
        Vector3d traceToward,
        out Voxel voxel,
        bool allowUnwalkableEndNode,
        Fixed64 unitSize,
        TraversalMedium medium)
    {
        if (!VolumeMediumRules.IsConfigured(medium))
        {
            voxel = null;
            return false;
        }

        voxel = null;

        if (GlobalGridManager.TryGetVoxel(position, out voxel))
        {
            if (PassesMedium(voxel, medium)
                && (allowUnwalkableEndNode || IsBaseTraversable(voxel, unitSize)))
            {
                return true;
            }

            if (TryGetClosestTraversableVoxel(voxel, out Voxel closestNeighbor, unitSize, medium))
            {
                voxel = closestNeighbor;
                return true;
            }
        }

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(position, traceToward))
        {
            foreach (Voxel current in gridVoxelSet.Voxels)
            {
                if (!IsTraversable(current, unitSize, medium))
                    continue;

                voxel = current;
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PassesMedium(Voxel voxel, TraversalMedium medium)
    {
        return VolumeMediumRules.Matches(voxel, medium);
    }

    internal static bool HasClearance(Voxel origin, Fixed64 unitSize)
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
