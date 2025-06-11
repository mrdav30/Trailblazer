using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Provides access to pooled and reusable IGuide instances for both A* and FlowField pathing strategies.
    /// Handles guide request routing, instantiation, and lifecycle management.
    /// </summary>
    public static class PathGuideFactory
    {
        private const int MaxFramesUnused = 600;

        private static readonly ReusableSurveyResultCache<AStarSurveyResult> _cachedAStarResults = new();

        /// <summary>
        /// Returns the number of active (pooled or in-use) A* results currently tracked.
        /// </summary>
        public static int ActiveAStarGuideCount => _cachedAStarResults.Count;

        private static readonly ReusableSurveyResultCache<FlowFieldSurveyResult> _cachedFlowResults = new();

        /// <summary>
        /// Returns the number of active (pooled or in-use) FlowField guides currently tracked.
        /// </summary>
        public static int ActiveFlowGuideCount => _cachedFlowResults.Count;

        /// <summary>
        /// Indicates whether any pathing guides are currently pooled and available.
        /// </summary>
        public static bool IsPooling => ActiveAStarGuideCount > 0 || ActiveFlowGuideCount > 0;

        /// <summary>
        /// Attempts to remove guides from the pool that haven't been used for a configured number of frames.
        /// </summary>
        /// <param name="currentFrame">The current frame index used to check guide staleness.</param>
        public static void CullExpiredGuides(int currentFrame)
        {
            if (!IsPooling) return;

            _cachedAStarResults.EvictStaleEntries(currentFrame, MaxFramesUnused);
            _cachedFlowResults.EvictStaleEntries(currentFrame, MaxFramesUnused);
        }

        /// <summary>
        /// Requests a guide of a specific type using the given origin, destination, and request parameters.
        /// </summary>
        /// <typeparam name="T">The guide type to return (AStarGuide or FlowFieldGuide).</typeparam>
        /// <param name="origin">The world position to start the path from.</param>
        /// <param name="destination">The destination world position.</param>
        /// <param name="request">The configuration describing the path search.</param>
        /// <param name="result">The resolved guide or default if the request was invalid.</param>
        /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
        public static bool RequestGuide<T>(
            Vector3d origin, 
            Vector3d destination, 
            IPathRequest request, 
            out T result) where T : IGuide
        {
            result = default;
            bool success = RequestGuide(origin, destination, request, out IGuide guide);
            if (success)
                result = (T)guide;
            return success;
        }

        /// <summary>
        /// Attempts to retrieve a guide for the given path request using validated voxels internally.
        /// </summary>
        /// <param name="origin">The world position to start from.</param>
        /// <param name="destination">The world destination to path toward.</param>
        /// <param name="request">The configuration describing the path request.</param>
        /// <param name="result">The resolved guide or null if the request was invalid.</param>
        /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
        public static bool RequestGuide(
            Vector3d origin, 
            Vector3d destination, 
            IPathRequest request,
            out IGuide result)
        {
            result = null;
            if (!PathManager.GetValidPathRequest(origin, destination, out Voxel startVoxel, out Voxel endVoxel))
                return false;

            request.Start = startVoxel;
            request.End = endVoxel;

            return RequestGuide(request, out result);
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
        /// <param name="request">The polymorphic request to resolve (AStar or FlowField).</param>
        /// <param name="result">The resolved guide or null if the request was invalid.</param>
        /// <returns><c>true</c> if the guide was properly configured, otherwise <c>false</c>.</returns>
        public static bool RequestGuide(IPathRequest request, out IGuide result)
        {
            result = null;
            request.Prepare();
            if (!request.IsValid)
                return false;

            switch (request)
            {
                case AStarPathRequest a:
                    result = RequestAStar(a);
                    break;
                case FlowFieldPathRequest f:
                    result = RequestFlowField(f);
                    break;
                default:
                    break;
            }

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
                () => AStarSurveyor.Shared.FindPath(request),
                out AStarSurveyResult path);

            if (!pathFound)
                return null;

            path.MarkInUse();

            AStarGuide guide = new();
            guide.Initialize(path);
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
                out FlowFieldSurveyResult path);

            // Make sure the start voxel is within the current fields collection
            // Note: for flow fields, the SpawnToken of the Start voxel is not included
            if (!pathFound || !path.Fields.ContainsKey(request.Start.SpawnToken))
                return null;

            path.MarkInUse();

            FlowFieldGuide guide = new();
            guide.Initialize(path);
            return guide;
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
                    _cachedFlowResults.Return(f.FlowMap, dispose);
                    break;
            }
        }

        /// <summary>
        /// Removes all cached guides from both A* and FlowField pools.
        /// </summary>
        public static void FlushPools()
        {
            _cachedAStarResults.Clear();
            _cachedFlowResults.Clear();
        }
    }
}
