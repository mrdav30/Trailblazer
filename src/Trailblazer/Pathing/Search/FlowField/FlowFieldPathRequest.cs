using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Diagnostics.CodeAnalysis;
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
    /// The maximum Y-axis height delta a unit can step or climb per voxel while the field is built.
    /// Voxels exceeding this are ignored even if walkable and adjacent.
    /// </summary>
    public Fixed64 MaxClimbHeight { get; set; }

    /// <summary>
    /// Limits how much extra distance the flood will expand after the target is reached.
    /// </summary>
    public int ExtraFloodRange { get; set; }

    private FlowFieldPathRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreateWithSize(Vector3d origin, Vector3d destination, Fixed64 unitSize, [NotNullWhen(true)] out FlowFieldPathRequest? request)
    {
        request = Create(origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(Vector3d origin, Vector3d destination, [NotNullWhen(true)] out FlowFieldPathRequest? request) =>
        TryCreateWithSize(origin, destination, TrailblazerWorldManager.VoxelSize, out request);

    public static FlowFieldPathRequest? Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
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

        FlowFieldPathRequest request = new()
        {
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = TrailblazerWorldManager.VoxelSize,
            ExtraFloodRange = DefaultExtraFloodRange
        };

        if (PathManager.TryGetMaxSearchSize(startNode, endNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    public override bool Equals(object? obj) =>
        obj is FlowFieldPathRequest other && Equals(other);

    public bool Equals(FlowFieldPathRequest? other) =>
        other != null && RequestCacheKey == other.RequestCacheKey;

    public override int GetHashCode()
    {
        // Note: For FlowFields we don't care about the start voxel (only that the FlowField contains it)
        return (
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkableEndpoints,
            AllowTraversalTransitions,
            MaxClimbHeight,
            ExtraFloodRange,
            MaxPathSearchRange,
            AllowTraversalTransitions ? TraversalTransitionRegistry.RegistryVersion : 0
        ).CombineHashCodes();
    }
}
