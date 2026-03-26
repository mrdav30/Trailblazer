using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

public enum HeuristicMethod
{
    Manhattan,
    Octile,
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

    public HeuristicMethod Heuristic { get; set; }

    // Prevent external use of the default constructor to ensure proper initialization through factory methods.
    private AStarPathRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        out AStarPathRequest request)
    {
        request = Create(origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        out AStarPathRequest request) => TryCreate(origin, destination, GlobalGridManager.VoxelSize, out request);

    public static AStarPathRequest Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        bool allowUnwalkableEndNode = false,
        bool allowTraversalTransitions = false)
    {
        if (!VoxelFinder.TryGetPathEdgeVoxels(
            origin,
            destination,
            out Voxel startNode,
            out Voxel endNode,
            unitSize,
            allowUnwalkableEndNode))
        {
            return null;
        }

        AStarPathRequest request = new()
        {
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkableEndNode = allowUnwalkableEndNode,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = GlobalGridManager.VoxelSize
        };

        if (PathManager.TryGetMaxSearchSize(request.StartNode, request.EndNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    public override bool Equals(object obj) =>
        obj is AStarPathRequest other && Equals(other);

    public bool Equals(AStarPathRequest other) => RequestCacheKey == other.RequestCacheKey;

    public override int GetHashCode()
    {
        return (
            StartNode?.SpawnToken ?? 0,
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkableEndNode,
            AllowTraversalTransitions,
            Heuristic,
            MaxClimbHeight,
            MaxPathSearchRange,
            AllowTraversalTransitions ? TraversalTransitionRegistry.RegistryVersion : 0
        ).CombineHashCodes();
    }
}
