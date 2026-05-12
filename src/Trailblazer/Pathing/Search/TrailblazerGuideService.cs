using System;
using System.Diagnostics.CodeAnalysis;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned API for guide request, return, cache invalidation, and cache diagnostics.
/// </summary>
public sealed class TrailblazerGuideService
{
    private readonly TrailblazerWorldContext _context;
    private readonly PathingWorldState _state;

    internal TrailblazerGuideService(TrailblazerWorldContext context, PathingWorldState state)
    {
        _context = context;
        _state = state;
    }

    /// <inheritdoc cref="PathGuideFactory.TotalAStarGuideCount"/>
    public int TotalAStarGuideCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.TotalAStarGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseAStarGuideCount"/>
    public int InUseAStarGuideCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.InUseAStarGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalFlowGuideCount"/>
    public int TotalFlowGuideCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.TotalFlowGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseFlowGuideCount"/>
    public int InUseFlowGuideCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.InUseFlowGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalVolumeGuideCount"/>
    public int TotalVolumeGuideCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.TotalVolumeGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseVolumeGuideCount"/>
    public int InUseVolumeGuideCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.InUseVolumeGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalHybridRoutePlanCount"/>
    public int TotalHybridRoutePlanCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.TotalHybridRoutePlanCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseHybridRoutePlanCount"/>
    public int InUseHybridRoutePlanCount
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.InUseHybridRoutePlanCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.IsPooling"/>
    public bool IsPooling
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.IsPooling;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.AnyInUse"/>
    public bool AnyInUse
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return PathGuideFactory.AnyInUse;
        }
    }

    /// <summary>
    /// Requests a typed guide for the supplied validated path request.
    /// </summary>
    public bool RequestGuide<T>(IPathRequest request, [NotNullWhen(true)] out T? result)
        where T : class, IGuide
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return PathGuideFactory.RequestGuide(request, out result);
    }

    /// <inheritdoc cref="PathGuideFactory.RequestGuide(IPathRequest,out IGuide?)"/>
    public bool RequestGuide(IPathRequest request, [NotNullWhen(true)] out IGuide? result)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return PathGuideFactory.RequestGuide(request, out result);
    }

    /// <inheritdoc cref="PathGuideFactory.ReturnGuide(IGuide?,bool)"/>
    public void ReturnGuide(IGuide? guide, bool dispose = false)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            PathGuideFactory.ReturnGuide(guide, dispose);
    }

    /// <inheritdoc cref="PathGuideFactory.InvalidateCacheFor(string)"/>
    public void InvalidateCacheFor(string chartKey)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            PathGuideFactory.InvalidateCacheFor(chartKey);
    }

    /// <inheritdoc cref="PathGuideFactory.FlushCache(bool)"/>
    public void FlushCache(bool force = false)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            PathGuideFactory.FlushCache(force);
    }

    internal void CullExpiredGuides(int currentFrame)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            PathGuideFactory.CullExpiredGuides(currentFrame);
    }

    internal void InvalidateVolumeCache()
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            PathGuideFactory.InvalidateVolumeCache();
    }

    internal SolidPartitionReachability.SolidPartitionReachabilityStats CaptureReachabilityStats()
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return SolidPartitionReachability.CaptureStats();
    }

    internal bool TrySeedAStarCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return PathGuideFactory.TrySeedAStarCacheForBenchmark(requestKey, chartKeys, checkout);
    }

    internal bool TrySeedFlowFieldCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return PathGuideFactory.TrySeedFlowFieldCacheForBenchmark(requestKey, chartKeys, checkout);
    }

    internal bool TrySeedVolumeCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return PathGuideFactory.TrySeedVolumeCacheForBenchmark(requestKey, chartKeys, checkout);
    }

    internal bool TrySeedHybridRoutePlanCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
        {
            return PathGuideFactory.TrySeedHybridRoutePlanCacheForBenchmark(
                requestKey,
                chartKeys,
                checkout);
        }
    }

    internal int CountIndexedCacheEntriesForBenchmark(string chartKey)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return PathGuideFactory.CountIndexedCacheEntriesForBenchmark(chartKey);
    }

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerGuideService is bound to an inactive GridWorld.");
    }
}
