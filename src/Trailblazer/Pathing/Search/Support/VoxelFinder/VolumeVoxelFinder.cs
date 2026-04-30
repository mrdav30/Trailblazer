using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Utility;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Resolves and validates raw voxel volumes without requiring navigation chart partitions.
/// </summary>
public static class VolumeVoxelFinder
{
    /// <summary>
    /// Attempts to determine the voxels at the origin and target endpoints of a path segment, 
    /// based on the specified traversal medium and unit size.
    /// </summary>
    /// <remarks>
    /// If either endpoint cannot be mapped to a valid voxel according to the specified parameters,
    /// both <paramref name="originVoxel"/> and <paramref name="targetVoxel"/> are set to <see langword="null"/> and the
    /// method returns <see langword="false"/>.
    /// </remarks>
    /// <param name="origin">The starting point of the path segment, specified in world coordinates.</param>
    /// <param name="target">The ending point of the path segment, specified in world coordinates.</param>
    /// <param name="originVoxel">When this method returns <see langword="true"/>, contains the voxel at the origin endpoint of the path;
    /// otherwise, <see langword="null"/>.</param>
    /// <param name="targetVoxel">When this method returns <see langword="true"/>, contains the voxel at the target endpoint of the path;
    /// otherwise, <see langword="null"/>.</param>
    /// <param name="unitSize">The size of a single voxel, used to map world coordinates to voxel grid coordinates.</param>
    /// <param name="allowUnwalkableEndpoints">If <see langword="true"/>, allows the origin or target endpoints to be in unwalkable voxels; otherwise, only
    /// walkable voxels are considered valid endpoints.</param>
    /// <param name="medium">The traversal medium to use when determining voxel walkability and mapping coordinates.</param>
    /// <returns>
    /// <see langword="true"/> if both the origin and target voxels are successfully determined; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetPathEdgeVoxels(
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
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

    /// <summary>
    /// Attempts to determine the voxel at the specified origin position that serves as the starting point for a path
    /// toward the target position.
    /// </summary>
    /// <remarks>
    /// This method is typically used as a preliminary step in pathfinding operations to ensure that
    /// the origin position corresponds to a valid voxel for traversal. The result depends on the specified traversal
    /// medium and walkability constraints.
    /// </remarks>
    /// <param name="origin">The world-space coordinates representing the starting position for the voxel search.</param>
    /// <param name="target">The world-space coordinates representing the intended target position for the path.</param>
    /// <param name="originVoxel">
    /// When this method returns, contains the voxel at the origin position if a valid start voxel is found; otherwise,
    /// contains null.
    /// </param>
    /// <param name="allowUnwalkableEndpoints">
    /// true to allow the start or end positions to be in unwalkable voxels; otherwise, false. 
    /// If false, only walkable voxels are considered valid endpoints.
    /// </param>
    /// <param name="unitSize">
    /// The size of the unit, in world units, to use when determining voxel boundaries. 
    /// If null, the default voxel size is used.
    /// </param>
    /// <param name="medium">
    /// The traversal medium to consider when evaluating voxel walkability, such as gas or other supported media.
    /// </param>
    /// <returns>true if a valid start voxel is found at the origin position; otherwise, false.</returns>
    public static bool GetStartVoxel(
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
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

    /// <summary>
    /// Attempts to find the voxel at the endpoint of a line segment from the specified origin to the target position.
    /// </summary>
    /// <remarks>
    /// If the endpoint does not correspond to a valid or walkable voxel (depending on the allowUnwalkableEndpoints parameter), 
    /// the method returns false and targetVoxel is set to null.
    /// </remarks>
    /// <param name="origin">The starting point of the line segment, in world coordinates.</param>
    /// <param name="target">The target point of the line segment, in world coordinates.</param>
    /// <param name="targetVoxel">
    /// When this method returns <see langword="true"/>, contains the voxel at the endpoint of the segment; otherwise,
    /// contains <see langword="null"/>.
    /// </param>
    /// <param name="allowUnwalkableEndpoints">
    /// true to allow the endpoint voxel to be unwalkable; otherwise, false to require a walkable endpoint. 
    /// The default is false.
    /// </param>
    /// <param name="unitSize">The size of a voxel edge, in world units. If null, the default voxel size is used.</param>
    /// <param name="medium">
    /// The traversal medium to consider when determining voxel walkability. 
    /// The default is TraversalMedium.Gas.
    /// </param>
    /// <returns>true if a valid endpoint voxel is found; otherwise, false.</returns>
    public static bool GetEndVoxel(
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
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

    /// <summary>
    /// Determines whether a direct, traversable path exists between two points in the voxel grid for 
    /// a specified traversal medium and unit size.
    /// </summary>
    /// <remarks>
    /// If the traversal medium is not configured, the method returns false. 
    /// When allowUnwalkableEndpoints is true and startNode or endNode is specified, the method permits the endpoints to be
    /// unwalkable for the given medium, provided all other voxels along the path are traversable.
    /// </remarks>
    /// <param name="start">The starting position of the path, in world coordinates.</param>
    /// <param name="end">The ending position of the path, in world coordinates.</param>
    /// <param name="unitSize">The size of the unit attempting to traverse the path. Must be compatible with the traversable space.</param>
    /// <param name="allowUnwalkableEndpoints">true to allow the start and end nodes to be unwalkable for the specified medium; otherwise, false.</param>
    /// <param name="medium">The traversal medium to use when evaluating path clearance. Defaults to TraversalMedium.Gas.</param>
    /// <param name="startNode">
    /// An optional voxel representing the start node. 
    /// If specified, allows relaxed traversability checks at this endpoint when allowUnwalkableEndpoints is true.
    /// </param>
    /// <param name="endNode">
    /// An optional voxel representing the end node. If specified, allows relaxed traversability checks at this endpoint
    /// when allowUnwalkableEndpoints is true.
    /// </param>
    /// <returns>
    /// true if a direct, traversable path exists between the start and end points for the given medium and unit size;
    /// otherwise, false.
    /// </returns>
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

    /// <summary>
    /// Determines whether the specified voxel can be traversed by an entity of the given size and traversal medium.
    /// </summary>
    /// <remarks>
    /// This method checks both the physical properties of the voxel and the traversal medium to
    /// determine if movement through the voxel is allowed. Use this method to validate movement or pathfinding logic
    /// based on voxel characteristics and environmental constraints.
    /// </remarks>
    /// <param name="voxel">The voxel to evaluate for traversability.</param>
    /// <param name="unitSize">The size of the entity, in fixed-point units, used to assess whether traversal is possible.</param>
    /// <param name="medium">The traversal medium to consider when evaluating traversability. Defaults to TraversalMedium.Gas.</param>
    /// <returns>true if the voxel is traversable for the specified unit size and medium; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTraversable(
        Voxel voxel,
        Fixed64 unitSize,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return IsBaseTraversable(voxel, unitSize)
            && PassesMedium(voxel, medium);
    }

    /// <summary>
    /// Attempts to find the closest traversable neighboring voxel to the specified voxel using the given traversal medium.
    /// </summary>
    /// <param name="voxel">The voxel from which to search for a traversable neighbor.</param>
    /// <param name="closestNeighbor">
    /// When this method returns, contains the closest traversable neighboring voxel if one is found; otherwise, the
    /// value is unspecified.
    /// </param>
    /// <param name="unitSize">The size of a single voxel unit, used to determine neighbor proximity.</param>
    /// <param name="medium">The traversal medium to consider when determining traversability. The default is TraversalMedium.Gas.</param>
    /// <returns>true if a traversable neighboring voxel is found; otherwise, false.</returns>
    public static bool TryGetClosestTraversableVoxel(
        Voxel voxel,
        [MaybeNullWhen(false)] out Voxel closestNeighbor,
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

        if (voxel.TryGetPartition(out VolumeChartPartition? volumePartition)
            && volumePartition != null)
            return !volumePartition.IsImpassable(unitSize);

        if (voxel.TryGetPartition(out SolidChartPartition? partition)
            && partition != null)
            return !partition.IsImpassable(unitSize);

        return false;
    }

    private static bool TryGetEndpointVoxel(
        Vector3d position,
        Vector3d traceToward,
        [MaybeNullWhen(false)] out Voxel voxel,
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

        if (voxel.TryGetPartition(out VolumeChartPartition? volumePartition)
            && volumePartition != null)
            return volumePartition.IsImpassable(unitSize);

        return voxel.TryGetPartition(out SolidChartPartition? partition)
            && partition != null
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
            [MaybeNullWhen(false)] out Voxel voxel)
        {
            voxel = null;
            return false;
        }
    }
}
