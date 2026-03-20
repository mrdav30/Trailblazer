using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

internal enum HybridRouteStepKind
{
    PathSegment,
    Waypoint
}

internal sealed class HybridRouteStep
{
    public HybridRouteStepKind Kind { get; private set; }

    public IPathRequest SegmentRequest { get; private set; }

    public Vector3d WaypointPosition { get; private set; }

    public int AdditionalCost { get; private set; }

    public static HybridRouteStep Segment(IPathRequest request, int additionalCost = 0) => new()
    {
        Kind = HybridRouteStepKind.PathSegment,
        SegmentRequest = request,
        AdditionalCost = additionalCost
    };

    public static HybridRouteStep Waypoint(Vector3d position, int additionalCost = 0) => new()
    {
        Kind = HybridRouteStepKind.Waypoint,
        WaypointPosition = position,
        AdditionalCost = additionalCost
    };
}

internal sealed class HybridRoutePlan
{
    public HybridRoutePlan(
        HybridRouteStep[] steps,
        TraversalTransition[] directedTransitions,
        int totalPathCost)
    {
        Steps = steps ?? Array.Empty<HybridRouteStep>();
        DirectedTransitions = directedTransitions ?? Array.Empty<TraversalTransition>();
        TotalPathCost = totalPathCost;
    }

    public HybridRouteStep[] Steps { get; }

    public TraversalTransition[] DirectedTransitions { get; }

    public int TotalPathCost { get; }
}

/// <summary>
/// Represents a narrow hybrid route request that may bridge chart-backed traversal through explicit transitions.
/// </summary>
/// <remarks>
/// The current implementation supports:
/// chart -> chart direct paths,
/// chart -> transition -> chart,
/// chart -> transition -> volume -> transition -> chart.
/// </remarks>
public sealed class HybridPathRequest : IPathRequest, IEquatable<HybridPathRequest>
{
    public Vector3d Origin { get; private set; }

    public Voxel StartNode { get; private set; }

    public Vector3d TargetPosition { get; private set; }

    public Voxel EndNode { get; private set; }

    public Fixed64 UnitSize { get; private set; }

    public bool AllowUnwalkable { get; set; }

    public int MaxPathSearchRange { get; set; }

    public HeuristicMethod Heuristic { get; set; }

    public Fixed64 MaxClimbHeight { get; set; }

    public bool HasOrigin => StartNode != null;

    public bool HasDestination => EndNode != null;

    public bool HasValidEndpoints => HasOrigin && HasDestination;

    public bool IsValid => HasValidEndpoints && MaxPathSearchRange > 0 && RoutePlan != null;

    public bool HasZeroDisplacement =>
        !IsValid
        || StartNode == EndNode && RoutePlan.DirectedTransitions.Length == 0;

    public int RequestCacheKey => GetHashCode();

    internal HybridRoutePlan RoutePlan { get; private set; }

    private HybridPathRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        out HybridPathRequest request,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        Fixed64? maxClimbHeight = null,
        bool allowUnwalkable = false)
    {
        request = Create(origin, destination, unitSize, heuristic, maxClimbHeight, allowUnwalkable);
        return request != null;
    }

    public static HybridPathRequest Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        Fixed64? maxClimbHeight = null,
        bool allowUnwalkable = false)
    {
        if (!VoxelFinder.TryGetPathEdgeVoxels(origin, destination, out Voxel startNode, out Voxel endNode, unitSize))
            return null;

        var request = new HybridPathRequest
        {
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            Heuristic = heuristic,
            AllowUnwalkable = allowUnwalkable,
            MaxClimbHeight = maxClimbHeight ?? GlobalGridManager.VoxelSize
        };

        if (!request.RebuildPlan())
            return null;

        return request;
    }

    public bool UpdateRequest(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize)
    {
        Fixed64 resolvedUnitSize = unitSize ?? GlobalGridManager.VoxelSize;
        bool success = VoxelFinder.TryGetPathEdgeVoxels(
            origin,
            destination,
            out Voxel startVoxel,
            out Voxel endVoxel,
            resolvedUnitSize);

        Origin = origin;
        TargetPosition = destination;
        StartNode = startVoxel;
        EndNode = endVoxel;
        UnitSize = resolvedUnitSize;

        if (!success)
        {
            RoutePlan = null;
            MaxPathSearchRange = 0;
            return false;
        }

        return RebuildPlan();
    }

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null)
            return false;

        if (!VoxelFinder.GetStartVoxel(
            origin,
            TargetPosition,
            out Voxel startNode,
            AllowUnwalkable,
            UnitSize))
        {
            return false;
        }

        Origin = origin;
        StartNode = startNode;
        return RebuildPlan();
    }

    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null)
            return false;

        if (!VoxelFinder.GetEndVoxel(
            Origin,
            destination,
            out Voxel endNode,
            AllowUnwalkable,
            UnitSize))
        {
            return false;
        }

        TargetPosition = destination;
        EndNode = endNode;
        return RebuildPlan();
    }

    public bool TrySetUnitSize(Fixed64 unitSize)
    {
        if (UnitSize == unitSize)
            return false;

        return UpdateRequest(Origin, TargetPosition, unitSize);
    }

    public override bool Equals(object obj) =>
        obj is HybridPathRequest other && Equals(other);

    public bool Equals(HybridPathRequest other) =>
        other != null
        && RequestCacheKey == other.RequestCacheKey;

    public override int GetHashCode()
    {
        int transitionHash = 17;
        if (RoutePlan != null)
        {
            for (int i = 0; i < RoutePlan.DirectedTransitions.Length; i++)
                transitionHash = HashCode.Combine(transitionHash, RoutePlan.DirectedTransitions[i].Id);
        }

        return (
            StartNode?.SpawnToken ?? 0,
            EndNode?.SpawnToken ?? 0,
            UnitSize,
            AllowUnwalkable,
            Heuristic,
            MaxClimbHeight,
            MaxPathSearchRange,
            transitionHash
        ).CombineHashCodes();
    }

    internal bool RebuildPlan()
    {
        RoutePlan = null;
        MaxPathSearchRange = 0;

        if (!HasValidEndpoints)
            return false;

        if (!HybridRoutePlanner.TryPlan(this, out HybridRoutePlan plan))
            return false;

        RoutePlan = plan;

        int totalSearchRange = 0;
        for (int i = 0; i < plan.Steps.Length; i++)
        {
            if (plan.Steps[i].Kind == HybridRouteStepKind.PathSegment)
                totalSearchRange += plan.Steps[i].SegmentRequest.MaxPathSearchRange;
        }

        MaxPathSearchRange = totalSearchRange > 0 ? totalSearchRange : 1;
        return true;
    }
}
