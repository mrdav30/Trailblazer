using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Utility;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Resolves and validates raw voxel volumes without requiring navigation chart partitions.
/// </summary>
public static class VolumeVoxelFinder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetPathEdgeVoxels(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        out Voxel targetVoxel,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        targetVoxel = null;
        if (!GetStartVoxel(origin, target, out originVoxel, allowUnwalkableEndpoints, unitSize, medium))
            return false;

        if (!GetEndVoxel(origin, target, out targetVoxel, allowUnwalkableEndpoints, unitSize, medium))
            return false;

        return true;
    }

    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return TryGetEndpointVoxel(
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? TrailblazerWorldManager.VoxelSize,
            medium);
    }

    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return TryGetEndpointVoxel(
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? TrailblazerWorldManager.VoxelSize,
            medium);
    }

    public static bool IsDirectPathClear(
        Vector3d start,
        Vector3d end,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        TraversalMedium medium = TraversalMedium.Gas,
        Voxel? startNode = null,
        Voxel? endNode = null)
    {
        if (!VolumeMediumRules.IsConfigured(medium))
            return false;

        bool foundAny = false;

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(TrailblazerWorldManager.World, start, end))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                foundAny = true;

                bool isRelaxedEndpoint = allowUnwalkableEndpoints
                    && ((startNode != null && voxel.WorldIndex == startNode.WorldIndex)
                    || (endNode != null && voxel.WorldIndex == endNode.WorldIndex));
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
        return EndpointVoxelResolver.TryGetClosestTraversableVoxel(
            voxel,
            out closestNeighbor,
            unitSize,
            new VolumeEndpointPolicy(medium));
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
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize,
        TraversalMedium medium)
    {
        return EndpointVoxelResolver.TryGetEndpointVoxel(
            position,
            traceToward,
            out voxel,
            allowUnwalkableEndpoints,
            unitSize,
            new VolumeEndpointPolicy(medium));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PassesMedium(Voxel voxel, TraversalMedium medium)
    {
        return VolumeMediumRules.Matches(voxel, medium);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresSizeFallback(
        Voxel voxel,
        Fixed64 unitSize,
        TraversalMedium medium)
    {
        if (unitSize == TrailblazerWorldManager.VoxelSize
            || voxel == null
            || voxel.IsBlocked
            || !PassesMedium(voxel, medium))
        {
            return false;
        }

        if (voxel.TryGetPartition(out VolumeChartPartition volumePartition))
            return volumePartition.IsImpassable(unitSize);

        return voxel.TryGetPartition(out SolidChartPartition partition)
            && partition.IsImpassable(unitSize);
    }

    internal static bool HasClearance(Voxel origin, Fixed64 unitSize)
    {
        if (unitSize <= TrailblazerWorldManager.VoxelSize)
            return true;

        int requiredRadius = (unitSize / TrailblazerWorldManager.VoxelSize).CeilToInt() - 1;
        if (requiredRadius <= 0)
            return true;

        if (!TrailblazerWorldManager.World.TryGetGrid(origin.GridIndex, out VoxelGrid? grid))
            return false;

        for (int x = -requiredRadius; x <= requiredRadius; x++)
        {
            for (int y = -requiredRadius; y <= requiredRadius; y++)
            {
                for (int z = -requiredRadius; z <= requiredRadius; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;

                    if (!origin.TryGetNeighborFromOffset(grid!, (x, y, z), out Voxel? neighbor)
                        || neighbor!.IsBlocked)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private readonly struct VolumeEndpointPolicy : IVoxelEndpointResolutionPolicy
    {
        private readonly TraversalMedium _medium;

        public VolumeEndpointPolicy(TraversalMedium medium)
        {
            _medium = medium;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanResolve()
        {
            return VolumeMediumRules.IsConfigured(_medium);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAcceptDirectVoxel(
            Voxel voxel,
            Fixed64 unitSize,
            bool allowUnwalkableEndpoints)
        {
            return PassesMedium(voxel, _medium)
                && (allowUnwalkableEndpoints || IsBaseTraversable(voxel, unitSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize)
        {
            return VolumeVoxelFinder.RequiresSizeFallback(voxel, unitSize, _medium);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTraversable(Voxel voxel, Fixed64 unitSize)
        {
            return VolumeVoxelFinder.IsTraversable(voxel, unitSize, _medium);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetFinalFallbackVoxel(
            Vector3d position,
            Voxel directVoxel,
            Fixed64 unitSize,
            out Voxel voxel)
        {
            voxel = null;
            return false;
        }
    }
}
