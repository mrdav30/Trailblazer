using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// A pathfinding request used for flow field generation. Contains configuration for 
/// destination targeting, dynamic agent sizing, and walkability override. 
/// Implements value-based equality for guide pooling.
/// </summary>
public class FlowFieldPathRequest : PathRequest, IEquatable<FlowFieldPathRequest>
{
    public const int DefaultExtraFloodRange = 10;

    /// <summary>
    /// Limits how much extra distance the flood will expand after the target is reached.
    /// </summary>
    public int ExtraFloodRange { get; set; }

    private FlowFieldPathRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreateWithSize(Vector3d origin, Vector3d destination, Fixed64 unitSize, out FlowFieldPathRequest request)
    {
        request = Create(origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(Vector3d origin, Vector3d destination, out FlowFieldPathRequest request) =>
        TryCreateWithSize(origin, destination, GlobalGridManager.VoxelSize, out request);

    public static FlowFieldPathRequest Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        bool allowUnwalkable = false)
    {
        if (!VoxelFinder.TryGetPathEdgeVoxels(origin, destination, out Voxel startNode, out Voxel endNode, unitSize))
            return null;

        FlowFieldPathRequest request = new()
        {
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            AllowUnwalkable = allowUnwalkable,
            ExtraFloodRange = DefaultExtraFloodRange
        };

        if (PathManager.TryGetMaxSearchSize(request.StartNode, request.EndNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    public override bool Equals(object obj) =>
        obj is FlowFieldPathRequest other && Equals(other);

    public bool Equals(FlowFieldPathRequest other) => RequestCacheKey == other.RequestCacheKey;

    public override int GetHashCode()
    {
        // Note: For FlowFields we don't care about the start voxel (only that the FlowField contains it)
        return (
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkable,
            ExtraFloodRange,
            MaxPathSearchRange
        ).CombineHashCodes();
    }
}
