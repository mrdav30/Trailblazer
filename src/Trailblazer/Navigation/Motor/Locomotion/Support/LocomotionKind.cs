using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Identifies the built-in locomotion modules that can be installed on a <see cref="LocomotionHandler"/>.
/// </summary>
[Flags]
public enum LocomotionKind
{
    /// <summary>
    /// No locomotions are installed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Core ground and air movement settings.
    /// </summary>
    Move = 1 << 0,

    /// <summary>
    /// Moving-platform attachment and transfer behavior.
    /// </summary>
    Platform = 1 << 1,

    /// <summary>
    /// Jump impulse and jump-state behavior.
    /// </summary>
    Jump = 1 << 2,

    /// <summary>
    /// Fall-state tracking and fall-control behavior.
    /// </summary>
    Fall = 1 << 3,

    /// <summary>
    /// Steep-surface sliding behavior.
    /// </summary>
    Slide = 1 << 4,

    /// <summary>
    /// Active swim behavior and water-specific runtime state.
    /// </summary>
    Swim = 1 << 5,

    /// <summary>
    /// Controlled airborne flight behavior.
    /// </summary>
    Fly = 1 << 6,

    /// <summary>
    /// Attached climb behavior and runtime state.
    /// </summary>
    Climb = 1 << 7,

    /// <summary>
    /// The required locomotion set used by all motors.
    /// </summary>
    Core = Move | Fall,

    /// <summary>
    /// The optional locomotion set used by the built-in presets.
    /// </summary>
    Optional = Platform | Jump | Slide | Swim | Fly | Climb,

    /// <summary>
    /// The built-in locomotion set containing every shipped module.
    /// </summary>
    All = Core | Optional
}
