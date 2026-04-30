using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Optional authored hints attached to a <see cref="NavigationChartCell"/>.
/// </summary>
[Flags]
public enum NavigationChartCellFlags
{
    /// <summary>
    /// Indicates that no options are specified.
    /// </summary>
    None = 0,
    /// <summary>
    /// Indicates that the source of the transition is a hint.
    /// </summary>
    TransitionSourceHint = 1 << 0,
    /// <summary>
    /// Indicates that the destination of a transition is a hint.
    /// </summary>
    TransitionDestinationHint = 1 << 1,
    /// <summary>
    /// Indicates that the surface is intended to be climbable.
    /// </summary>
    ClimbSurfaceHint = 1 << 2,
    /// <summary>
    /// Indicates that the transition involves a climbing action.
    /// </summary>
    ClimbTransitionHint = 1 << 3
}
