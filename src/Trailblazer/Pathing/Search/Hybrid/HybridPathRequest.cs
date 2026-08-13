//=======================================================================
// HybridPathRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Grids;

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
    private int _maxPathSearchRange;
    private PathRequestCacheKey _requestCacheKey;

    #region Properties

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
    public bool AllowUnwalkableEndpoints { get; private set; }

    /// <inheritdoc/>
    public int MaxPathSearchRange
    {
        get => _maxPathSearchRange;
        set
        {
            if (_maxPathSearchRange == value)
                return;

            _maxPathSearchRange = value;
            RefreshCacheKey();
        }
    }

    public HeuristicMethod Heuristic { get; private set; }

    public Fixed64 MaxClimbHeight { get; private set; }

    public int ExtraFloodRange { get; private set; }

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
        || (StartNode == EndNode
            && (RoutePlan?.DirectedTransitions.Length ?? 0) == 0);

    /// <inheritdoc/>
    public PathRequestCacheKey RequestCacheKey => _requestCacheKey;

    internal HybridRoutePlan? RoutePlan { get; private set; }

    internal HybridChartRequestKind ChartRequestKind { get; private set; }

    #endregion

    #region Construction and Initialization

    /// <summary>
    /// Private constructor to enforce the use of factory methods for creating instances of HybridPathRequest.
    /// </summary>
    private HybridPathRequest() { }

    /// <summary>
    /// Attempts to create a new context-bound HybridPathRequest with the specified parameters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        [NotNullWhen(true)] out HybridPathRequest? request,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        Fixed64? maxClimbHeight = null,
        bool allowUnwalkableEndpoints = false)
    {
        request = Create(context, origin, destination, unitSize, heuristic, maxClimbHeight, allowUnwalkableEndpoints);
        return request != null;
    }

    /// <summary>
    /// Creates a new context-bound HybridPathRequest with the specified parameters.
    /// </summary>
    public static HybridPathRequest? Create(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize,
        HeuristicMethod heuristic = HeuristicMethod.Manhattan,
        Fixed64? maxClimbHeight = null,
        bool allowUnwalkableEndpoints = false)
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

        var request = new HybridPathRequest
        {
            Context = context,
            Origin = origin,
            StartNode = startNode,
            TargetPosition = destination,
            EndNode = endNode,
            UnitSize = unitSize,
            ChartRequestKind = HybridChartRequestKind.AStar,
            Heuristic = heuristic,
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints,
            MaxClimbHeight = maxClimbHeight ?? context.VoxelSize
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
    internal static HybridPathRequest? CreateFromAStar(AStarPathRequest request)
    {
        if (request == null || !request.HasValidEndpoints)
            return null;

        var hybridRequest = new HybridPathRequest
        {
            Context = request.Context,
            Origin = request.Origin,
            StartNode = request.StartNode,
            TargetPosition = request.TargetPosition,
            EndNode = request.EndNode,
            UnitSize = request.UnitSize,
            ChartRequestKind = HybridChartRequestKind.AStar,
            Heuristic = request.Heuristic,
            AllowUnwalkableEndpoints = request.AllowUnwalkableEndpoints,
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
    internal static HybridPathRequest? CreateFromFlowField(FlowFieldPathRequest request)
    {
        if (request == null || !request.HasValidEndpoints)
            return null;

        var hybridRequest = new HybridPathRequest
        {
            Context = request.Context,
            Origin = request.Origin,
            StartNode = request.StartNode,
            TargetPosition = request.TargetPosition,
            EndNode = request.EndNode,
            UnitSize = request.UnitSize,
            ChartRequestKind = HybridChartRequestKind.FlowField,
            AllowUnwalkableEndpoints = request.AllowUnwalkableEndpoints,
            MaxClimbHeight = request.MaxClimbHeight,
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
        Fixed64 resolvedUnitSize = unitSize ?? Context.VoxelSize;
        bool success = SolidVoxelFinder.TryGetPathEdgeVoxels(
            Context,
            origin,
            destination,
            out Voxel? startVoxel,
            out Voxel? endVoxel,
            resolvedUnitSize,
            AllowUnwalkableEndpoints);

        Origin = origin;
        TargetPosition = destination;
        StartNode = startVoxel;
        EndNode = endVoxel;
        UnitSize = resolvedUnitSize;

        if (!success || startVoxel == null || endVoxel == null)
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

        if (!SolidVoxelFinder.GetStartVoxel(
            Context,
            origin,
            TargetPosition,
            out Voxel? startNode,
            AllowUnwalkableEndpoints,
            UnitSize))
        {
            return false;
        }

        if (startNode == null)
            return false;

        Origin = origin;
        StartNode = startNode;
        return RebuildPlan();
    }

    /// <inheritdoc/>
    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null)
            return false;

        if (!SolidVoxelFinder.GetEndVoxel(
            Context,
            Origin,
            destination,
            out Voxel? endNode,
            AllowUnwalkableEndpoints,
            UnitSize))
        {
            return false;
        }

        if (endNode == null)
            return false;

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
    public override bool Equals(object? obj) =>
        obj is HybridPathRequest other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(HybridPathRequest? other) =>
        other != null
        && RequestCacheKey == other.RequestCacheKey;

    /// <summary>
    /// Generates a hash code for the current path request based on its properties and route plan.
    /// This hash code is used for caching and guide pooling, allowing for efficient retrieval of guides based on request parameters.
    /// </summary>
    /// <returns>A hash code representing the current path request.</returns>
    public override int GetHashCode() => RequestCacheKey.GetHashCode();

    /// <summary>
    /// Rebuilds the route plan for the current request using the HybridRoutePlanner.
    /// This method is called whenever the request parameters are updated (e.g. origin, destination, unit size) to ensure that the route plan reflects the current state of the request.
    /// </summary>
    /// <returns>True if the route plan was successfully rebuilt; otherwise, false.</returns>
    internal bool RebuildPlan()
    {
        RoutePlan = null;
        MaxPathSearchRange = 0;
        _requestCacheKey = default;

        if (!HasValidEndpoints)
            return false;

        HybridRoutePlan? plan;
        using (PathManager.EnterState(Context.Pathing.State))
        {
            if (!HybridRoutePlanner.TryPlan(this, out plan)
                || plan == null)
                return false;
        }

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

    private void RefreshCacheKey()
    {
        HybridRoutePlan? plan = RoutePlan;
        if (plan == null || !HasValidEndpoints || MaxPathSearchRange <= 0)
        {
            _requestCacheKey = default;
            return;
        }

        _requestCacheKey = PathRequestCacheKey.CreateHybrid(
            StartNode!.WorldIndex,
            EndNode!.WorldIndex,
            UnitSize,
            ChartRequestKind,
            AllowUnwalkableEndpoints,
            Heuristic,
            MaxClimbHeight,
            ExtraFloodRange,
            MaxPathSearchRange,
            plan.DirectedTransitions,
            Context.Pathing.State.TransitionRegistryState.RegistryVersion,
            Context.Pathing.State.VolumeRulesState.RegistryVersion,
            Origin,
            TargetPosition);
    }
}
