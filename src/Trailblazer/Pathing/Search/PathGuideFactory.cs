using GridForge.Grids;
using SwiftCollections;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides access to pooled and reusable IGuide instances for the built-in pathing strategies.
/// Handles guide request routing, instantiation, and lifecycle management.
/// </summary>
public static class PathGuideFactory
{
    /// <summary>
    /// The number of frames after which unused guides are considered stale and eligible for eviction from the pool.
    /// This helps prevent memory bloat from guides that are rarely used but still occupy cache space.
    /// Adjust this value based on typical pathfinding usage patterns and acceptable memory overhead in your application.
    /// </summary>
    private const int MaxFramesUnused = 600;

    /// <summary>
    /// A shared cache for A* survey results, keyed by request parameters. 
    /// This allows for efficient reuse of recently computed paths without needing to re-run the A* algorithm for identical requests.
    /// </summary>
    private static readonly ReusableSurveyResultCache<AStarSurveyResult> _cachedAStarResults = new();

    /// <summary>
    /// Returns the number of active (pooled or in-use) A* results currently tracked.
    /// </summary>
    public static int TotalAStarGuideCount => _cachedAStarResults.Count;

    /// <summary>
    /// Returns only the number of active (in-use) A* results currently tracked.
    /// </summary>
    public static int InUseAStarGuideCount => _cachedAStarResults.CountInUse;

    /// <summary>
    /// A shared cache for FlowField survey results, keyed by request parameters.
    /// This allows for efficient reuse of recently computed flow fields without needing to re-run the flow field generation for identical requests.
    /// </summary>
    private static readonly ReusableSurveyResultCache<FlowFieldSurveyResult> _cachedFlowResults = new();

    /// <summary>
    /// Returns the number of active (pooled or in-use) FlowField guides currently tracked.
    /// </summary>
    public static int TotalFlowGuideCount => _cachedFlowResults.Count;

    /// <summary>
    /// Returns only the number of active (in-use) FlowField guides currently tracked.
    /// </summary>
    public static int InUseFlowGuideCount => _cachedFlowResults.CountInUse;

    /// <summary>
    /// A shared cache for raw-volume survey results, keyed by request parameters.
    /// This allows for efficient reuse of recently computed raw-volume data without needing to re-run the volume generation for identical requests.
    /// </summary>
    private static readonly ReusableSurveyResultCache<VolumeSurveyResult> _cachedVolumeResults = new();

    /// <summary>
    /// Returns the number of active raw-volume guides currently tracked.
    /// </summary>
    public static int TotalVolumeGuideCount => _cachedVolumeResults.Count;

    /// <summary>
    /// Returns only the number of active (in-use) raw-volume guides currently tracked.
    /// </summary>
    public static int InUseVolumeGuideCount => _cachedVolumeResults.CountInUse;

    private static readonly ReusableSurveyResultCache<HybridRoutePlanSurveyResult> _cachedHybridRoutePlans = new();

    /// <summary>
    /// Returns the number of cached transition route plans currently tracked.
    /// </summary>
    public static int TotalHybridRoutePlanCount => _cachedHybridRoutePlans.Count;

    /// <summary>
    /// Returns only the number of active (in-use) hybrid route plans currently tracked.
    /// </summary>
    public static int InUseHybridRoutePlanCount => _cachedHybridRoutePlans.CountInUse;

    /// <summary>
    /// Indicates whether any pathing guides are currently pooled and available.
    /// </summary>
    public static bool IsPooling =>
        TotalAStarGuideCount > 0
        || TotalFlowGuideCount > 0
        || TotalVolumeGuideCount > 0
        || TotalHybridRoutePlanCount > 0;

    /// <summary>
    /// Indicates whether any guides are currently in use (checked out from the pool and not yet returned).
    /// </summary>
    public static bool AnyInUse =>
        InUseAStarGuideCount > 0
        || InUseFlowGuideCount > 0
        || InUseVolumeGuideCount > 0
        || InUseHybridRoutePlanCount > 0;

    /// <summary>
    /// Attempts to remove guides from the pool that haven't been used for a configured number of frames.
    /// </summary>
    /// <param name="currentFrame">The current frame index used to check guide staleness.</param>
    public static void CullExpiredGuides(int currentFrame)
    {
        if (!IsPooling) return;

        _cachedAStarResults.EvictStaleEntries(currentFrame, MaxFramesUnused);
        _cachedFlowResults.EvictStaleEntries(currentFrame, MaxFramesUnused);
        _cachedVolumeResults.EvictStaleEntries(currentFrame, MaxFramesUnused);
        _cachedHybridRoutePlans.EvictStaleEntries(currentFrame, MaxFramesUnused);
    }

    /// <summary>
    /// Requests a guide of a specific type using an already populated path request.
    /// </summary>
    /// <typeparam name="T">The concrete guide type to return.</typeparam>
    /// <param name="request">The path request with validated parameters.</param>
    /// <param name="result">The resolved guide or null if the request was invalid.</param>
    /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
    public static bool RequestGuide<T>(IPathRequest request, [NotNullWhen(true)] out T? result) where T : class, IGuide
    {
        result = default;
        if (!RequestGuide(request, out IGuide? guide) || guide is not T typedGuide)
            return false;

        result = typedGuide;
        return true;
    }

    /// <summary>
    /// Routes the path request to the appropriate guide implementation based on type.
    /// </summary>
    /// <param name="request">The polymorphic request to resolve.</param>
    /// <param name="result">The resolved guide or null if the request was invalid.</param>
    /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
    public static bool RequestGuide(IPathRequest request, [NotNullWhen(true)] out IGuide? result)
    {
        if (request?.IsValid != true)
        {
            Console.WriteLine("Request is invalid. Create or update the request before requesting a guide.");
            result = null;
            return false;
        }

        if (request is AStarPathRequest unreachableAStar
            && SolidPartitionReachability.IsProvablyUnreachable(unreachableAStar))
        {
            result = null;
            return false;
        }

        result = request switch
        {
            AStarPathRequest a => RequestAStar(a),
            FlowFieldPathRequest f => RequestFlowField(f),
            VolumePathRequest v => RequestVolume(v),
            HybridPathRequest h => RequestHybrid(h),
            _ => null,
        };
        return result != null;
    }

    /// <summary>
    /// Retrieves an A* guide from the pool or creates a new one based on the provided request.
    /// </summary>
    /// <param name="request">The configured A* pathfinding request.</param>
    /// <returns>A valid AStarGuide instance.</returns>
    public static AStarGuide? RequestAStar(AStarPathRequest request)
    {
        if (SolidPartitionReachability.IsProvablyUnreachable(request))
            return null;

        bool pathFound = _cachedAStarResults.TryGetOrCreate(request,
            () => ResolveAStarResult(request),
            out AStarSurveyResult result);

        if (!pathFound)
            return null;

        AStarGuide guide = new();
        guide.Initialize(result);
        return guide;
    }

    /// <summary>
    /// Retrieves a FlowField guide from the pool or creates a new one based on the provided request.
    /// </summary>
    /// <param name="request">The configured FlowField pathfinding request.</param>
    /// <returns>A valid FlowFieldGuide instance.</returns>
    public static FlowFieldGuide? RequestFlowField(FlowFieldPathRequest request)
    {
        if (request.AllowTraversalTransitions
            && TryGetCachedTransitionFallbackFlowPlan(request, out HybridRoutePlan? cachedRoutePlan))
        {
            FlowFieldGuide cachedGuide = new();
            return cachedGuide.InitializeStaged(cachedRoutePlan) ? cachedGuide : null;
        }

        bool pathFound = _cachedFlowResults.TryGetOrCreate(request,
            () => FlowFieldSurveyor.Shared.FindPath(request),
            out FlowFieldSurveyResult result);

        // Make sure the start voxel is within the current fields collection
        // Note: for flow fields, the world-scoped index of the start voxel is used as the key to check for path validity,
        // since the flow field is generated around the start position and may not cover the entire map.
        if (pathFound
            && result.Fields != null
            && request.StartNode != null
            && result.Fields.ContainsKey(request.StartNode.WorldIndex))
        {
            FlowFieldGuide guide = new();
            guide.Initialize(result);
            return guide;
        }

        if (pathFound)
            _cachedFlowResults.Return(result, dispose: false);

        if (!request.AllowTraversalTransitions)
            return null;

        return TryBuildTransitionFallbackFlowGuide(request, out FlowFieldGuide? fallbackGuide)
            ? fallbackGuide
            : null;
    }

    /// <summary>
    /// Retrieves a raw-volume guide from the pool or creates a new one based on the provided request.
    /// </summary>
    public static VolumeGuide? RequestVolume(VolumePathRequest request)
    {
        bool pathFound = _cachedVolumeResults.TryGetOrCreate(request,
            () => VolumeSurveyor.Shared.FindPath(request),
            out VolumeSurveyResult result);

        if (!pathFound)
            return null;

        VolumeGuide guide = new();
        guide.Initialize(result);
        return guide;
    }

    /// <summary>
    /// Builds a hybrid guide by composing cached chart and volume segment guides from a planned route request.
    /// </summary>
    private static HybridGuide? RequestHybrid(HybridPathRequest request)
    {
        HybridRoutePlan? routePlan = request.RoutePlan;
        if (routePlan == null
            || !HybridWaypointFlattener.TryBuild(
            routePlan,
            out AStarWaypoint[]? flattened,
            out _))
        {
            return null;
        }

        HybridGuide guide = new();
        return guide.Initialize(flattened!) ? guide : null;
    }

    /// <summary>
    /// Returns the guide back to its associated pool, optionally disposing it completely.
    /// </summary>
    /// <param name="guide">The guide to return to the cache.</param>
    /// <param name="dispose">Whether to destroy the guide instead of pooling it.</param>
    public static void ReturnGuide(IGuide? guide, bool dispose = false)
    {
        if (guide == null) return;

        switch (guide)
        {
            case AStarGuide a:
                _cachedAStarResults.Return(a.TrailMap, dispose);
                break;
            case FlowFieldGuide f:
                f.ReleaseStagedResources(dispose);
                if (f.FlowMap != null)
                    _cachedFlowResults.Return(f.FlowMap, dispose);
                break;
            case VolumeGuide v:
                if (v.TrailMap != null)
                    _cachedVolumeResults.Return(v.TrailMap, dispose);
                break;
        }
    }

    /// <summary>
    /// Invalidates all cached results associated with the specified chart key.
    /// </summary>
    /// <remarks>
    /// Call this method to ensure that any cached data related to the specified chart is removed and
    /// will be recalculated on the next access. 
    /// This is useful when the underlying chart data has changed and stale cache entries must be cleared.
    /// </remarks>
    /// <param name="chartKey">The unique key identifying the chart whose cached results should be invalidated. Cannot be null or empty.</param>
    public static void InvalidateCacheFor(string chartKey)
    {
        if (string.IsNullOrEmpty(chartKey)) return;

        _cachedAStarResults.InvalidateForChart(chartKey);
        _cachedFlowResults.InvalidateForChart(chartKey);
        _cachedVolumeResults.InvalidateForChart(chartKey);
        _cachedHybridRoutePlans.InvalidateForChart(chartKey);
    }

    internal static void InvalidateVolumeCache()
    {
        _cachedVolumeResults.InvalidateAll();
    }

    /// <summary>
    /// Removes all cached A*, FlowField, and Volume guides.
    /// </summary>
    public static void FlushCache(bool force = false)
    {
        if (!force && AnyInUse) return;
        _cachedAStarResults.InvalidateAll();
        _cachedFlowResults.InvalidateAll();
        _cachedVolumeResults.InvalidateAll();
        _cachedHybridRoutePlans.InvalidateAll();
    }

    private static AStarSurveyResult ResolveAStarResult(AStarPathRequest request)
    {
        AStarSurveyResult directResult = AStarSurveyor.Shared.FindPath(request);
        if (directResult.HasPath || !request.AllowTraversalTransitions)
            return directResult;

        return TryBuildTransitionFallbackAStarResult(request, out AStarSurveyResult fallbackResult)
            ? fallbackResult
            : directResult;
    }

    private static bool TryBuildTransitionFallbackAStarResult(
        AStarPathRequest request,
        out AStarSurveyResult result)
    {
        result = AStarSurveyResult.Empty;

        HybridPathRequest? hybridRequest = HybridPathRequest.CreateFromAStar(request);
        HybridRoutePlan? routePlan = hybridRequest?.RoutePlan;
        if (routePlan == null
            || routePlan.DirectedTransitions.Length == 0)
        {
            return false;
        }

        if (!HybridWaypointFlattener.TryBuild(
            routePlan,
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys))
        {
            return false;
        }

        result = AStarSurveyResult.Create(flattenedWaypoints!, chartKeys, request.RequestCacheKey);
        return true;
    }

    private static bool TryBuildTransitionFallbackFlowGuide(
        FlowFieldPathRequest request,
        [NotNullWhen(true)] out FlowFieldGuide? guide)
    {
        guide = null;

        if (!TryGetTransitionFallbackFlowPlan(request, out HybridRoutePlan? routePlan))
            return false;

        guide = new FlowFieldGuide();
        return guide.InitializeStaged(routePlan);
    }

    private static bool TryGetTransitionFallbackFlowPlan(
        FlowFieldPathRequest request,
        [NotNullWhen(true)] out HybridRoutePlan? routePlan)
    {
        routePlan = null;
        bool pathFound = _cachedHybridRoutePlans.TryGetOrCreate(
            request,
            () => ResolveTransitionFallbackFlowPlan(request),
            out HybridRoutePlanSurveyResult result);

        if (!pathFound || result.RoutePlan == null)
            return false;

        routePlan = result.RoutePlan;
        _cachedHybridRoutePlans.Return(result, dispose: false);
        return true;
    }

    private static bool TryGetCachedTransitionFallbackFlowPlan(
        FlowFieldPathRequest request,
        [NotNullWhen(true)] out HybridRoutePlan? routePlan)
    {
        routePlan = null;
        if (!_cachedHybridRoutePlans.TryCheckout(request, out HybridRoutePlanSurveyResult result))
            return false;

        try
        {
            routePlan = result.RoutePlan;
            return routePlan != null;
        }
        finally
        {
            _cachedHybridRoutePlans.Return(result, dispose: false);
        }
    }

    private static HybridRoutePlanSurveyResult ResolveTransitionFallbackFlowPlan(FlowFieldPathRequest request)
    {
        HybridPathRequest? hybridRequest = HybridPathRequest.CreateFromFlowField(request);
        HybridRoutePlan? routePlan = hybridRequest?.RoutePlan;
        if (routePlan == null
            || routePlan.DirectedTransitions.Length == 0)
        {
            return HybridRoutePlanSurveyResult.Empty;
        }

        return HybridRoutePlanSurveyResult.Create(
            routePlan,
            CollectRoutePlanChartKeys(routePlan),
            request.RequestCacheKey);
    }

    private static string[] CollectRoutePlanChartKeys(HybridRoutePlan routePlan)
    {
        if (routePlan == null || routePlan.Steps.Length == 0)
            return Array.Empty<string>();

        SwiftHashSet<string> chartKeys = new();
        for (int i = 0; i < routePlan.Steps.Length; i++)
        {
            HybridRouteStep step = routePlan.Steps[i];
            if (step.Kind != HybridRouteStepKind.PathSegment)
                continue;

            AddRequestEndpointChartOwners(chartKeys, step.SegmentRequest);
        }

        if (chartKeys.Count == 0)
            return Array.Empty<string>();

        string[] result = new string[chartKeys.Count];
        int index = 0;
        foreach (string chartKey in chartKeys)
            result[index++] = chartKey;

        return result;
    }

    private static void AddRequestEndpointChartOwners(SwiftHashSet<string> chartKeys, IPathRequest request)
    {
        if (request == null)
            return;

        AddVoxelChartOwners(chartKeys, request.StartNode);
        AddVoxelChartOwners(chartKeys, request.EndNode);
    }

    private static void AddVoxelChartOwners(SwiftHashSet<string> chartKeys, Voxel? voxel)
    {
        if (voxel == null)
            return;

        if (voxel.TryGetPartition(out SolidChartPartition? solidPartition) && solidPartition != null)
            ChartOwnerUtility.AddOwners(chartKeys, solidPartition.ChartOwners);

        if (voxel.TryGetPartition(out VolumeChartPartition? volumePartition) && volumePartition != null)
            ChartOwnerUtility.AddOwners(chartKeys, volumePartition.ChartOwners);
    }
}
