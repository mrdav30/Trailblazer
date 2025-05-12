using FixedMathSharp;
using Trailblazer.Navigator;
using Trailblazer.Navigator.Motor;

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
            Position = position;

            TraversalCondition = traversalCondition;

            MockGroundCheck();

            Events = new();

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

            Controller = NavigatorMotor.CreateNew(this, TraversalCondition);
            Controller.SetVelocity(velocity);
        }

        public override void FinalizeTraversal()
        {
            Vector3d previousPosition = Position;

            // resolve velocity
            Position += _positionDelta + _pendingVelocity;

            if (_rotationDelta != FixedQuaternion.Identity)
            {
                Rotation *= _rotationDelta;
                _rotationDelta = FixedQuaternion.Identity;
            }

            MockGroundCheck();

            Velocity = (Position - previousPosition) / TrailblazerManager.DeltaTime;

            _positionDelta = Vector3d.Zero;
            _pendingVelocity = Vector3d.Zero;

            base.FinalizeTraversal();
        }

        private TraversalMedium? _previousMedium;
        // Update TraversalState based on output from controller
        private void MockGroundCheck()
        {
            // If scout is already grounded, maintain state unless velocity pushes it up
            if (TraversalCondition.Medium == TraversalMedium.Ground)
            {
                if (_pendingVelocity.y > Fixed64.Zero)
                {
                    // If scout is moving upwards, it should no longer be grounded
                    _previousMedium = TraversalCondition.Medium;
                    TraversalCondition.Medium = TraversalMedium.Air;
                    return;
                }

                var surfaceMatrix = TraversalCondition.GroundState?.GroundMatrix;
                if (surfaceMatrix != null)
                {
                    // Compute world Y value from surface plane based on scout's X/Z
                    Vector3d localPosition = surfaceMatrix.Value.InverseTransformPoint(Position);
                    localPosition.y = Fixed64.Zero; // align to the platform's base plane
                    Vector3d alignedWorld = surfaceMatrix.Value.TransformPoint(localPosition);

                    if (Position.y < alignedWorld.y)
                        Position = alignedWorld;
                }

                return;
            }

            // If scout is airborne, check if it should transition to grounded
            if (TraversalCondition.Medium == TraversalMedium.Air)
            {
                Fixed64 surfaceLevel = TraversalCondition.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                // Ensure velocity is downward and scout is within landing range
                if (_pendingVelocity.y < Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
                {
                    // Set state to previous state or assume ground
                    TraversalCondition.Medium = _previousMedium ?? TraversalMedium.Ground;
                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);

                    if (TraversalCondition.Medium == TraversalMedium.Ground)
                    {
                        // Update ground normal if needed (assuming ground is flat for now)
                        TraversalCondition.GroundState ??= new GroundCondition
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
                Fixed64 scoutHeight = Position.y;

                if (scoutHeight > surfaceLevel)
                {
                    if (_pendingVelocity.y > Fixed64.Zero)
                    {
                        // If scout is moving upwards, it should no longer be grounded
                        _previousMedium = TraversalCondition.Medium;
                        TraversalCondition.Medium = TraversalMedium.Air;
                        return;
                    }

                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);
                }
            }
        }
    }
}