using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;

namespace Trailblazer.Pathing;

internal interface IVoxelEndpointResolutionPolicy
{
    bool CanResolve();

    bool TryAcceptDirectVoxel(
        Voxel voxel,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints);

    bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize);

    bool IsTraversable(Voxel voxel, Fixed64 unitSize);

    bool TryGetFinalFallbackVoxel(
        Vector3d position,
        Voxel directVoxel,
        Fixed64 unitSize,
        out Voxel voxel);
}

internal static class EndpointVoxelResolver
{
    public static bool TryGetEndpointVoxel<TPolicy>(
        Vector3d position,
        Vector3d traceToward,
        out Voxel voxel,
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        if (!policy.CanResolve())
        {
            voxel = null;
            return false;
        }

        voxel = null;
        bool shouldRelaxEndpoint = allowUnwalkableEndpoints;

        if (GlobalGridManager.TryGetVoxel(position, out Voxel directVoxel))
        {
            if (policy.TryAcceptDirectVoxel(directVoxel, unitSize, allowUnwalkableEndpoints))
            {
                voxel = directVoxel;
                return true;
            }

            shouldRelaxEndpoint = shouldRelaxEndpoint || policy.RequiresSizeFallback(directVoxel, unitSize);
            if (shouldRelaxEndpoint
                && TryGetClosestTraversableVoxel(directVoxel, out Voxel closestNeighbor, unitSize, policy))
            {
                voxel = closestNeighbor;
                return true;
            }

            if (!shouldRelaxEndpoint)
                return false;

            if (TryTraceToClosestTraversableVoxel(position, traceToward, unitSize, out voxel, policy))
                return true;

            return policy.TryGetFinalFallbackVoxel(position, directVoxel, unitSize, out voxel);
        }

        if (!shouldRelaxEndpoint)
            return false;

        if (TryTraceToClosestTraversableVoxel(position, traceToward, unitSize, out voxel, policy))
            return true;

        voxel = null;
        return false;
    }

    public static bool TryGetClosestTraversableVoxel<TPolicy>(
        Voxel voxel,
        out Voxel closestNeighbor,
        Fixed64 unitSize,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        closestNeighbor = null;

        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out Voxel candidate)
                || !policy.IsTraversable(candidate, unitSize))
            {
                continue;
            }

            closestNeighbor = candidate;
            return true;
        }

        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out Voxel candidate)
                || !policy.IsTraversable(candidate, unitSize))
            {
                continue;
            }

            closestNeighbor = candidate;
            return true;
        }

        return false;
    }

    private static bool TryTraceToClosestTraversableVoxel<TPolicy>(
        Vector3d position,
        Vector3d traceToward,
        Fixed64 unitSize,
        out Voxel voxel,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(position, traceToward))
        {
            foreach (Voxel current in gridVoxelSet.Voxels)
            {
                if (!policy.IsTraversable(current, unitSize))
                    continue;

                voxel = current;
                return true;
            }
        }

        voxel = null;
        return false;
    }
}
