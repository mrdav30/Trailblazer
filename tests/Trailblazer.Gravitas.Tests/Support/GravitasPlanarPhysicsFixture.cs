using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;

namespace Trailblazer.Gravitas.Tests.Support;

internal static class GravitasPlanarPhysicsFixture
{
    internal static void Configure(TrailblazerWorldContext navigation, GravitasWorldContext physics)
    {
        navigation.SetFrameRate(16);
        physics.SetFrameRate(16);
        physics.Settings.RuntimeMode = PhysicsRuntimeMode.TwoD;
    }

    internal static GravitasFixtureMatterAgent CreateAgent(GravitasWorldContext physics, Vector2d center, Fixed64 hostY) =>
        new(physics, new FixedTransform(center.ToVector3d(hostY), FixedQuaternion.Identity, Vector3d.One));

    internal static LSAABBoxCollider2D AddBox(GravitasWorldContext physics, Vector2d center, Vector2d size, Fixed64 hostY)
    {
        var collider = new LSAABBoxCollider2D(size);
        collider.InitializeWithNoBody(CreateAgent(physics, center, hostY));
        return collider;
    }

    internal static SolidBody2D CreateKinematic(GravitasWorldContext physics, Vector2d center, Fixed64 hostY, LSCollider2D collider)
    {
        var body = new SolidBody2D(CreateAgent(physics, center, hostY), collider)
        {
            Mass = Fixed64.One,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
        };
        body.Initialize(center, motionType: PhysicsMobilityType.Kinematic);
        return body;
    }
}
