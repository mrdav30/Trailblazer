namespace Trailblazer.Navigation.Motor
{
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

    /// <summary>
    /// Defines how a scout inherits movement from the platform it is standing on.
    /// </summary>
    public enum MotionTransfer
    {
        /// <summary>
        /// The scout is unaffected by the movement of the platform.
        /// </summary>
        None = 0,

        /// <summary>
        /// The scout receives an initial velocity from the platform but gradually slows down.
        /// </summary>
        InitTransfer = 1,

        /// <summary>
        /// The scout maintains its velocity from the platform until it lands again.
        /// </summary>
        PermaTransfer = 2,

        /// <summary>
        /// The scout is locked to the movement of the platform and moves along with it.
        /// </summary>
        PermaLocked = 3
    }
}
