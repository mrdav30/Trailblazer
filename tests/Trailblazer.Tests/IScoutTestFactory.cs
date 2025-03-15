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
        Fixed64? gravity = null,
        TraversalMedium startingMedium = TraversalMedium.Unknown)
    {
        var mock = new MockScout(
            startPosition ?? Vector3d.Zero,
            startVelocity ?? Vector3d.Zero
        );

        if (gravity.HasValue)
            mock.ScoutController.Gravity = gravity.Value;

        switch (startingMedium)
        {
            case TraversalMedium.Ground:
                {
                    mock.SetTraversalState(
                        TraversalMedium.Ground,
                        Fixed64.Zero,
                        new GroundState
                        {
                            HitObject = new object(), // Separate from platform
                            GroundMatrix = Fixed4x4.Identity,
                        }
                    );
                }
                break;
            case TraversalMedium.Air:
                mock.SetTraversalState(TraversalMedium.Air);
                break;
            case TraversalMedium.Water:
                mock.SetTraversalState(TraversalMedium.Water);
                break;
            case TraversalMedium.Unknown:
                break;
            default:
                break;
        }

        return mock;
    }

    /// <summary>
    /// Generates a Falling Scout (for gravity tests)
    /// </summary>
    public static Scout CreateFallingScout(Vector3d? startVelocity = null, Fixed64? surfaceLevel = null)
    {
        MockScout mock = new MockScout(
            Vector3d.Zero, 
            startVelocity ?? Vector3d.Down);
        mock.SetTraversalState(TraversalMedium.Air, surfaceLevel ?? -(Fixed64)999);
        return mock;
    }

    /// <summary>
    /// Generates a Scout + Platform (Separation of Concerns)
    /// </summary>
    public static Scout CreatePlatformScout(
        Vector3d? startPosition = null,
        Fixed4x4? platformMatrix = null,
        Fixed64? gravity = null,
        MovementTransferState movementTransfer = MovementTransferState.PermaTransfer)
    {
        var mock = new MockScout(startPosition ?? Vector3d.Zero, Vector3d.Zero);

        if (gravity.HasValue)
            mock.ScoutController.Gravity = gravity.Value;

        mock.SetTraversalState(
            TraversalMedium.Ground,
            Fixed64.Zero,
            new GroundState
            {
                HitObject = new object(), // Separate from platform
                GroundMatrix = platformMatrix ?? Fixed4x4.Identity,
                MovementTransfer = movementTransfer
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
