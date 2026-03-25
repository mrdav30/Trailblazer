using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents one authored traversal cell inside a dense <see cref="NavigationChart"/>.
/// </summary>
[Serializable]
public readonly struct NavigationChartCell
{
    #region Static Presets

    /// <summary>
    /// A reusable empty authored cell with no traversal data.
    /// </summary>
    public static readonly NavigationChartCell Empty = new(NavigationChartTraversalKinds.None);

    /// <summary>
    /// A reusable surface traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell Surface = new(NavigationChartTraversalKinds.Surface);

    #endregion

    #region Properties

    /// <summary>
    /// Describes which authored traversal spaces this cell contributes to.
    /// </summary>
    public NavigationChartTraversalKinds TraversalKinds { get; }

    /// <summary>
    /// Returns true when this cell contributes any authored traversal data.
    /// </summary>
    public bool HasTraversalData => TraversalKinds != NavigationChartTraversalKinds.None;

    /// <summary>
    /// Returns true when this cell contributes chart-backed surface traversal.
    /// </summary>
    public bool HasSurface => (TraversalKinds & NavigationChartTraversalKinds.Surface) != 0;

    /// <summary>
    /// Returns true when this cell contributes any authored raw-volume traversal data.
    /// </summary>
    public bool HasVolume => (TraversalKinds & NavigationChartTraversalKinds.AnyVolume) != 0;

    /// <summary>
    /// An authored path cost adjustment applied when this cell initializes a live partition.
    /// </summary>
    public int PathCostModifier { get; }

    /// <summary>
    /// Optional authored hints currently applied to surface partitions.
    /// </summary>
    public NavigationChartCellFlags Flags { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new chart cell payload.
    /// </summary>
    public NavigationChartCell(
        NavigationChartTraversalKinds traversalKinds,
        int pathCostModifier = 0,
        NavigationChartCellFlags flags = NavigationChartCellFlags.None)
    {
        bool hasOpenVolume = (traversalKinds & NavigationChartTraversalKinds.OpenVolume) != 0;
        bool hasWaterVolume = (traversalKinds & NavigationChartTraversalKinds.WaterVolume) != 0;
        if (hasOpenVolume && hasWaterVolume)
        {
            throw new ArgumentException(
                "A single authored cell cannot currently declare both open-volume and water-volume traversal.",
                nameof(traversalKinds));
        }

        TraversalKinds = traversalKinds;
        PathCostModifier = pathCostModifier;
        Flags = flags;
    }

    #endregion

    /// <summary>
    /// Returns true when this cell contributes the requested raw volume traversal mode.
    /// </summary>
    public bool SupportsVolumeTraversal(VolumeTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            VolumeTraversalMode.Open => (TraversalKinds & NavigationChartTraversalKinds.OpenVolume) != 0,
            VolumeTraversalMode.Water => (TraversalKinds & NavigationChartTraversalKinds.WaterVolume) != 0,
            _ => false
        };
    }
}
