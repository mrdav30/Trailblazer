using SwiftCollections;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Caches and reuses IGuide instances to reduce allocation overhead and improve pathfinding performance.
    /// Supports LRU eviction and optional pooling of released guides.
    /// </summary>
    public class ReusableGuideCache<T> where T : IGuide
    {
        /// <summary>
        /// Maximum number of guides allowed in the return pool.
        /// </summary>
        private const int MaxPoolSize = 128;

        /// <summary>
        /// Maximum number of guides allowed in the cache before eviction occurs.
        /// </summary>
        private const int MaxCacheSize = 128;

        /// <summary>
        /// Active guide cache indexed by the request's cache key.
        /// </summary>
        private readonly SwiftDictionary<int, T> _cache = new();

        /// <summary>
        /// Queue for reusable guide instances not currently in use.
        /// </summary>
        private readonly SwiftQueue<T> _pool = new();

        /// <summary>
        /// Gets the total number of cached and pooled guide instances.
        /// </summary>
        public int Count => _cache.Count + _pool.Count;

        /// <summary>
        /// Attempts to retrieve a valid guide from the cache, or creates and initializes a new one if none are reusable.
        /// Evicts the least recently used guide if the cache is at capacity.
        /// </summary>
        /// <param name="request">The path request used as the cache key.</param>
        /// <param name="create">Factory method to create a new guide instance.</param>
        /// <param name="guide">The resulting guide instance.</param>
        /// <returns>True if a valid guide was obtained; otherwise, false.</returns>
        public bool TryGetOrCreate(IPathRequest request, Func<T> create, out T guide)
        {
            int key = request.RequestCacheKey;
            if (_cache.TryGetValue(key, out guide) && guide.IsValid)
                return true;

            if (_cache.Count >= MaxCacheSize)
            {
                // Find least recently used
                T evictCandidate = _cache.OrderBy(g => g.Value.LastUsedFrame).FirstOrDefault(g => !g.Value.IsInUse).Value;
                if (evictCandidate != null)
                    _cache.Remove(evictCandidate.RequestHashKey);
            }

            guide = _pool.Count > 0 ? _pool.Dequeue() : create();
            if (guide.Initialize(request))
            {
                if (_cache.Count < MaxCacheSize)
                    _cache[key] = guide;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns a guide to the pool or disposes it based on the given flag.
        /// Also removes invalid guides from the active cache.
        /// </summary>
        /// <param name="guide">The guide to return.</param>
        /// <param name="dispose">Whether the guide should be disposed and not pooled.</param>
        public void Return(T guide, bool dispose)
        {
            if (guide.IsValid && !dispose)
                guide.Release();
            else
            {
                _cache.Remove(guide.RequestHashKey);
                guide.Dispose();
            }

            if (!dispose && _pool.Count < MaxPoolSize)
                _pool.Enqueue(guide);
        }

        /// <summary>
        /// Evicts guides from the cache that have not been used within the specified expiration window.
        /// Guides that are not in use are optionally returned to the pool.
        /// </summary>
        /// <param name="currentFrame">The current simulation frame.</param>
        /// <param name="expiration">The number of frames after which a guide is considered stale.</param>
        public void EvictStaleEntries(int currentFrame, int expiration)
        {
            var toRemove = new List<int>();
            foreach (var kvp in _cache)
            {
                if (currentFrame - kvp.Value.LastUsedFrame > expiration)
                {
                    toRemove.Add(kvp.Key);
                    if (_pool.Count < MaxPoolSize)
                        _pool.Enqueue(kvp.Value);
                }
            }

            foreach (var key in toRemove)
                _cache.Remove(key);
        }

        /// <summary>
        /// Clears all cached and pooled guide instances.
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _pool.Clear();
        }
    }
}