using System;
using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Queries;
using Gravitas.Support;
using GridForge.Configuration;
using Trailblazer.Navigation;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasCrowdScenarioFixture3D : GravitasNavigatorScenarioFixture, IDisposable
{
    internal GravitasCrowdScenarioFixture3D(int actorCount, int blockerCount = 8)
        : base(addWall: false, addNavigation: false)
    {
        int width = 1;
        while (width * width < actorCount)
            width++;
        if (!Trailblazer.World.TryAddGrid(new GridConfiguration(new Vector3d(-2, -2, -2),
            new Vector3d(width * 2 + 2, 2, width * 2 + 2)), out _))
            throw new InvalidOperationException("Crowd grid registration failed.");
        Fixed64 span = (Fixed64)(width * 2 + 4);
        Floor = AddBox(new Vector3d((Fixed64)(width - 1), Fixed64.FromFraction(-3, 4), (Fixed64)(width - 1)),
            new Vector3d(span, Fixed64.Half, span), layer: 0);
        for (int i = 0; i < blockerCount; i++)
            AddBox(new Vector3d((Fixed64)((i % width) * 2) + Fixed64.FromFraction(3, 4),
                Fixed64.Zero, (Fixed64)((i / width) * 2)),
                new Vector3d(Fixed64.FromFraction(1, 8), Fixed64.One, Fixed64.Quarter), layer: 0);

        Actors = new GravitasNavigatorActor[actorCount];
        Bodies = new SolidBody[actorCount];
        Navigators = new GravitasNavigator3D[actorCount];
        for (int i = 0; i < actorCount; i++)
        {
            var position = new Vector3d((i % width) * 2, 0, (i / width) * 2);
            Actors[i] = CreateActor(position, carrierIdResolver: _ => 71);
            Bodies[i] = Actors[i].Body;
            Bodies[i].Collider.IgnoredCollisionLayers = PhysicsLayerMask.None;
            Navigators[i] = Actors[i].Navigator;
            Navigators[i].Motor!.Handler.Forces.GravityForce = Fixed64.One;
        }
    }

    internal TrailblazerWorldContext Navigation => Trailblazer;
    internal GravitasWorldContext Physics => Gravitas;
    internal LSCuboidCollider Floor { get; }
    internal GravitasNavigatorActor[] Actors { get; }
    internal SolidBody[] Bodies { get; }
    internal GravitasNavigator3D[] Navigators { get; }

    internal void Configure(PhysicsQueryBudget budget, PhysicsQueryScratch scratch)
    {
        foreach (var navigator in Navigators)
            navigator.ConfigureSupportQueryBudget(budget, scratch);
    }

    internal void Prepare() => base.Prepare(Actors);

    internal void Resolve(bool invalidateView = false)
    {
        Gravitas.LateSimulate();
        if (invalidateView && !Trailblazer.World.TryAddGrid(new GridConfiguration(new Vector3d(-100, -1, -100),
            new Vector3d(-99, 1, -99)), out _))
            throw new InvalidOperationException("Crowd topology change failed.");
        foreach (var navigator in Navigators)
            navigator.CommitFrameMotion();
        Trailblazer.LateSimulate();
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
            if (navigator.Position != Bodies[i].Position3d || navigator.SupportStatus != NavigationSupportStatus.Found
                || !navigator.Motor!.IsOnSolid || navigator.Motor.Handler.Platform.ActivePlatform?.Id != 71)
                throw new InvalidOperationException($"Crowd actor {i} lost its floor or accepted pose.");
        }
    }

    public new void Dispose()
    {
        foreach (var actor in Actors)
            actor.Dispose();
        base.Dispose();
    }
}
