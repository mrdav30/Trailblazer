//=======================================================================
// TrailblazerGuideState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Owns guide result caches and reusable guide pools for one pathing context.
/// </summary>
internal sealed class TrailblazerGuideState : IDisposable
{
    private bool _disposed;

    internal AStarSurveyor AStarSurveyor { get; } = new();

    internal FlowFieldSurveyor FlowFieldSurveyor { get; } = new();

    internal VolumeSurveyor VolumeSurveyor { get; } = new();

    internal ReusableSurveyResultCache<AStarSurveyResult> CachedAStarResults { get; } = new();

    internal ReusableSurveyResultCache<FlowFieldSurveyResult> CachedFlowResults { get; } = new();

    internal ReusableSurveyResultCache<VolumeSurveyResult> CachedVolumeResults { get; } = new();

    internal ReusableSurveyResultCache<HybridRoutePlanSurveyResult> CachedHybridRoutePlans { get; } = new();

    internal GuidePool<AStarGuide> AStarGuides { get; } =
        new(static () => new AStarGuide(), static guide => guide.ResetForReuse());

    internal GuidePool<FlowFieldGuide> FlowFieldGuides { get; } =
        new(static () => new FlowFieldGuide(), static guide => guide.ResetForReuse());

    internal GuidePool<VolumeGuide> VolumeGuides { get; } =
        new(static () => new VolumeGuide(), static guide => guide.ResetForReuse());

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CachedAStarResults.Dispose();
        CachedFlowResults.Dispose();
        CachedVolumeResults.Dispose();
        CachedHybridRoutePlans.Dispose();
    }
}
