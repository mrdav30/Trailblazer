using FixedMathSharp;
using GridForge.Grids;
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
    /// Attempts to create a new context-bound flow field path request using the specified origin, destination, and unit size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreateWithSize(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        [NotNullWhen(true)] out FlowFieldPathRequest? request)
    {
        request = Create(context, origin, destination, unitSize);
        if (request == null)
            return false;
        return true;
    }

    /// <summary>
    /// Attempts to create a new context-bound flow field path request using the context's voxel size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        [NotNullWhen(true)] out FlowFieldPathRequest? request) =>
        TryCreateWithSize(context, origin, destination, context.VoxelSize, out request);

    /// <summary>
    /// Creates a context-bound flow field path request.
    /// </summary>
    public static FlowFieldPathRequest? Create(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
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

        FlowFieldPathRequest request = new()
        {
            Context = context,
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            AllowTraversalTransitions = allowTraversalTransitions,
            MaxClimbHeight = context.VoxelSize,
            ExtraFloodRange = DefaultExtraFloodRange
        };

        if (context.Pathing.TryGetMaxSearchSize(startNode, endNode, out int searchSize))
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
        PathRequestHashBuilder hash = PathRequestHashBuilder.Create();
        hash.Add(EndNode?.SpawnToken ?? 0);
        hash.Add(UnitSize.GetHashCode());
        hash.Add(AllowUnwalkableEndpoints);
        hash.Add(AllowTraversalTransitions);
        hash.Add(MaxClimbHeight.GetHashCode());
        hash.Add(ExtraFloodRange);
        hash.Add(MaxPathSearchRange);
        hash.Add(AllowTraversalTransitions ? Context.Pathing.State.TransitionRegistryState.RegistryVersion : 0);
        return hash.ToHashCode();
    }
}
