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
    /// Gets the normal vector of the surface at the current location.
    /// </summary>
    public Vector3d SurfaceNormal { get; }

    /// <summary>
    /// Gets the upward direction vector for the current coordinate system.
    /// </summary>
    public Vector3d UpDirection { get; }

    /// <summary>
    /// Gets a value indicating whether climbing can be started based on the current state.
    /// </summary>
    public bool CanStartClimb { get; }

    /// <summary>
    /// Gets a value indicating whether the climb can continue based on the current state.
    /// </summary>
    public bool CanContinueClimb { get; }

    /// <summary>
    /// Gets a value indicating whether lateral traversal is permitted.
    /// </summary>
    public bool AllowLateralTraverse { get; }

    /// <summary>
    /// Gets a value indicating whether descent is permitted in the current context.
    /// </summary>
    public bool AllowDescent { get; }

    /// <summary>
    /// Gets a value indicating whether mantle actions are permitted.
    /// </summary>
    public bool AllowMantle { get; }

    /// <summary>
    /// Gets a value indicating whether detach jump operations are permitted.
    /// </summary>
    public bool AllowDetachJump { get; }

    /// <summary>
    /// Gets the target position for the mantle action, if available.
    /// </summary>
    /// <remarks>
    /// The target position is determined when a mantle action is in progress. 
    /// If no mantle is being performed, the value is null.
    /// </remarks>
    public Vector3d? MantleTargetPosition { get; }
}
