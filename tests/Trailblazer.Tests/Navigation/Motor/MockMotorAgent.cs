using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation.Motor
{
    public class MockMotorAgent : Navigator
    {
        public MockMotorAgent() { }

        public MockMotorAgent(Vector3d position)
        {
            Setup(position);
        }

        public override void Initialize(TraversalCondition surfaceState)
        {
            base.Initialize(surfaceState);

            Motor.Events.CanAffordJump = () => true;

            CheckTraversalCondition();
            Motor.UpdateTraversal(TraversalState, true);
        }

        private TraversalMedium? _previousMedium;
        // Update TraversalState based on output from controller
        protected override void CheckTraversalCondition()
        {
            // If scout is already grounded, maintain state unless velocity pushes it up
            if (TraversalState.Medium == TraversalMedium.Ground)
            {
                if (_velocityDelta.y > Fixed64.Zero)
                {
                    // If scout is moving upwards, it should no longer be grounded
                    _previousMedium = TraversalState.Medium;
                    TraversalState.Medium = TraversalMedium.Air;
                    return;
                }

                var surfaceMatrix = TraversalState.GroundState?.GroundMatrix;
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
            if (TraversalState.Medium == TraversalMedium.Air)
            {
                Fixed64 surfaceLevel = TraversalState.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                // Ensure velocity is downward and scout is within landing range
                if (_velocityDelta.y < Fixed64.Zero && scoutHeight <= surfaceLevel + Fixed64.FromRaw(0x10000L)) // Small threshold
                {
                    // Set state to previous state or assume ground
                    TraversalState.Medium = _previousMedium ?? TraversalMedium.Ground;
                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);

                    if (TraversalState.Medium == TraversalMedium.Ground)
                    {
                        // Update ground normal if needed (assuming ground is flat for now)
                        TraversalState.GroundState ??= new GroundCondition
                        {
                            GroundMatrix = Fixed4x4.Identity, // Assuming a flat ground by default
                        };
                    }
                }

                return;
            }

            if (TraversalState.Medium == TraversalMedium.Water)
            {
                Fixed64 surfaceLevel = TraversalState.SurfaceLevel;
                Fixed64 scoutHeight = Position.y;

                if (scoutHeight > surfaceLevel)
                {
                    if (_velocityDelta.y > Fixed64.Zero)
                    {
                        // If scout is moving upwards, it should no longer be grounded
                        _previousMedium = TraversalState.Medium;
                        TraversalState.Medium = TraversalMedium.Air;
                        return;
                    }

                    Position = new Vector3d(Position.x, surfaceLevel, Position.z);
                }
            }
        }

        protected override void CheckVoxelOccupancy(bool init = false)
        {
            return;
        }
    }
}