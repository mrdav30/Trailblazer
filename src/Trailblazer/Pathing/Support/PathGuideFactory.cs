using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System.Collections.Generic;
using Trailblazer.Navigation.Steering;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Provides access to pooled and reusable IGuide instances for both A* and FlowField pathing strategies.
    /// Handles guide request routing, instantiation, and lifecycle management.
    /// </summary>
    public static class PathGuideFactory
    {
        private const int MaxFramesUnused = 600;

        private static readonly ReusableGuideCache<AStarGuide> _cachedAStar = new();

        /// <summary>
        /// Returns the number of active (pooled or in-use) A* guides currently tracked.
        /// </summary>
        public static int ActiveAStarGuideCount => _cachedAStar.Count;

        private static readonly ReusableGuideCache<FlowFieldGuide> _cachedFlow = new();

        /// <summary>
        /// Returns the number of active (pooled or in-use) FlowField guides currently tracked.
        /// </summary>
        public static int ActiveFlowGuideCount => _cachedFlow.Count;

        /// <summary>
        /// Indicates whether any pathing guides are currently pooled and available.
        /// </summary>
        public static bool IsPooling => _cachedAStar.Count > 0 || _cachedFlow.Count > 0;

        /// <summary>
        /// Attempts to remove guides from the pool that haven't been used for a configured number of frames.
        /// </summary>
        /// <param name="currentFrame">The current frame index used to check guide staleness.</param>
        public static void CullExpiredGuides(int currentFrame)
        {
            if (!IsPooling) return;

            _cachedAStar.EvictStaleEntries(currentFrame, MaxFramesUnused);
            _cachedFlow.EvictStaleEntries(currentFrame, MaxFramesUnused);
        }

        /// <summary>
        /// Requests a guide of a specific type using the given origin, destination, and request parameters.
        /// </summary>
        /// <typeparam name="T">The guide type to return (AStarGuide or FlowFieldGuide).</typeparam>
        /// <param name="origin">The world position to start the path from.</param>
        /// <param name="destination">The destination world position.</param>
        /// <param name="request">The configuration describing the path search.</param>
        /// <returns>The pooled or newly created guide instance.</returns>
        public static T RequestGuide<T>(Vector3d origin, Vector3d destination, IPathRequest request) where T : IGuide
        {
            return (T)RequestGuide(origin, destination, request);
        }

        /// <summary>
        /// Attempts to retrieve a guide for the given path request using validated nodes internally.
        /// </summary>
        /// <param name="origin">The world position to start from.</param>
        /// <param name="destination">The world destination to path toward.</param>
        /// <param name="request">The configuration describing the path request.</param>
        /// <returns>The resolved guide or null if the request was invalid.</returns>
        public static IGuide RequestGuide(Vector3d origin, Vector3d destination, IPathRequest request)
        {
            if (!PathManager.GetValidPathRequest(origin, destination, out Node startNode, out Node endNode))
                return null;

            request.Start = startNode;
            request.End = endNode;

            return RequestGuide(request);
        }

        /// <summary>
        /// Requests a guide of a specific type using an already populated path request.
        /// </summary>
        /// <typeparam name="T">The concrete guide type to return.</typeparam>
        /// <param name="request">The path request with validated parameters.</param>
        /// <returns>The resolved or pooled guide.</returns>
        public static T RequestGuide<T>(IPathRequest request) where T : IGuide
        {
            return (T)RequestGuide(request);
        }

        /// <summary>
        /// Routes the path request to the appropriate guide implementation based on type.
        /// </summary>
        /// <param name="request">The polymorphic request to resolve (AStar or FlowField).</param>
        /// <returns>The resolved or pooled guide.</returns>
        public static IGuide RequestGuide(IPathRequest request)
        {
            IGuide guide = null;
            switch (request)
            {
                case AStarPathRequest a:
                    guide = RequestAStar(a);
                    break;
                case FlowFieldPathRequest f:
                    guide = RequestFlowField(f);
                    break;
                default:
                    break;
            }

            guide?.MarkInUse();

            return guide;
        }

        /// <summary>
        /// Retrieves an A* guide from the pool or creates a new one based on the provided request.
        /// </summary>
        /// <param name="request">The configured A* pathfinding request.</param>
        /// <returns>A valid AStarGuide instance.</returns>
        public static AStarGuide RequestAStar(AStarPathRequest request)
        {
            if (!_cachedAStar.TryGetOrCreate(request, () => { return new AStarGuide(); }, out AStarGuide guide))
                return null;

            return guide;
        }

        /// <summary>
        /// Retrieves a FlowField guide from the pool or creates a new one based on the provided request.
        /// </summary>
        /// <param name="request">The configured FlowField pathfinding request.</param>
        /// <returns>A valid FlowFieldGuide instance.</returns>
        public static FlowFieldGuide RequestFlowField(FlowFieldPathRequest request)
        {
            if (!_cachedFlow.TryGetOrCreate(request, () => { return new FlowFieldGuide(); }, out FlowFieldGuide guide))
                return null;

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
                    _cachedAStar.Return(a, dispose);
                    break;
                case FlowFieldGuide f:
                    _cachedFlow.Return(f, dispose);
                    break;
            }
        }

        /// <summary>
        /// Removes all cached guides from both A* and FlowField pools.
        /// </summary>
        public static void FlushPools()
        {
            _cachedAStar.Clear();
            _cachedFlow.Clear();
        }
    }
}
