using FixedMathSharp;
using Trailblazer.Controllers;

public static class IScoutTestFactory
{
    /// <summary>
    /// Generates a Scout (no platform logic)
    /// </summary>
    public static IScout CreateMockScout(
        Vector3d? startPosition = null,
        Vector3d? startVelocity = null,
        bool grounded = true,
        Fixed64? gravity = null)
    {
        var mock = new MockScout(
            startPosition ?? Vector3d.Zero,
            startVelocity ?? Vector3d.Zero
        );

        if (gravity.HasValue)
            mock.SetGravity(gravity.Value);

        if (grounded)
            mock.SetTraversalState(
                TraversalMedium.Ground,
                Fixed64.Zero,
                new GroundState
                {
                    HitObject = new object(), // Separate from platform
                    GroundMatrix = Fixed4x4.Identity
                }
            );
        else
            mock.SetTraversalState(TraversalMedium.Air);

        return mock;
    }

    /// <summary>
    /// Generates a Falling Scout (for gravity tests)
    /// </summary>
    public static IScout CreateFallingScout(Vector3d? startVelocity = null, Fixed64? surfaceLevel = null)
    {
        MockScout mock = new MockScout(
            Vector3d.Zero, 
            startVelocity ?? new Vector3d(Fixed64.Zero, -Fixed64.One, Fixed64.Zero));
        mock.SetTraversalState(TraversalMedium.Air, surfaceLevel ?? -(Fixed64)999);
        return mock;
    }

    /// <summary>
    /// Generates a Scout + Platform (Separation of Concerns)
    /// </summary>
    public static IScout CreatePlatformScout(
        Vector3d? startPosition = null,
        Fixed4x4? platformMatrix = null,
        Fixed64? gravity = null)
    {
        var mock = new MockScout(startPosition ?? Vector3d.Zero, Vector3d.Zero);

        if (gravity.HasValue)
            mock.SetGravity(gravity.Value);

        mock.SetTraversalState(
            TraversalMedium.Ground,
            Fixed64.Zero,
            new GroundState
            {
                HitObject = new object(), // Separate from platform
                GroundMatrix = platformMatrix ?? Fixed4x4.Identity
            }
        );

        return mock;
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
