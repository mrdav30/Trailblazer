using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Specifies the heuristic method used for estimating distances in pathfinding algorithms.
/// </summary>
/// <remarks>
/// Use this enumeration to select the distance calculation strategy appropriate for the grid or
/// coordinate system in use. 
/// Manhattan is typically used for four-directional grids, Octile for eight-directional
/// grids, and Euclidean for continuous or diagonal movement scenarios.
/// </remarks>
public enum HeuristicMethod
{
    /// <summary>
    /// Represents the Manhattan distance metric, also known as the L1 norm, used to calculate the distance between
    /// points as the sum of the absolute differences of their coordinates.
    /// </summary>
    Manhattan,
    /// <summary>
    /// Represents the Octile distance metric, which is a modification of the Manhattan distance that accounts for 
    /// diagonal movement in grid-based pathfinding.
    /// </summary>
    Octile,
    /// <summary>
    /// Represents the Euclidean distance metric used for measuring straight-line distance between points in Euclidean space.
    /// </summary>
    Euclidean
    //Chebyshev?
}

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
    /// Attempts to create a new AStarPathRequest for the specified origin, destination, and unit size.
    /// </summary>
    /// <param name="origin">The starting point for the pathfinding request.</param>
    /// <param name="destination">The target point for the pathfinding request.</param>
    /// <param name="unitSize">The size of each unit or step used in the pathfinding calculation.</param>
    /// <param name="request">When this method returns, contains the created AStarPathRequest if successful; otherwise, null.</param>
    /// <returns>true if the request was successfully created; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        out AStarPathRequest? request)
    {
        request = Create(origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    /// <summary>
    /// Attempts to create an A* pathfinding request between the specified origin and destination points using the default voxel size.
    /// </summary>
    /// <remarks>This method uses the voxel size from Trailblazer's configured <see cref="GridWorld"/>.
    /// Use this overload for standard pathfinding scenarios where custom voxel sizing is not required.</remarks>
    /// <param name="origin">The starting point of the path, represented as a Vector3d.</param>
    /// <param name="destination">The target point of the path, represented as a Vector3d.</param>
    /// <param name="request">When this method returns, contains the created AStarPathRequest if the operation succeeds; 
    /// otherwise, contains the default value.</param>
    /// <returns>true if the path request was successfully created; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        out AStarPathRequest? request) => TryCreate(origin, destination, TrailblazerWorldManager.VoxelSize, out request);

    /// <summary>
    /// Creates a new A* pathfinding request between the specified origin and destination positions, using the given
    /// unit size and heuristic method.
    /// </summary>
    /// <remarks>
    /// If the origin or destination cannot be mapped to valid path edge voxels, the method returns null. 
    /// The returned request may have its maximum search range set based on the start and end nodes.
    /// </remarks>
    /// <param name="origin">The starting position for the pathfinding request.</param>
    /// <param name="destination">The target position for the pathfinding request.</param>
    /// <param name="unitSize">The size of the unit for which the path is being calculated. Must be a positive value.</param>
    /// <param name="heuristic">The heuristic method to use for pathfinding. Defaults to Manhattan if not specified.</param>
    /// <param name="allowUnwalkableEndpoints">true to allow the origin or destination to be unwalkable; otherwise, false.</param>
    /// <param name="allowTraversalTransitions">true to allow traversal transitions during pathfinding; otherwise, false.</param>
    /// <returns>
    /// An AStarPathRequest representing the configured pathfinding request, or null if a valid path cannot be
    /// initialized between the specified positions.
    /// </returns>
    public static AStarPathRequest? Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        bool allowUnwalkableEndpoints = false,
        bool allowTraversalTransitions = false)
    {
        if (!SolidVoxelFinder.TryGetPathEdgeVoxels(
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
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = TrailblazerWorldManager.VoxelSize
        };

        if (PathManager.TryGetMaxSearchSize(request.StartNode, request.EndNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is AStarPathRequest other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(AStarPathRequest? other) => RequestCacheKey == other?.RequestCacheKey;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return (
            StartNode?.SpawnToken ?? 0,
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkableEndpoints,
            AllowTraversalTransitions,
            Heuristic,
            MaxClimbHeight,
            MaxPathSearchRange,
            AllowTraversalTransitions ? TraversalTransitionRegistry.RegistryVersion : 0
        ).CombineHashCodes();
    }
}
