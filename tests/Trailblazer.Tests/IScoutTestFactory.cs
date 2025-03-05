namespace Trailblazer.Tests
{
    using FixedMathSharp;
    using global::Trailblazer.Controllers;

    public static class IScoutTestFactory
    {
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

            Fixed4x4 newPlatform = Fixed4x4.Identity;
            newPlatform.SetTransform(startPosition ?? Vector3d.Up, FixedQuaternion.Identity, Vector3d.One);

            if (grounded)
                mock.SetTraversalState(new TraversalData { 
                    Medium = TraversalMedium.Ground, 
                    HitObject = new object(), 
                    GroundMatrix = newPlatform, 
                    GroundNormal = Vector3d.Up, 
                    SurfaceLevel = Fixed64.Zero
                });
            else
                mock.SetTraversalState(new TraversalData
                {
                    Medium = TraversalMedium.Air
                });

            return mock;
        }

        public static IScout CreateFallingScout(Vector3d? startVelocity = null)
        {
            MockScout mock = new MockScout(Vector3d.Zero, startVelocity ?? new Vector3d(Fixed64.Zero, -Fixed64.One, Fixed64.Zero));
            mock.SetTraversalState(new TraversalData
            {
                Medium = TraversalMedium.Air
            });
            return mock;
        }

        public static IScout CreatePlatformScout(
            Vector3d? startPosition = null,
            Vector3d? platformVelocity = null,
            FixedQuaternion? platformRotation = null,
            Fixed64? gravity = null)
        {
            var mock = new MockScout(startPosition ?? Vector3d.Zero, Vector3d.Zero);

            if (gravity.HasValue)
                mock.SetGravity(gravity.Value);

            Fixed4x4 groundMatrix = Fixed4x4.CreateTransform(
                startPosition ?? Vector3d.Zero, 
                platformRotation ?? FixedQuaternion.Identity, 
                Vector3d.One);

            mock.SetTraversalState(new TraversalData
            {
                Medium = TraversalMedium.Ground,
                HitObject = new object(),
                GroundMatrix = Fixed4x4.Identity,
                GroundNormal = Vector3d.Up,
                SurfaceLevel = Fixed64.Zero
            });

            return mock;
        }
    }
}
