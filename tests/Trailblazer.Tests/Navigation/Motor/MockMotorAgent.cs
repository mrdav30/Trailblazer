using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation.Motor
{
    public class MockMotorAgent : IMotor
    {
        private TraversalMedium? _previousMedium;

        public Vector3d Position { get; set; }

        public Vector3d LastPosition { get; set; }

        public FixedQuaternion Rotation { get; set; }

        public Vector3d Velocity { get; set; }

        public TrekCondition FrameCondition { get; set; }

        public TrekRequest FrameRequest { get; set; }

        private Vector3d _positionDelta;

        private FixedQuaternion _rotationDelta = FixedQuaternion.Identity;

        private Vector3d _velocityDelta;

        public NavMotor Motor { get; set; }

        public MockMotorAgent(
            Vector3d position,
            TrekCondition condition,
            FixedQuaternion? rotation = null,
            Vector3d? velocity = null)
        {
            LastPosition = Position = position;

            FrameCondition = condition;
            FrameRequest = TrekRequest.CreateEmpty();

            Rotation = rotation ?? FixedQuaternion.Identity;
            Velocity = velocity ?? Vector3d.Zero;

            Motor = NavMotor.CreateNew(Position, FrameCondition);
            Motor.SetVelocity(Velocity);

            Motor.Events.CanAffordJump = () => true;

            CheckTrekCondition();
            Motor.UpdateTraversal(FrameCondition, true);
        }

        public void Simulate()
        {
            FrameRequest.Origin = Position;
            FrameRequest.Rotation = Rotation;

            Motor.Traverse(this);

            LastPosition = Position;
            Position += _positionDelta + _velocityDelta;

            if (_rotationDelta != FixedQuaternion.Identity)
            {
                Rotation *= _rotationDelta;
                _rotationDelta = FixedQuaternion.Identity;
            }

            CheckTrekCondition();

            Fixed64 invDelta = TrailblazerManager.InvDeltaTime;
            Velocity = (Position - LastPosition) * invDelta;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;

            Motor.FinalizeTraversal(this);

            // Reset travel request for next frame
            FrameRequest.Reset();
        }

        // Update TraversalState based on output from controller
        public void CheckTrekCondition()
        {
            // If scout is already grounded, maintain state unless velocity pushes it up
            if (FrameCondition.Medium == TraversalMedium.Ground)
            {
                if (_velocityDelta.y > Fixed64.Zero)
                {
                    // If scout is moving upwards, it should no longer be grounded
                    _previousMedium = FrameCondition.Medium;
                    FrameCondition.Medium = TraversalMedium.Air;
                    return;
                }

                var surfaceMatrix = FrameCondition.GroundState?.GroundMatrix;
                if (surfaceMatrix != null)
                {
                    // Compute world Y value from surface plane based on scout's X/Z
                    Vector3d localPosition = surfaceMatrix.Value.InverseTransformPoint(Position);
                    localPosition.y = Fixed64.Zero; // align to the platform's base plane
                    Vector3d alignedWorld = surfaceMatrix.Value.TransformPoint(localPosition);

                    // Note: agent must be fully snapped to slope plane or velocity won't match projection.
                    if (Position.y < alignedWorld.y)
                        Position = alignedWorld;
                }

                return;
            }

            // If scout is airborne, check if it should transition to grounded
            if (FrameCondition.Medium == TraversalMedium.Air)
            {
                Fixed64 surfaceLevel = FrameCondition.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                // Ensure velocity is downward and scout is within landing range
                if (_velocityDelta.y < Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
                {
                    // Set state to previous state or assume ground
                    FrameCondition.Medium = _previousMedium ?? TraversalMedium.Ground;
                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);

                    if (FrameCondition.Medium == TraversalMedium.Ground)
                    {
                        // Update ground normal if needed (assuming ground is flat for now)
                        FrameCondition.GroundState ??= new GroundCondition
                        {
                            GroundMatrix = Fixed4x4.Identity, // Assuming a flat ground by default
                        };
                    }
                }

                return;
            }

            if (FrameCondition.Medium == TraversalMedium.Water)
            {
                Fixed64 surfaceLevel = FrameCondition.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                if (scoutHeight > surfaceLevel)
                {
                    if (_velocityDelta.y > Fixed64.Zero)
                    {
                        // If scout is moving upwards, it should no longer be grounded
                        _previousMedium = FrameCondition.Medium;
                        FrameCondition.Medium = TraversalMedium.Air;
                        return;
                    }

                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);
                }
            }
        }

        public virtual void AddPositionDelta(Vector3d delta)
        {
            _positionDelta += delta;
            // shift last position so it doesn't alter navigator's velocity
            LastPosition += delta;
        }

        public virtual void AddRotationDelta(FixedQuaternion delta)
        {
            _rotationDelta *= delta;
        }

        public virtual void AddVelocityDelta(Vector3d delta)
        {
            _velocityDelta += delta;
        }

        public virtual Vector3d GetFootPosition()
        {
            return Position + Vector3d.Down * Fixed64.Half;
        }
    }
}