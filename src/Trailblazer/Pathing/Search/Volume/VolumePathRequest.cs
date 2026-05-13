using FixedMathSharp;
using GridForge.Grids;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents a chart-optional guided travel request through raw voxel volume.
/// </summary>
/// <remarks>
/// Volume requests resolve directly against raw voxels instead of surface partitions. Traversal membership
/// can come from authored <see cref="VolumeChartPartition"/> data, host-configured <see cref="VolumeMediumRules"/>,
/// or both, depending on the requested <see cref="Medium"/>.
/// </remarks>
public sealed class VolumePathRequest : IPathRequest, IEquatable<VolumePathRequest>
{
    /// <inheritdoc/>
    public TrailblazerWorldContext Context { get; private set; } = null!;

    /// <inheritdoc/>
    public Vector3d Origin { get; private set; }

    /// <inheritdoc/>
    public Voxel? StartNode { get; private set; }

    /// <inheritdoc/>
    public Vector3d TargetPosition { get; private set; }

    /// <inheritdoc/>
    public Voxel? EndNode { get; private set; }

    /// <inheritdoc/>
    public Fixed64 UnitSize { get; private set; }

    /// <inheritdoc/>
    public bool AllowUnwalkableEndpoints { get; set; }

    /// <inheritdoc/>
    public int MaxPathSearchRange { get; set; }

    /// <summary>
    /// Gets or sets the heuristic method used to guide the algorithm's decision-making process.
    /// </summary>
    /// <remarks>
    /// Selecting an appropriate heuristic can significantly impact the performance and accuracy of
    /// the algorithm. Refer to the documentation for HeuristicMethod for available options and their intended use
    /// cases.
    /// </remarks>
    public HeuristicMethod Heuristic { get; set; }

    /// <summary>
    /// Gets the medium used for traversal operations.
    /// </summary>
    /// <remarks>
    /// The traversal medium determines the method or environment by which traversal is performed.
    /// The value is set internally and cannot be modified directly by consumers of the class.
    /// </remarks>
    public TraversalMedium Medium { get; private set; }

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
    public int RequestCacheKey => GetHashCode();

    private VolumePathRequest() { }

    /// <summary>
    /// Attempts to create a new context-bound volume pathfinding request.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        [NotNullWhen(true)] out VolumePathRequest? request,
        HeuristicMethod heuristic = HeuristicMethod.Euclidean,
        bool allowUnwalkableEndpoints = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        request = Create(
            context,
            origin,
            destination,
            unitSize,
            heuristic,
            allowUnwalkableEndpoints,
            medium);

        return request != null;
    }

    /// <summary>
    /// Creates a context-bound volume pathfinding request.
    /// </summary>
    public static VolumePathRequest? Create(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Euclidean,
        bool allowUnwalkableEndpoints = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (!VolumeVoxelFinder.TryGetPathEdgeVoxels(
            context,
            origin,
            destination,
            out Voxel? startNode,
            out Voxel? endNode,
            unitSize,
            allowUnwalkableEndpoints,
            medium))
        {
            return null;
        }

        if (startNode == null || endNode == null)
            return null;

        var request = new VolumePathRequest
        {
            Context = context,
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            Medium = medium
        };

        if (context.Pathing.TryGetMaxSearchSize(startNode, endNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    /// <inheritdoc/>
    public bool UpdateRequest(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize)
    {
        Fixed64 resolvedUnitSize = unitSize ?? Context.VoxelSize;
        bool hasEndpoints = VolumeVoxelFinder.TryGetPathEdgeVoxels(
            Context,
            origin,
            destination,
            out Voxel? startNode,
            out Voxel? endNode,
            resolvedUnitSize,
            AllowUnwalkableEndpoints,
            Medium);

        Origin = origin;
        TargetPosition = destination;
        StartNode = hasEndpoints ? startNode : null;
        EndNode = hasEndpoints ? endNode : null;
        UnitSize = resolvedUnitSize;
        MaxPathSearchRange = 0;

        if (hasEndpoints
            && StartNode != null
            && EndNode != null
            && Context.Pathing.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
            MaxPathSearchRange = searchSize;

        return HasValidEndpoints;
    }

    /// <inheritdoc/>
    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null)
            return false;

        if (!VolumeVoxelFinder.GetStartVoxel(
            Context,
            origin,
            TargetPosition,
            out Voxel? startNode,
            AllowUnwalkableEndpoints,
            UnitSize,
            Medium))
        {
            return false;
        }

        if (startNode == null)
            return false;

        Origin = origin;

        if (StartNode != null)
        {
            if (startNode == StartNode)
                return true;

            if (startNode.GridIndex != StartNode.GridIndex)
                resetSearchRange = true;
        }

        StartNode = startNode;

        if (resetSearchRange)
        {
            MaxPathSearchRange = 0;
            if (HasDestination && Context.Pathing.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
                MaxPathSearchRange = searchSize;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null)
            return false;

        if (!VolumeVoxelFinder.GetEndVoxel(
            Context,
            Origin,
            destination,
            out Voxel? endNode,
            AllowUnwalkableEndpoints,
            UnitSize,
            Medium))
        {
            return false;
        }

        if (endNode == null)
            return false;

        TargetPosition = destination;

        if (EndNode != null)
        {
            if (endNode == EndNode)
                return true;

            if (endNode.GridIndex != EndNode.GridIndex)
                resetSearchRange = true;
        }

        EndNode = endNode;

        if (resetSearchRange)
        {
            MaxPathSearchRange = 0;
            if (HasOrigin && Context.Pathing.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
                MaxPathSearchRange = searchSize;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TrySetUnitSize(Fixed64 unitSize)
    {
        if (UnitSize == unitSize || !HasValidEndpoints)
            return false;

        return UpdateRequest(Origin, TargetPosition, unitSize);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is VolumePathRequest other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(VolumePathRequest? other) =>
        other != null
        && RequestCacheKey == other.RequestCacheKey;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        PathRequestHashBuilder hash = PathRequestHashBuilder.Create();
        hash.Add(StartNode?.SpawnToken ?? 0);
        hash.Add(EndNode?.SpawnToken ?? 0);
        hash.Add(UnitSize.GetHashCode());
        hash.Add(AllowUnwalkableEndpoints);
        hash.Add((int)Heuristic);
        hash.Add((int)Medium);
        hash.Add(MaxPathSearchRange);
        hash.Add(Context.Pathing.State.VolumeRulesState.RegistryVersion);
        return hash.ToHashCode();
    }
}
