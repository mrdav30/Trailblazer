using FixedMathSharp;
using GridForge.Grids;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Utility for resolving valid start and end voxels for pathfinding based on world positions, 
/// with optional size consideration and walkability fallback.
/// </summary>
public static class SolidVoxelFinder
{
    /// <summary>
    /// Specifies the maximum allowable test distance.
    /// </summary>
    public const int MaxTestDistance = 3;

    /// <summary>
    /// Attempts to get valid start and end voxels based on provided world positions.
    /// Falls back to the closest walkable neighbor if necessary.
    /// </summary>
    /// <param name="origin">The start position in world space.</param>
    /// <param name="target">The end position in world space.</param>
    /// <param name="originVoxel">Resolved start voxel.</param>
    /// <param name="targetVoxel">Resolved end voxel.</param>
    /// <param name="unitSize">The size of the unit in voxels</param>
    /// <param name="allowUnwalkableEndpoints">
    /// Whether blocked or non-chart endpoints may relax to the nearest valid chart voxel.
    /// Size-based endpoint relaxation still applies regardless so larger units can snap to a nearby valid cell.
    /// </param>
    /// <returns>True if both voxels were resolved successfully; otherwise, false.</returns>
    public static bool TryGetPathEdgeVoxels(
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        Fixed64? unitSize = null,
        bool allowUnwalkableEndpoints = false) =>
        TryGetPathEdgeVoxels(
            PathRequestContextResolver.DefaultContext,
            origin,
            target,
            out originVoxel,
            out targetVoxel,
            unitSize,
            allowUnwalkableEndpoints);

    /// <summary>
    /// Attempts to get valid start and end voxels from one explicit context.
    /// </summary>
    public static bool TryGetPathEdgeVoxels(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        Fixed64? unitSize = null,
        bool allowUnwalkableEndpoints = false)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        Fixed64 resolvedUnitSize = unitSize ?? context.VoxelSize;
        targetVoxel = null;
        if (!GetStartVoxel(context, origin, target, out originVoxel, allowUnwalkableEndpoints, resolvedUnitSize))
            return false;

        return GetEndVoxel(context, origin, target, out targetVoxel, allowUnwalkableEndpoints, resolvedUnitSize);
    }


    /// <summary>
    /// Finds closest valid end voxel, with optional fallback to nearest walkable neighbor if the direct voxel is blocked or too small for the unit.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="allowUnwalkableEndpoints"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null) =>
        GetEndVoxel(
            PathRequestContextResolver.DefaultContext,
            origin,
            target,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize);

    /// <summary>
    /// Finds a closest valid end voxel in one explicit context.
    /// </summary>
    public static bool GetEndVoxel(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            context,
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? context.VoxelSize);
    }

    /// <summary>
    /// Finds closest valid start voxel, with optional fallback to nearest walkable neighbor if the direct voxel is blocked or too small for the unit.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="originVoxel"></param>
    /// <param name="allowUnwalkableEndpoints"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null) =>
        GetStartVoxel(
            PathRequestContextResolver.DefaultContext,
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndpoints,
            unitSize);

    /// <summary>
    /// Finds a closest valid start voxel in one explicit context.
    /// </summary>
    public static bool GetStartVoxel(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            context,
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? context.VoxelSize);
    }


    /// <summary>
    /// Performs a bounded same-layer star search around the target position and returns the first valid voxel found,
    /// prioritizing straight directions before diagonals.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <returns></returns>
    public static bool StarCast(Vector3d target, [MaybeNullWhen(false)] out Voxel targetVoxel) =>
        StarCast(PathRequestContextResolver.DefaultContext, target, out targetVoxel, PathRequestContextResolver.DefaultContext.VoxelSize);

    /// <summary>
    /// Performs a bounded same-layer star search in one explicit context.
    /// </summary>
    public static bool StarCast(
        TrailblazerWorldContext context,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel targetVoxel) =>
        StarCast(context, target, out targetVoxel, context.VoxelSize);

    /// <summary>
    /// Performs a bounded same-layer star search around the target position and returns the first valid voxel found,
    /// prioritizing straight directions before diagonals.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool StarCast(Vector3d target, [MaybeNullWhen(false)] out Voxel targetVoxel, Fixed64 unitSize)
    {
        return StarCast(PathRequestContextResolver.DefaultContext, target, out targetVoxel, unitSize);
    }

    /// <summary>
    /// Performs a bounded same-layer star search in one explicit context.
    /// </summary>
    public static bool StarCast(
        TrailblazerWorldContext context,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        Fixed64 unitSize)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (!context.World.TryGetVoxel(target, out Voxel? directVoxel)
            || directVoxel == null)
        {
            targetVoxel = null;
            return false;
        }

        return StarCast(context, target, directVoxel, out targetVoxel, unitSize);
    }

    /// <summary>
    /// Checks the 8 neighboring voxels around the provided voxel for a walkable option, prioritizing straight directions first.
    /// </summary>
    /// <param name="voxel"></param>
    /// <param name="closestNeighbor"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool TryGetClosestWalkableVoxel(
    Voxel voxel,
    [MaybeNullWhen(false)] out Voxel closestNeighbor,
    Fixed64? unitSize = null) =>
        TryGetClosestWalkableVoxel(
            PathRequestContextResolver.DefaultContext,
            voxel,
            out closestNeighbor,
            unitSize);

    /// <summary>
    /// Finds the closest valid neighboring solid voxel in one explicit context.
    /// </summary>
    public static bool TryGetClosestWalkableVoxel(
        TrailblazerWorldContext context,
        Voxel voxel,
        [MaybeNullWhen(false)] out Voxel closestNeighbor,
        Fixed64? unitSize = null)
    {
        return EndpointVoxelResolver.TryGetClosestTraversableVoxel(
            context,
            voxel,
            out closestNeighbor,
            unitSize ?? context.VoxelSize,
            new SolidEndpointPolicy(context));
    }

    /// <summary>
    /// Finds closest valid end voxel, with optional fallback to nearest walkable neighbor if the direct voxel is blocked or too small for the unit.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="unitSize"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="allowUnwalkableEndpoints"></param>
    /// <returns></returns>
    public static bool GetClosestVoxelForSize(
        Vector3d origin,
        Vector3d target,
        Fixed64 unitSize,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false) =>
        GetClosestVoxelForSize(
            PathRequestContextResolver.DefaultContext,
            origin,
            target,
            unitSize,
            out targetVoxel,
            allowUnwalkableEndpoints);

    /// <summary>
    /// Finds the closest valid endpoint voxel for a unit size in one explicit context.
    /// </summary>
    public static bool GetClosestVoxelForSize(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        Fixed64 unitSize,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false)
    {
        return EndpointVoxelResolver.TryGetEndpointVoxel(
            context,
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize,
            new SolidEndpointPolicy(context));
    }

    private static bool TryGetEndpointVoxel(
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d traceToward,
        [MaybeNullWhen(false)] out Voxel voxel,
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize)
    {
        return EndpointVoxelResolver.TryGetEndpointVoxel(
            context,
            position,
            traceToward,
            out voxel,
            allowUnwalkableEndpoints,
            unitSize,
            new SolidEndpointPolicy(context));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsChartTraversable(Voxel voxel, Fixed64 unitSize, Fixed64 voxelSize)
    {
        if (!IsBaseChartTraversable(voxel))
            return false;

        voxel.TryGetPartition(out SolidChartPartition? partition);
        return unitSize == voxelSize
            || (partition != null && !partition.IsImpassable(unitSize));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBaseChartTraversable(Voxel voxel) =>
        voxel != null
        && !voxel.IsBlocked
        && voxel.HasPartition<SolidChartPartition>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize, Fixed64 voxelSize)
    {
        if (unitSize == voxelSize
            || !IsBaseChartTraversable(voxel)
            || !voxel.TryGetPartition(out SolidChartPartition? partition)
            || partition == null)
        {
            return false;
        }

        return partition.IsImpassable(unitSize);
    }

    private readonly struct SolidEndpointPolicy : IVoxelEndpointResolutionPolicy
    {
        private readonly TrailblazerWorldContext _context;
        private readonly Fixed64 _voxelSize;

        public SolidEndpointPolicy(TrailblazerWorldContext context)
        {
            _context = context;
            _voxelSize = context.VoxelSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanResolve() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAcceptDirectVoxel(
            Voxel voxel,
            Fixed64 unitSize,
            bool allowUnwalkableEndpoints)
        {
            return IsChartTraversable(voxel, unitSize, _voxelSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize)
        {
            return SolidVoxelFinder.RequiresSizeFallback(voxel, unitSize, _voxelSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTraversable(Voxel voxel, Fixed64 unitSize)
        {
            return IsChartTraversable(voxel, unitSize, _voxelSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetFinalFallbackVoxel(
            Vector3d position,
            Voxel directVoxel,
            Fixed64 unitSize,
            [MaybeNullWhen(false)] out Voxel voxel)
        {
            return StarCast(_context, position, directVoxel, out voxel, unitSize);
        }
    }

    private static bool StarCast(
        TrailblazerWorldContext context,
        Vector3d target,
        Voxel directVoxel,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        Fixed64 unitSize)
    {
        targetVoxel = null;

        AlternativeVoxelFinder finder = context.Pathing.State.AlternativeVoxelFinder;
        finder.SetQuery(context, target, directVoxel, MaxTestDistance);

        if (!finder.GetVoxel(out Voxel? candidateVoxel)
            || candidateVoxel == null)
            return false;

        if (IsChartTraversable(candidateVoxel, unitSize, context.VoxelSize))
        {
            targetVoxel = candidateVoxel;
            return true;
        }

        return TryGetClosestWalkableVoxel(context, candidateVoxel, out targetVoxel, unitSize);
    }
}
