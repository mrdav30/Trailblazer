using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

public partial class NavMotor
{
    #region Utility

    /// <summary>
    /// Computes the vertical jump speed required to reach the desired jump height (apex).
    /// </summary>
    /// <returns>The initial vertical velocity needed for the jump.</returns>
    public Fixed64 GetVerticalJumpSpeed() => JumpModule == null
        ? Fixed64.Zero
        : FixedMath.Sqrt(2 * JumpModule.BaseJumpHeight * Handler.Forces.GravityForce);

    /// <summary>
    /// Determines whether the current surface is too steep for normal movement.
    /// </summary>
    /// <returns>True if the slope exceeds the allowable incline; otherwise, false.</returns>
    public bool IsTooSteep(Fixed64 angle)
    {
        if (!IsOnSolid) return false;

        Fixed64 absAngle = FixedMath.Abs(angle); // Handle both positive (uphill) and negative (downhill) slopes
        return absAngle > Handler.Move.SlopeLimit - Fixed64.Epsilon;
    }

    /// <summary>
    /// Checks if the object is on a sloped surface that is not considered too steep.
    /// </summary>
    /// <returns>True if the object is on a valid slope; otherwise, false.</returns>
    public bool IsOnSlope(Fixed64 angle)
    {
        if (!IsOnSolid) return false;

        Fixed64 absAngle = FixedMath.Abs(angle); // Account for downhill slopes too
        return absAngle > Fixed64.One && absAngle <= Handler.Move.SlopeLimit + Fixed64.Epsilon;
    }

    /// <summary>
    /// Manually sets the object’s velocity, overriding the computed velocity for the next frame.
    /// </summary>
    /// <param name="velocity">The new velocity to assign to the object.</param>
    public void SetVelocity(Vector3d velocity)
    {
        Handler.Move.FrameVelocity = velocity;
    }

    /// <summary>
    /// Pushes a traversal snapshot into the motor before the next traversal phase begins.
    /// </summary>
    /// <remarks>
    /// This is the explicit pre-traversal sync seam for hosts that learn about medium or surface
    /// changes before the next call to <see cref="TryTraversal(TrekRequest, out Vector3d, out Vector3d, out FixedQuaternion)"/>.
    /// </remarks>
    public void SyncTraversalState(TrekCondition newCondition, bool isInitializing = false)
    {
        if (isInitializing)
        {
            // Don't set the previous state as an empty state
            CurrentState.Update(newCondition, newCondition);
            return;
        }

        TrekCondition previousCondition = CurrentState.ToTrekCondition();
        CurrentState.Update(newCondition, previousCondition);
    }

    /// <summary>
    /// Clears the current traversal-finalization requirement without reconciling frame results.
    /// </summary>
    /// <remarks>
    /// This is an explicit recovery escape hatch for hosts that must discard an in-progress traversal.
    /// It clears traversal bookkeeping only and does not roll back locomotion state changes that already occurred.
    /// </remarks>
    public void AbortTraversalFrame()
    {
        TraversalInProgress = false;
        _pendingTraversalFrame = -1;
        FrameSlopeAngle = Fixed64.Zero;
        _forceOutput = Vector3d.Zero;
    }

    #endregion
}
