using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests
{
    public class MockAgent
    {
        public Vector3d Position { get; set; }

        public FixedQuaternion Rotation { get; set; }

        public Vector3d Velocity { get; set; }

        public Fixed64 Radius => Fixed64.Half;

        private Vector3d _velocityDelta;

        private Vector3d _positionDelta;

        private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

        public Navigator Navigator { get; set; } = new Navigator();

        public MockAgent(Vector3d position, Vector3d velocity, TraversalCondition traversalCondition)
        {
            Position = position;

            MockGroundCheck();

            Navigator.Initialize(position, velocity, Radius, traversalCondition);

            Navigator.Events.CanAffordJump = () => true;

            Navigator.Events.OnAddPositionDelta += (deltaPos) =>
            {
                _positionDelta += deltaPos;
            };
            Navigator.Events.OnAddRotationDelta += (rot) =>
            {
                _rotationDelta *= rot;
            };
            Navigator.Events.OnAddLinearForce += (force) =>
            {
                // assume a mass of 1
                _velocityDelta += force;
            };

        }

        public void Simulate()
        {
            Navigator.OnSimulate();
        }

        public void Visualize()
        {
            Vector3d previousPosition = Position;

            // resolve velocity
            Position += _positionDelta + _velocityDelta;

            if (_rotationDelta != FixedQuaternion.Identity)
            {
                Rotation *= _rotationDelta;
                _rotationDelta = FixedQuaternion.Identity;
            }

            MockGroundCheck();

            Velocity = (Position - previousPosition) / TrailblazerManager.DeltaTime;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;

            Navigator.OnVisualize();
        }

        private TraversalMedium? _previousMedium;
        // Update TraversalState based on output from controller
        public void MockGroundCheck()
        {
            // If scout is already grounded, maintain state unless velocity pushes it up
            if (Navigator.TraversalCondition.Medium == TraversalMedium.Ground)
            {
                if (_velocityDelta.y > Fixed64.Zero)
                {
                    // If scout is moving upwards, it should no longer be grounded
                    _previousMedium = Navigator.TraversalCondition.Medium;
                    Navigator.TraversalCondition.Medium = TraversalMedium.Air;
                    return;
                }

                var surfaceMatrix = Navigator.TraversalCondition.GroundState?.GroundMatrix;
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
            if (Navigator.TraversalCondition.Medium == TraversalMedium.Air)
            {
                Fixed64 surfaceLevel = Navigator.TraversalCondition.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                // Ensure velocity is downward and scout is within landing range
                if (_velocityDelta.y < Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
                {
                    // Set state to previous state or assume ground
                    Navigator.TraversalCondition.Medium = _previousMedium ?? TraversalMedium.Ground;
                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);

                    if (Navigator.TraversalCondition.Medium == TraversalMedium.Ground)
                    {
                        // Update ground normal if needed (assuming ground is flat for now)
                        Navigator.TraversalCondition.GroundState ??= new GroundCondition
                        {
                            GroundMatrix = Fixed4x4.Identity, // Assuming a flat ground by default
                        };
                    }
                }

                return;
            }

            if (Navigator.TraversalCondition.Medium == TraversalMedium.Water)
            {
                Fixed64 surfaceLevel = Navigator.TraversalCondition.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                if (scoutHeight > surfaceLevel)
                {
                    if (_velocityDelta.y > Fixed64.Zero)
                    {
                        // If scout is moving upwards, it should no longer be grounded
                        _previousMedium = Navigator.TraversalCondition.Medium;
                        Navigator.TraversalCondition.Medium = TraversalMedium.Air;
                        return;
                    }

                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);
                }
            }
        }
    }
}