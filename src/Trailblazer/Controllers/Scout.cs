using FixedMathSharp;
using System;

namespace Trailblazer.Controllers
{
    public abstract class Scout : IScout
    {
        public Vector3d WorldPosition { get; protected set; }

        public FixedQuaternion VisualRotation { get; protected set; } = FixedQuaternion.Identity;

        protected ScoutController _controller;
        public ScoutController ScoutController => _controller;

        protected ScoutEvents _events;
        public ScoutEvents Events => _events;

        protected TraversalState _traversalState;
        public TraversalState TraversalState => _traversalState;

        protected TraversalRequest _traversalRequest;

        public virtual void OnInitialize()
        {
            _events = new();
            _controller = ScoutController.CreateNew(this);
        }

        public virtual void SetTraversalState(
            TraversalMedium medium,
            Fixed64? surfaceLevel = null,
            GroundState? movementState = null)
        {
            _traversalState.Medium = medium;
            _traversalState.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
            _traversalState.Ground = movementState ?? null;
        }

        public virtual void SetTraversalRequest(
            Vector3d movementDirection,
            TraversalSpeed traversalSpeed,
            bool isRequestingJump = false)
        {
            _traversalRequest.MovementDirection = movementDirection;
            _traversalRequest.TraversalSpeed = traversalSpeed;
            _traversalRequest.IsRequestingJump = isRequestingJump;
        }

        public virtual void Simulate()
        {
            OnInitiateTraversal();
            OnFinalizeTraversal();
        }

        public virtual void OnInitiateTraversal()
        {
            ScoutController.Traverse(_traversalRequest);
            _traversalRequest = default;
        }

        public virtual void GetTraversalState(out TraversalState traversalState)
        {
            traversalState = TraversalState;
        }

        public virtual void OnFinalizeTraversal()
        {
            ScoutController.FinishTraversing();
        }

        public virtual Vector3d GetFootPosition()
        {
            return WorldPosition + Vector3d.Down * Fixed64.FromRaw(0x40000000L);
        }
    }
}
