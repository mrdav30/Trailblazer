namespace Trailblazer.Pathing;

/// <summary>
/// Describes the authored handoff a transition represents.
/// </summary>
public enum TraversalTransitionType
{
    Custom = 0,
    Jump = 1,
    SwimEntry = 2,
    SwimExit = 3,
    Takeoff = 4,
    Landing = 5
}
