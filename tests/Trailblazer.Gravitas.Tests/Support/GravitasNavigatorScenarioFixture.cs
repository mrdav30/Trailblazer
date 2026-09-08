using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.CollisionHandling;
using Gravitas.Materials;
using Gravitas.Queries;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal class GravitasNavigatorScenarioFixture : IDisposable
{
    private const string MapId = "gravitas-wall";

    private readonly GridWorld _world;
    private readonly List<LSCuboidCollider> _fixtureColliders = new();
    private readonly LSCuboidCollider? _wall;
    private readonly LSCuboidCollider? _support;
    private string _startMapId = MapId;
    private string _endMapId = MapId;
    private PhysicsLayerMask _supportLayers = PhysicsLayerMask.FromLayer(0);
    private PhysicsLayerMask _actorIgnoredCollisionLayers = PhysicsLayerMask.FromLayer(0);

    internal GravitasNavigatorScenarioFixture(
        bool addWall = true,
        Fixed64? wallCenterX = null,
        bool addSupport = false,
        Fixed64? supportCenterY = null,
        bool addNavigation = true)
    {
        _world = new GridWorld();
        Trailblazer = TrailblazerWorldContext.Attach(_world);
        Gravitas = GravitasWorldContext.Attach(_world);
        Trailblazer.SetFrameRate(32);
        Gravitas.SetFrameRate(32);

        Profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Half),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        StartFoot = Vector3d.Zero;
        EndFoot = Vector3d.Zero;
        StartRoot = Vector3d.Up * Fixed64.Half;

        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("gravitas-proof", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            policy,
            2,
            Trailblazer.FrameCount + 1);
        Trailblazer.Pathing.Admit(policyOperation).Should().BeTrue();

        NavigationMapCommitOperation? mapOperation = null;
        if (addNavigation)
        {
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
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, 1),
                OverlayReplacementPolicy.Clear,
                1,
                Trailblazer.FrameCount + 1);
            mapOperation = operation;
            Trailblazer.Pathing.Admit(operation).Should().BeTrue();

            StartFoot = Anchor(binding, new VoxelIndex(4, 4, 4));
            EndFoot = Anchor(binding, new VoxelIndex(6, 4, 4));
            StartRoot = StartFoot + Vector3d.Up * Fixed64.Half;
        }

        Trailblazer.Simulate();
        mapOperation?.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

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
            _wall.InitializeWithNoBody(new GravitasFixtureMatterAgent(Gravitas, wallTransform));
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
            _support.InitializeWithNoBody(new GravitasFixtureMatterAgent(Gravitas, SupportTransform));
        }
    }

    internal TrailblazerWorldContext Trailblazer { get; }

    internal GravitasWorldContext Gravitas { get; }

    internal NavigationAgentProfile Profile { get; private set; }

    internal Vector3d StartFoot { get; private set; }

    internal Vector3d EndFoot { get; private set; }

    internal Vector3d StartRoot { get; private set; }

    internal Vector3d EndRoot =>
        EndFoot + Vector3d.Up * Profile.Shape.RootToFootOffsetY;

    internal FixedTransform? SupportTransform { get; }

    internal GravitasNavigatorActor CreateActor(
        Vector3d? position = null,
        FixedQuaternion? rotation = null,
        Func<LSCollider, int>? carrierIdResolver = null)
    {
        Vector3d resolvedPosition = position ?? StartRoot;
        FixedQuaternion resolvedRotation = rotation ?? FixedQuaternion.FromDirection(Vector3d.Right);
        var transform = new FixedTransform(resolvedPosition, resolvedRotation, Vector3d.One);
        LSCollider collider = CreateActorCollider();
        collider.Layer = new PhysicsLayer(2);
        collider.IgnoredCollisionLayers = _actorIgnoredCollisionLayers;
        collider.Material = PhysicsMaterial.Frictionless;
        var body = new SolidBody(new GravitasFixtureMatterAgent(Gravitas, transform), collider)
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
            _supportLayers,
            Fixed64.Quarter,
            Fixed64.FromFraction(1, 8),
            Fixed64.Quarter,
            carrierIdResolver);
        ActivateNavigator(navigator, resolvedPosition, resolvedRotation);
        navigator.Motor!.Handler.Forces.GravityForce = Fixed64.Zero;
        return new GravitasNavigatorActor(navigator, body);
    }

    internal PathQuery CreateQuery(Vector3d? startFoot = null, Vector3d? endFoot = null) =>
        CreateQuery(PathAlgorithm.AStar, startFoot, endFoot);

    internal PathQuery CreateQuery(
        PathAlgorithm algorithm,
        Vector3d? startFoot = null,
        Vector3d? endFoot = null) => new(
        new NavigationEndpoint(startFoot ?? StartFoot, _startMapId),
        new NavigationEndpoint(endFoot ?? EndFoot, _endMapId),
        Profile,
        new NavigationAreaPolicyKey("gravitas-proof", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        algorithm,
        new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0),
        allowTransitions: false);

    internal PathQuery CreateQueryForProfile(
        PathAlgorithm algorithm,
        NavigationAgentProfile profile,
        bool reverse = false) => new(
        new NavigationEndpoint(
            reverse ? EndFoot : StartFoot,
            reverse ? _endMapId : _startMapId),
        new NavigationEndpoint(
            reverse ? StartFoot : EndFoot,
            reverse ? _startMapId : _endMapId),
        profile,
        new NavigationAreaPolicyKey("gravitas-proof", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        algorithm,
        new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0),
        allowTransitions: false);

    internal static GravitasNavigatorScenarioFixture CreateStairs()
    {
        return new GravitasStairScenarioFixture();
    }

    internal static GravitasNavigatorScenarioFixture CreateStackedFloors()
    {
        return new GravitasStackedFloorScenarioFixture();
    }

    internal static GravitasNavigatorScenarioFixture CreateOffAxisCorridor(
        Fixed64 radius,
        Fixed64? arrivalRadius = null)
    {
        return new GravitasOffAxisCorridorScenarioFixture(
            radius,
            arrivalRadius ?? Fixed64.FromFraction(1, 16));
    }

    internal void Step(params GravitasNavigatorActor[] actors)
    {
        Prepare(actors);
        Resolve(actors);
    }

    internal void Prepare(params GravitasNavigatorActor[] actors)
    {
        Trailblazer.Simulate();
        Gravitas.Simulate();
        for (int i = 0; i < actors.Length; i++)
        {
            actors[i].Navigator.Simulate();
            actors[i].NavigatorRequestedPosition =
                actors[i].Body.PositionTransform.WorldPosition;
            PrepareHostMotion(actors[i]);
            actors[i].HostRequestedPosition =
                actors[i].Body.PositionTransform.WorldPosition;
        }
    }

    internal void Resolve(params GravitasNavigatorActor[] actors)
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
        for (int i = 0; i < _fixtureColliders.Count; i++)
            _fixtureColliders[i].Deactivate();
        Gravitas.Dispose();
        Trailblazer.Dispose();
        _world.Dispose();
    }

    protected static Vector3d Anchor(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    protected GridWorld World => _world;

    protected void ConfigureScenario(
        NavigationAgentProfile profile,
        Vector3d startFoot,
        Vector3d endFoot,
        string startMapId,
        string endMapId,
        PhysicsLayerMask supportLayers,
        PhysicsLayerMask actorIgnoredCollisionLayers)
    {
        Profile = profile;
        StartFoot = startFoot;
        EndFoot = endFoot;
        StartRoot = startFoot + Vector3d.Up * profile.Shape.RootToFootOffsetY;
        _startMapId = startMapId;
        _endMapId = endMapId;
        _supportLayers = supportLayers;
        _actorIgnoredCollisionLayers = actorIgnoredCollisionLayers;
    }

    protected void AddFixtureCollider(LSCuboidCollider collider) =>
        _fixtureColliders.Add(collider);

    protected virtual LSCollider CreateActorCollider() => new LSSphereCollider
    {
        Radius = Profile.Shape.Radius
    };

    protected virtual void PrepareHostMotion(GravitasNavigatorActor actor)
    {
    }

    internal void DeactivateSupport() => _support?.Deactivate();

    internal void Reactivate(GravitasNavigatorActor actor, TrekCondition condition)
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

    protected void AddSupportPlatform(Vector3d center)
    {
        var collider = new LSCuboidCollider
        {
            Size = new Vector3d((Fixed64)4, Fixed64.Half, (Fixed64)4),
            Layer = new PhysicsLayer(1),
            IgnoredCollisionLayers = PhysicsLayerMask.None,
            Material = PhysicsMaterial.Frictionless
        };
        collider.InitializeWithNoBody(new GravitasFixtureMatterAgent(
            Gravitas,
            new FixedTransform(center, FixedQuaternion.Identity, Vector3d.One)));
        _fixtureColliders.Add(collider);
    }

    protected void SimulateUntilApplied(NavigationMapCommitOperation[] operations)
    {
        for (int frame = 0; frame < 1_024; frame++)
        {
            bool pending = false;
            for (int i = 0; i < operations.Length; i++)
                pending |= operations[i].Receipt.Status == NavigationOperationStatus.Pending;
            if (!pending)
                break;
            Trailblazer.Simulate();
        }
        var outcomes = new string[operations.Length];
        for (int i = 0; i < operations.Length; i++)
        {
            outcomes[i] = $"{i}:{operations[i].Receipt.Status}/{operations[i].Receipt.Rejection}";
        }
        string outcomeSummary = string.Join(", ", outcomes);
        for (int i = 0; i < operations.Length; i++)
        {
            operations[i].Receipt.Status.Should().Be(
                NavigationOperationStatus.Applied,
                "operation {0} should publish; all outcomes were {1}",
                i,
                outcomeSummary);
        }
    }

    protected void SimulateUntilSingleComponent(int expectedMapCount)
    {
        for (int frame = 0; frame < 1_024; frame++)
        {
            NavigationGraphDiagnosticsSnapshot diagnostics =
                Trailblazer.Pathing.GetNavigationGraphDiagnostics();
            if (diagnostics.Maps.Count == expectedMapCount)
            {
                int componentId = diagnostics.Maps[0].ComponentId;
                bool connected = true;
                for (int i = 1; i < diagnostics.Maps.Count; i++)
                    connected &= diagnostics.Maps[i].ComponentId == componentId;
                if (connected)
                    return;
            }
            Trailblazer.Simulate();
        }

        NavigationGraphDiagnosticsSnapshot finalDiagnostics =
            Trailblazer.Pathing.GetNavigationGraphDiagnostics();
        var components = new string[finalDiagnostics.Maps.Count];
        int expectedComponent = finalDiagnostics.Maps.Count == 0
            ? 0
            : finalDiagnostics.Maps[0].ComponentId;
        bool allConnected = finalDiagnostics.Maps.Count == expectedMapCount;
        for (int i = 0; i < finalDiagnostics.Maps.Count; i++)
        {
            NavigationGraphMapDiagnostic map = finalDiagnostics.Maps[i];
            components[i] = $"{map.MapId}:{map.ComponentId}";
            allConnected &= map.ComponentId == expectedComponent;
        }
        allConnected.Should().BeTrue(
            "the authored stair maps must finish automatic-seam composition; observed {0}",
            string.Join(", ", components));
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

}
