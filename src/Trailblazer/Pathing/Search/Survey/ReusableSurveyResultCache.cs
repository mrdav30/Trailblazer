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
    private readonly SwiftDictionary<PathRequestCacheKey, T> _cache = new();

    /// <summary>
    /// Reverse lookup from chart key to cached request keys that reference the chart.
    /// </summary>
    private readonly SwiftDictionary<string, SwiftList<PathRequestCacheKey>> _chartIndex = new(8, StringComparer.Ordinal);

    private readonly SwiftList<PathRequestCacheKey> _staleKeys = new(MaxCacheSize);

    /// <summary>
    /// Active results that are intentionally outside the shared key cache but must still participate in invalidation.
    /// </summary>
    private readonly SwiftList<T> _uncachedActiveResults = new(4);

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
        => TryGetOrCreate(request.RequestCacheKey, request.Context, create, out result);

    internal bool TryGetOrCreate(
        PathRequestCacheKey key,
        TrailblazerWorldContext context,
        Func<T> create,
        out T result)
    {
        if (!key.IsInitialized)
        {
            result = null!;
            return false;
        }

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out result) && result.HasPath)
            {
                result.Context = context;
                CheckoutCachedResult(result);
                return true;
            }

            if (_cache.Count >= MaxCacheSize)
            {
                if (TryGetLeastRecentlyUsedReusableEntry(out PathRequestCacheKey evictKey, out T evictCandidate))
                {
                    _lock.EnterWriteLock();
                    try { RemoveCachedResult(evictKey, evictCandidate); } finally { _lock.ExitWriteLock(); }
                }
            }

            result = create();
            if (result.HasPath)
            {
                result.Context = context;
                _lock.EnterWriteLock();
                try
                {
                    if (_cache.Count < MaxCacheSize)
                    {
                        AddCachedResult(key, result);
                    }
                    else
                    {
                        TrackUncachedActiveResult(result);
                    }

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
    /// Creates and checks out a result without inserting it into the shared cache.
    /// </summary>
    /// <remarks>
    /// Used when an exact destination-centric cache hit is valid but does not cover a farther request origin.
    /// The existing shared result remains untouched for active guides.
    /// </remarks>
    public bool TryCreateUncached(IPathRequest request, Func<T> create, out T result)
    {
        if (!request.RequestCacheKey.IsInitialized)
        {
            result = null!;
            return false;
        }

        result = create();
        if (!result.HasPath)
            return false;

        result.Context = request.Context;
        _lock.EnterWriteLock();
        try
        {
            result.Checkout();
            _uncachedActiveResults.Add(result);
            CountInUse++;
        }
        finally { _lock.ExitWriteLock(); }

        return true;
    }

    /// <summary>
    /// Promotes a covering active result into the shared cache while preserving any leases on the prior entry.
    /// </summary>
    internal bool TryPromoteUncached(IPathRequest request, T result)
    {
        if (request == null
            || result == null
            || !result.HasPath
            || !result.IsInUse
            || result.RequestCacheKey != request.RequestCacheKey)
        {
            return false;
        }

        PathRequestCacheKey key = request.RequestCacheKey;
        _lock.EnterWriteLock();
        try
        {
            bool hasExisting = _cache.TryGetValue(key, out T existing);
            if (hasExisting && ReferenceEquals(existing, result))
            {
                _uncachedActiveResults.Remove(result);
                return true;
            }

            if ((!hasExisting && _cache.Count >= MaxCacheSize)
                || !_uncachedActiveResults.Remove(result))
            {
                return false;
            }

            if (hasExisting)
            {
                RemoveCachedResult(key, existing);
                if (existing.IsInUse)
                    TrackUncachedActiveResult(existing);
                else
                    existing.Reset();
            }

            AddCachedResult(key, result);
            return true;
        }
        finally { _lock.ExitWriteLock(); }
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
            {
                result.Release();
                CountInUse--;
            }

            if (!result.IsInUse && _uncachedActiveResults.Count > 0)
                _uncachedActiveResults.Remove(result);

            if (result.HasPath && !dispose)
                return;

            if (result.RequestCacheKey.IsInitialized
                && _cache.TryGetValue(result.RequestCacheKey, out T cached)
                && ReferenceEquals(cached, result))
            {
                RemoveCachedResult(result.RequestCacheKey, result);
                if (result.IsInUse)
                    TrackUncachedActiveResult(result);
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
            foreach (KeyValuePair<PathRequestCacheKey, T> kvp in _cache)
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
                    PathRequestCacheKey key = _staleKeys[i];
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
        => TryCheckout(request.RequestCacheKey, request.Context, out result);

    internal bool TryCheckout(
        PathRequestCacheKey key,
        TrailblazerWorldContext context,
        out T result)
    {
        if (!key.IsInitialized)
        {
            result = null!;
            return false;
        }

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out result) && result.HasPath)
            {
                result.Context = context;
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
        if (result == null || !result.HasPath || !result.RequestCacheKey.IsInitialized || result.Context == null)
            return false;

        PathRequestCacheKey key = result.RequestCacheKey;

        _lock.EnterWriteLock();
        try
        {
            bool replacesExisting = _cache.TryGetValue(key, out T existing);
            if (!replacesExisting && _cache.Count >= MaxCacheSize)
                return false;

            if (_uncachedActiveResults.Remove(result))
                CountInUse -= result.ActiveCheckoutCount;

            if (replacesExisting)
            {
                CountInUse -= existing.ActiveCheckoutCount;

                RemoveCachedResult(key, existing);
                if (!ReferenceEquals(existing, result))
                    existing.Reset();
            }

            result.ReleaseAllCheckouts();

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
            return _chartIndex.TryGetValue(chartKey, out SwiftList<PathRequestCacheKey> keys)
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

        _lock.EnterWriteLock();
        try
        {
            while (_chartIndex.TryGetValue(chartKey, out SwiftList<PathRequestCacheKey> indexedKeys)
                && indexedKeys.Count > 0)
            {
                PathRequestCacheKey key = indexedKeys[indexedKeys.Count - 1];
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

            for (int i = _uncachedActiveResults.Count - 1; i >= 0; i--)
            {
                T result = _uncachedActiveResults[i];
                if (ResultUsesChart(result, chartKey))
                    InvalidateUncachedResultAt(i, result);
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void InvalidateWhere(Func<T, bool> predicate)
    {
        SwiftList<PathRequestCacheKey> toRemove = new();
        _lock.EnterWriteLock();
        try
        {
            foreach (KeyValuePair<PathRequestCacheKey, T> kvp in _cache)
            {
                if (kvp.Value == null || !predicate(kvp.Value))
                    continue;

                toRemove.Add(kvp.Key);
            }

            foreach (PathRequestCacheKey key in toRemove)
            {
                if (_cache.TryGetValue(key, out T result))
                    InvalidateCachedResult(key, result);
            }

            for (int i = _uncachedActiveResults.Count - 1; i >= 0; i--)
            {
                T result = _uncachedActiveResults[i];
                if (result != null && predicate(result))
                    InvalidateUncachedResultAt(i, result);
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void InvalidateAll()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (KeyValuePair<PathRequestCacheKey, T> kvp in _cache)
            {
                if (kvp.Value == null) continue;

                kvp.Value.Reset();
            }

            for (int i = 0; i < _uncachedActiveResults.Count; i++)
                _uncachedActiveResults[i].Reset();

            _cache.Clear();
            _chartIndex.Clear();
            _uncachedActiveResults.Clear();
            CountInUse = 0;
        }
        finally { _lock.ExitWriteLock(); }
    }

    private bool TryGetLeastRecentlyUsedReusableEntry(out PathRequestCacheKey key, out T result)
    {
        key = default;
        result = null!;

        foreach (KeyValuePair<PathRequestCacheKey, T> kvp in _cache)
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

    private void AddCachedResult(PathRequestCacheKey key, T result)
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

    private void InvalidateCachedResult(PathRequestCacheKey key, T result)
    {
        CountInUse -= result.ActiveCheckoutCount;

        RemoveCachedResult(key, result);
        result.Reset();
    }

    private void InvalidateUncachedResultAt(int index, T result)
    {
        CountInUse -= result.ActiveCheckoutCount;
        _uncachedActiveResults.RemoveAt(index);
        result.Reset();
    }

    private void TrackUncachedActiveResult(T result)
    {
        if (!_uncachedActiveResults.Contains(result))
            _uncachedActiveResults.Add(result);
    }

    private void RemoveCachedResult(PathRequestCacheKey key, T result)
    {
        RemoveFromChartIndex(key, result.ChartsUtilized);
        _cache.Remove(key);
    }

    private void AddToChartIndex(PathRequestCacheKey cacheKey, string[] chartKeys)
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

            if (!_chartIndex.TryGetValue(chartKey, out SwiftList<PathRequestCacheKey> keys))
            {
                keys = new SwiftList<PathRequestCacheKey>(1);
                _chartIndex[chartKey] = keys;
            }

            keys.Add(cacheKey);
        }
    }

    private void RemoveFromChartIndex(PathRequestCacheKey cacheKey, string[] chartKeys)
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

            if (!_chartIndex.TryGetValue(chartKey, out SwiftList<PathRequestCacheKey> keys))
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

    private static bool ResultUsesChart(T result, string chartKey)
    {
        string[] chartKeys = result.ChartsUtilized;
        if (chartKeys == null)
            return false;

        for (int i = 0; i < chartKeys.Length; i++)
        {
            if (string.Equals(chartKeys[i], chartKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public void Dispose() => _lock?.Dispose();
}
