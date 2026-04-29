using System;
using System.Runtime.CompilerServices;

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
    public static int ActiveAStarGuideCount => _cachedAStarResults.Count;

    /// <summary>
    /// A shared cache for FlowField survey results, keyed by request parameters.
    /// This allows for efficient reuse of recently computed flow fields without needing to re-run the flow field generation for identical requests.
    /// </summary>
    private static readonly ReusableSurveyResultCache<FlowFieldSurveyResult> _cachedFlowResults = new();

    /// <summary>
    /// Returns the number of active (pooled or in-use) FlowField guides currently tracked.
    /// </summary>
    public static int ActiveFlowGuideCount => _cachedFlowResults.Count;

    /// <summary>
    /// A shared cache for raw-volume survey results, keyed by request parameters.
    /// This allows for efficient reuse of recently computed raw-volume data without needing to re-run the volume generation for identical requests.
    /// </summary>
    private static readonly ReusableSurveyResultCache<VolumeSurveyResult> _cachedVolumeResults = new();

    /// <summary>
    /// Returns the number of active raw-volume guides currently tracked.
    /// </summary>
    public static int ActiveVolumeGuideCount => _cachedVolumeResults.Count;

    /// <summary>
    /// Indicates whether any pathing guides are currently pooled and available.
    /// </summary>
    public static bool IsPooling =>
        ActiveAStarGuideCount > 0
        || ActiveFlowGuideCount > 0
        || ActiveVolumeGuideCount > 0;

    /// <summary>
    /// Indicates whether any guides are currently in use (checked out from the pool and not yet returned).
    /// </summary>
    public static bool AnyInUse =>
        _cachedAStarResults.CountInUse > 0
        || _cachedFlowResults.CountInUse > 0
        || _cachedVolumeResults.CountInUse > 0;

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
    }

    /// <summary>
    /// Requests a guide of a specific type using an already populated path request.
    /// </summary>
    /// <typeparam name="T">The concrete guide type to return.</typeparam>
    /// <param name="request">The path request with validated parameters.</param>
    /// <param name="result">The resolved guide or null if the request was invalid.</param>
    /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
    public static bool RequestGuide<T>(IPathRequest request, out T result) where T : IGuide
    {
        result = default;
        bool success = RequestGuide(request, out IGuide guide);
        if (success)
            result = (T)guide;
        return success;
    }

    /// <summary>
    /// Routes the path request to the appropriate guide implementation based on type.
    /// </summary>
    /// <param name="request">The polymorphic request to resolve.</param>
    /// <param name="result">The resolved guide or null if the request was invalid.</param>
    /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
    public static bool RequestGuide(IPathRequest request, out IGuide result)
    {
        if (request?.IsValid != true)
        {
            Console.WriteLine("Request is invalid. Create or update the request before requesting a guide.");
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
    public static AStarGuide RequestAStar(AStarPathRequest request)
    {
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
    public static FlowFieldGuide RequestFlowField(FlowFieldPathRequest request)
    {
        bool pathFound = _cachedFlowResults.TryGetOrCreate(request,
            () => FlowFieldSurveyor.Shared.FindPath(request),
            out FlowFieldSurveyResult result);

        // Make sure the start voxel is within the current fields collection
        // Note: for flow fields, the world-scoped index of the start voxel is used as the key to check for path validity,
        // since the flow field is generated around the start position and may not cover the entire map.
        if (pathFound && result.Fields.ContainsKey(request.StartNode.WorldIndex))
        {
            FlowFieldGuide guide = new();
            guide.Initialize(result);
            return guide;
        }

        if (pathFound)
            _cachedFlowResults.Return(result, dispose: false);

        if (!request.AllowTraversalTransitions)
            return null;

        return TryBuildTransitionFallbackFlowGuide(request, out FlowFieldGuide fallbackGuide)
            ? fallbackGuide
            : null;
    }

    /// <summary>
    /// Retrieves a raw-volume guide from the pool or creates a new one based on the provided request.
    /// </summary>
    public static VolumeGuide RequestVolume(VolumePathRequest request)
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
    private static HybridGuide RequestHybrid(HybridPathRequest request)
    {
        if (!HybridWaypointFlattener.TryBuild(
            request.RoutePlan,
            out AStarWaypoint[] flattened,
            out _))
        {
            return null;
        }

        HybridGuide guide = new();
        return guide.Initialize(flattened) ? guide : null;
    }

    /// <summary>
    /// Returns the guide back to its associated pool, optionally disposing it completely.
    /// </summary>
    /// <param name="guide">The guide to return to the cache.</param>
    /// <param name="dispose">Whether to destroy the guide instead of pooling it.</param>
    public static void ReturnGuide(IGuide guide, bool dispose = false)
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
                _cachedVolumeResults.Return(v.TrailMap, dispose);
                break;
        }
    }

    public static void InvalidateCacheFor(string chartKey)
    {
        if (string.IsNullOrEmpty(chartKey)) return;

        _cachedAStarResults.InvalidateWhere(r => UsesChart(r, chartKey));
        _cachedFlowResults.InvalidateWhere(r => UsesChart(r, chartKey));
        _cachedVolumeResults.InvalidateWhere(r => UsesChart(r, chartKey));
    }

    internal static void InvalidateVolumeCache()
    {
        _cachedVolumeResults.InvalidateAll();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool UsesChart(ISurveyResult result, string chartId)
    {
        var charts = result.ChartsUtilized;
        if (charts == null)
            return false;

        for (int i = 0; i < charts.Length; i++)
        {
            if (charts[i] == chartId)
                return true;
        }

        return false;
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

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromAStar(request);
        if (hybridRequest?.RoutePlan == null
            || hybridRequest.RoutePlan.DirectedTransitions.Length == 0)
        {
            return false;
        }

        if (!HybridWaypointFlattener.TryBuild(
            hybridRequest.RoutePlan,
            out AStarWaypoint[] flattenedWaypoints,
            out string[] chartKeys))
        {
            return false;
        }

        result = AStarSurveyResult.Create(flattenedWaypoints, chartKeys, request.RequestCacheKey);
        return true;
    }

    private static bool TryBuildTransitionFallbackFlowGuide(
        FlowFieldPathRequest request,
        out FlowFieldGuide guide)
    {
        guide = null;

        HybridPathRequest hybridRequest = HybridPathRequest.CreateFromFlowField(request);
        if (hybridRequest?.RoutePlan == null
            || hybridRequest.RoutePlan.DirectedTransitions.Length == 0)
        {
            return false;
        }

        guide = new FlowFieldGuide();
        return guide.InitializeStaged(hybridRequest.RoutePlan);
    }
}
