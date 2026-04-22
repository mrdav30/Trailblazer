using FixedMathSharp;
using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Defines host interaction hooks for the <see cref="NavMotor"/>.
/// Remaining query callbacks are climb-specific; the rest are state notifications.
/// </summary>
public class NavMotorEvents
{
#nullable enable

    /// <summary>
    /// Optional callback that performs a final host veto before a climb starts
    /// after the frame's climb affordance snapshot has already been resolved.
    /// </summary>
    public Func<bool>? CanStartClimb { get; set; } = null;

    /// <summary>
    /// Optional callback that performs a final host veto before an active climb continues
    /// after the frame's climb affordance snapshot has already been resolved.
    /// </summary>
    public Func<bool>? CanContinueClimb { get; set; } = null;

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
    /// Event triggered when the scout starts climbing.
    /// </summary>
    public Action<ClimbAffordanceSnapshot>? OnStartClimb { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout stops climbing.
    /// </summary>
    public Action? OnStopClimb { get; set; } = null;

    /// <summary>
    /// Event triggered when the scout begins mantling from a climb.
    /// </summary>
    public Action? OnStartMantle { get; set; } = null;

    /// <summary>
    /// Event triggered when the active climb is forcibly broken.
    /// </summary>
    public Action? OnClimbSlip { get; set; } = null;

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
