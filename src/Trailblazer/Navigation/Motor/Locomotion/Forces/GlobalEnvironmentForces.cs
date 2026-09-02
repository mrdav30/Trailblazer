//=======================================================================
// GlobalEnvironmentForces.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Holds simulation-wide gravity defaults read by all <see cref="MoveLocomotion"/> instances
/// that do not carry a per-instance override.
/// </summary>
/// <remarks>
/// Access the shared instance through <see cref="LocomotionForces.GlobalForces"/>.
/// Assigning fields on that instance changes the effective gravity for every unoverridden object
/// on the next simulation frame without touching individual locomotion instances.
/// </remarks>
public sealed class GlobalEnvironmentForces
{
    /// <summary>
    /// The default fixed-point acceleration force for gravity.
    /// </summary>
    /// <remarks>
    /// Default acceleration due to gravity is approximately 9.8 m/s².
    /// </remarks>
    public readonly static Fixed64 DefaultGravityForce = Fixed64.FromRaw(0x9CCCCCCCDL);

    /// <summary>
    /// The default maximum downward fixed-point velocity a scout can reach due to gravity.
    /// </summary>
    /// <remarks>
    /// Default terminal velocity is roughly 53 m/s (190 km/h or ~120 mph).
    /// </remarks>
    public readonly static Fixed64 DefaultTerminalVelocity = (Fixed64)53f;

    /// <summary>
    /// The gravity force applied to all navigators without a per-instance override.
    /// </summary>
    public Fixed64 GravityForce;

    /// <summary>
    /// The terminal fall velocity cap applied to all navigators without a per-instance override.
    /// </summary>
    public Fixed64 TerminalVelocity;

    internal GlobalEnvironmentForces()
    {
        GravityForce = DefaultGravityForce;
        TerminalVelocity = DefaultTerminalVelocity;
    }

    /// <summary>
    /// Restores both global forces to the <see cref="MoveLocomotion"/> defaults.
    /// </summary>
    public void Reset()
    {
        GravityForce = DefaultGravityForce;
        TerminalVelocity = DefaultTerminalVelocity;
    }
}
