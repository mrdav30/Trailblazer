//=======================================================================
// NavigationMotorSettings2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>Defines native planar motor tuning separately from body geometry.</summary>
public readonly struct NavigationMotorSettings2D
{
    /// <summary>Creates non-negative motor tuning. Gravity is a magnitude along negative Forward.</summary>
    public NavigationMotorSettings2D(Fixed64 moveSpeed, Fixed64 turnRateRadians, Fixed64 gravity, Fixed64 jumpSpeed)
        : this(moveSpeed, turnRateRadians, gravity, jumpSpeed, 1) { }

    /// <summary>Creates motor tuning with a positive limit on consecutive side-view jumps.</summary>
    public NavigationMotorSettings2D(Fixed64 moveSpeed, Fixed64 turnRateRadians, Fixed64 gravity, Fixed64 jumpSpeed,
        int maxJumpCount)
    {
        ChronicleGuard.ThrowIfArgument(moveSpeed < Fixed64.Zero || turnRateRadians < Fixed64.Zero
            || gravity < Fixed64.Zero || jumpSpeed < Fixed64.Zero, nameof(moveSpeed));
        ChronicleGuard.ThrowIfArgument(maxJumpCount <= 0, nameof(maxJumpCount));
        MoveSpeed = moveSpeed;
        TurnRateRadians = turnRateRadians;
        Gravity = gravity;
        JumpSpeed = jumpSpeed;
        MaxJumpCount = maxJumpCount;
    }
    /// <summary>Gets movement speed in world units per second.</summary>
    public Fixed64 MoveSpeed { get; }
    /// <summary>Gets angular speed in radians per second.</summary>
    public Fixed64 TurnRateRadians { get; }
    /// <summary>Gets downward acceleration in world units per second squared.</summary>
    public Fixed64 Gravity { get; }
    /// <summary>Gets upward jump speed in world units per second.</summary>
    public Fixed64 JumpSpeed { get; }
    /// <summary>Gets the side-view launch limit. Values above one permit extra jumps during ascent.</summary>
    public int MaxJumpCount { get; }

    internal void Validate(NavigationMode2D mode)
    {
        ChronicleGuard.ThrowIfArgument(mode == NavigationMode2D.TopDown
            ? Gravity != Fixed64.Zero || JumpSpeed != Fixed64.Zero || MaxJumpCount > 1
            : Gravity <= Fixed64.Zero || TurnRateRadians != Fixed64.Zero, nameof(mode));
    }
}
