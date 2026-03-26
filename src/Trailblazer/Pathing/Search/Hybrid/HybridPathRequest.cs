using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Internal adapter request used to build staged transition-aware routes from normal chart-backed request intent.
/// </summary>
/// <remarks>
/// The current implementation supports:
/// chart -> chart direct paths,
/// chart -> transition -> chart,
/// chart -> transition -> volume -> transition -> chart.
/// </remarks>
internal sealed class HybridPathRequest : IPathRequest, IEquatable<HybridPathRequest>
{
    #region Properties

    /// <inheritdoc/>
    public Vector3d Origin { get; private set; }

    /// <inheritdoc/>
    public Voxel StartNode { get; private set; }

    /// <inheritdoc/>
    public Vector3d TargetPosition { get; private set; }

    /// <inheritdoc/>
    public Voxel EndNode { get; private set; }

    /// <inheritdoc/>
    public Fixed64 UnitSize { get; private set; }

    /// <inheritdoc/>
    public bool AllowUnwalkableEndNode { get; set; }

    /// <inheritdoc/>
    public int MaxPathSearchRange { get; set; }

    public HeuristicMethod Heuristic { get; set; }

    public Fixed64 MaxClimbHeight { get; set; }

    public int ExtraFloodRange { get; set; }

    /// <inheritdoc/>
    public bool HasOrigin => StartNode != null;

    /// <inheritdoc/>
    public bool HasDestination => EndNode != null;

    /// <inheritdoc/>
    public bool HasValidEndpoints => HasOrigin && HasDestination;

    /// <inheritdoc/>
    public bool IsValid => HasValidEndpoints && MaxPathSearchRange > 0 && RoutePlan != null;

    /// <inheritdoc/>
    public bool HasZeroDisplacement =>
        !IsValid
        || StartNode == EndNode && RoutePlan.DirectedTransitions.Length == 0;

    /// <inheritdoc/>
    public int RequestCacheKey => GetHashCode();

    internal HybridRoutePlan RoutePlan { get; private set; }

    internal HybridChartRequestKind ChartRequestKind { get; private set; }

    #endregion

    #region Construction and Initialization

    /// <summary>
    /// Private constructor to enforce the use of factory methods for creating instances of HybridPathRequest.
    /// </summary>
    private HybridPathRequest() { }

    /// <summary>
    /// Attempts to create a new HybridPathRequest with the specified parameters.
    /// </summary>
    /// <param name="origin">The starting position of the path request.</param>
    /// <param name="destination">The target position of the path request.</param>
    /// <param name="unitSize">The size of the unit for pathfinding.</param>
    /// <param name="request">The resulting HybridPathRequest if creation is successful.</param>
    /// <param name="heuristic">The heuristic method to use for pathfinding.</param>
    /// <param name="maxClimbHeight">The maximum climb height for the pathfinding unit.</param>
    /// <param name="allowUnwalkableEndNode">Whether to allow paths to unwalkable areas.</param>
    /// <returns>True if the request was successfully created; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        out HybridPathRequest request,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        Fixed64? maxClimbHeight = null,
        bool allowUnwalkableEndNode = false)
    {
        request = Create(origin, destination, unitSize, heuristic, maxClimbHeight, allowUnwalkableEndNode);
        return request != null;
    }

    /// <summary>
    /// Creates a new HybridPathRequest with the specified parameters.
    /// </summary>
    /// <param name="origin">The starting position of the path request.</param>
    /// <param name="destination">The target position of the path request.</param>
    /// <param name="unitSize">The size of the unit for pathfinding.</param>
    /// <param name="heuristic">The heuristic method to use for pathfinding.</param>
    /// <param name="maxClimbHeight">The maximum climb height for the pathfinding unit.</param>
    /// <param name="allowUnwalkableEndNode">Whether to allow paths to unwalkable areas.</param>
    /// <returns>The created HybridPathRequest if successful; otherwise, null.</returns>
    public static HybridPathRequest Create(
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        Fixed64? maxClimbHeight = null,
        bool allowUnwalkableEndNode = false)
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

        var request = new HybridPathRequest
        {
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            ChartRequestKind = HybridChartRequestKind.AStar,
            Heuristic = heuristic,
            AllowUnwalkableEndNode = allowUnwalkableEndNode,
            MaxClimbHeight = maxClimbHeight ?? GlobalGridManager.VoxelSize
        };

        if (!request.RebuildPlan())
            return null;

        return request;
    }

    /// <summary>
    /// Creates a new HybridPathRequest based on an existing AStarPathRequest. 
    /// This factory method is used to convert a standard A* path request into a hybrid request that can be processed by the hybrid pathfinding system, allowing for more complex routing that may involve transitions and multiple pathfinding strategies.
    ///  The method checks the validity of the input request and attempts to build a corresponding route plan for the hybrid system, returning null if the conversion fails or if the input request is invalid.
    /// </summary>
    /// <param name="request">The AStarPathRequest to convert into a HybridPathRequest.</param>
    /// <returns>The created HybridPathRequest if successful; otherwise, null.</returns>
    internal static HybridPathRequest CreateFromAStar(AStarPathRequest request)
    {
        if (request == null || !request.HasValidEndpoints)
            return null;

        var hybridRequest = new HybridPathRequest
        {
            Origin = request.Origin,
            StartNode = request.StartNode,
            TargetPosition = request.TargetPosition,
            EndNode = request.EndNode,
            UnitSize = request.UnitSize,
            ChartRequestKind = HybridChartRequestKind.AStar,
            Heuristic = request.Heuristic,
            AllowUnwalkableEndNode = request.AllowUnwalkableEndNode,
            MaxClimbHeight = request.MaxClimbHeight
        };

        return hybridRequest.RebuildPlan() ? hybridRequest : null;
    }

    /// <summary>
    /// Creates a new HybridPathRequest based on an existing FlowFieldPathRequest.
    /// This factory method is used to convert a standard flow field path request into a hybrid request that can be processed by the hybrid pathfinding system, allowing for more complex routing that may involve transitions and multiple pathfinding strategies. 
    /// The method checks the validity of the input request and attempts to build a corresponding route plan for the hybrid system, returning null if the conversion fails or if the input request is invalid.
    /// </summary>
    /// <param name="request">The FlowFieldPathRequest to convert into a HybridPathRequest.</param>
    /// <returns>The created HybridPathRequest if successful; otherwise, null.</returns>
    internal static HybridPathRequest CreateFromFlowField(FlowFieldPathRequest request)
    {
        if (request == null || !request.HasValidEndpoints)
            return null;

        var hybridRequest = new HybridPathRequest
        {
            Origin = request.Origin,
            StartNode = request.StartNode,
            TargetPosition = request.TargetPosition,
            EndNode = request.EndNode,
            UnitSize = request.UnitSize,
            ChartRequestKind = HybridChartRequestKind.FlowField,
            AllowUnwalkableEndNode = request.AllowUnwalkableEndNode,
            ExtraFloodRange = request.ExtraFloodRange
        };

        return hybridRequest.RebuildPlan() ? hybridRequest : null;
    }

    #endregion

    /// <inheritdoc/>
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
            resolvedUnitSize,
            AllowUnwalkableEndNode);

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

    /// <inheritdoc/>
    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null)
            return false;

        if (!VoxelFinder.GetStartVoxel(
            origin,
            TargetPosition,
            out Voxel startNode,
            AllowUnwalkableEndNode,
            UnitSize))
        {
            return false;
        }

        Origin = origin;
        StartNode = startNode;
        return RebuildPlan();
    }

    /// <inheritdoc/>
    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null)
            return false;

        if (!VoxelFinder.GetEndVoxel(
            Origin,
            destination,
            out Voxel endNode,
            AllowUnwalkableEndNode,
            UnitSize))
        {
            return false;
        }

        TargetPosition = destination;
        EndNode = endNode;
        return RebuildPlan();
    }

    /// <inheritdoc/>
    public bool TrySetUnitSize(Fixed64 unitSize)
    {
        if (UnitSize == unitSize)
            return false;

        return UpdateRequest(Origin, TargetPosition, unitSize);
    }

    /// <inheritdoc/>
    public override bool Equals(object obj) =>
        obj is HybridPathRequest other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(HybridPathRequest other) =>
        other != null
        && RequestCacheKey == other.RequestCacheKey;

    /// <summary>
    /// Generates a hash code for the current path request based on its properties and route plan. 
    /// This hash code is used for caching and guide pooling, allowing for efficient retrieval of guides based on request parameters. 
    /// </summary>
    /// <returns>A hash code representing the current path request.</returns>
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
            ChartRequestKind,
            AllowUnwalkableEndNode,
            Heuristic,
            MaxClimbHeight,
            ExtraFloodRange,
            MaxPathSearchRange,
            transitionHash
        ).CombineHashCodes();
    }

    /// <summary>
    /// Rebuilds the route plan for the current request using the HybridRoutePlanner. 
    /// This method is called whenever the request parameters are updated (e.g. origin, destination, unit size) to ensure that the route plan reflects the current state of the request. 
    /// </summary>
    /// <returns>True if the route plan was successfully rebuilt; otherwise, false.</returns>
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
