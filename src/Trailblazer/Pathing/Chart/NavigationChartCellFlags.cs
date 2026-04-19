using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Optional authored hints attached to a <see cref="NavigationChartCell"/>.
/// </summary>
/// <remarks>
/// These flags are reserved for future transition-aware routing. They do not alter pathing behavior yet.
/// </remarks>
[Flags]
public enum NavigationChartCellFlags
{
    None = 0,
    TransitionSourceHint = 1 << 0,
    TransitionDestinationHint = 1 << 1,
    ClimbSurfaceHint = 1 << 2,
    ClimbTransitionHint = 1 << 3
}
