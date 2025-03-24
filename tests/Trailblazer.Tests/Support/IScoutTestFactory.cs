using FixedMathSharp;
using Trailblazer.Controllers;

public static class IScoutTestFactory
{
    /// <summary>
    /// Generates a Scout (no platform logic)
    /// </summary>
    public static Scout CreateMockScout(
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
                    condition.SurfaceCondition = new SurfaceCondition
                    {
                        SurfaceObject = new object(), // Separate from platform
                        SurfaceMatrix = Fixed4x4.Identity,
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

        return new MockScout(
            startPosition ?? Vector3d.Zero,
            startVelocity ?? Vector3d.Zero,
            condition
        );
    }

    /// <summary>
    /// Generates a Falling Scout (for gravity tests)
    /// </summary>
    public static Scout CreateFallingScout(
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
            condition.SurfaceCondition = new SurfaceCondition
            {
                MotionTransferState = MotionTransfer.InitTransfer,
                SurfaceMatrix = platformMatrix.Value,
                SurfaceObject = new object()
            };
        }

        MockScout mock = new MockScout(
            startPosition ?? Vector3d.Zero,
            startVelocity ?? Vector3d.Down,
            condition
        );

        mock.ScoutController.Locomotions.Fall.IsFalling = true;
        return mock;
    }

    /// <summary>
    /// Generates a Scout + Platform (Separation of Concerns)
    /// </summary>
    public static Scout CreatePlatformScout(
        Vector3d? startPosition = null,
        Fixed4x4? platformMatrix = null,
        MotionTransfer motionTransfer = MotionTransfer.None)
    {
        TraversalCondition condition = new TraversalCondition
        {
            Medium = TraversalMedium.Ground,
            CeilingLevel = Fixed64.MAX_VALUE,
            SurfaceCondition = new SurfaceCondition
            {
                SurfaceObject = new object(), // Separate from platform
                SurfaceMatrix = platformMatrix ?? Fixed4x4.Identity,
                MotionTransferState = motionTransfer
            }
        };

        return new MockScout(
            startPosition ?? Vector3d.Zero,
            Vector3d.Zero,
            condition
        );
    }

    public static Scout CreateJumpReadyScout(Vector3d? startPosition = null)
    {
        TraversalCondition condition = new TraversalCondition
        {
            Medium = TraversalMedium.Ground,
            CeilingLevel = Fixed64.MAX_VALUE,
            SurfaceCondition = new SurfaceCondition
            {
                SurfaceObject = new object(), // Separate from platform
                SurfaceMatrix = Fixed4x4.Identity,
            }
        };

        return new MockScout(
            startPosition ?? Vector3d.Zero,
            Vector3d.Zero,
            condition
        );
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
