using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
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
    public Vector3d Origin { get; private set; }

    public Voxel? StartNode { get; private set; }

    public Vector3d TargetPosition { get; private set; }

    public Voxel? EndNode { get; private set; }

    public Fixed64 UnitSize { get; private set; }

    public bool AllowUnwalkableEndpoints { get; set; }

    public int MaxPathSearchRange { get; set; }

    public HeuristicMethod Heuristic { get; set; }

    public TraversalMedium Medium { get; private set; }

    public bool HasOrigin => StartNode != null;

    public bool HasDestination => EndNode != null;

    public bool HasValidEndpoints => HasOrigin && HasDestination;

    public bool IsValid => HasValidEndpoints && MaxPathSearchRange > 0;

    public bool HasZeroDisplacement =>
        !IsValid
        || StartNode == EndNode;

    public int RequestCacheKey => GetHashCode();

    private VolumePathRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        [NotNullWhen(true)] out VolumePathRequest? request,
        HeuristicMethod heuristic = HeuristicMethod.Euclidean,
        bool allowUnwalkableEndpoints = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        request = Create(
            origin,
            destination,
            unitSize,
            heuristic,
            allowUnwalkableEndpoints,
            medium);

        return request != null;
    }

    public static VolumePathRequest? Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Euclidean,
        bool allowUnwalkableEndpoints = false,
        TraversalMedium medium = TraversalMedium.Gas)
    {
        if (!VolumeVoxelFinder.TryGetPathEdgeVoxels(
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
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            Medium = medium
        };

        if (PathManager.TryGetMaxSearchSize(startNode, endNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    public bool UpdateRequest(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize)
    {
        Fixed64 resolvedUnitSize = unitSize ?? TrailblazerWorldManager.VoxelSize;
        bool hasEndpoints = VolumeVoxelFinder.TryGetPathEdgeVoxels(
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
            && PathManager.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
            MaxPathSearchRange = searchSize;

        return HasValidEndpoints;
    }

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null)
            return false;

        if (!VolumeVoxelFinder.GetStartVoxel(
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
            if (HasDestination && PathManager.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
                MaxPathSearchRange = searchSize;
        }

        return true;
    }

    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null)
            return false;

        if (!VolumeVoxelFinder.GetEndVoxel(
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
            if (HasOrigin && PathManager.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
                MaxPathSearchRange = searchSize;
        }

        return true;
    }

    public bool TrySetUnitSize(Fixed64 unitSize)
    {
        if (UnitSize == unitSize || !HasValidEndpoints)
            return false;

        return UpdateRequest(Origin, TargetPosition, unitSize);
    }

    public override bool Equals(object? obj) =>
        obj is VolumePathRequest other && Equals(other);

    public bool Equals(VolumePathRequest? other) =>
        other != null
        && RequestCacheKey == other.RequestCacheKey;

    public override int GetHashCode()
    {
        return (
            StartNode?.SpawnToken ?? 0,
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkableEndpoints,
            Heuristic,
            Medium,
            MaxPathSearchRange,
            VolumeMediumRules.RegistryVersion
        ).CombineHashCodes();
    }
}
