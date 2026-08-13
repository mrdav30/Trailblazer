//=======================================================================
// ActiveMantleState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Immutable snapshot describing the currently latched mantle state.
/// </summary>
public readonly struct ActiveMantleState
{
    /// <summary>
    /// Initializes a new active mantle snapshot.
    /// </summary>
    public ActiveMantleState(
        ClimbAffordanceKind kind,
        int? affordanceId,
        Vector3d attachmentPoint,
        Vector3d surfaceNormal,
        Vector3d upDirection,
        Vector3d mantleTargetPosition)
    {
        Kind = kind;
        AffordanceId = affordanceId;
        AttachmentPoint = attachmentPoint;
        SurfaceNormal = surfaceNormal;
        UpDirection = upDirection;
        MantleTargetPosition = mantleTargetPosition;
    }

    /// <summary>
    /// Gets the type of climb affordance represented by this instance.
    /// </summary>
    public ClimbAffordanceKind Kind { get; }

    /// <summary>
    /// Gets the identifier of the associated affordance, if available.
    /// </summary>
    public int? AffordanceId { get; }

    /// <summary>
    /// Gets the position in 3D space where the object is attached.
    /// </summary>
    public Vector3d AttachmentPoint { get; }

    /// <summary>
    /// Gets the normal vector perpendicular to the surface at the current location.
    /// </summary>
    public Vector3d SurfaceNormal { get; }

    /// <summary>
    /// Gets the up direction vector for the current coordinate system or object.
    /// </summary>
    public Vector3d UpDirection { get; }

    /// <summary>
    /// Gets the target world position for the mantling action.
    /// </summary>
    public Vector3d MantleTargetPosition { get; }
}
