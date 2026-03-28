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
    public static readonly NavigationChartCell Empty = new(TraversalMedia.None);

    /// <summary>
    /// A reusable solid traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell Solid = new(TraversalMedia.Solid);

    /// <summary>
    /// A reusable gas traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell Gas = new(TraversalMedia.Gas);

    /// <summary>
    /// A reusable solid-plus-gas traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell SolidGas = new(TraversalMedia.Solid | TraversalMedia.Gas);

    /// <summary>
    /// A reusable liquid traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell Liquid = new(TraversalMedia.Liquid);

    /// <summary>
    /// A reusable solid-plus-liquid traversal cell with no additional authored metadata.
    /// </summary>
    public static readonly NavigationChartCell SolidLiquid = new(TraversalMedia.Solid | TraversalMedia.Liquid);

    #endregion

    #region Properties

    /// <summary>
    /// Describes which authored traversal media this cell contributes to.
    /// </summary>
    public TraversalMedia TraversalKinds { get; }

    /// <summary>
    /// Returns true when this cell contributes any authored traversal data.
    /// </summary>
    public bool HasTraversalData => TraversalKinds != TraversalMedia.None;

    /// <summary>
    /// Returns true when this cell contributes chart-backed solid traversal.
    /// </summary>
    public bool HasSolid => (TraversalKinds & TraversalMedia.Solid) != 0;

    /// <summary>
    /// Returns true when this cell contributes any authored raw-volume traversal data.
    /// </summary>
    public bool HasVolume => (TraversalKinds & TraversalMedia.AnyVolume) != 0;

    /// <summary>
    /// An authored path cost adjustment applied when this cell initializes a live partition.
    /// </summary>
    public int PathCostModifier { get; }

    /// <summary>
    /// Optional authored hints currently applied to surface partitions.
    /// </summary>
    public NavigationChartCellFlags Flags { get; }

    /// <summary>
    /// Indicates which authored media on this cell participate in generated transition pairing.
    /// </summary>
    public TraversalMedia GeneratedTransitionMedia { get; }

    /// <summary>
    /// Returns true when this cell may participate in generated transition pairing.
    /// </summary>
    public bool CanGenerateTransition => GeneratedTransitionMedia != TraversalMedia.None;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new chart cell payload.
    /// </summary>
    public NavigationChartCell(
        TraversalMedia traversalKinds,
        int pathCostModifier = 0,
        NavigationChartCellFlags flags = NavigationChartCellFlags.None,
        TraversalMedia generatedTransitionMedia = TraversalMedia.None)
    {
        bool hasGas = (traversalKinds & TraversalMedia.Gas) != 0;
        bool hasLiquid = (traversalKinds & TraversalMedia.Liquid) != 0;
        bool hasSolid = (traversalKinds & TraversalMedia.Solid) != 0;
        if (hasGas && hasLiquid)
        {
            throw new ArgumentException(
                hasSolid
                    ? "A single authored cell cannot currently declare solid, gas, and liquid traversal together."
                    : "A single authored cell cannot currently declare both gas and liquid traversal.",
                nameof(traversalKinds));
        }

        if ((generatedTransitionMedia & ~traversalKinds) != 0)
        {
            throw new ArgumentException(
                "Generated transition media must be a subset of the authored traversal media.",
                nameof(generatedTransitionMedia));
        }

        TraversalKinds = traversalKinds;
        PathCostModifier = pathCostModifier;
        Flags = flags;
        GeneratedTransitionMedia = generatedTransitionMedia;
    }

    #endregion

    /// <summary>
    /// Returns true when this cell contributes the requested traversal medium.
    /// </summary>
    public bool SupportsMedium(TraversalMedium medium)
    {
        return medium switch
        {
            TraversalMedium.Solid => (TraversalKinds & TraversalMedia.Solid) != 0,
            TraversalMedium.Gas => (TraversalKinds & TraversalMedia.Gas) != 0,
            TraversalMedium.Liquid => (TraversalKinds & TraversalMedia.Liquid) != 0,
            _ => false
        };
    }
}
