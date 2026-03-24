using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes how a single authoring token maps into chart and transition data.
/// </summary>
[Serializable]
public readonly struct TraversalLegendEntry
{
    /// <summary>
    /// The chart cell emitted for this token.
    /// </summary>
    public NavigationChartCell ChartCell { get; }

    /// <summary>
    /// Indicates whether this token may participate in generated transition anchors.
    /// </summary>
    public bool HasAnchorSpace { get; }

    /// <summary>
    /// The transition anchor space emitted for this token when it is explicitly marked.
    /// </summary>
    public TraversalTransitionAnchorSpace AnchorSpace { get; }

    public TraversalLegendEntry(
        NavigationChartCell chartCell,
        TraversalTransitionAnchorSpace anchorSpace = TraversalTransitionAnchorSpace.Chart,
        bool hasAnchorSpace = false)
    {
        ChartCell = chartCell;
        AnchorSpace = anchorSpace;
        HasAnchorSpace = hasAnchorSpace;
    }

    public static TraversalLegendEntry Blocked() =>
        new(NavigationChartCell.Blocked);

    public static TraversalLegendEntry Chart(NavigationChartCell chartCell) =>
        new(chartCell, TraversalTransitionAnchorSpace.Chart, hasAnchorSpace: true);

    public static TraversalLegendEntry OpenVolume() =>
        new(NavigationChartCell.Blocked, TraversalTransitionAnchorSpace.OpenVolume, hasAnchorSpace: true);

    public static TraversalLegendEntry WaterVolume() =>
        new(NavigationChartCell.Blocked, TraversalTransitionAnchorSpace.WaterVolume, hasAnchorSpace: true);
}
