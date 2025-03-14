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

        ScoutController ScoutController { get; }

        /// <summary>
        /// The events of the scout.
        /// </summary>
#nullable enable
        ScoutEvents? Events { get; }
#nullable disable
        
        void SetTraversalState(TraversalMedium medium, Fixed64? surfaceLevel = null, GroundState? movementState = null);

        void SetTraversalRequest(Vector3d movementDirection, TraversalSpeed traversalSpeed, bool isRequestingJump = false);

        void InitiateTraversal();

        void GetTraversalState(out TraversalState traversalState);

        // Call before the end of the current frame to unlock the controller for the next frame
        void FinalizeTraversal();

        Vector3d GetFootPosition();
    }
}
