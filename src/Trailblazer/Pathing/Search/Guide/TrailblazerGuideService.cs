//=======================================================================
// TrailblazerGuideService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
            using (EnterUsableState())
                return PathGuideFactory.TotalAStarGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseAStarGuideCount"/>
    public int InUseAStarGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.InUseAStarGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalFlowGuideCount"/>
    public int TotalFlowGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.TotalFlowGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseFlowGuideCount"/>
    public int InUseFlowGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.InUseFlowGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalVolumeGuideCount"/>
    public int TotalVolumeGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.TotalVolumeGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseVolumeGuideCount"/>
    public int InUseVolumeGuideCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.InUseVolumeGuideCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.TotalHybridRoutePlanCount"/>
    public int TotalHybridRoutePlanCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.TotalHybridRoutePlanCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.InUseHybridRoutePlanCount"/>
    public int InUseHybridRoutePlanCount
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.InUseHybridRoutePlanCount;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.IsPooling"/>
    public bool IsPooling
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.IsPooling;
        }
    }

    /// <inheritdoc cref="PathGuideFactory.AnyInUse"/>
    public bool AnyInUse
    {
        get
        {
            using (EnterUsableState())
                return PathGuideFactory.AnyInUse;
        }
    }

    /// <summary>
    /// Requests a typed guide for the supplied validated path request.
    /// </summary>
    public bool RequestGuide<T>(IPathRequest request, [NotNullWhen(true)] out T? result)
        where T : class, IGuide
    {
        if (!IsRequestOwnedByThisContext(request))
        {
            result = null;
            return false;
        }

        using (EnterUsableState())
            return PathGuideFactory.RequestGuide(request, out result);
    }

    /// <inheritdoc cref="PathGuideFactory.RequestGuide(IPathRequest,out IGuide?)"/>
    public bool RequestGuide(IPathRequest request, [NotNullWhen(true)] out IGuide? result)
    {
        if (!IsRequestOwnedByThisContext(request))
        {
            result = null;
            return false;
        }

        using (EnterUsableState())
            return PathGuideFactory.RequestGuide(request, out result);
    }

    /// <inheritdoc cref="PathGuideFactory.ReturnGuide(IGuide?,bool)"/>
    public void ReturnGuide(IGuide? guide, bool dispose = false)
    {
        if (guide == null)
            return;

        using (EnterUsableState())
            PathGuideFactory.ReturnGuide(guide, dispose);
    }

    /// <inheritdoc cref="PathGuideFactory.InvalidateCacheFor(string)"/>
    public void InvalidateCacheFor(string chartKey)
    {
        using (EnterUsableState())
            PathGuideFactory.InvalidateCacheFor(chartKey);
    }

    /// <inheritdoc cref="PathGuideFactory.FlushCache(bool)"/>
    public void FlushCache(bool force = false)
    {
        using (EnterUsableState())
            PathGuideFactory.FlushCache(force);
    }

    internal void CullExpiredGuides(int currentFrame)
    {
        using (EnterUsableState())
            PathGuideFactory.CullExpiredGuides(currentFrame);
    }

    internal void InvalidateVolumeCache()
    {
        using (EnterUsableState())
            PathGuideFactory.InvalidateVolumeCache();
    }

    internal SolidPartitionReachability.SolidPartitionReachabilityStats CaptureReachabilityStats()
    {
        using (EnterUsableState())
            return SolidPartitionReachability.CaptureStats();
    }

    internal bool TrySeedAStarCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        using (EnterUsableState())
            return PathGuideFactory.TrySeedAStarCacheForBenchmark(requestKey, chartKeys, checkout);
    }

    internal bool TrySeedFlowFieldCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        using (EnterUsableState())
            return PathGuideFactory.TrySeedFlowFieldCacheForBenchmark(requestKey, chartKeys, checkout);
    }

    internal bool TrySeedVolumeCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        using (EnterUsableState())
            return PathGuideFactory.TrySeedVolumeCacheForBenchmark(requestKey, chartKeys, checkout);
    }

    internal bool TrySeedHybridRoutePlanCacheForBenchmark(int requestKey, string[] chartKeys, bool checkout)
    {
        using (EnterUsableState())
        {
            return PathGuideFactory.TrySeedHybridRoutePlanCacheForBenchmark(
                requestKey,
                chartKeys,
                checkout);
        }
    }

    internal int CountIndexedCacheEntriesForBenchmark(string chartKey)
    {
        using (EnterUsableState())
            return PathGuideFactory.CountIndexedCacheEntriesForBenchmark(chartKey);
    }

    private IDisposable EnterUsableState()
    {
        EnsureUsable();
        return PathManager.EnterState(_state);
    }

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerGuideService is bound to an inactive GridWorld.");
    }

    private bool IsRequestOwnedByThisContext(IPathRequest request) =>
        request != null && ReferenceEquals(request.Context, _context);
}
