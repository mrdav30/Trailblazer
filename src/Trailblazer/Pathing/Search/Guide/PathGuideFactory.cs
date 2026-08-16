//=======================================================================
// PathGuideFactory.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;

namespace Trailblazer.Pathing;

/// <summary>Owns retained volume-guide caching and pooling.</summary>
internal static class PathGuideFactory
{
    private const int MaxFramesUnused = 600;

    private static TrailblazerGuideState GuideState => PathManager.ActiveState.GuideState;

    private static ReusableSurveyResultCache<VolumeSurveyResult> CachedResults =>
        GuideState.CachedVolumeResults;

    private static GuidePool<VolumeGuide> Guides => GuideState.VolumeGuides;

    public static int TotalVolumeGuideCount => CachedResults.Count;

    public static int InUseVolumeGuideCount => CachedResults.CountInUse;

    public static bool IsPooling => TotalVolumeGuideCount > 0;

    public static bool AnyInUse => InUseVolumeGuideCount > 0;

    public static void CullExpiredGuides(int currentFrame)
    {
        if (IsPooling)
            CachedResults.EvictStaleEntries(currentFrame, MaxFramesUnused);
    }

    public static bool RequestGuide<T>(IPathRequest request, [NotNullWhen(true)] out T? result)
        where T : class, IGuide
    {
        result = null;
        if (!RequestGuide(request, out IGuide? guide))
            return false;

        if (guide is T typedGuide)
        {
            result = typedGuide;
            return true;
        }

        ReturnGuide(guide);
        return false;
    }

    public static bool RequestGuide(IPathRequest request, [NotNullWhen(true)] out IGuide? result)
    {
        result = null;
        if (request is not VolumePathRequest volume
            || !volume.IsValid
            || !ReferenceEquals(volume.Context, PathManager.ActiveState.Context))
        {
            return false;
        }

        result = RequestVolume(volume);
        return result != null;
    }

    public static VolumeGuide? RequestVolume(VolumePathRequest request)
    {
        if (CachedResults.TryCheckout(request, out VolumeSurveyResult cachedResult))
            return Rent(cachedResult);

        bool pathFound = CachedResults.TryGetOrCreate(
            request,
            () => GuideState.VolumeSurveyor.FindPath(request),
            out VolumeSurveyResult result);
        return pathFound ? Rent(result) : null;
    }

    public static void ReturnGuide(IGuide? guide, bool dispose = false)
    {
        if (guide is not VolumeGuide volume)
            return;

        TrailblazerWorldContext? ownerContext = volume.VolumeResult?.Context;
        if (ownerContext != null && !ReferenceEquals(ownerContext, PathManager.ActiveState.Context))
        {
            throw new InvalidOperationException(
                "Guide belongs to a different owning TrailblazerWorldContext. Return it through the context that created it.");
        }

        if (volume.VolumeResult != null)
            CachedResults.Return(volume.VolumeResult, dispose);

        if (dispose)
            Guides.Destroy(volume);
        else
            Guides.Release(volume);
    }

    public static void InvalidateCacheFor(string chartKey)
    {
        if (!string.IsNullOrEmpty(chartKey))
            CachedResults.InvalidateForChart(chartKey);
    }

    internal static void InvalidateVolumeCache() => CachedResults.InvalidateAll();

    public static void FlushCache(bool force = false)
    {
        if (!force && AnyInUse)
            return;

        CachedResults.InvalidateAll();
        Guides.Clear();
    }

    private static VolumeGuide? Rent(VolumeSurveyResult result)
    {
        VolumeGuide guide = Guides.Rent();
        if (guide.Initialize(result))
            return guide;

        Guides.Release(guide);
        CachedResults.Return(result, dispose: false);
        return null;
    }
}
