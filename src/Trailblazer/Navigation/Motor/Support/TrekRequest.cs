using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Serialization;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents a movement request for a navigator to pass to <see cref="NavMotor"/>,
/// containing the current origin, foot position, rotation, direction, speed, jump intent, and flight intent.
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
    /// Optional world-space facing direction that overrides the default "face movement" behavior for this request.
    /// </summary>
    public Vector3d? FacingDirection;

    /// <summary>
    /// The speed at which the scout wants to move.
    /// </summary>
    public TrekRate Rate;

    /// <summary>
    /// Indicates whether the scout is requesting to jump.
    /// </summary>
    public bool IsRequestingJump;

    /// <summary>
    /// Indicates whether the scout is requesting controlled flight.
    /// </summary>
    public bool IsRequestingFlight;

    public TrekRequest() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRequest(
        Vector3d direction, 
        TrekRate rate, 
        bool isRequestingJump, 
        bool isRequestingFlight,
        Vector3d? facingDirection = null)
    {
        Direction = direction;
        FacingDirection = facingDirection.HasValue && facingDirection.Value != Vector3d.Zero
            ? facingDirection
            : null;
        Rate = rate;
        IsRequestingJump = isRequestingJump;
        IsRequestingFlight = isRequestingFlight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTransientState(
        Vector3d origin, 
        Vector3d? footPosition, 
        FixedQuaternion rotation, 
        Vector3d? direction)
    {
        Origin = origin;
        FootPosition = footPosition;
        Rotation = rotation;

        // Only update direction if a new value is provided, 
        // otherwise preserve the existing direction for this frame.
        if (direction.HasValue)
            Direction = direction.Value;
    }

    /// <summary>
    /// Creates a deep copy of the current <see cref="TrekRequest"/> instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TrekRequest Clone() => new()
    {
        Origin = Origin,
        Rotation = Rotation,
        Direction = Direction,
        FacingDirection = FacingDirection,
        Rate = Rate,
        IsRequestingJump = IsRequestingJump,
        IsRequestingFlight = IsRequestingFlight,
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
        FacingDirection = null;
        IsRequestingJump = false;
        Rate = TrekRate.Stationary;
        IsRequestingFlight = false;
    }

    /// <summary>
    /// Resets only the transient movement data that should be cleared each frame, 
    /// while preserving any persistent data that may be set externally.
    /// </summary>
    public void ResetTransient()
    {
        Origin = Vector3d.Zero;
        FootPosition = null;
        Rotation = FixedQuaternion.Identity;
        Direction = Vector3d.Zero;
        IsRequestingJump = false;
    }

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref Origin, nameof(Origin), Vector3d.Zero);
        RecordValues.Look(chronicler, ref FootPosition, nameof(FootPosition), null);
        RecordValues.Look(chronicler, ref Rotation, nameof(Rotation), FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref Direction, nameof(Direction), Vector3d.Zero);
        RecordValues.Look(chronicler, ref FacingDirection, nameof(FacingDirection), null);
        RecordValues.Look(chronicler, ref Rate, nameof(Rate), TrekRate.Stationary);
        RecordValues.Look(chronicler, ref IsRequestingJump, nameof(IsRequestingJump), false);
        RecordValues.Look(chronicler, ref IsRequestingFlight, nameof(IsRequestingFlight), false);
    }
}
