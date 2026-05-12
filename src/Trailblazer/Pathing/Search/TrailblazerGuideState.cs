namespace Trailblazer.Pathing;

/// <summary>
/// Owns guide result caches and reusable guide pools for one pathing context.
/// </summary>
internal sealed class TrailblazerGuideState
{
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
}
