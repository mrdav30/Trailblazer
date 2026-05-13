using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides context-scoped access to pooled and reusable IGuide instances for the built-in pathing strategies.
/// Handles guide request routing, instantiation, and lifecycle management.
/// </summary>
internal static class PathGuideFactory
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
    private static TrailblazerGuideState GuideState => PathManager.ActiveState.GuideState;

    private static AStarSurveyor _aStarSurveyor => GuideState.AStarSurveyor;

    private static FlowFieldSurveyor _flowFieldSurveyor => GuideState.FlowFieldSurveyor;

    private static VolumeSurveyor _volumeSurveyor => GuideState.VolumeSurveyor;

    private static ReusableSurveyResultCache<AStarSurveyResult> _cachedAStarResults =>
        GuideState.CachedAStarResults;

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
    private static ReusableSurveyResultCache<FlowFieldSurveyResult> _cachedFlowResults =>
        GuideState.CachedFlowResults;

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
    private static ReusableSurveyResultCache<VolumeSurveyResult> _cachedVolumeResults =>
        GuideState.CachedVolumeResults;

    /// <summary>
    /// Returns the number of active raw-volume guides currently tracked.
    /// </summary>
    public static int TotalVolumeGuideCount => _cachedVolumeResults.Count;

    /// <summary>
    /// Returns only the number of active (in-use) raw-volume guides currently tracked.
    /// </summary>
    public static int InUseVolumeGuideCount => _cachedVolumeResults.CountInUse;

    private static ReusableSurveyResultCache<HybridRoutePlanSurveyResult> _cachedHybridRoutePlans =>
        GuideState.CachedHybridRoutePlans;

    private static GuidePool<AStarGuide> _aStarGuides => GuideState.AStarGuides;

    private static GuidePool<FlowFieldGuide> _flowFieldGuides => GuideState.FlowFieldGuides;

    private static GuidePool<VolumeGuide> _volumeGuides => GuideState.VolumeGuides;

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
        if (!RequestGuide(request, out IGuide? guide))
            return false;

        if (guide is not T typedGuide)
        {
            ReturnGuide(guide);
            return false;
        }

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

        if (!ReferenceEquals(request.Context, PathManager.ActiveState.Context))
        {
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

        if (_cachedAStarResults.TryCheckout(request, out AStarSurveyResult cachedResult))
            return RentAStarGuide(cachedResult);

        return RequestAStarMiss(request);
    }

    private static AStarGuide? RequestAStarMiss(AStarPathRequest request)
    {
        bool pathFound = _cachedAStarResults.TryGetOrCreate(request,
            () => ResolveAStarResult(request),
            out AStarSurveyResult result);

        if (!pathFound)
            return null;

        return RentAStarGuide(result);
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
            FlowFieldGuide cachedGuide = _flowFieldGuides.Rent();
            if (cachedGuide.InitializeStaged(cachedRoutePlan))
                return cachedGuide;

            ReturnFlowFieldGuide(cachedGuide, dispose: false);
            return null;
        }

        if (_cachedFlowResults.TryCheckout(request, out FlowFieldSurveyResult cachedResult))
        {
            if (TryRentFlowFieldGuide(request, cachedResult, out FlowFieldGuide? cachedGuide))
                return cachedGuide;

            _cachedFlowResults.Return(cachedResult, dispose: false);
        }

        return RequestFlowFieldMiss(request);
    }

    private static FlowFieldGuide? RequestFlowFieldMiss(FlowFieldPathRequest request)
    {
        bool pathFound = _cachedFlowResults.TryGetOrCreate(request,
            () => _flowFieldSurveyor.FindPath(request),
            out FlowFieldSurveyResult result);

        // Make sure the start voxel is within the current fields collection. This dictionary probe is on the warm
        // cache-hit path, so GridForge's WorldVoxelIndex hash must stay allocation-free to keep FlowField hits near A*.
        if (pathFound
            && result.Fields != null
            && request.StartNode != null
            && result.Fields.ContainsKey(request.StartNode.WorldIndex))
        {
            return RentFlowFieldGuide(result);
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
        if (_cachedVolumeResults.TryCheckout(request, out VolumeSurveyResult cachedResult))
            return RentVolumeGuide(cachedResult);

        return RequestVolumeMiss(request);
    }

    private static VolumeGuide? RequestVolumeMiss(VolumePathRequest request)
    {
        bool pathFound = _cachedVolumeResults.TryGetOrCreate(request,
            () => _volumeSurveyor.FindPath(request),
            out VolumeSurveyResult result);

        if (!pathFound)
            return null;

        return RentVolumeGuide(result);
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
                ReturnAStarGuide(a, dispose);
                break;
            case FlowFieldGuide f:
                f.ReleaseStagedResources(dispose);
                if (f.FlowMap != null)
                    _cachedFlowResults.Return(f.FlowMap, dispose);
                ReturnFlowFieldGuide(f, dispose);
                break;
            case VolumeGuide v:
                if (v.TrailMap != null)
                    _cachedVolumeResults.Return(v.TrailMap, dispose);
                ReturnVolumeGuide(v, dispose);
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
        ClearGuidePools();
    }

    internal static bool TrySeedAStarCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        return _cachedAStarResults.TrySeed(
            AStarSurveyResult.Create(CreateSeedWaypoints(), chartKeys, requestKey),
            checkout);
    }

    internal static bool TrySeedFlowFieldCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        WorldVoxelIndex index = default;
        var fields = new SwiftDictionary<WorldVoxelIndex, FlowField>(1)
        {
            [index] = new FlowField
            {
                GlobalIndex = index,
                IsGoal = true
            }
        };

        return _cachedFlowResults.TrySeed(
            FlowFieldSurveyResult.Create(fields, chartKeys, requestKey),
            checkout);
    }

    internal static bool TrySeedVolumeCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        return _cachedVolumeResults.TrySeed(
            VolumeSurveyResult.Create(CreateSeedWaypoints(), chartKeys, requestKey),
            checkout);
    }

    internal static bool TrySeedHybridRoutePlanCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        var routePlan = new HybridRoutePlan(
            new[] { HybridRouteStep.Waypoint(PathManager.ActiveState.Context, Vector3d.Zero) },
            Array.Empty<TraversalTransition>(),
            totalPathCost: 0);

        return _cachedHybridRoutePlans.TrySeed(
            HybridRoutePlanSurveyResult.Create(routePlan, chartKeys, requestKey),
            checkout);
    }

    internal static int CountIndexedCacheEntriesForBenchmark(string chartKey)
    {
        return _cachedAStarResults.CountIndexedEntriesForChart(chartKey)
            + _cachedFlowResults.CountIndexedEntriesForChart(chartKey)
            + _cachedVolumeResults.CountIndexedEntriesForChart(chartKey)
            + _cachedHybridRoutePlans.CountIndexedEntriesForChart(chartKey);
    }

    private static AStarWaypoint[] CreateSeedWaypoints()
    {
        return new[]
        {
            new AStarWaypoint
            {
                Position = Vector3d.Zero,
                IsGoal = true
            }
        };
    }

    private static AStarGuide? RentAStarGuide(AStarSurveyResult result)
    {
        AStarGuide guide = _aStarGuides.Rent();
        if (guide.Initialize(result))
            return guide;

        ReturnAStarGuide(guide, dispose: false);
        _cachedAStarResults.Return(result, dispose: false);
        return null;
    }

    private static FlowFieldGuide? RentFlowFieldGuide(FlowFieldSurveyResult result)
    {
        FlowFieldGuide guide = _flowFieldGuides.Rent();
        if (guide.Initialize(result))
            return guide;

        ReturnFlowFieldGuide(guide, dispose: false);
        _cachedFlowResults.Return(result, dispose: false);
        return null;
    }

    private static bool TryRentFlowFieldGuide(
        FlowFieldPathRequest request,
        FlowFieldSurveyResult result,
        [NotNullWhen(true)] out FlowFieldGuide? guide)
    {
        guide = null;
        if (result.Fields == null
            || request.StartNode == null
            || !result.Fields.ContainsKey(request.StartNode.WorldIndex))
        {
            return false;
        }

        guide = RentFlowFieldGuide(result);
        return guide != null;
    }

    private static VolumeGuide? RentVolumeGuide(VolumeSurveyResult result)
    {
        VolumeGuide guide = _volumeGuides.Rent();
        if (guide.Initialize(result))
            return guide;

        ReturnVolumeGuide(guide, dispose: false);
        _cachedVolumeResults.Return(result, dispose: false);
        return null;
    }

    private static void ReturnAStarGuide(AStarGuide guide, bool dispose)
    {
        if (dispose || guide.GetType() != typeof(AStarGuide))
            _aStarGuides.Destroy(guide);
        else
            _aStarGuides.Release(guide);
    }

    private static void ReturnFlowFieldGuide(FlowFieldGuide guide, bool dispose)
    {
        if (dispose || guide.GetType() != typeof(FlowFieldGuide))
            _flowFieldGuides.Destroy(guide);
        else
            _flowFieldGuides.Release(guide);
    }

    private static void ReturnVolumeGuide(VolumeGuide guide, bool dispose)
    {
        if (dispose)
            _volumeGuides.Destroy(guide);
        else
            _volumeGuides.Release(guide);
    }

    private static void ClearGuidePools()
    {
        _aStarGuides.Clear();
        _flowFieldGuides.Clear();
        _volumeGuides.Clear();
    }

    private static AStarSurveyResult ResolveAStarResult(AStarPathRequest request)
    {
        AStarSurveyResult directResult = _aStarSurveyor.FindPath(request);
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

        result = AStarSurveyResult.Create(request.Context, flattenedWaypoints!, chartKeys, request.RequestCacheKey);
        return true;
    }

    private static bool TryBuildTransitionFallbackFlowGuide(
        FlowFieldPathRequest request,
        [NotNullWhen(true)] out FlowFieldGuide? guide)
    {
        guide = null;

        if (!TryGetTransitionFallbackFlowPlan(request, out HybridRoutePlan? routePlan))
            return false;

        FlowFieldGuide stagedGuide = _flowFieldGuides.Rent();
        if (stagedGuide.InitializeStaged(routePlan))
        {
            guide = stagedGuide;
            return true;
        }

        ReturnFlowFieldGuide(stagedGuide, dispose: false);
        return false;
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
            request.Context,
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

            if (step.SegmentChartKeys.Length > 0)
                AddChartKeys(chartKeys, step.SegmentChartKeys);
            else
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

    private static void AddChartKeys(SwiftHashSet<string> chartKeys, string[] segmentChartKeys)
    {
        if (chartKeys == null || segmentChartKeys == null)
            return;

        for (int i = 0; i < segmentChartKeys.Length; i++)
        {
            string chartKey = segmentChartKeys[i];
            if (!string.IsNullOrEmpty(chartKey))
                chartKeys.Add(chartKey);
        }
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
