using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents a movement request for a navigator to pass to <see cref="NavMotor"/>,
/// containing the current origin, foot position, rotation, direction, speed, and jump intent.
/// </summary>
[Serializable]
public struct TrekRequest : IRecordable
{
    /// <summary>
    /// The world position from which the scout is requesting movement. 
    /// </summary>
    public Vector3d Origin;

    /// <summary>
    /// The position of the scout's foot in world space, used for ground detection and platform interaction.
    /// </summary>
    public Vector3d? FootPosition;

    /// <summary>
    /// The desired rotation of the scout in world space, used for orientation and facing direction.
    /// </summary>
    public FixedQuaternion Rotation;

    /// <summary>
    /// Normalized distance of movement
    /// </summary>
    public Vector3d Direction;

    /// <summary>
    /// The speed at which the scout wants to move.
    /// </summary>
    public TrekRate Rate;

    /// <summary>
    /// Indicates whether the scout is requesting to jump.
    /// </summary>
    public bool IsRequestingJump;

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
        FootPosition = FootPosition
    };

    /// <summary>
    /// Resets the per-frame movement data.
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

    public void RecordData(IChronicler chronicler)
    {
        chronicler.LookValue(ref Origin, nameof(Origin), Vector3d.Zero);
        chronicler.LookValue(ref FootPosition, nameof(FootPosition), Vector3d.Zero);
        chronicler.LookValue(ref Rotation, nameof(Rotation), FixedQuaternion.Identity);
        chronicler.LookValue(ref Direction, nameof(Direction), Vector3d.Zero);
        chronicler.LookValue(ref Rate, nameof(Rate), TrekRate.Stationary);
        chronicler.LookValue(ref IsRequestingJump, nameof(IsRequestingJump), false);
    }
}
