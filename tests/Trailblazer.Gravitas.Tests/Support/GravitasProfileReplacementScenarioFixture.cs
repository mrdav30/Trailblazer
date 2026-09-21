using FixedMathSharp;
using Gravitas.Colliders;
using Gravitas.Materials;
using Gravitas.Support;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasProfileReplacementScenarioFixture
    : GravitasNavigatorScenarioFixture
{
    internal GravitasProfileReplacementScenarioFixture(
        bool addSupport = false)
        : base(
            addWall: false,
            addSupport: addSupport)
    {
    }

    protected override LSCollider CreateActorCollider(
        NavigationAgentProfile profile)
    {
        KinematicBodyShape shape = profile.Shape;
        return new LSCapsuleCollider(
            ColliderShapeDefinition.Capsule(shape.Radius, shape.Height))
        {
            LocalOffset = Vector3d.Up
                * (shape.Height * Fixed64.Half - shape.RootToFootOffsetY)
        };
    }

    internal LSCuboidCollider AddCeiling(Vector3d center, Vector3d size)
    {
        var ceiling = new LSCuboidCollider
        {
            Size = size,
            Layer = new PhysicsLayer(1),
            IgnoredCollisionLayers = PhysicsLayerMask.None,
            Material = PhysicsMaterial.Frictionless
        };
        ceiling.InitializeWithNoBody(new GravitasFixtureMatterAgent(
            Gravitas,
            new FixedTransform(
                center,
                FixedQuaternion.Identity,
                Vector3d.One)));
        AddFixtureCollider(ceiling);
        return ceiling;
    }
}
