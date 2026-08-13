//=======================================================================
// VolumeVoxelFinder.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Resolves and validates raw voxel volumes without requiring navigation chart partitions.
/// </summary>
public static class VolumeVoxelFinder
{
    /// <summary>
    /// Attempts to determine the voxels at the origin and target endpoints in one explicit context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetPathEdgeVoxels(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        targetVoxel = null;
        if (!GetStartVoxel(context, origin, target, out originVoxel, allowUnwalkableEndpoints, unitSize, medium))
            return false;

        if (!GetEndVoxel(context, origin, target, out targetVoxel, allowUnwalkableEndpoints, unitSize, medium))
            return false;

        return true;
    }

    /// <summary>
    /// Attempts to determine the start voxel in one explicit context.
    /// </summary>
    public static bool GetStartVoxel(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel originVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return TryGetEndpointVoxel(
            context,
            origin,
            target,
            out originVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? context.VoxelSize,
            medium);
    }

    /// <summary>
    /// Attempts to determine the end voxel in one explicit context.
    /// </summary>
    public static bool GetEndVoxel(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d target,
        [MaybeNullWhen(false)] out Voxel targetVoxel,
        bool allowUnwalkableEndpoints = false,
        Fixed64? unitSize = null,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return TryGetEndpointVoxel(
            context,
            target,
            origin,
            out targetVoxel,
            allowUnwalkableEndpoints,
            unitSize ?? context.VoxelSize,
            medium);
    }

    /// <summary>
    /// Determines whether a direct, traversable path exists in one explicit context.
    /// </summary>
    public static bool IsDirectPathClear(
        TrailblazerWorldContext context,
        Vector3d start,
        Vector3d end,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        TraversalMedium medium = TraversalMedium.Gas,
        Voxel? startNode = null,
        Voxel? endNode = null)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        PathingWorldState state = context.Pathing.State;
        if (!VolumeMediumRules.IsConfigured(state, medium))
            return false;

        bool foundAny = false;

        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(context.World, start, end))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                foundAny = true;

                bool isRelaxedEndpoint = allowUnwalkableEndpoints
                    && ((startNode != null && voxel.WorldIndex == startNode.WorldIndex)
                    || (endNode != null && voxel.WorldIndex == endNode.WorldIndex));
                if (isRelaxedEndpoint)
                {
                    if (!PassesMedium(state, voxel, medium))
                        return false;

                    continue;
                }

                if (!IsTraversable(state, voxel, unitSize, medium))
                    return false;
            }
        }

        return foundAny;
    }

    /// <summary>
    /// Determines whether the specified voxel can be traversed in one explicit context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTraversable(
        TrailblazerWorldContext context,
        Voxel voxel,
        Fixed64 unitSize,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        return IsTraversable(context.Pathing.State, voxel, unitSize, medium);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsTraversable(
        PathingWorldState state,
        Voxel voxel,
        Fixed64 unitSize,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return IsBaseTraversable(voxel, unitSize)
            && PassesMedium(state, voxel, medium);
    }

    /// <summary>
    /// Attempts to find the closest traversable neighboring voxel in one explicit context.
    /// </summary>
    public static bool TryGetClosestTraversableVoxel(
        TrailblazerWorldContext context,
        Voxel voxel,
        [MaybeNullWhen(false)] out Voxel closestNeighbor,
        Fixed64 unitSize,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        return EndpointVoxelResolver.TryGetClosestTraversableVoxel(
            context,
            voxel,
            out closestNeighbor,
            unitSize,
            new VolumeEndpointPolicy(context, medium));
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
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d traceToward,
        [MaybeNullWhen(false)] out Voxel voxel,
        bool allowUnwalkableEndpoints,
        Fixed64 unitSize,
        TraversalMedium medium)
    {
        return EndpointVoxelResolver.TryGetEndpointVoxel(
            context,
            position,
            traceToward,
            out voxel,
            allowUnwalkableEndpoints,
            unitSize,
            new VolumeEndpointPolicy(context, medium));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PassesMedium(PathingWorldState state, Voxel voxel, TraversalMedium medium)
    {
        return VolumeMediumRules.Matches(state, voxel, medium);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool RequiresSizeFallback(
        PathingWorldState state,
        Voxel voxel,
        Fixed64 unitSize,
        Fixed64 voxelSize,
        TraversalMedium medium)
    {
        if (unitSize == voxelSize
            || voxel == null
            || voxel.IsBlocked
            || !PassesMedium(state, voxel, medium))
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

    internal static bool HasClearance(TrailblazerWorldContext context, Voxel origin, Fixed64 unitSize)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        Fixed64 voxelSize = context.VoxelSize;
        if (unitSize <= voxelSize)
            return true;

        int requiredRadius = (unitSize / voxelSize).CeilToInt() - 1;
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

                    Vector3d expectedPosition = origin.WorldPosition + new Vector3d(
                        voxelSize * x,
                        voxelSize * y,
                        voxelSize * z);
                    if (!context.World.TryGetGridAndVoxel(expectedPosition, out _, out Voxel? neighbor)
                        || neighbor == null
                        || neighbor.WorldPosition != expectedPosition
                        || neighbor.IsBlocked)
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
        private readonly PathingWorldState _state;
        private readonly Fixed64 _voxelSize;
        private readonly TraversalMedium _medium;

        public VolumeEndpointPolicy(TrailblazerWorldContext context, TraversalMedium medium)
        {
            _state = context.Pathing.State;
            _voxelSize = context.VoxelSize;
            _medium = medium;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanResolve()
        {
            return VolumeMediumRules.IsConfigured(_state, _medium);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAcceptDirectVoxel(
            Voxel voxel,
            Fixed64 unitSize,
            bool allowUnwalkableEndpoints)
        {
            return PassesMedium(_state, voxel, _medium)
                && (allowUnwalkableEndpoints || IsBaseTraversable(voxel, unitSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RequiresSizeFallback(Voxel voxel, Fixed64 unitSize)
        {
            return VolumeVoxelFinder.RequiresSizeFallback(_state, voxel, unitSize, _voxelSize, _medium);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTraversable(Voxel voxel, Fixed64 unitSize)
        {
            return VolumeVoxelFinder.IsTraversable(_state, voxel, unitSize, _medium);
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
