using Chronicler;
using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents a movement request for a object to pass to <see cref="NavMotor"/>,
/// containing the current origin, foot position, rotation, direction, speed, and frame-owned locomotion intent.
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
    /// Per-frame host query result that authoritatively answers whether jump may spend host-owned resources this frame.
    /// </summary>
    public bool CanAffordJump;

    /// <summary>
    /// Indicates whether the scout is requesting controlled flight.
    /// </summary>
    public bool IsRequestingFlight;

    /// <summary>
    /// Indicates whether the scout is requesting climb engagement or continuation.
    /// </summary>
    public bool IsRequestingClimb;

    /// <summary>
    /// Initializes a new instance of the TrekRequest class with default values.
    /// </summary>
    public TrekRequest()
    {
        CanAffordJump = true;
    }

    /// <summary>
    /// Sets the current movement request parameters, including direction, rate, and action flags such as jump, flight, and climb.
    /// </summary>
    /// <param name="direction">The movement direction vector to apply for the request.</param>
    /// <param name="rate">The rate at which the movement should be performed.</param>
    /// <param name="isRequestingJump">true to request a jump action; otherwise, false.</param>
    /// <param name="isRequestingFlight">true to request a flight action; otherwise, false.</param>
    /// <param name="isRequestingClimb">true to request a climb action; otherwise, false.</param>
    /// <param name="facingDirection">
    /// An optional vector specifying the desired facing direction. 
    /// If null or equal to Vector3d.Zero, the facing direction is not changed.</param>
    /// <param name="canAffordJump">true if the jump action can be afforded; otherwise, false. Defaults to true.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRequest(
        Vector3d direction,
        TrekRate rate,
        bool isRequestingJump,
        bool isRequestingFlight,
        bool isRequestingClimb,
        Vector3d? facingDirection = null,
        bool canAffordJump = true)
    {
        Direction = direction;
        FacingDirection = facingDirection.HasValue && facingDirection.Value != Vector3d.Zero
            ? facingDirection
            : null;
        Rate = rate;
        IsRequestingJump = isRequestingJump;
        CanAffordJump = canAffordJump;
        IsRequestingFlight = isRequestingFlight;
        IsRequestingClimb = isRequestingClimb;
    }

    /// <summary>
    /// Sets the transient state for the current frame, including origin, optional foot position, rotation, and optionally direction.
    /// </summary>
    /// <remarks>
    /// If <paramref name="direction"/> is null, the current direction is retained for this frame.
    /// This method is intended for per-frame updates where only some state components may change.
    /// </remarks>
    /// <param name="origin">The new origin position to set for the transient state.</param>
    /// <param name="footPosition">The foot position to set for the transient state, or null to leave it unset.</param>
    /// <param name="rotation">The rotation to apply to the transient state.</param>
    /// <param name="direction">The direction to set for the transient state, or null to preserve the existing direction.</param>
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
        CanAffordJump = CanAffordJump,
        IsRequestingFlight = IsRequestingFlight,
        IsRequestingClimb = IsRequestingClimb,
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
        CanAffordJump = true;
        Rate = TrekRate.Stationary;
        IsRequestingFlight = false;
        IsRequestingClimb = false;
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
        CanAffordJump = true;
        IsRequestingClimb = false;
    }

    /// <inheritdoc/>
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref Origin, nameof(Origin), Vector3d.Zero);
        RecordValues.Look(chronicler, ref FootPosition, nameof(FootPosition), null);
        RecordValues.Look(chronicler, ref Rotation, nameof(Rotation), FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref Direction, nameof(Direction), Vector3d.Zero);
        RecordValues.Look(chronicler, ref FacingDirection, nameof(FacingDirection), null);
        RecordValues.Look(chronicler, ref Rate, nameof(Rate), TrekRate.Stationary);
        RecordValues.Look(chronicler, ref IsRequestingJump, nameof(IsRequestingJump), false);
        RecordValues.Look(chronicler, ref CanAffordJump, nameof(CanAffordJump), true);
        RecordValues.Look(chronicler, ref IsRequestingFlight, nameof(IsRequestingFlight), false);
        RecordValues.Look(chronicler, ref IsRequestingClimb, nameof(IsRequestingClimb), false);
    }
}
