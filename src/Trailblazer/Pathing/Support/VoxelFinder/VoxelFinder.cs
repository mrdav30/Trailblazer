using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Utility for resolving valid start and end voxels for pathfinding based on world positions, 
/// with optional size consideration and walkability fallback.
/// </summary>
public static class VoxelFinder
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
    /// <param name="allowUnwalkableEndNode">
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
        bool allowUnwalkableEndNode = false)
    {
        Fixed64 resolvedUnitSize = unitSize ?? GlobalGridManager.VoxelSize;
        targetVoxel = null;
        if (!GetStartVoxel(origin, target, out originVoxel, allowUnwalkableEndNode, resolvedUnitSize))
            return false;

        return GetEndVoxel(origin, target, out targetVoxel, allowUnwalkableEndNode, resolvedUnitSize);
    }


    /// <summary>
    /// Finds closest valid end voxel, with optional fallback to nearest walkable neighbor if the direct voxel is blocked or too small for the unit.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="allowUnwalkableEndNode"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel targetVoxel,
        bool allowUnwalkableEndNode = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndNode,
            unitSize ?? GlobalGridManager.VoxelSize);
    }

    /// <summary>
    /// Finds closest valid start voxel, with optional fallback to nearest walkable neighbor if the direct voxel is blocked or too small for the unit.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="originVoxel"></param>
    /// <param name="allowUnwalkableEndNode"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        out Voxel originVoxel,
        bool allowUnwalkableEndNode = false,
        Fixed64? unitSize = null)
    {
        return TryGetEndpointVoxel(
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndNode,
            unitSize ?? GlobalGridManager.VoxelSize);
    }


    /// <summary>
    /// Performs a star-shaped radial search around the target position to find the closest valid voxel, prioritizing straight directions first.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <returns></returns>
    public static bool StarCast(Vector3d target, out Voxel targetVoxel) =>
        StarCast(target, out targetVoxel, GlobalGridManager.VoxelSize);

    /// <summary>
    /// Performs a radial search around the target position to find the closest valid voxel, prioritizing straight directions first.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="unitSize"></param>
    /// <returns></returns>
    public static bool StarCast(Vector3d target, out Voxel targetVoxel, Fixed64 unitSize)
    {
        targetVoxel = null;
        if (!GlobalGridManager.TryGetGrid(target, out VoxelGrid outGrid))
            return false; // no grid found at this position!

        AlternativeVoxelFinder.Instance.SetQuery(target, outGrid.BoundsMin, MaxTestDistance);

        if (!AlternativeVoxelFinder.Instance.GetVoxel(out Voxel candidateVoxel))
            return false;

        if (IsChartTraversable(candidateVoxel, unitSize))
        {
            targetVoxel = candidateVoxel;
            return true;
        }

        return TryGetClosestWalkableVoxel(candidateVoxel, out targetVoxel, unitSize);
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
        closestNeighbor = null;
        Fixed64 resolvedUnitSize = unitSize ?? GlobalGridManager.VoxelSize;

        // prefer straight neighbors since they cost less
        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !IsChartTraversable(closestNeighbor, resolvedUnitSize)) continue;
            return true;
        }

        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out closestNeighbor)
                || !IsChartTraversable(closestNeighbor, resolvedUnitSize)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds closest valid end voxel, with optional fallback to nearest walkable neighbor if the direct voxel is blocked or too small for the unit.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="unitSize"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="allowUnwalkableEndNode"></param>
    /// <returns></returns>
    public static bool GetClosestVoxelForSize(
        Vector3d origin,
        Vector3d target,
        Fixed64 unitSize,
        out Voxel targetVoxel,
        bool allowUnwalkableEndNode = false)
    {
        return TryGetEndpointVoxel(origin, target, out targetVoxel, allowUnwalkableEndNode, unitSize);
    }

    private static bool TryGetEndpointVoxel(
        Vector3d position,
        Vector3d traceToward,
        out Voxel voxel,
        bool allowUnwalkableEndNode,
        Fixed64 unitSize)
    {
        voxel = null;
        bool shouldRelaxEndpoint = allowUnwalkableEndNode;

        if (GlobalGridManager.TryGetVoxel(position, out Voxel directVoxel))
        {
            if (IsChartTraversable(directVoxel, unitSize))
            {
                voxel = directVoxel;
                return true;
            }

            shouldRelaxEndpoint = shouldRelaxEndpoint || RequiresSizeFallback(directVoxel, unitSize);
            if (shouldRelaxEndpoint
                && TryGetClosestWalkableVoxel(directVoxel, out Voxel closestNeighbor, unitSize))
            {
                voxel = closestNeighbor;
                return true;
            }
        }

        if (!shouldRelaxEndpoint)
            return false;

        if (TryTraceToClosestWalkableVoxel(position, traceToward, unitSize, out voxel))
            return true;

        return StarCast(position, out voxel, unitSize)
            && IsChartTraversable(voxel, unitSize);
    }

    private static bool TryTraceToClosestWalkableVoxel(
        Vector3d position,
        Vector3d traceToward,
        Fixed64 unitSize,
        out Voxel voxel)
    {
        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(position, traceToward))
        {
            foreach (Voxel current in gridVoxelSet.Voxels)
            {
                if (!IsChartTraversable(current, unitSize))
                    continue;

                voxel = current;
                return true;
            }
        }

        voxel = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsChartTraversable(Voxel voxel, Fixed64 unitSize)
    {
        if (!IsBaseChartTraversable(voxel))
        {
            return false;
        }

        voxel.TryGetPartition(out PathPartition partition);
        return unitSize == GlobalGridManager.VoxelSize
            || !partition.IsImpassable(unitSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBaseChartTraversable(Voxel voxel) =>
        voxel != null
        && !voxel.IsBlocked
        && voxel.HasPartition<PathPartition>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize)
    {
        if (unitSize == GlobalGridManager.VoxelSize
            || !IsBaseChartTraversable(voxel)
            || !voxel.TryGetPartition(out PathPartition partition))
        {
            return false;
        }

        return partition.IsImpassable(unitSize);
    }
}
