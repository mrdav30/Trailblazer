using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// The base interface for the scout.
    /// </summary>
    public interface IScout
    {
        public Vector3d WorldPosition { get; }

        public FixedQuaternion VisualRotation { get; }

        public Vector3d LinearVelocity { get; }// TODO: get rid of this and store seperately

        ScoutController ScoutController { get; }

        /// <summary>
        /// The events of the scout.
        /// </summary>
#nullable enable
        ScoutEvents? Events { get; }
#nullable disable

        void Simulate();

        void SetTraversalState(TraversalMedium medium, Fixed64? surfaceLevel = null, GroundState? movementState = null);

        void GetTraversalState(out TraversalState traversalState);

        void SetTraversalRequest(Vector3d movementDirection, TraversalSpeed traversalSpeed, bool isRequestingJump = false);

        Vector3d GetFootPosition();

        // Call before the end of the current frame (after the body of the IScout has actually applied controller movement) to unlock the motor
        void UnlockController();
    }
}
