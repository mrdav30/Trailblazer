//=======================================================================
// PathRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents an abstract base class for a pathfinding request, encapsulating the parameters and
/// state required to compute a path between two points in a voxel-based environment.
/// </summary>
/// <remarks>
/// PathRequest provides a common interface and shared logic for specifying origins, destinations, and
/// traversal options for pathfinding operations.
/// Derived classes should implement additional behavior as needed for specific pathfinding scenarios.
/// Thread safety is not guaranteed; synchronize access if used concurrently.
/// </remarks>
public abstract class PathRequest : IPathRequest
{
    private TrailblazerWorldContext? _context;

    /// <inheritdoc/>
    public TrailblazerWorldContext Context
    {
        get => _context ?? throw new InvalidOperationException("Path request is not bound to a TrailblazerWorldContext.");
        protected set => _context = value;
    }

    /// <inheritdoc/>
    public Vector3d Origin { get; protected set; }

    /// <inheritdoc/>
    public Voxel? StartNode { get; protected set; }

    /// <inheritdoc/>
    public Vector3d TargetPosition { get; protected set; }

    /// <inheritdoc/>
    public Voxel? EndNode { get; protected set; }

    /// <inheritdoc/>
    public Fixed64 UnitSize { get; protected set; }

    /// <inheritdoc/>
    public bool AllowUnwalkableEndpoints { get; set; }

    /// <summary>
    /// Whether chart-backed requests may fall back through authored traversal transitions when direct chart routing fails.
    /// </summary>
    public bool AllowTraversalTransitions { get; set; }

    /// <inheritdoc/>
    public int MaxPathSearchRange { get; set; }

    /// <inheritdoc/>
    public bool HasOrigin => StartNode != null;

    /// <inheritdoc/>
    public bool HasDestination => EndNode != null;

    /// <inheritdoc/>
    public bool HasValidEndpoints => HasOrigin && HasDestination;

    /// <inheritdoc/>
    public bool IsValid => HasValidEndpoints && MaxPathSearchRange > 0;

    /// <inheritdoc/>
    public bool HasZeroDisplacement =>
        !IsValid
        || StartNode == EndNode;

    /// <inheritdoc/>
    public abstract PathRequestCacheKey RequestCacheKey { get; }

    /// <inheritdoc/>
    public bool UpdateRequest(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize = null)
    {
        TrailblazerWorldContext context = Context;
        Fixed64 resolvedUnitSize = unitSize ?? context.VoxelSize;
        bool success = SolidVoxelFinder.TryGetPathEdgeVoxels(
            context,
            origin,
            destination,
            out Voxel? startVoxel,
            out Voxel? endVoxel,
            resolvedUnitSize,
            AllowUnwalkableEndpoints);

        // need to set these even if null incase the new size invalidates the request
        Origin = origin;
        TargetPosition = destination;
        StartNode = startVoxel;
        EndNode = endVoxel;
        UnitSize = resolvedUnitSize;
        MaxPathSearchRange = 0;

        if (!success)
            return false;

        if (StartNode != null
            && EndNode != null
            && context.Pathing.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
            MaxPathSearchRange = searchSize;

        return true;
    }

    /// <inheritdoc/>
    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null) return false;

        bool success = SolidVoxelFinder.GetStartVoxel(
            Context,
            origin,
            TargetPosition,
            out Voxel? newVoxel,
            AllowUnwalkableEndpoints,
            UnitSize);

        if (!success || newVoxel == null) return false;

        Origin = origin;

        if (StartNode != null)
        {
            // nothing to do here then
            if (newVoxel == StartNode)
                return true;

            // A recycled slot is a different GridForge runtime identity even when GridIndex is unchanged.
            if (!Context.World.TryGetGrid(StartNode.WorldIndex, out VoxelGrid? previousGrid)
                || newVoxel.GridIndex != previousGrid!.GridIndex)
                resetSearchRange = true;
        }

        StartNode = newVoxel;

        if (resetSearchRange)
        {
            MaxPathSearchRange = 0;
            if (StartNode != null
                && EndNode != null
                && Context.Pathing.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
                MaxPathSearchRange = searchSize;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null) return false;

        bool success = SolidVoxelFinder.GetEndVoxel(
            Context,
            Origin,
            destination,
            out Voxel? newVoxel,
            AllowUnwalkableEndpoints,
            UnitSize);

        if (!success || newVoxel == null) return false;

        TargetPosition = destination;

        if (EndNode != null)
        {
            // nothing to do here then
            if (newVoxel == EndNode)
                return true;

            // A recycled slot is a different GridForge runtime identity even when GridIndex is unchanged.
            if (!Context.World.TryGetGrid(EndNode.WorldIndex, out VoxelGrid? previousGrid)
                || newVoxel.GridIndex != previousGrid!.GridIndex)
                resetSearchRange = true;
        }

        EndNode = newVoxel;

        if (resetSearchRange)
        {
            MaxPathSearchRange = 0;
            if (StartNode != null
                && EndNode != null
                && Context.Pathing.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
                MaxPathSearchRange = searchSize;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TrySetUnitSize(Fixed64 unitSize)
    {
        // no change
        if (UnitSize == unitSize || !HasValidEndpoints) return false;

        return UpdateRequest(Origin, TargetPosition, unitSize);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => RequestCacheKey.GetHashCode();
}
