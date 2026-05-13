using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Trailblazer.Pathing;

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

    /// <summary>
    /// Reverse lookup from chart key to cached request keys that reference the chart.
    /// </summary>
    private readonly SwiftDictionary<string, SwiftList<int>> _chartIndex = new(8, StringComparer.Ordinal);

    private readonly SwiftList<int> _staleKeys = new(MaxCacheSize);

    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// Gets the total number of cached and pooled <see cref="SurveyResult"/> instances.
    /// </summary>
    public int Count => _cache.Count;

    private int _countInUse;

    public int CountInUse
    {
        get => _countInUse;
        private set => _countInUse = Math.Max(0, value);
    }

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
                result.Context = request.Context;
                CheckoutCachedResult(result);
                return true;
            }

            if (_cache.Count >= MaxCacheSize)
            {
                if (TryGetLeastRecentlyUsedReusableEntry(out int evictKey, out T evictCandidate))
                {
                    _lock.EnterWriteLock();
                    try { RemoveCachedResult(evictKey, evictCandidate); } finally { _lock.ExitWriteLock(); }
                }
            }

            result = create();
            if (result.HasPath)
            {
                result.Context = request.Context;
                _lock.EnterWriteLock();
                try
                {
                    if (_cache.Count < MaxCacheSize)
                        AddCachedResult(key, result);

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
            if (result.IsInUse)
                CountInUse--;

            if (result.HasPath && !dispose)
            {
                result.Release();
                return;
            }

            if (result.RequestHashKey >= 0
                && _cache.TryGetValue(result.RequestHashKey, out T cached)
                && ReferenceEquals(cached, result))
            {
                RemoveCachedResult(result.RequestHashKey, result);
            }
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
        _lock.EnterUpgradeableReadLock();
        try
        {
            _staleKeys.Clear();
            foreach (KeyValuePair<int, T> kvp in _cache)
            {
                if (!kvp.Value.IsInUse && currentFrame - kvp.Value.LastUsedFrame > expiration)
                    _staleKeys.Add(kvp.Key);
            }

            if (_staleKeys.Count == 0)
                return;

            _lock.EnterWriteLock();
            try
            {
                for (int i = 0; i < _staleKeys.Count; i++)
                {
                    int key = _staleKeys[i];
                    if (_cache.TryGetValue(key, out T result))
                        RemoveCachedResult(key, result);
                }
            }
            finally { _lock.ExitWriteLock(); }

        }
        finally
        {
            _staleKeys.Clear();
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Attempts to check out an existing cached result without invoking a creation callback.
    /// </summary>
    /// <param name="request">The path request used as the cache key.</param>
    /// <param name="result">The checked-out cached result.</param>
    /// <returns><c>true</c> when a valid cached result was found; otherwise, <c>false</c>.</returns>
    public bool TryCheckout(IPathRequest request, out T result)
    {
        int key = request.RequestCacheKey;

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out result) && result.HasPath)
            {
                result.Context = request.Context;
                CheckoutCachedResult(result);
                return true;
            }

            result = null!;
            return false;
        }
        finally { _lock.ExitUpgradeableReadLock(); }
    }

    /// <summary>
    /// Seeds a valid result directly into the cache for internal benchmark and test fixtures.
    /// </summary>
    /// <remarks>
    /// This intentionally does not evict entries; callers use it to create exact cache-pressure
    /// shapes and should choose unique request keys up to the cache capacity.
    /// </remarks>
    internal bool TrySeed(T result, bool checkout)
    {
        if (result == null || !result.HasPath || result.RequestHashKey < 0 || result.Context == null)
            return false;

        int key = result.RequestHashKey;

        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out T existing))
            {
                if (existing.IsInUse)
                    CountInUse--;

                RemoveCachedResult(key, existing);
                if (!ReferenceEquals(existing, result))
                    existing.Reset();
            }
            else if (_cache.Count >= MaxCacheSize)
            {
                return false;
            }

            if (result.IsInUse)
                result.Release();

            if (checkout)
            {
                result.Checkout();
                CountInUse++;
            }

            AddCachedResult(key, result);
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    internal int CountIndexedEntriesForChart(string chartKey)
    {
        if (string.IsNullOrEmpty(chartKey))
            return 0;

        _lock.EnterReadLock();
        try
        {
            return _chartIndex.TryGetValue(chartKey, out SwiftList<int> keys)
                ? keys.Count
                : 0;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Invalidates cached results that reference the specified chart key by using the chart reverse index.
    /// </summary>
    /// <param name="chartKey">The chart key whose dependent cached results should be removed.</param>
    public void InvalidateForChart(string chartKey)
    {
        if (string.IsNullOrEmpty(chartKey)) return;

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (!_chartIndex.TryGetValue(chartKey, out SwiftList<int> indexedKeys)
                || indexedKeys.Count == 0)
            {
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                while (_chartIndex.TryGetValue(chartKey, out indexedKeys)
                    && indexedKeys.Count > 0)
                {
                    int key = indexedKeys[indexedKeys.Count - 1];
                    if (_cache.TryGetValue(key, out T result))
                    {
                        InvalidateCachedResult(key, result);
                    }
                    else
                    {
                        indexedKeys.Remove(key);
                        if (indexedKeys.Count == 0)
                            _chartIndex.Remove(chartKey);
                    }
                }
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

                toRemove.Add(kvp.Key);
            }

            if (toRemove.Count == 0)
                return;

            _lock.EnterWriteLock();
            try
            {
                foreach (int key in toRemove)
                {
                    if (_cache.TryGetValue(key, out T result))
                        InvalidateCachedResult(key, result);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }
        finally { _lock.ExitUpgradeableReadLock(); }
    }

    public void InvalidateAll()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (KeyValuePair<int, T> kvp in _cache)
            {
                if (kvp.Value == null) continue;

                kvp.Value.Reset();
            }

            _cache.Clear();
            _chartIndex.Clear();
            CountInUse = 0;
        }
        finally { _lock.ExitWriteLock(); }
    }

    private bool TryGetLeastRecentlyUsedReusableEntry(out int key, out T result)
    {
        key = -1;
        result = null!;

        foreach (KeyValuePair<int, T> kvp in _cache)
        {
            T candidate = kvp.Value;
            if (candidate == null || candidate.IsInUse)
                continue;

            if (result == null || candidate.LastUsedFrame < result.LastUsedFrame)
            {
                key = kvp.Key;
                result = candidate;
            }
        }

        return result != null;
    }

    private void AddCachedResult(int key, T result)
    {
        if (_cache.TryGetValue(key, out T existing))
            RemoveFromChartIndex(key, existing.ChartsUtilized);

        _cache[key] = result;
        AddToChartIndex(key, result.ChartsUtilized);
    }

    private void CheckoutCachedResult(T result)
    {
        _lock.EnterWriteLock();
        try
        {
            result.Checkout();
            CountInUse++;
        }
        finally { _lock.ExitWriteLock(); }
    }

    private void InvalidateCachedResult(int key, T result)
    {
        if (result.IsInUse)
            CountInUse--;

        RemoveCachedResult(key, result);
        result.Reset();
    }

    private void RemoveCachedResult(int key, T result)
    {
        RemoveFromChartIndex(key, result.ChartsUtilized);
        _cache.Remove(key);
    }

    private void AddToChartIndex(int cacheKey, string[] chartKeys)
    {
        if (chartKeys == null || chartKeys.Length == 0)
            return;

        for (int i = 0; i < chartKeys.Length; i++)
        {
            string chartKey = chartKeys[i];
            if (string.IsNullOrEmpty(chartKey)
                || ContainsPriorChartKey(chartKeys, i, chartKey))
            {
                continue;
            }

            if (!_chartIndex.TryGetValue(chartKey, out SwiftList<int> keys))
            {
                keys = new SwiftList<int>(1);
                _chartIndex[chartKey] = keys;
            }

            keys.Add(cacheKey);
        }
    }

    private void RemoveFromChartIndex(int cacheKey, string[] chartKeys)
    {
        if (chartKeys == null || chartKeys.Length == 0)
            return;

        for (int i = 0; i < chartKeys.Length; i++)
        {
            string chartKey = chartKeys[i];
            if (string.IsNullOrEmpty(chartKey)
                || ContainsPriorChartKey(chartKeys, i, chartKey))
            {
                continue;
            }

            if (!_chartIndex.TryGetValue(chartKey, out SwiftList<int> keys))
                continue;

            keys.Remove(cacheKey);
            if (keys.Count == 0)
                _chartIndex.Remove(chartKey);
        }
    }

    private static bool ContainsPriorChartKey(string[] chartKeys, int exclusiveEnd, string chartKey)
    {
        for (int i = 0; i < exclusiveEnd; i++)
        {
            if (string.Equals(chartKeys[i], chartKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public void Dispose() => _lock?.Dispose();
}
