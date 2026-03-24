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

    /// <summary>
    /// Creates a new legend entry with the specified chart output and optional transition-anchor output.
    /// </summary>
    /// <param name="chartCell">The chart cell emitted for this token.</param>
    /// <param name="anchorSpace">The transition-anchor space emitted when this token is marked.</param>
    /// <param name="hasAnchorSpace">Whether this token may participate in generated transition anchors.</param>
    public TraversalLegendEntry(
        NavigationChartCell chartCell,
        TraversalTransitionAnchorSpace anchorSpace = TraversalTransitionAnchorSpace.Chart,
        bool hasAnchorSpace = false)
    {
        ChartCell = chartCell;
        AnchorSpace = anchorSpace;
        HasAnchorSpace = hasAnchorSpace;
    }

    /// <summary>
    /// Creates an entry that contributes no chart surface and no transition anchor.
    /// </summary>
    public static TraversalLegendEntry SkipCell() =>
        new(NavigationChartCell.Blocked);

    /// <summary>
    /// Creates an entry that emits a chart cell and may participate in generated chart anchors.
    /// </summary>
    public static TraversalLegendEntry Chart(NavigationChartCell chartCell) =>
        new(chartCell, TraversalTransitionAnchorSpace.Chart, hasAnchorSpace: true);

    /// <summary>
    /// Creates an entry that emits an open-volume anchor when explicitly marked.
    /// </summary>
    public static TraversalLegendEntry OpenVolume() =>
        new(NavigationChartCell.Blocked, TraversalTransitionAnchorSpace.OpenVolume, hasAnchorSpace: true);

    /// <summary>
    /// Creates an entry that emits a water-volume anchor when explicitly marked.
    /// </summary>
    public static TraversalLegendEntry WaterVolume() =>
        new(NavigationChartCell.Blocked, TraversalTransitionAnchorSpace.WaterVolume, hasAnchorSpace: true);
}
