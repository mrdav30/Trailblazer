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
    public bool HasAnchorMedium { get; }

    /// <summary>
    /// The transition anchor medium emitted for this token when it is explicitly marked.
    /// </summary>
    public TraversalMedium Medium { get; }

    /// <summary>
    /// Creates a new legend entry with the specified chart output and optional transition-anchor output.
    /// </summary>
    /// <param name="chartCell">The chart cell emitted for this token.</param>
    /// <param name="medium">The transition-anchor medium emitted when this token is marked.</param>
    /// <param name="hasAnchorMedium">Whether this token may participate in generated transition anchors.</param>
    public TraversalLegendEntry(
        NavigationChartCell chartCell,
        TraversalMedium medium = TraversalMedium.Solid,
        bool hasAnchorMedium = false)
    {
        ChartCell = chartCell;
        Medium = medium;
        HasAnchorMedium = hasAnchorMedium;
    }

    /// <summary>
    /// Creates an entry that contributes no chart traversal and no transition anchor.
    /// </summary>
    public static TraversalLegendEntry SkipCell() =>
        new(NavigationChartCell.Empty);

    /// <summary>
    /// Creates an entry that emits an authored solid cell and may participate in generated solid anchors.
    /// </summary>
    public static TraversalLegendEntry Solid(NavigationChartCell chartCell) =>
        new(chartCell, TraversalMedium.Solid, hasAnchorMedium: true);

    /// <summary>
    /// Creates an entry that emits authored gas traversal data and an anchor when explicitly marked.
    /// </summary>
    public static TraversalLegendEntry Gas() =>
        new(new NavigationChartCell(TraversalMedia.Gas), TraversalMedium.Gas, hasAnchorMedium: true);

    /// <summary>
    /// Creates an entry that emits authored liquid traversal data and an anchor when explicitly marked.
    /// </summary>
    public static TraversalLegendEntry Liquid() =>
        new(new NavigationChartCell(TraversalMedia.Liquid), TraversalMedium.Liquid, hasAnchorMedium: true);
}
