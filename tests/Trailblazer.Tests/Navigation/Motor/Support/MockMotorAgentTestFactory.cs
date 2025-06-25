using FixedMathSharp;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation.Motor
{
    public static class MockMotorAgentTestFactory
    {
        /// <summary>
        /// Generates a Scout (no platform logic)
        /// </summary>
        public static MockMotorAgent CreateMockAgent(
            Vector3d? startPosition = null,
            Vector3d? startVelocity = null,
            TraversalMedium startingMedium = TraversalMedium.Unknown,
            Fixed64? surfaceLevel = null)
        {
            TraversalCondition condition = TraversalCondition.Empty;
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

            MockMotorAgent agent = new MockMotorAgent();
            agent.Setup(
                startPosition ?? Vector3d.Zero,
                null,
                startVelocity ?? Vector3d.Zero
            );
            agent.Initialize(condition);

            return agent;
        }

        /// <summary>
        /// Generates a Falling Scout (for gravity tests)
        /// </summary>
        public static MockMotorAgent CreateFallingAgent(
            Vector3d? startPosition = null,
            Vector3d? startVelocity = null,
            Fixed64? surfaceLevel = null,
            Fixed4x4? platformMatrix = null)
        {
            TraversalCondition condition = new TraversalCondition
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

            MockMotorAgent agent = new MockMotorAgent();
            agent.Setup(
                startPosition ?? Vector3d.Zero,
                null,
                startVelocity ?? Vector3d.Down
            );
            agent.Initialize(condition);

            agent.Motor.Locomotions.Fall.IsFalling = true;

            return agent;
        }

        /// <summary>
        /// Generates a Scout + Platform (Separation of Concerns)
        /// </summary>
        public static MockMotorAgent CreatePlatformAgent(
            Vector3d? startPosition = null,
            Fixed4x4? platformMatrix = null,
            Fixed64? surfaceFriction = null,
            MotionTransfer motionTransfer = MotionTransfer.None)
        {
            TraversalCondition condition = new TraversalCondition
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

            MockMotorAgent agent = new MockMotorAgent();
            agent.Setup(startPosition ?? Vector3d.Zero);
            agent.Initialize(condition);

            return agent;


        }

        public static MockMotorAgent CreateWaterAgent(Vector3d? startPosition = null, Fixed64? surfaceLevel = null)
        {
            TraversalCondition condition = new TraversalCondition
            {
                Medium = TraversalMedium.Water,
                SurfaceLevel = surfaceLevel ?? Fixed64.Zero,
                CeilingLevel = Fixed64.MAX_VALUE
            };

            MockMotorAgent agent = new MockMotorAgent();
            agent.Setup(startPosition ?? Vector3d.Zero);
            agent.Initialize(condition);

            return agent;
        }

        public static MockMotorAgent CreateJumpReadyAgent(Vector3d? startPosition = null)
        {
            TraversalCondition condition = new TraversalCondition
            {
                Medium = TraversalMedium.Ground,
                CeilingLevel = Fixed64.MAX_VALUE,
                GroundState = new GroundCondition
                {
                    BaseObject = new object(), // Separate from platform
                    GroundMatrix = Fixed4x4.Identity,
                }
            };

            MockMotorAgent agent = new MockMotorAgent();
            agent.Setup(startPosition ?? Vector3d.Zero);
            agent.Initialize(condition);

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