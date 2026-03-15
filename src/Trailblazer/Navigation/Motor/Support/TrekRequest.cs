using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents a movement request for a navigator to pass to <see cref="NavMotor"/> , containing the desired position, rotation, direction, speed, and jump intent.
/// </summary>
[Serializable]
public class TrekRequest
{
    /// <summary>
    /// The world position from which the scout is requesting movement. 
    /// </summary>
    public Vector3d Origin { get; set; }

    /// <summary>
    /// The position of the scout's foot in world space, used for ground detection and platform interaction.
    /// </summary>
    public Vector3d? FootPosition { get; set; }

    /// <summary>
    /// The desired rotation of the scout in world space, used for orientation and facing direction.
    /// </summary>
    public FixedQuaternion Rotation { get; set; }

    /// <summary>
    /// The target position the scout is trying to reach. 
    /// </summary>
    public Vector3d? TargetPosition { get; set; }

    /// <summary>
    /// Normalized distance of movement
    /// </summary>
    public Vector3d Direction { get; set; }

    /// <summary>
    /// The speed at which the scout wants to move.
    /// </summary>
    public TrekRate Rate { get; set; }

    /// <summary>
    /// Indicates whether the scout is requesting to jump.
    /// </summary>
    public bool IsRequestingJump { get; set; }

    public TrekRequest() { }

    /// <summary>
    /// Creates a deep copy of the current <see cref="TrekRequest"/> instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TrekRequest Clone() => new()
    {
        Origin = Origin,
        Rotation = Rotation,
        Direction = Direction,
        Rate = Rate,
        IsRequestingJump = IsRequestingJump,
        FootPosition = FootPosition,
        TargetPosition = TargetPosition
    };

    /// <summary>
    /// Resets the per-frame movement data while preserving any guided target owned by the current request.
    /// </summary>
    public void Reset()
    {
        Origin = Vector3d.Zero;
        FootPosition = null;
        Rotation = FixedQuaternion.Identity;
        Direction = Vector3d.Zero;
        Rate = TrekRate.Stationary;
        IsRequestingJump = false;
    }
}
