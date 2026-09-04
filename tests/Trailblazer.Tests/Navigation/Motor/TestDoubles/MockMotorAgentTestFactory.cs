using FixedMathSharp;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation.Motor;

public static class MockMotorAgentTestFactory
{
    /// <summary>
    /// Generates an agent (no platform logic)
    /// </summary>
    public static MockMotorAgent CreateMockAgent(
        Vector3d? startPosition = null,
        Vector3d? startVelocity = null,
        TraversalMedium startingMedium = TraversalMedium.Unknown,
        Fixed64? surfaceLevel = null,
        LocomotionProfile? profile = null)
    {
        TrekCondition condition = new();
        switch (startingMedium)
        {
            case TraversalMedium.Solid:
                {
                    condition.Medium = TraversalMedium.Solid;
                    condition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
                    condition.GroundState = new GroundCondition
                    {
                        Platform = new(1, Fixed4x4.Identity),
                        SurfaceNormal = Vector3d.Up
                    };
                }
                break;
            case TraversalMedium.Gas:
                condition.Medium = TraversalMedium.Gas;
                condition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
                break;
            case TraversalMedium.Liquid:
                condition.Medium = TraversalMedium.Liquid;
                condition.SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
                break;
            default:
                break;
        }

        MockMotorAgent agent = new(
            startPosition ?? Vector3d.Zero,
            condition,
            null,
            startVelocity ?? Vector3d.Zero,
            profile
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
        Fixed4x4? platformMatrix = null,
        LocomotionProfile? profile = null,
        Vector3d? surfaceNormal = null)
    {
        TrekCondition condition = new()
        {
            Medium = TraversalMedium.Gas,
            SurfaceLevel = surfaceLevel ?? -(Fixed64)999,
            CeilingLevel = Fixed64.MaxValue,
        };

        if (platformMatrix.HasValue)
        {
            condition.GroundState = new GroundCondition
            {
                MotionTransferState = MotionTransfer.InitTransfer,
                Platform = new(1, platformMatrix.Value),
                SurfaceNormal = surfaceNormal ?? Vector3d.Up
            };
        }

        MockMotorAgent agent = new(
            startPosition ?? Vector3d.Zero,
            condition,
            null,
            startVelocity ?? Vector3d.Down,
            profile
        );

        agent.Motor.Handler.Fall.IsFalling = true;

        return agent;
    }

    /// <summary>
    /// Generates an agent + Platform (Separation of Concerns)
    /// </summary>
    public static MockMotorAgent CreatePlatformAgent(
        Vector3d? startPosition = null,
        Fixed4x4? platformMatrix = null,
        Fixed64? surfaceFriction = null,
        MotionTransfer motionTransfer = MotionTransfer.None,
        bool platformInert = false,
        LocomotionProfile? profile = null,
        Vector3d? surfaceNormal = null)
    {
        Fixed4x4 sampledPlatform = platformMatrix ?? Fixed4x4.Identity;
        TrekCondition condition = new()
        {
            Medium = TraversalMedium.Solid,
            CeilingLevel = Fixed64.MaxValue,
            GroundState = new GroundCondition
            {
                Platform = new(1, sampledPlatform, platformInert),
                SurfaceNormal = surfaceNormal ?? Vector3d.Up,
                SurfaceFriction = surfaceFriction ?? Fixed64.Zero,
                MotionTransferState = motionTransfer
            }
        };

        MockMotorAgent agent = new(
            startPosition ?? Vector3d.Zero,
            condition,
            null,
            null,
            profile
        );

        return agent;


    }

    public static MockMotorAgent CreateWaterAgent(
        Vector3d? startPosition = null,
        Fixed64? surfaceLevel = null,
        LocomotionProfile? profile = null)
    {
        TrekCondition condition = new()
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = surfaceLevel ?? Fixed64.Zero,
            CeilingLevel = Fixed64.MaxValue
        };

        MockMotorAgent agent = new(
            startPosition ?? Vector3d.Zero,
            condition,
            null,
            null,
            profile
        );

        return agent;
    }

    public static MockMotorAgent CreateJumpReadyAgent(
        Vector3d? startPosition = null,
        LocomotionProfile? profile = null)
    {
        TrekCondition condition = new()
        {
            Medium = TraversalMedium.Solid,
            CeilingLevel = Fixed64.MaxValue,
            GroundState = new GroundCondition
            {
                Platform = new(1, Fixed4x4.Identity),
                SurfaceNormal = Vector3d.Up
            }
        };

        MockMotorAgent agent = new(
            startPosition ?? Vector3d.Zero,
            condition,
            null,
            null,
            profile
        );

        return agent;
    }

    /// <summary>
    /// Generates a Platform with Custom Position, Rotation, Velocity
    /// </summary>
    public static Fixed4x4 CreatePlatformTransform(
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
