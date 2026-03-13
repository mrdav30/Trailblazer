using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation;

[Serializable]
public class TrekRequest
{
    /// <summary>
    /// The world position from which the scout is requesting movement. 
    /// </summary>
    public Vector3d Origin { get; set; }

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

    public TrekRequest(
        Vector3d? origin = null,
        FixedQuaternion? rotation = null,
        Vector3d? direction = null,
        TrekRate rate = TrekRate.Stationary,
        bool requestingJump = false)
    {
        Origin = origin ?? Vector3d.Zero;
        Rotation = rotation ?? FixedQuaternion.Identity;
        Direction = direction ?? Vector3d.Zero;
        Rate = rate;
        IsRequestingJump = requestingJump;
    }

    /// <summary>
    /// Represents an empty movement request with default values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TrekRequest CreateEmpty() => new();

    /// <summary>
    /// Creates a deep copy of the current <see cref="TrekRequest"/> instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TrekRequest Clone() => new TrekRequest(Origin, Rotation, Direction, Rate, IsRequestingJump);

    /// <summary>
    /// Resets the movement request to default values, indicating no movement intent.
    /// </summary>
    public void Reset()
    {
        Origin = Vector3d.Zero;
        Rotation = FixedQuaternion.Identity;
        Direction = Vector3d.Zero;
        Rate = TrekRate.Stationary;
        IsRequestingJump = false;
    }
}
