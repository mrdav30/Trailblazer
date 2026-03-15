using FixedMathSharp;
using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Defines event-driven interactions for the <see cref="NavMotor"/>, including movement, forces, and state transitions.
/// </summary>
public class NavMotorEvents
{
#nullable enable

    /// <summary>
    /// Function that determines if the scout can afford to perform a jump action.
    /// </summary>
    /// <returns>True if the scout has enough resources or conditions to jump, otherwise false.</returns>
    public Func<bool>? CanAffordJump { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout breaches the water surface.
    /// </summary>
    public Action? OnStartWaterBreach { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout fully submerges after breaching the water surface.
    /// </summary>
    public Action? OnStopWaterBreach { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout initiates a jump.
    /// The provided value indicates how long ground checks should be ignored after jumping.
    /// </summary>
    public Action<Fixed64>? OnStartJump { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout stops jumping, typically after reaching the apex or landing.
    /// </summary>
    public Action? OnStopJump { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout lands after a jump or fall (but not a water breach).
    /// </summary>
    public Action? OnLandedFall { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout begins drowning due to prolonged underwater exposure.
    /// The provided value represents the duration the scout has been underwater before drowning.
    /// </summary>
    public Action<Fixed64>? OnDrowning { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout begins falling.
    /// </summary>
    public Action? OnStartFall { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout stops falling and lands.
    /// The event provides the height from which the scout fell.
    /// </summary>
    public Action<Fixed64>? OnStopFall { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout reaches the maximum allowable fall height.
    /// </summary>
    public Action? OnMaxFallHeightReached { get; set; } = null;

}
