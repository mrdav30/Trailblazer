using System;
using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Materials;
using Gravitas.Queries;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasCrowdScenarioFixture2D : IDisposable
{
    private const string MapId = "crowded-support";
    private static readonly Fixed64 HostY = (Fixed64)7;
    private readonly GridWorld _world = new();

    internal GravitasCrowdScenarioFixture2D(int actorCount, int blockerCount = 8)
    {
        Navigation = TrailblazerWorldContext.Attach(_world);
        Physics = GravitasWorldContext.Attach(_world);
        GravitasPlanarPhysicsFixture.Configure(Navigation, Physics);
        var configuration = new GridConfiguration(new Vector3d(-2, 0, -2), new Vector3d(actorCount * 2 + 2, 0, 4));
        if (!_world.TryAddGrid(configuration, out _))
            throw new InvalidOperationException("Crowd grid registration failed.");

        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, (Fixed64)3);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var map = new NavigationMapBuilder(MapId, configuration)
            .SetPlanarLayer(NavigationMode2D.SideView, 0, Fixed64.Zero).SetDefaultCell(air);
        for (int x = -2; x <= actorCount * 2 + 2; x++)
        {
            var index = new VoxelIndex(x + 2, 0, 2);
            map.AddCell(index, solid).AddPlanarSupport(index,
                new Vector2d((Fixed64)x - Fixed64.Half, Fixed64.Half),
                new Vector2d((Fixed64)x + Fixed64.Half, Fixed64.Half));
        }
        GravitasPlanarMapFixture.Publish(Navigation, Physics, map.Build());

        Floor = GravitasPlanarPhysicsFixture.AddBox(Physics, new Vector2d(actorCount - 1, 0),
            new Vector2d(actorCount * 2 + 4, 1), HostY);
        for (int i = 0; i < blockerCount; i++)
            GravitasPlanarPhysicsFixture.AddBox(Physics,
                new Vector2d((Fixed64)((i % actorCount) * 2) + Fixed64.FromFraction(3, 4), Fixed64.Half),
                new Vector2d(Fixed64.FromFraction(1, 8), Fixed64.Half), HostY);

        Bodies = new SolidBody2D[actorCount];
        Navigators = new GravitasNavigator2D[actorCount];
        var profile = new NavigationAgentProfile2D(NavigationMode2D.SideView, Fixed64.Half,
            Fixed64.One, Fixed64.Zero, Fixed64.Zero, Fixed64.Half, TraversalMedia.Solid, TraversalCapability.None);
        var settings = new NavigationMotorSettings2D((Fixed64)16, Fixed64.Zero, Fixed64.One, Fixed64.Zero);
        for (int i = 0; i < actorCount; i++)
        {
            var center = new Vector2d((Fixed64)(i * 2), Fixed64.One + Fixed64.Half);
            Bodies[i] = GravitasPlanarPhysicsFixture.CreateKinematic(Physics, center, HostY,
                new LSCapsuleCollider2D(Fixed64.Half, (Fixed64)2) { Material = PhysicsMaterial.Frictionless });
            Navigators[i] = new GravitasNavigator2D(Navigation, Bodies[i], MapId,
                new Guid(i + 1, 0, 0, new byte[8]), profile, settings,
                supportProbeDistance: Fixed64.Quarter,
                carrierIdentity: collider => ReferenceEquals(collider, Floor) ? (71, 1L) : null);
        }
    }

    internal TrailblazerWorldContext Navigation { get; }
    internal GravitasWorldContext Physics { get; }
    internal LSAABBoxCollider2D Floor { get; }
    internal SolidBody2D[] Bodies { get; }
    internal GravitasNavigator2D[] Navigators { get; }

    internal void Configure(PhysicsQueryBudget budget, PhysicsQueryScratch scratch)
    {
        foreach (var navigator in Navigators)
            navigator.ConfigureSupportQueryBudget(budget, scratch);
    }

    internal void Prepare()
    {
        Navigation.Simulate();
        Physics.Simulate();
        foreach (var navigator in Navigators)
            navigator.PrepareFrameMotion();
    }

    internal void Resolve(bool invalidateView = false)
    {
        Physics.LateSimulate();
        if (invalidateView && !_world.TryAddGrid(new GridConfiguration(new Vector3d(-100, 0, -100),
            new Vector3d(-99, 0, -99)), out _))
            throw new InvalidOperationException("Crowd topology change failed.");
        foreach (var navigator in Navigators)
            navigator.CommitFrameMotion();
        Navigation.LateSimulate();
    }

    internal void Step()
    {
        Prepare();
        Resolve();
    }

    internal void Verify()
    {
        for (int i = 0; i < Navigators.Length; i++)
        {
            var navigator = Navigators[i];
            if (navigator.Pose.Center != Bodies[i].Position || navigator.SupportStatus != NavigationSupportStatus.Found
                || !navigator.IsGrounded || navigator.Support?.Carrier?.StableId != 71)
                throw new InvalidOperationException($"Crowd actor {i} lost its floor or accepted pose.");
        }
    }

    public void Dispose()
    {
        foreach (var navigator in Navigators)
            navigator.Dispose();
        foreach (var body in Bodies)
            body.Deactivate();
        Physics.Dispose();
        Navigation.Dispose();
        _world.Dispose();
    }
}
