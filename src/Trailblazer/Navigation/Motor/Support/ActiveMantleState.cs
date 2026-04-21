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

    public ClimbAffordanceKind Kind { get; }

    public int? AffordanceId { get; }

    public Vector3d AttachmentPoint { get; }

    public Vector3d SurfaceNormal { get; }

    public Vector3d UpDirection { get; }

    public Vector3d MantleTargetPosition { get; }
}
