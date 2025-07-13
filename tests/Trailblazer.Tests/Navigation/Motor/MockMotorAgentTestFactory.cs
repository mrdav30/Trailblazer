using FixedMathSharp;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation.Motor
{
    public static class MockMotorAgentTestFactory
    {
        /// <summary>
        /// Generates an agent (no platform logic)
        /// </summary>
        public static MockMotorAgent CreateMockAgent(
            Vector3d? startPosition = null,
            Vector3d? startVelocity = null,
            TraversalMedium startingMedium = TraversalMedium.Unknown,
            Fixed64? surfaceLevel = null)
        {
            TrekCondition condition = TrekCondition.CreateEmpty();
            switch (startingMedium)
            {
                case TraversalMedium.Ground:
                    {
                        condition.Medium = TraversalMedium.Ground;
                        condition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
                        condition.GroundState = new GroundCondition
                        {
                            BaseObject = new object(), // Separate from platform
                            GroundMatrix = Fixed4x4.Identity
                        };
                    }
                    break;
                case TraversalMedium.Air:
                    condition.Medium = TraversalMedium.Air;
                    condition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
                    break;
                case TraversalMedium.Water:
                    condition.Medium = TraversalMedium.Water;
                    condition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
                    break;
                default:
                    break;
            }

            MockMotorAgent agent = new(
                startPosition ?? Vector3d.Zero,
                condition,
                null,
                startVelocity ?? Vector3d.Zero
            );

            return agent;
        }

        /// <summary>
        /// Generates a Falling agent (for gravity tests)
        /// </summary>
        public static MockMotorAgent CreateFallingAgent(
            Vector3d? startPosition = null,
            Vector3d? startVelocity = null,
            Fixed64? surfaceLevel = null,
            Fixed4x4? platformMatrix = null)
        {
            TrekCondition condition = new()
            {
                Medium = TraversalMedium.Air,
                SurfaceLevel = surfaceLevel ?? -(Fixed64)999,
                CeilingLevel = Fixed64.MAX_VALUE,
            };

            if (platformMatrix.HasValue)
            {
                condition.GroundState = new GroundCondition
                {
                    MotionTransferState = MotionTransfer.InitTransfer,
                    GroundMatrix = platformMatrix.Value,
                    BaseObject = new object()
                };
            }

            MockMotorAgent agent = new(
                startPosition ?? Vector3d.Zero,
                condition,
                null,
                startVelocity ?? Vector3d.Down
            );

            agent.Motor.Locomotions.Fall.IsFalling = true;

            return agent;
        }

        /// <summary>
        /// Generates an agent + Platform (Separation of Concerns)
        /// </summary>
        public static MockMotorAgent CreatePlatformAgent(
            Vector3d? startPosition = null,
            Fixed4x4? platformMatrix = null,
            Fixed64? surfaceFriction = null,
            MotionTransfer motionTransfer = MotionTransfer.None)
        {
            TrekCondition condition = new()
            {
                Medium = TraversalMedium.Ground,
                CeilingLevel = Fixed64.MAX_VALUE,
                GroundState = new GroundCondition
                {
                    BaseObject = new object(), // Separate from platform
                    GroundMatrix = platformMatrix ?? Fixed4x4.Identity,
                    SurfaceFriction = surfaceFriction ?? Fixed64.Zero,
                    MotionTransferState = motionTransfer
                }
            };

            MockMotorAgent agent = new(
                startPosition ?? Vector3d.Zero,
                condition
            );

            return agent;


        }

        public static MockMotorAgent CreateWaterAgent(Vector3d? startPosition = null, Fixed64? surfaceLevel = null)
        {
            TrekCondition condition = new()
            {
                Medium = TraversalMedium.Water,
                SurfaceLevel = surfaceLevel ?? Fixed64.Zero,
                CeilingLevel = Fixed64.MAX_VALUE
            };

            MockMotorAgent agent = new(
                startPosition ?? Vector3d.Zero,
                condition
            );

            return agent;
        }

        public static MockMotorAgent CreateJumpReadyAgent(Vector3d? startPosition = null)
        {
            TrekCondition condition = new()
            {
                Medium = TraversalMedium.Ground,
                CeilingLevel = Fixed64.MAX_VALUE,
                GroundState = new GroundCondition
                {
                    BaseObject = new object(), // Separate from platform
                    GroundMatrix = Fixed4x4.Identity,
                }
            };

            MockMotorAgent agent = new(
                startPosition ?? Vector3d.Zero,
                condition
            );

            return agent;
        }

        /// <summary>
        /// Generates a Platform with Custom Position, Rotation, Velocity
        /// </summary>
        public static Fixed4x4 CreatePlatform(
            Vector3d? startPosition = null,
            FixedQuaternion? platformRotation = null)
        {
            return Fixed4x4.CreateTransform(
                startPosition ?? Vector3d.Zero,
                platformRotation ?? FixedQuaternion.Identity,
                Vector3d.One
            );
        }
    }
}