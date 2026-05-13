namespace Trailblazer.Navigation.Motor;

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
