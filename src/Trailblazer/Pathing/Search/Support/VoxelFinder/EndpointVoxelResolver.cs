using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using System.Diagnostics.CodeAnalysis;

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
        [MaybeNullWhen(false)] out Voxel voxel);
}

internal static class EndpointVoxelResolver
{
    public static bool TryGetEndpointVoxel<TPolicy>(
        Vector3d position,
        Vector3d traceToward,
        [MaybeNullWhen(false)] out Voxel voxel,
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        return TryGetEndpointVoxel(
            PathRequestContextResolver.DefaultContext,
            position,
            traceToward,
            out voxel,
            allowUnwalkableEndpoints,
            unitSize,
            policy);
    }

    public static bool TryGetEndpointVoxel<TPolicy>(
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d traceToward,
        [MaybeNullWhen(false)] out Voxel voxel,
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        GridWorld world = context.World;
        if (!policy.CanResolve())
        {
            voxel = null;
            return false;
        }

        voxel = null;
        bool shouldRelaxEndpoint = allowUnwalkableEndpoints;

        if (world.TryGetVoxel(position, out Voxel? directVoxel)
            && directVoxel != null)
        {
            if (policy.TryAcceptDirectVoxel(directVoxel, unitSize, allowUnwalkableEndpoints))
            {
                voxel = directVoxel;
                return true;
            }

            shouldRelaxEndpoint = shouldRelaxEndpoint || policy.RequiresSizeFallback(directVoxel, unitSize);
            if (shouldRelaxEndpoint
                && TryGetClosestTraversableVoxel(context, directVoxel, out Voxel? closestNeighbor, unitSize, policy)
                && closestNeighbor != null)
            {
                voxel = closestNeighbor;
                return true;
            }

            if (!shouldRelaxEndpoint)
                return false;

            if (TryTraceToClosestTraversableVoxel(context, position, traceToward, unitSize, out voxel, policy))
                return true;

            return policy.TryGetFinalFallbackVoxel(position, directVoxel, unitSize, out voxel);
        }

        if (!shouldRelaxEndpoint)
            return false;

        if (TryTraceToClosestTraversableVoxel(context, position, traceToward, unitSize, out voxel, policy))
            return true;

        voxel = null;
        return false;
    }

    public static bool TryGetClosestTraversableVoxel<TPolicy>(
        Voxel voxel,
        [MaybeNullWhen(false)] out Voxel closestNeighbor,
        Fixed64 unitSize,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        return TryGetClosestTraversableVoxel(
            PathRequestContextResolver.DefaultContext,
            voxel,
            out closestNeighbor,
            unitSize,
            policy);
    }

    public static bool TryGetClosestTraversableVoxel<TPolicy>(
        TrailblazerWorldContext context,
        Voxel voxel,
        [MaybeNullWhen(false)] out Voxel closestNeighbor,
        Fixed64 unitSize,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        closestNeighbor = null;
        if (voxel == null || !context.World.TryGetGrid(voxel.WorldIndex.GridIndex, out VoxelGrid? grid))
            return false;

        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(grid!, dir, out Voxel? candidate)
                || candidate == null
                || !policy.IsTraversable(candidate, unitSize))
            {
                continue;
            }

            closestNeighbor = candidate;
            return true;
        }

        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(grid!, dir, out Voxel? candidate)
                || candidate == null
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
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d traceToward,
        Fixed64 unitSize,
        [MaybeNullWhen(false)] out Voxel voxel,
        TPolicy policy)
        where TPolicy : struct, IVoxelEndpointResolutionPolicy
    {
        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(context.World, position, traceToward))
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
