using FixedMathSharp;
using GridForge.Grids;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Utility for resolving valid start and end voxels for pathfinding based on world positions, 
/// with optional size consideration and walkability fallback.
/// </summary>
public static class SolidVoxelFinder
{
    // set to the highest height or width valu1e of any game object
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
        out Voxel originVoxel,
        out Voxel targetVoxel,
        Fixed64? unitSize = null,
        bool allowUnwalkableEndpoints = false)
    {
        Fixed64 resolvedUnitSize = unitSize ?? GlobalGridManager.VoxelSize;
        targetVoxel = null;
        if (!GetStartVoxel(origin, target, out originVoxel, allowUnwalkableEndpoints, resolvedUnitSize))
            return false;

        return GetEndVoxel(origin, target, out targetVoxel, allowUnwalkableEndpoints, resolvedUnitSize);
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
        out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? GlobalGridManager.VoxelSize);
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
        out Voxel originVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? GlobalGridManager.VoxelSize);
    }


    /// <summary>
    /// Performs a bounded same-layer star search around the target position and returns the first valid voxel found,
    /// prioritizing straight directions before diagonals.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <returns></returns>
    public static bool StarCast(Vector3d target, out Voxel targetVoxel) =>
        StarCast(target, out targetVoxel, GlobalGridManager.VoxelSize);

    /// <summary>
    /// Performs a bounded same-layer star search around the target position and returns the first valid voxel found,
    /// prioritizing straight directions before diagonals.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool StarCast(Vector3d target, out Voxel targetVoxel, Fixed64 unitSize)
    {
        if (!GlobalGridManager.TryGetVoxel(target, out Voxel directVoxel))
        {
            targetVoxel = null;
            return false;
        }

        return StarCast(target, directVoxel, out targetVoxel, unitSize);
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
    out Voxel closestNeighbor,
    Fixed64? unitSize = null)
    {
        return EndpointVoxelResolver.TryGetClosestTraversableVoxel(
            voxel,
            out closestNeighbor,
            unitSize ?? GlobalGridManager.VoxelSize,
            new SolidEndpointPolicy());
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
        out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false)
    {
        return EndpointVoxelResolver.TryGetEndpointVoxel(
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize,
            new SolidEndpointPolicy());
    }

    private static bool TryGetEndpointVoxel(
        Vector3d position,
        Vector3d traceToward,
        out Voxel voxel,
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize)
    {
        return EndpointVoxelResolver.TryGetEndpointVoxel(
            position,
            traceToward,
            out voxel,
            allowUnwalkableEndpoints,
            unitSize,
            new SolidEndpointPolicy());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsChartTraversable(Voxel voxel, Fixed64 unitSize)
    {
        if (!IsBaseChartTraversable(voxel))
            return false;

        voxel.TryGetPartition(out SolidChartPartition partition);
        return unitSize == GlobalGridManager.VoxelSize
            || !partition.IsImpassable(unitSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBaseChartTraversable(Voxel voxel) =>
        voxel != null
        && !voxel.IsBlocked
        && voxel.HasPartition<SolidChartPartition>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize)
    {
        if (unitSize == GlobalGridManager.VoxelSize
            || !IsBaseChartTraversable(voxel)
            || !voxel.TryGetPartition(out SolidChartPartition partition))
        {
            return false;
        }

        return partition.IsImpassable(unitSize);
    }

    private readonly struct SolidEndpointPolicy : IVoxelEndpointResolutionPolicy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanResolve() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAcceptDirectVoxel(
            Voxel voxel,
            Fixed64 unitSize,
            bool allowUnwalkableEndpoints)
        {
            return IsChartTraversable(voxel, unitSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize)
        {
            return SolidVoxelFinder.RequiresSizeFallback(voxel, unitSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTraversable(Voxel voxel, Fixed64 unitSize)
        {
            return IsChartTraversable(voxel, unitSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetFinalFallbackVoxel(
            Vector3d position,
            Voxel directVoxel,
            Fixed64 unitSize,
            out Voxel voxel)
        {
            return StarCast(position, directVoxel, out voxel, unitSize);
        }
    }

    private static bool StarCast(
        Vector3d target,
        Voxel directVoxel,
        out Voxel targetVoxel,
        Fixed64 unitSize)
    {
        targetVoxel = null;

        AlternativeVoxelFinder.Instance.SetQuery(target, directVoxel, MaxTestDistance);

        if (!AlternativeVoxelFinder.Instance.GetVoxel(out Voxel candidateVoxel))
            return false;

        if (IsChartTraversable(candidateVoxel, unitSize))
        {
            targetVoxel = candidateVoxel;
            return true;
        }

        return TryGetClosestWalkableVoxel(candidateVoxel, out targetVoxel, unitSize);
    }
}
