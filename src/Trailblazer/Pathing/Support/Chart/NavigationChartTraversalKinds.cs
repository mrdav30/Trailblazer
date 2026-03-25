using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes which authored traversal spaces are present in a dense <see cref="NavigationChartCell"/>.
/// </summary>
[Flags]
public enum NavigationChartTraversalKinds
{
    /// <summary>
    /// This cell contributes no authored traversal data.
    /// </summary>
    None = 0,

    /// <summary>
    /// This cell contributes chart-backed surface traversal.
    /// </summary>
    Surface = 1 << 0,

    /// <summary>
    /// This cell contributes authored open-volume traversal.
    /// </summary>
    OpenVolume = 1 << 1,

    /// <summary>
    /// This cell contributes authored water-volume traversal.
    /// </summary>
    WaterVolume = 1 << 2,

    /// <summary>
    /// Convenience mask covering all authored volume traversal kinds.
    /// </summary>
    AnyVolume = OpenVolume | WaterVolume
}
