//=======================================================================
// AStarPathRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// A pathfinding request used for A* trail generation, including options for climb height, heuristic weighting,
/// and path smoothing. Implements value-based comparison and hashing for guide pooling.
/// </summary>
public class AStarPathRequest : PathRequest, IEquatable<AStarPathRequest>
{
    /// <summary>
    /// The maximum Y-axis height delta a unit can step or climb per voxel.
    /// Voxels exceeding this are ignored even if walkable and adjacent.
    /// </summary>
    public Fixed64 MaxClimbHeight { get; set; }

    /// <summary>
    /// Gets or sets the heuristic method used for evaluating or guiding the algorithm.
    /// </summary>
    /// <remarks>
    /// Set this property to specify which heuristic strategy the algorithm should use.
    /// The selected heuristic can affect the performance and outcome of the algorithm.
    /// </remarks>
    public HeuristicMethod Heuristic { get; set; }

    // Prevent external use of the default constructor to ensure proper initialization through factory methods.
    private AStarPathRequest() { }

    /// <summary>
    /// Attempts to create a new context-bound A* pathfinding request.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        out AStarPathRequest? request)
    {
        request = Create(context, origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    /// <summary>
    /// Creates a context-bound A* pathfinding request.
    /// </summary>
    public static AStarPathRequest? Create(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        bool allowUnwalkableEndpoints = false,
        bool allowTraversalTransitions = false)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (!SolidVoxelFinder.TryGetPathEdgeVoxels(
            context,
            origin,
            destination,
            out Voxel? startNode,
            out Voxel? endNode,
            unitSize,
            allowUnwalkableEndpoints))
        {
            return null;
        }

        if (startNode == null || endNode == null)
            return null;

        AStarPathRequest request = new()
        {
            Context = context,
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = context.VoxelSize
        };

        if (context.Pathing.TryGetMaxSearchSize(request.StartNode, request.EndNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is AStarPathRequest other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(AStarPathRequest? other) =>
        other != null && RequestCacheKey == other.RequestCacheKey;

    /// <inheritdoc/>
    public override int GetHashCode() => RequestCacheKey.GetHashCode();

    /// <inheritdoc/>
    public override PathRequestCacheKey RequestCacheKey =>
        StartNode == null || EndNode == null
            ? default
            : PathRequestCacheKey.CreateAStar(
                StartNode.WorldIndex,
                EndNode.WorldIndex,
                UnitSize,
                AllowUnwalkableEndpoints,
                AllowTraversalTransitions,
                Heuristic,
                MaxClimbHeight,
                MaxPathSearchRange,
                Context.Pathing.State.TransitionRegistryState.RegistryVersion);
}
