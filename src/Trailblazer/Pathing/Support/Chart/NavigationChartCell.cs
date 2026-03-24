using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents one authored surface cell inside a <see cref="NavigationChart"/>.
/// </summary>
[Serializable]
public readonly struct NavigationChartCell
{
    #region Static Presets

    /// <summary>
    /// A reusable traversable cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell Walkable = new(true);

    /// <summary>
    /// A reusable blocked for chart traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell Blocked = new(false);

    #endregion

    #region Properties

    /// <summary>
    /// Indicates whether this chart cell contributes to chart-backed surface traversal.
    /// </summary>
    public bool IsTraversable { get; }

    /// <summary>
    /// An authored path cost adjustment applied when this cell initializes a live partition.
    /// </summary>
    public int PathCostModifier { get; }

    /// <summary>
    /// Optional authored hints reserved for future transition-aware routing.
    /// </summary>
    public NavigationChartCellFlags Flags { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new chart cell payload.
    /// </summary>
    public NavigationChartCell(
        bool isTraversable,
        int pathCostModifier = 0,
        NavigationChartCellFlags flags = NavigationChartCellFlags.None)
    {
        IsTraversable = isTraversable;
        PathCostModifier = pathCostModifier;
        Flags = flags;
    }

    #endregion
}
