//=======================================================================
// MotionTransfer.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Selects departure-velocity transfer or extended attachment to a moving platform.
/// </summary>
/// <remarks>
/// Ordinary grounded platform carry is independent of departure-velocity transfer.
/// Carry requires an enabled platform module and an active kinematic platform;
/// jumping, controlled flight, and climbing suppress it in every mode.
/// Departure transfer is evaluated after platform-state refresh. The refreshed ground condition
/// must retain the eligible launch-platform snapshot and transfer mode for that frame;
/// clearing the platform or making it ineligible selects <see cref="None"/> and skips transfer.
/// </remarks>
public enum MotionTransfer
{
    /// <summary>
    /// Does not add departure velocity. Ordinary grounded platform displacement and rotation still apply.
    /// </summary>
    None = 0,

    /// <summary>
    /// Adds the sampled platform velocity on a solid-to-gas transition.
    /// Subsequent locomotion can change that velocity.
    /// </summary>
    InitTransfer = 1,

    /// <summary>
    /// Performs the initial velocity transfer and adds the captured horizontal contribution
    /// to ordinary desired locomotion velocity while that contribution is retained.
    /// </summary>
    /// <remarks>
    /// Platform-state refresh clears the captured contribution. This mode does not guarantee
    /// an unchanged total velocity until landing.
    /// </remarks>
    PermaTransfer = 2,

    /// <summary>
    /// Allows platform carry beyond grounded movement while an eligible active platform remains.
    /// Does not select departure-velocity transfer or override jump, flight, or climb exclusions.
    /// </summary>
    PermaLocked = 3
}
