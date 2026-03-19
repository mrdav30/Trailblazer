using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Represents a direct 3D guided travel request for aerial locomotion.
/// </summary>
/// <remarks>
/// This request does not build or cache a voxel path. It only validates that the origin and target
/// remain inside registered grids so steering can guide the navigator directly through world space.
/// </remarks>
public sealed class AerialPathRequest : IPathRequest, IEquatable<AerialPathRequest>
{
    private const int DirectTravelSearchRange = 1;

    public Vector3d Origin { get; private set; }

    public Voxel StartNode { get; private set; }

    public Vector3d TargetPosition { get; private set; }

    public Voxel EndNode { get; private set; }

    public Fixed64 UnitSize { get; private set; }

    public bool AllowUnwalkable { get; set; }

    public int MaxPathSearchRange { get; set; }

    public bool HasOrigin => StartNode != null;

    public bool HasDestination => EndNode != null;

    public bool HasValidEndpoints => HasOrigin && HasDestination;

    public bool IsValid => HasValidEndpoints && MaxPathSearchRange > 0;

    public bool HasZeroDisplacement =>
        !IsValid
        || (TargetPosition - Origin).SqrMagnitude <= Fixed64.Epsilon;

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
        bool allowUnwalkable = false)
    {
        var request = new AerialPathRequest
        {
            AllowUnwalkable = allowUnwalkable
        };

        if (!request.UpdateRequest(origin, destination, unitSize))
            return null;

        return request;
    }

    public bool UpdateRequest(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize)
    {
        bool hasOrigin = TryResolveVoxel(origin, out Voxel startNode);
        bool hasDestination = TryResolveVoxel(destination, out Voxel endNode);

        Origin = origin;
        TargetPosition = destination;
        StartNode = hasOrigin ? startNode : null;
        EndNode = hasDestination ? endNode : null;
        UnitSize = unitSize ?? GlobalGridManager.VoxelSize;
        MaxPathSearchRange = hasOrigin && hasDestination
            ? DirectTravelSearchRange
            : 0;

        return HasValidEndpoints;
    }

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (!TryResolveVoxel(origin, out Voxel startNode))
            return false;

        Origin = origin;
        StartNode = startNode;

        if (resetSearchRange || MaxPathSearchRange <= 0)
            MaxPathSearchRange = HasDestination
                ? DirectTravelSearchRange
                : 0;

        return true;
    }

    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (!TryResolveVoxel(destination, out Voxel endNode))
            return false;

        TargetPosition = destination;
        EndNode = endNode;

        if (resetSearchRange || MaxPathSearchRange <= 0)
            MaxPathSearchRange = HasOrigin
                ? DirectTravelSearchRange
                : 0;

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
        && Origin == other.Origin
        && TargetPosition == other.TargetPosition
        && UnitSize == other.UnitSize
        && AllowUnwalkable == other.AllowUnwalkable;

    public override int GetHashCode()
    {
        return (
            Origin,
            TargetPosition,
            UnitSize,
            AllowUnwalkable
        ).CombineHashCodes();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryResolveVoxel(Vector3d position, out Voxel voxel)
    {
        return GlobalGridManager.TryGetGridAndVoxel(position, out _, out voxel);
    }
}
