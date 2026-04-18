using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Immutable frame snapshot describing a climb affordance supplied by the host.
/// </summary>
public readonly struct ClimbAffordanceSnapshot
{
    /// <summary>
    /// Initializes a new snapshot.
    /// </summary>
    public ClimbAffordanceSnapshot(
        ClimbAffordanceKind kind,
        Vector3d attachmentPoint,
        Vector3d surfaceNormal,
        Vector3d upDirection,
        int? affordanceId = null,
        bool canStartClimb = true,
        bool canContinueClimb = true,
        bool allowLateralTraverse = true,
        bool allowDescent = true,
        bool allowMantle = false,
        bool allowDetachJump = true,
        Vector3d? mantleTargetPosition = null)
    {
        Kind = kind;
        AttachmentPoint = attachmentPoint;
        SurfaceNormal = surfaceNormal;
        UpDirection = upDirection;
        AffordanceId = affordanceId;
        CanStartClimb = canStartClimb;
        CanContinueClimb = canContinueClimb;
        AllowLateralTraverse = allowLateralTraverse;
        AllowDescent = allowDescent;
        AllowMantle = allowMantle;
        AllowDetachJump = allowDetachJump;
        MantleTargetPosition = mantleTargetPosition;
    }

    public ClimbAffordanceKind Kind { get; }

    public int? AffordanceId { get; }

    public Vector3d AttachmentPoint { get; }

    public Vector3d SurfaceNormal { get; }

    public Vector3d UpDirection { get; }

    public bool CanStartClimb { get; }

    public bool CanContinueClimb { get; }

    public bool AllowLateralTraverse { get; }

    public bool AllowDescent { get; }

    public bool AllowMantle { get; }

    public bool AllowDetachJump { get; }

    public Vector3d? MantleTargetPosition { get; }
}
