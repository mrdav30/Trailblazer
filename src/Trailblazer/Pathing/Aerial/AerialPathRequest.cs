using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents a 3D guided travel request for aerial locomotion.
/// </summary>
/// <remarks>
/// Aerial requests resolve directly against raw voxels instead of chart partitions, which allows flight
/// to path through grid volumes that do not have a navigation chart attached.
/// </remarks>
public sealed class AerialPathRequest : IPathRequest, IEquatable<AerialPathRequest>
{
    public Vector3d Origin { get; private set; }

    public Voxel StartNode { get; private set; }

    public Vector3d TargetPosition { get; private set; }

    public Voxel EndNode { get; private set; }

    public Fixed64 UnitSize { get; private set; }

    public bool AllowUnwalkable { get; set; }

    public int MaxPathSearchRange { get; set; }

    public HeuristicMethod Heuristic { get; set; }

    public bool HasOrigin => StartNode != null;

    public bool HasDestination => EndNode != null;

    public bool HasValidEndpoints => HasOrigin && HasDestination;

    public bool IsValid => HasValidEndpoints && MaxPathSearchRange > 0;

    public bool HasZeroDisplacement =>
        !IsValid
        || StartNode == EndNode;

    public int RequestCacheKey => GetHashCode();

    private AerialPathRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        out AerialPathRequest request)
    {
        request = Create(origin, destination, unitSize);
        return request != null;
    }

    public static AerialPathRequest Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Euclidean,
        bool allowUnwalkable = false)
    {
        if (!RawVoxelFinder.TryGetPathEdgeVoxels(
            origin,
            destination,
            out Voxel startNode,
            out Voxel endNode,
            unitSize,
            allowUnwalkable))
        {
            return null;
        }

        var request = new AerialPathRequest
        {
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkable = allowUnwalkable
        };

        if (PathManager.TryGetMaxSearchSize(request.StartNode, request.EndNode, out int searchSize))
            request.MaxPathSearchRange = searchSize;

        return request;
    }

    public bool UpdateRequest(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize)
    {
        Fixed64 resolvedUnitSize = unitSize ?? GlobalGridManager.VoxelSize;
        bool hasEndpoints = RawVoxelFinder.TryGetPathEdgeVoxels(
            origin,
            destination,
            out Voxel startNode,
            out Voxel endNode,
            resolvedUnitSize,
            AllowUnwalkable);

        Origin = origin;
        TargetPosition = destination;
        StartNode = hasEndpoints ? startNode : null;
        EndNode = hasEndpoints ? endNode : null;
        UnitSize = resolvedUnitSize;
        MaxPathSearchRange = 0;

        if (hasEndpoints && PathManager.TryGetMaxSearchSize(StartNode, EndNode, out int searchSize))
            MaxPathSearchRange = searchSize;

        return HasValidEndpoints;
    }

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null)
            return false;

        if (!RawVoxelFinder.GetStartVoxel(
            origin,
            TargetPosition,
            out Voxel startNode,
            AllowUnwalkable,
            UnitSize))
        {
            return false;
        }

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

        if (!RawVoxelFinder.GetEndVoxel(
            Origin,
            destination,
            out Voxel endNode,
            AllowUnwalkable,
            UnitSize))
        {
            return false;
        }

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
        if (UnitSize == unitSize)
            return false;

        UnitSize = unitSize;
        return HasValidEndpoints;
    }

    public override bool Equals(object obj) =>
        obj is AerialPathRequest other && Equals(other);

    public bool Equals(AerialPathRequest other) =>
        other != null
        && RequestCacheKey == other.RequestCacheKey;

    public override int GetHashCode()
    {
        return (
            StartNode?.SpawnToken ?? 0,
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkable,
            Heuristic,
            MaxPathSearchRange
        ).CombineHashCodes();
    }
}
