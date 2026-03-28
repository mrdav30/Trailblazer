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
    /// Indicates which transition media this token may contribute when explicitly marked.
    /// </summary>
    public TraversalMedia TransitionMedia { get; }

    /// <summary>
    /// Returns true when this token may participate in generated transition anchors.
    /// </summary>
    public bool HasTransitionMedia => TransitionMedia != TraversalMedia.None;

    /// <summary>
    /// Creates a new legend entry with the specified chart output and optional transition-anchor output.
    /// </summary>
    /// <param name="chartCell">The chart cell emitted for this token.</param>
    /// <param name="transitionMedia">
    /// The transition media emitted when this token is marked.
    /// These media must be a subset of the authored media declared by <paramref name="chartCell"/>.
    /// </param>
    public TraversalLegendEntry(
        NavigationChartCell chartCell,
        TraversalMedia transitionMedia = TraversalMedia.None)
    {
        TraversalMedia authoredMedia = chartCell.TraversalKinds;
        if ((transitionMedia & ~authoredMedia) != 0)
        {
            throw new ArgumentException(
                "Transition media must be a subset of the authored chart cell traversal media.",
                nameof(transitionMedia));
        }

        ChartCell = chartCell;
        TransitionMedia = transitionMedia;
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
        new(chartCell, TraversalMedia.Solid);

    /// <summary>
    /// Creates an entry that emits authored gas traversal data and an anchor when explicitly marked.
    /// </summary>
    public static TraversalLegendEntry Gas() =>
        new(NavigationChartCell.Gas, TraversalMedia.Gas);

    /// <summary>
    /// Creates an entry that emits authored liquid traversal data and an anchor when explicitly marked.
    /// </summary>
    public static TraversalLegendEntry Liquid() =>
        new(NavigationChartCell.Liquid, TraversalMedia.Liquid);
}
