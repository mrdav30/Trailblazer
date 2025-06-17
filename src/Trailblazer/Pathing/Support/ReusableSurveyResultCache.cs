using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Caches and reuses <see cref="ISurveyResult"/> instances to reduce allocation overhead 
    /// and improve pathfinding performance.
    /// Supports LRU eviction and optional pooling of released guides.
    /// </summary>
    internal class ReusableSurveyResultCache<T> : IDisposable where T : SurveyResult
    {
        /// <summary>
        /// Maximum number of <see cref="SurveyResult"/>s allowed in the cache before eviction occurs.
        /// </summary>
        private const int MaxCacheSize = 128;

        /// <summary>
        /// Active <see cref="SurveyResult"/>s cache indexed by the request's cache key.
        /// </summary>
        private readonly SwiftDictionary<int, T> _cache = new();

        private readonly ReaderWriterLockSlim _lock = new();

        /// <summary>
        /// Gets the total number of cached and pooled <see cref="SurveyResult"/> instances.
        /// </summary>
        public int Count => _cache.Count;

        public int CountInUse { get; private set; }

        /// <summary>
        /// Attempts to retrieve a valid <see cref="SurveyResult"/> from the cache, 
        /// or creates and initializes a new one if none are reusable.
        /// Evicts the least recently used guide if the cache is at capacity.
        /// </summary>
        /// <param name="request">The path request used as the cache key.</param>
        /// <param name="create">Factory method to create a new guide instance.</param>
        /// <param name="result">The resulting <see cref="SurveyResult"/> instance.</param>
        /// <returns>True if a valid <see cref="SurveyResult"/> was obtained; otherwise, false.</returns>
        public bool TryGetOrCreate(IPathRequest request, Func<T> create, out T result)
        {
            int key = request.RequestCacheKey;

            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(key, out result) && result.HasPath)
                {
                    _lock.EnterWriteLock();
                    try { CountInUse++; } finally { _lock.ExitWriteLock(); }
                    return true;
                }

                if (_cache.Count >= MaxCacheSize)
                {
                    // Find least recently used
                    T evictCandidate = _cache.OrderBy(g => g.Value.LastUsedFrame)
                        .FirstOrDefault(g => !g.Value.IsInUse).Value;
                    if (evictCandidate != null)
                    {
                        _lock.EnterWriteLock();
                        try { _cache.Remove(evictCandidate.RequestHashKey); } finally { _lock.ExitWriteLock(); }
                    }
                }

                result = create();
                if (result.HasPath)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        if (_cache.Count < MaxCacheSize)
                            _cache[key] = result;

                        result.Checkout();
                        CountInUse++;
                    }
                    finally { _lock.ExitWriteLock(); }

                    return true;
                }

                return false;
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        /// <summary>
        /// Returns a <see cref="SurveyResult"/> to the pool or disposes it based on the given flag.
        /// Also removes invalid guides from the active cache.
        /// </summary>
        /// <param name="result">The <see cref="SurveyResult"/> to return.</param>
        /// <param name="dispose">Whether the <see cref="SurveyResult"/> should be disposed and not pooled.</param>
        public void Return(T result, bool dispose)
        {
            if (result == null) return;

            _lock.EnterWriteLock();
            try
            {
                CountInUse--;
                if (result.HasPath && !dispose)
                {
                    result.Release();
                    return;
                }

                _cache.Remove(result.RequestHashKey);
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Evicts <see cref="SurveyResult"/> from the cache that have not been used within the specified expiration window.
        /// <see cref="SurveyResult"/> that are not in use are optionally returned to the pool.
        /// </summary>
        /// <param name="currentFrame">The current simulation frame.</param>
        /// <param name="expiration">The number of frames after which a <see cref="SurveyResult"/> is considered stale.</param>
        internal void EvictStaleEntries(int currentFrame, int expiration)
        {
            SwiftList<int> toRemove = new();
            _lock.EnterUpgradeableReadLock();
            try
            {
                foreach (KeyValuePair<int, T> kvp in _cache)
                {
                    if (!kvp.Value.IsInUse && currentFrame - kvp.Value.LastUsedFrame > expiration)
                        toRemove.Add(kvp.Key);
                }

                if (toRemove.Count == 0)
                    return;

                _lock.EnterWriteLock();
                try
                {
                    foreach (int key in toRemove)
                        _cache.Remove(key);
                }
                finally { _lock.ExitWriteLock(); }

            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        public void InvalidateWhere(Func<T, bool> predicate)
        {
            SwiftList<int> toRemove = new();
            _lock.EnterUpgradeableReadLock();
            try
            {
                foreach (KeyValuePair<int, T> kvp in _cache)
                {
                    if (kvp.Value == null || !predicate(kvp.Value))
                        continue;

                    if (kvp.Value.IsInUse)
                    {
                        _lock.EnterWriteLock();
                        try { CountInUse--; } finally { _lock.ExitWriteLock(); }
                    }

                    kvp.Value.Reset();

                    toRemove.Add(kvp.Key);
                }

                _lock.EnterWriteLock();
                try
                {
                    foreach (int key in toRemove)
                        _cache.Remove(key);

                    if (CountInUse == 0)
                        _cache.Clear();
                }
                finally { _lock.ExitWriteLock(); }
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        public void InvalidateAll()
        {
            SwiftList<int> toRemove = new();
            _lock.EnterWriteLock();
            try
            {
                foreach (KeyValuePair<int, T> kvp in _cache)
                {
                    if (kvp.Value == null) continue;

                    kvp.Value.Reset();

                    toRemove.Add(kvp.Key);
                }

                foreach (int key in toRemove)
                    _cache.Remove(key);

                _cache.Clear();
                CountInUse = 0;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Dispose() => _lock?.Dispose();
    }
}