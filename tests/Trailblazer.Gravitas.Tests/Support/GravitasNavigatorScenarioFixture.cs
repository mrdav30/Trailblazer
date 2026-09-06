using System;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.CollisionHandling;
using Gravitas.Materials;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasNavigatorScenarioFixture : IDisposable
{
    private const string MapId = "gravitas-wall";

    private readonly GridWorld _world;
    private readonly LSCuboidCollider? _wall;
    private readonly LSCuboidCollider? _support;

    internal GravitasNavigatorScenarioFixture(
        bool addWall = true,
        Fixed64? wallCenterX = null,
        bool addSupport = false,
        Fixed64? supportCenterY = null)
    {
        _world = new GridWorld();
        Trailblazer = TrailblazerWorldContext.Attach(_world);
        Gravitas = GravitasWorldContext.Attach(_world);
        Trailblazer.SetFrameRate(32);
        Gravitas.SetFrameRate(32);

        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(12, 12, 12),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        _world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();

        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationMap map = new NavigationMapBuilder(MapId, binding)
            .AddCell(new VoxelIndex(4, 4, 4), cell)
            .AddCell(new VoxelIndex(5, 4, 4), cell)
            .AddCell(new VoxelIndex(6, 4, 4), cell)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("gravitas-proof", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            Trailblazer.FrameCount + 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            policy,
            2,
            Trailblazer.FrameCount + 1);
        Trailblazer.Pathing.Admit(mapOperation).Should().BeTrue();
        Trailblazer.Pathing.Admit(policyOperation).Should().BeTrue();
        Trailblazer.Simulate();
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        StartFoot = Anchor(binding, new VoxelIndex(4, 4, 4));
        EndFoot = Anchor(binding, new VoxelIndex(6, 4, 4));
        StartRoot = StartFoot + Vector3d.Up * Fixed64.Half;
        Profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Half),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);

        if (addWall)
        {
            var wallTransform = new FixedTransform(
                new Vector3d(wallCenterX ?? (Fixed64)2, Fixed64.One, Fixed64.Half),
                FixedQuaternion.Identity,
                Vector3d.One);
            _wall = new LSCuboidCollider
            {
                Size = new Vector3d(Fixed64.Quarter, (Fixed64)2, (Fixed64)2),
                Layer = new PhysicsLayer(1),
                IgnoredCollisionLayers = PhysicsLayerMask.None,
                Material = PhysicsMaterial.Frictionless
            };
            _wall.InitializeWithNoBody(new HostMatterAgent(Gravitas, wallTransform));
        }

        if (addSupport)
        {
            SupportTransform = new FixedTransform(
                new Vector3d(
                    Fixed64.One,
                    supportCenterY ?? Fixed64.FromFraction(-3, 4),
                    Fixed64.Zero),
                FixedQuaternion.Identity,
                Vector3d.One);
            _support = new LSCuboidCollider
            {
                Size = new Vector3d((Fixed64)8, Fixed64.Half, (Fixed64)8),
                Layer = new PhysicsLayer(0),
                IgnoredCollisionLayers = PhysicsLayerMask.None,
                Material = new PhysicsMaterial(
                    Fixed64.Half,
                    Fixed64.Quarter,
                    Fixed64.Zero)
            };
            _support.InitializeWithNoBody(new HostMatterAgent(Gravitas, SupportTransform));
        }
    }

    internal TrailblazerWorldContext Trailblazer { get; }

    internal GravitasWorldContext Gravitas { get; }

    internal NavigationAgentProfile Profile { get; }

    internal Vector3d StartFoot { get; }

    internal Vector3d EndFoot { get; }

    internal Vector3d StartRoot { get; }

    internal Vector3d EndRoot => EndFoot + Vector3d.Up * Fixed64.Half;

    internal FixedTransform? SupportTransform { get; }

    internal Actor CreateActor(
        Vector3d? position = null,
        FixedQuaternion? rotation = null,
        Func<LSCollider, int>? carrierIdResolver = null)
    {
        Vector3d resolvedPosition = position ?? StartRoot;
        FixedQuaternion resolvedRotation = rotation ?? FixedQuaternion.FromDirection(Vector3d.Right);
        var transform = new FixedTransform(resolvedPosition, resolvedRotation, Vector3d.One);
        var collider = new LSSphereCollider
        {
            Radius = Fixed64.Half,
            Layer = new PhysicsLayer(2),
            IgnoredCollisionLayers = PhysicsLayerMask.FromLayer(0),
            Material = PhysicsMaterial.Frictionless
        };
        var body = new SolidBody(new HostMatterAgent(Gravitas, transform), collider)
        {
            GravityScale = Fixed64.Zero,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
            Mass = Fixed64.One
        };
        body.UseManualGrounding();
        body.Initialize(resolvedPosition, resolvedRotation, BodyMotionType.Kinematic);

        var navigator = new GravitasNavigator3D(
            Trailblazer,
            body,
            PhysicsLayerMask.FromLayer(0),
            Fixed64.Quarter,
            Fixed64.FromFraction(1, 8),
            Fixed64.Quarter,
            carrierIdResolver);
        ActivateNavigator(navigator, resolvedPosition, resolvedRotation);
        navigator.Motor!.Handler.Forces.GravityForce = Fixed64.Zero;
        return new Actor(navigator, body);
    }

    internal PathQuery CreateQuery(Vector3d? startFoot = null, Vector3d? endFoot = null) => new(
        new NavigationEndpoint(startFoot ?? StartFoot, MapId),
        new NavigationEndpoint(endFoot ?? EndFoot, MapId),
        Profile,
        new NavigationAreaPolicyKey("gravitas-proof", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0),
        allowTransitions: false);

    internal void Step(params Actor[] actors)
    {
        Prepare(actors);
        Resolve(actors);
    }

    internal void Prepare(params Actor[] actors)
    {
        Trailblazer.Simulate();
        Gravitas.Simulate();
        for (int i = 0; i < actors.Length; i++)
            actors[i].Navigator.Simulate();
    }

    internal void Resolve(params Actor[] actors)
    {
        Gravitas.LateSimulate();
        for (int i = 0; i < actors.Length; i++)
            actors[i].Navigator.CommitFrameMotion();
        Trailblazer.LateSimulate();
    }

    public void Dispose()
    {
        _wall?.Deactivate();
        _support?.Deactivate();
        Gravitas.Dispose();
        Trailblazer.Dispose();
        _world.Dispose();
    }

    private static Vector3d Anchor(NormalizedGridConfiguration binding, VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    internal void DeactivateSupport() => _support?.Deactivate();

    internal void Reactivate(Actor actor, TrekCondition condition)
    {
        actor.Body.PositionTransform.LocalPosition = StartRoot;
        actor.Body.RotationTransform.LocalRotation = FixedQuaternion.FromDirection(Vector3d.Right);
        ActivateNavigator(
            actor.Navigator,
            StartRoot,
            FixedQuaternion.FromDirection(Vector3d.Right),
            condition);
        actor.Navigator.Motor!.Handler.Forces.GravityForce = Fixed64.Zero;
    }

    private void ActivateNavigator(
        GravitasNavigator3D navigator,
        Vector3d position,
        FixedQuaternion rotation,
        TrekCondition? condition = null)
    {
        navigator.Setup(Trailblazer, position, Profile, rotation);
        navigator.Initialize(condition ?? new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = position.Y - Profile.Shape.RootToFootOffsetY,
            GroundState = new GroundCondition
            {
                SurfaceNormal = Vector3d.Up,
                Platform = new PlatformSnapshot(0, Fixed4x4.Identity, inert: true)
            }
        });
    }

    internal sealed class Actor : IDisposable
    {
        internal Actor(GravitasNavigator3D navigator, SolidBody body)
        {
            Navigator = navigator;
            Body = body;
        }

        internal GravitasNavigator3D Navigator { get; }

        internal SolidBody Body { get; }

        public void Dispose()
        {
            Navigator.Reset();
            Body.Deactivate();
        }
    }

    private sealed class HostMatterAgent : IMatterAgent
    {
        internal HostMatterAgent(GravitasWorldContext context, FixedTransform transform)
        {
            Context = context;
            Transform = transform;
        }

        public GravitasWorldContext Context { get; }

        public FixedTransform Transform { get; }

        public bool IsParent => true;

        public bool IsInteracting => false;
    }
}
