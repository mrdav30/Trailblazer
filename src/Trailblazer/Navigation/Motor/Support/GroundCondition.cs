using FixedMathSharp;
using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Represents the state of the surface the scout is interacting with, including surface movement and normal data.
/// </summary>
[Serializable]
public struct GroundCondition
{
    /// <summary>
    /// The object the scout is currently standing on.
    /// </summary>
    public PlatformHandle Platform;

    /// <summary>
    /// The current surface friction applied to movement.
    /// </summary>
    public Fixed64 SurfaceFriction;

    /// <summary>
    /// Determines how the scout inherits movement from the ground surface.
    /// </summary>
    public MotionTransfer MotionTransferState;

    public readonly Vector3d GroundNormal => Platform.Active ? Platform.Transform.Up : Vector3d.Zero;

    public GroundCondition Clone() => new()
    {
        Platform = Platform,
        SurfaceFriction = SurfaceFriction,
        MotionTransferState = MotionTransferState
    };
}
