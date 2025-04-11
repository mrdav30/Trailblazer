using FixedMathSharp;
using Trailblazer.Controllers;

namespace Trailblazer.Tests
{
    public class MockScout : Scout
    {
        public Vector3d Velocity { get; set; }

        private Vector3d _pendingVelocity;

        private Vector3d _positionDelta;

        private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

        public MockScout(Vector3d position, Vector3d velocity, TraversalCondition traversalCondition)
        {
            WorldPosition = position;

            _traversalCondition = traversalCondition;

            MockGroundCheck();

            _events = new();

            Events.CanAffordJump = () => true;

            Events.OnAddPlatformPositionDelta += (deltaPos) =>
            {
                _positionDelta += deltaPos;
            };
            Events.OnAddPlatformRotationDelta += (rot) =>
            {
                _rotationDelta *= rot;
            };
            Events.OnAddLinearForce += (force) =>
            {
                // assume a mass of 1
                _pendingVelocity += force;
            };

            _controller = ScoutController.CreateNew(this, _traversalCondition);
            ScoutController.SetVelocity(velocity);
        }

        public override void FinalizeTraversal()
        {
            Vector3d previousPosition = WorldPosition;

            // resolve velocity
            WorldPosition += _positionDelta + _pendingVelocity;

            if (_rotationDelta != FixedQuaternion.Identity)
            {
                VisualRotation *= _rotationDelta;
                _rotationDelta = FixedQuaternion.Identity;
            }

            MockGroundCheck();

            Velocity = (WorldPosition - previousPosition) / TrailblazerManager.DeltaTime;

            _positionDelta = Vector3d.Zero;
            _pendingVelocity = Vector3d.Zero;

            base.FinalizeTraversal();
        }

        private TraversalMedium? _previousMedium;
        // Update TraversalState based on output from controller
        private void MockGroundCheck()
        {
            // If scout is already grounded, maintain state unless velocity pushes it up
            if (_traversalCondition.Medium == TraversalMedium.Ground)
            {
                if (_pendingVelocity.y > Fixed64.Zero)
                {
                    // If scout is moving upwards, it should no longer be grounded
                    _previousMedium = _traversalCondition.Medium;
                    _traversalCondition.Medium = TraversalMedium.Air;
                    return;
                }

                var surfaceMatrix = _traversalCondition.GroundState?.GroundMatrix;
                if (surfaceMatrix != null)
                {
                    // Compute world Y value from surface plane based on scout's X/Z
                    Vector3d localPosition = surfaceMatrix.Value.InverseTransformPoint(WorldPosition);
                    localPosition.y = Fixed64.Zero; // align to the platform's base plane
                    Vector3d alignedWorld = surfaceMatrix.Value.TransformPoint(localPosition);

                    if (WorldPosition.y < alignedWorld.y)
                        WorldPosition = alignedWorld;
                }

                return;
            }

            // If scout is airborne, check if it should transition to grounded
            if (TraversalCondition.Medium == TraversalMedium.Air)
            {
                Fixed64 surfaceLevel = TraversalCondition.SurfaceLevel;
                Fixed64 scoutHeight = WorldPosition.y;

                // Ensure velocity is downward and scout is within landing range
                if (_pendingVelocity.y < Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
                {
                    // Set state to previous state or assume ground
                    _traversalCondition.Medium = _previousMedium ?? TraversalMedium.Ground;
                    WorldPosition = new Vector3d(WorldPosition.x, surfaceLevel, WorldPosition.z);

                    if (_traversalCondition.Medium == TraversalMedium.Ground)
                    {
                        // Update ground normal if needed (assuming ground is flat for now)
                        _traversalCondition.GroundState ??= new GroundCondition
                        {
                            GroundMatrix = Fixed4x4.Identity, // Assuming a flat ground by default
                        };
                    }
                }

                return;
            }

            if (TraversalCondition.Medium == TraversalMedium.Water)
            {
                Fixed64 surfaceLevel = TraversalCondition.SurfaceLevel;
                Fixed64 scoutHeight = WorldPosition.y;

                if (scoutHeight > surfaceLevel)
                {
                    if (_pendingVelocity.y > Fixed64.Zero)
                    {
                        // If scout is moving upwards, it should no longer be grounded
                        _previousMedium = _traversalCondition.Medium;
                        _traversalCondition.Medium = TraversalMedium.Air;
                        return;
                    }

                    WorldPosition = new Vector3d(WorldPosition.x, surfaceLevel, WorldPosition.z);
                }
            }
        }
    }
}