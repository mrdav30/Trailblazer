using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Cached transition-aware route plan used by staged guides.
/// </summary>
internal sealed class HybridRoutePlanSurveyResult : SurveyResult
{
    public HybridRoutePlan? RoutePlan { get; private set; }

    /// <inheritdoc/>
    public override bool HasPath => IsValid && RoutePlan != null && RoutePlan.Steps.Length > 0;

    public static readonly HybridRoutePlanSurveyResult Empty = new();

    private HybridRoutePlanSurveyResult() { }

    internal static HybridRoutePlanSurveyResult Create(
        TrailblazerWorldContext context,
        HybridRoutePlan routePlan,
        string[] chartsUtilized,
        int key)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        return new HybridRoutePlanSurveyResult
        {
            IsValid = true,
            IsInUse = false,
            Context = context,
            ChartsUtilized = chartsUtilized ?? Array.Empty<string>(),
            RoutePlan = routePlan,
            LastUsedFrame = -1,
            RequestHashKey = key
        };
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        RoutePlan = null;
    }
}
