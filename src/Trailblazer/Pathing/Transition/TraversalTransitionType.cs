namespace Trailblazer.Pathing;

/// <summary>
/// Describes the authored handoff a transition represents.
/// </summary>
public enum TraversalTransitionType
{
    /// <summary>
    /// Indicates a custom value that does not match any predefined options.
    /// </summary>
    Custom = 0,
    /// <summary>
    /// Represents a jump transition.
    /// </summary>
    Jump = 1,
    /// <summary>
    /// Represents a swim entry transition.
    /// </summary>
    SwimEntry = 2,
    /// <summary>
    /// Represents a swim exit transition.
    /// </summary>
    SwimExit = 3,
    /// <summary>
    /// Represents a takeoff transition.
    /// </summary>
    Takeoff = 4,
    /// <summary>
    /// Represents a landing transition.
    /// </summary>
    Landing = 5,
    /// <summary>
    /// Represents a climb transition.
    /// </summary>
    Climb = 6
}
