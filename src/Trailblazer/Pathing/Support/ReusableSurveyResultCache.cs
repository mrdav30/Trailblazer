using SwiftCollections;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Caches and reuses <see cref="ISurveyResult"/> instances to reduce allocation overhead 
    /// and improve pathfinding performance.
    /// Supports LRU eviction and optional pooling of released guides.
    /// </summary>
    public class ReusableSurveyResultCache<T> where T : ISurveyResult
    {
        /// <summary>
        /// Maximum number of <see cref="ISurveyResult"/> allowed in the cache before eviction occurs.
        /// </summary>
        private const int MaxCacheSize = 128;

        /// <summary>
        /// Active <see cref="ISurveyResult"/> cache indexed by the request's cache key.
        /// </summary>
        private readonly SwiftDictionary<int, T> _cache = new();

        /// <summary>
        /// Gets the total number of cached and pooled <see cref="ISurveyResult"/> instances.
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// Attempts to retrieve a valid <see cref="ISurveyResult"/> from the cache, 
        /// or creates and initializes a new one if none are reusable.
        /// Evicts the least recently used guide if the cache is at capacity.
        /// </summary>
        /// <param name="request">The path request used as the cache key.</param>
        /// <param name="create">Factory method to create a new guide instance.</param>
        /// <param name="result">The resulting <see cref="ISurveyResult"/> instance.</param>
        /// <returns>True if a valid <see cref="ISurveyResult"/> was obtained; otherwise, false.</returns>
        public bool TryGetOrCreate(IPathRequest request, Func<T> create, out T result)
        {
            int key = request.RequestCacheKey;
            if (_cache.TryGetValue(key, out result) && result.IsValid)
                return true;

            if (_cache.Count >= MaxCacheSize)
            {
                // Find least recently used
                T evictCandidate = _cache.OrderBy(g => g.Value.LastUsedFrame)
                    .FirstOrDefault(g => !g.Value.IsInUse).Value;
                if (evictCandidate != null)
                    _cache.Remove(evictCandidate.RequestHashKey);
            }

            result = create();
            if (result.IsValid)
            {
                if (_cache.Count < MaxCacheSize)
                    _cache[key] = result;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns a <see cref="ISurveyResult"/> to the pool or disposes it based on the given flag.
        /// Also removes invalid guides from the active cache.
        /// </summary>
        /// <param name="result">The <see cref="ISurveyResult"/> to return.</param>
        /// <param name="dispose">Whether the <see cref="ISurveyResult"/> should be disposed and not pooled.</param>
        public void Return(T result, bool dispose)
        {
            if (result.IsValid && !dispose)
            {
                result.Release();
                return;
            }

            _cache.Remove(result.RequestHashKey);
        }

        /// <summary>
        /// Evicts <see cref="ISurveyResult"/> from the cache that have not been used within the specified expiration window.
        /// <see cref="ISurveyResult"/> that are not in use are optionally returned to the pool.
        /// </summary>
        /// <param name="currentFrame">The current simulation frame.</param>
        /// <param name="expiration">The number of frames after which a <see cref="ISurveyResult"/> is considered stale.</param>
        public void EvictStaleEntries(int currentFrame, int expiration)
        {
            var toRemove = new List<int>();
            foreach (var kvp in _cache)
            {
                if (currentFrame - kvp.Value.LastUsedFrame > expiration)
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
                _cache.Remove(key);
        }

        /// <summary>
        /// Clears all cached and pooled guide instances.
        /// </summary>
        public void Clear() => _cache.Clear();
    }
}