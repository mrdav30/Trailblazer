//=======================================================================
// HybridRoutePlanSurveyResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
        PathRequestCacheKey key)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        return new HybridRoutePlanSurveyResult
        {
            IsValid = true,
            Context = context,
            ChartsUtilized = chartsUtilized ?? Array.Empty<string>(),
            RoutePlan = routePlan,
            LastUsedFrame = -1,
            RequestCacheKey = key
        };
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        RoutePlan = null;
    }
}
