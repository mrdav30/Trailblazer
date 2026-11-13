using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Materials;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasAirborneScenarioFixture : IDisposable
{
    private readonly GridWorld _world;
    private readonly List<LSCuboidCollider> _supports = new();

    internal GravitasAirborneScenarioFixture()
    {
        _world = new GridWorld();
        Trailblazer = TrailblazerWorldContext.Attach(_world);
        Gravitas = GravitasWorldContext.Attach(_world);
        Trailblazer.SetFrameRate(32);
        Gravitas.SetFrameRate(32);

        var configuration = new GridConfiguration(
            new Vector3d(-8, -8, -8),
            new Vector3d(8, 8, 8),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        _world.TryAddGrid(configuration, out _).Should().BeTrue();

        Profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Half),
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Gas,
            TraversalCapability.Jump | TraversalCapability.Fly);

        AddSupport(
            new Vector3d(-Fixed64.One, -Fixed64.Quarter, Fixed64.Zero),
            new Vector3d((Fixed64)2, Fixed64.Half, (Fixed64)3));
        AddSupport(
            new Vector3d(Fixed64.One, Fixed64.FromFraction(-9, 4), Fixed64.Zero),
            new Vector3d((Fixed64)6, Fixed64.Half, (Fixed64)6));
    }

    internal TrailblazerWorldContext Trailblazer { get; }

    internal GravitasWorldContext Gravitas { get; }

    internal NavigationAgentProfile Profile { get; }

    internal Fixed64 UpperSurface => Fixed64.Zero;

    internal Fixed64 LandingSurface => (Fixed64)(-2);

    internal Vector3d WalkingStartRoot =>
        new(-Fixed64.One, UpperSurface + Profile.Shape.RootToFootOffsetY, Fixed64.Zero);

    internal GravitasNavigatorActor CreateWalkingActor() =>
        CreateActor(WalkingStartRoot, TraversalMedium.Solid, UpperSurface);

    internal GravitasNavigatorActor CreateAirborneActor(Vector3d? position = null) =>
        CreateActor(
            position ?? new Vector3d(Fixed64.One, (Fixed64)2, Fixed64.Zero),
            TraversalMedium.Gas,
            LandingSurface);

    internal void Step(params GravitasNavigatorActor[] actors)
    {
        Trailblazer.Simulate();
        Gravitas.Simulate();
        for (int i = 0; i < actors.Length; i++)
        {
            actors[i].Navigator.Simulate();
            actors[i].NavigatorRequestedPosition =
                actors[i].Body.PositionTransform.WorldPosition;
            PreserveAcceptedHeightAcrossUpperLedge(actors[i]);
            actors[i].HostRequestedPosition =
                actors[i].Body.PositionTransform.WorldPosition;
        }

        Gravitas.LateSimulate();
        for (int i = 0; i < actors.Length; i++)
        {
            actors[i].Navigator.CommitFrameMotion();
        }
        Trailblazer.LateSimulate();
    }

    public void Dispose()
    {
        for (int i = 0; i < _supports.Count; i++)
            _supports[i].Deactivate();
        Gravitas.Dispose();
        Trailblazer.Dispose();
        _world.Dispose();
    }

    private GravitasNavigatorActor CreateActor(
        Vector3d position,
        TraversalMedium medium,
        Fixed64 surfaceLevel)
    {
        FixedQuaternion rotation = FixedQuaternion.FromDirection(Vector3d.Right);
        var collider = new LSCylinderCollider
        {
            Radius = Profile.Shape.Radius,
            Size = new Vector3d(
                Profile.Shape.Radius * 2,
                Profile.Shape.Height,
                Profile.Shape.Radius * 2),
            Layer = new PhysicsLayer(2),
            IgnoredCollisionLayers = PhysicsLayerMask.FromLayer(2),
            Material = PhysicsMaterial.Frictionless
        };
        var body = new SolidBody(
            new GravitasFixtureMatterAgent(
                Gravitas,
                new FixedTransform(position, rotation, Vector3d.One)),
            collider)
        {
            GravityScale = Fixed64.Zero,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
            Mass = Fixed64.One
        };
        body.UseManualGrounding();
        body.Initialize(position, rotation, PhysicsMobilityType.Kinematic);

        var navigator = new GravitasNavigator3D(
            Trailblazer,
            body,
            PhysicsLayerMask.FromLayer(0),
            Fixed64.FromFraction(1, 64),
            Fixed64.FromFraction(1, 64),
            Fixed64.FromFraction(1, 64));
        navigator.Setup(Trailblazer, position, Profile, rotation);
        navigator.Initialize(new TrekCondition
        {
            Medium = medium,
            SurfaceLevel = surfaceLevel,
            GroundState = medium == TraversalMedium.Solid
                ? new GroundCondition
                {
                    SurfaceNormal = Vector3d.Up,
                    Platform = new PlatformSnapshot(0, Fixed4x4.Identity, inert: true)
                }
                : null
        });

        return new GravitasNavigatorActor(navigator, body);
    }

    private void AddSupport(Vector3d center, Vector3d size)
    {
        var collider = new LSCuboidCollider
        {
            Size = size,
            Layer = new PhysicsLayer(0),
            IgnoredCollisionLayers = PhysicsLayerMask.None,
            Material = PhysicsMaterial.Frictionless
        };
        collider.InitializeWithNoBody(new GravitasFixtureMatterAgent(
            Gravitas,
            new FixedTransform(center, FixedQuaternion.Identity, Vector3d.One)));
        _supports.Add(collider);
    }

    private void PreserveAcceptedHeightAcrossUpperLedge(GravitasNavigatorActor actor)
    {
        Vector3d accepted = actor.Body.Position3d;
        Vector3d requested = actor.Body.PositionTransform.WorldPosition;
        Fixed64 upperRoot = UpperSurface + Profile.Shape.RootToFootOffsetY;
        if (accepted.Y != upperRoot
            || requested.Y >= accepted.Y
            || requested.X < (Fixed64)(-2) - Profile.Shape.Radius
            || requested.X > Profile.Shape.Radius)
        {
            return;
        }

        requested.Y = accepted.Y;
        actor.Body.PositionTransform.LocalPosition = requested;
    }

}
