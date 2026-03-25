namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Specifies the different movement mediums a scout can traverse through.
/// </summary>
public enum TraversalMedium
{
    /// <summary>
    /// The scout's movement medium is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The scout is traversing on the ground.
    /// </summary>
    Ground = 1,

    /// <summary>
    /// The scout is airborne.
    /// </summary>
    Air = 2,

    /// <summary>
    /// The scout is moving through water.
    /// </summary>
    Water = 3,
}
