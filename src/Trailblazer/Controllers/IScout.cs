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

        public Vector3d LinearVelocity { get; }

        ScoutController ScoutMotor { get; }

        /// <summary>
        /// The events of the scout.
        /// </summary>
        ScoutEvents Events { get; }

        void SetTraversalState(TraversalData traversalState);

        void GetTraversalState(out TraversalData traversalState);

        Vector3d GetFootPosition();

        /// <summary>
        /// Call after <see cref="ScoutController.Simulate"/> and before next frame to unlock the ScoutController.
        /// </summary>
        void FinalizeMovement();
    }
}
