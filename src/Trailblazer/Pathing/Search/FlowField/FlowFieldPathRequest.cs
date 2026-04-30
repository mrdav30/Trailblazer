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
    /// <summary>
    /// Specifies the default value for the extra flood range used in calculations or operations that require an
    /// additional range parameter.
    /// </summary>
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

    /// <summary>
    /// Attempts to create a new flow field path request using the specified origin, destination, and unit size.
    /// </summary>
    /// <param name="origin">The starting point of the path in world coordinates.</param>
    /// <param name="destination">The target point of the path in world coordinates.</param>
    /// <param name="unitSize">The size of each unit or cell in the flow field. Must be a positive value.</param>
    /// <param name="request">When this method returns, contains the created flow field path request if successful; otherwise, null.</param>
    /// <returns>true if the flow field path request was successfully created; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreateWithSize(Vector3d origin, Vector3d destination, Fixed64 unitSize, [NotNullWhen(true)] out FlowFieldPathRequest? request)
    {
        request = Create(origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    /// <summary>
    /// Attempts to create a new flow field path request between the specified origin and destination points using the
    /// default voxel size.
    /// </summary>
    /// <param name="origin">The starting point of the path in world coordinates.</param>
    /// <param name="destination">The target point of the path in world coordinates.</param>
    /// <param name="request">When this method returns <see langword="true"/>, contains the created flow field path request; otherwise, <see
    /// langword="null"/>.</param>
    /// <returns><see langword="true"/> if the path request was successfully created; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(Vector3d origin, Vector3d destination, [NotNullWhen(true)] out FlowFieldPathRequest? request) =>
        TryCreateWithSize(origin, destination, TrailblazerWorldManager.VoxelSize, out request);

    /// <summary>
    /// Creates a new flow field path request between the specified origin and destination positions, using the given
    /// unit size and traversal options.
    /// </summary>
    /// <remarks>
    /// If the origin or destination cannot be mapped to valid voxels, or if a path cannot be
    /// established, the method returns null. The returned request includes additional configuration such as maximum
    /// climb height and extra flood range, which are set to default values.
    /// </remarks>
    /// <param name="origin">The starting position for the path request.</param>
    /// <param name="destination">The target position for the path request.</param>
    /// <param name="unitSize">The size of the unit used for pathfinding calculations. Must be a positive value.</param>
    /// <param name="allowUnwalkableEndpoints">true to allow the origin or destination to be on unwalkable terrain; otherwise, false.</param>
    /// <param name="allowTraversalTransitions">true to allow traversal transitions during pathfinding; otherwise, false.</param>
    /// <returns>A FlowFieldPathRequest representing the path request if a valid path can be established; otherwise, null.</returns>
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

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FlowFieldPathRequest other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(FlowFieldPathRequest? other) =>
        other != null && RequestCacheKey == other.RequestCacheKey;

    /// <inheritdoc/>
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
