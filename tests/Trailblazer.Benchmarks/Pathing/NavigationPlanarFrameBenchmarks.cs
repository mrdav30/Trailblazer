using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using Gravitas;
using Gravitas.Colliders;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures complete shared fixed steps with a native Gravitas 2D body.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Guidance", "Frame", "Planar")]
public class NavigationPlanarFrameBenchmarks
{
    private const string MapId = "planar-frame";
    private static readonly NavigationWorkBudget Budget = new(8192, 128, 128, 512, 512, 128, 128, 128, 1024, 4096, 0);
    private static readonly NavigationAreaPolicyKey Policy = new("planar-frame", 1);
    private static readonly Fixed64 HostY = (Fixed64)7;
    private static readonly NavigationCellAddress Lower = new(MapId, new VoxelIndex(5, 1, 1));
    private static readonly NavigationCellAddress Upper = new(MapId, new VoxelIndex(6, 1, 2));
    private GridWorld _world;
    private TrailblazerWorldContext _navigation;
    private GravitasWorldContext _physics;
    private GravitasNavigator2D _navigator;
    private Vector2d _start;
    private bool _blocked;
    private bool _sideView;
    private bool _ladder;

    [Params("TopDownOpen", "TopDownBlocked", "SideViewSupport", "SideViewLadder")]
    public string Scenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _blocked = Scenario == "TopDownBlocked";
        _ladder = Scenario == "SideViewLadder";
        _sideView = _ladder || Scenario == "SideViewSupport";
        _world = new GridWorld();
        _navigation = TrailblazerWorldContext.Attach(_world);
        _physics = GravitasWorldContext.Attach(_world);
        _navigation.SetFrameRate(16);
        _physics.SetFrameRate(16);
        _physics.Settings.RuntimeMode = PhysicsRuntimeMode.TwoD;
        var grid = new GridConfiguration(new Vector3d(-4, 0, -1), new Vector3d(4, 1, 2));
        Check(_world.TryAddGrid(grid, out _), "Grid registration failed.");
        var policy = new NavigationAreaPolicy(Policy, new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        Check(_navigation.Pathing.Admit(new NavigationAreaPolicyCommitOperation(policy, 1, 1)), "Policy admission failed.");
        var mode = _sideView ? NavigationMode2D.SideView : NavigationMode2D.TopDown;
        var solid = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var map = new NavigationMapBuilder(MapId, grid).SetPlanarLayer(mode, 1, Fixed64.One)
            .SetDefaultCell(_sideView ? air : solid);
        Fixed64 axisLength = _sideView ? Fixed64.Quarter : Fixed64.Zero;
        _start = new Vector2d(_ladder ? Fixed64.One : Fixed64.Zero, _sideView ? -Fixed64.Quarter + axisLength * Fixed64.Half : Fixed64.Zero);
        if (_ladder)
        {
            AddFloor(1, Lower.Index, -Fixed64.Half);
            AddFloor(2, Upper.Index, Fixed64.Half + Fixed64.Quarter);
            map.AddTransition(new TraversalTransitionDefinition("up", TraversalTransitionType.Climb, Lower.Index,
                TraversalMedium.Solid, Upper, TraversalMedium.Solid, TraversalCapability.Climb, Fixed64.One));
            map.AddTransition(new TraversalTransitionDefinition("down", TraversalTransitionType.Climb, Upper.Index,
                TraversalMedium.Solid, Lower, TraversalMedium.Solid, TraversalCapability.Climb, Fixed64.One));
            AddBox(new Vector2d(Fixed64.Half, -Fixed64.One), new Vector2d(2, 1));
            AddBox(new Vector2d((Fixed64)2 + Fixed64.Half, Fixed64.Quarter), new Vector2d(2, 1));
        }
        else if (_sideView)
        {
            for (int x = -1; x <= 1; x++)
                AddFloor(x, new VoxelIndex(x + 4, 1, 1), -Fixed64.Half);
            AddBox(new Vector2d(0, -1), new Vector2d(3, 1));
        }
        else if (_blocked)
        {
            AddBox(new Vector2d(Fixed64.Half + Fixed64.Quarter, Fixed64.Zero), new Vector2d(1, 3));
        }
        var publication = new NavigationMapCommitOperation(new PreparedNavigationMap(map.Build(), 1),
            OverlayReplacementPolicy.Clear, 2, 1);
        Check(_navigation.Pathing.Admit(publication), "Map admission failed.");
        for (int i = 0; i < 32; i++)
        {
            AdvanceClocks();
            _physics.LateSimulate();
            _navigation.LateSimulate();
        }
        Check(publication.Receipt.Status == NavigationOperationStatus.Applied
            && _navigation.Pathing.GetNavigationGraphDiagnostics().Maps.Single().IsMaterialized, "Map publication failed.");
        var profile = new NavigationAgentProfile2D(mode, Fixed64.Quarter, axisLength, Fixed64.Zero,
            Fixed64.Zero, _sideView ? Fixed64.Half : Fixed64.Zero, TraversalMedia.Solid,
            _ladder ? TraversalCapability.Climb : TraversalCapability.None);
        LSCollider2D collider = _sideView
            ? new LSCapsuleCollider2D(Fixed64.Quarter, Fixed64.Half + axisLength)
            : new LSCircleCollider2D(Fixed64.Quarter);
        var body = new SolidBody2D(AgentAt(_start), collider)
        {
            Mass = Fixed64.One,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
        };
        body.Initialize(_start, motionType: PhysicsMobilityType.Kinematic);
        _navigator = new GravitasNavigator2D(_navigation, body, MapId,
            new Guid("3833213d-8e04-458c-bc30-7b7ba60d045b"), profile,
            new NavigationMotorSettings2D((Fixed64)4, Fixed64.Zero, _sideView ? (Fixed64)16 : Fixed64.Zero, Fixed64.Zero),
            supportProbeDistance: Fixed64.MinIncrement);
        if (_sideView)
            _navigator.RefreshSupport();
        RunPair(verify: true);
        RunPair(verify: true);

        void AddFloor(int x, VoxelIndex index, Fixed64 height) =>
            map.AddCell(index, solid).AddPlanarSupport(index, new Vector2d((Fixed64)x - Fixed64.Half, height),
                new Vector2d((Fixed64)x + Fixed64.Half, height));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _navigator?.Dispose();
        _physics?.Dispose();
        _navigation?.Dispose();
        _world?.Dispose();
    }

    // A pair returns the actor to its starting center without resetting either world.
    [Benchmark]
    public void SimulatePair() => RunPair(verify: false);

    private void RunPair(bool verify)
    {
        if (_ladder)
        {
            Traverse(Lower, Upper, ascending: true, verify);
            Traverse(Upper, Lower, ascending: false, verify);
            return;
        }

        Move(Vector2d.Right, _blocked ? _start : _start + Vector2d.Right * Fixed64.Quarter, verify);
        Move(_blocked ? Vector2d.Right : -Vector2d.Right, _start, verify);
    }

    private void Move(Vector2d input, Vector2d accepted, bool verify)
    {
        AdvanceClocks();
        _navigator.PrepareFrameMotion(input);
        ResolveAndCommit();
        if (verify)
        {
            CheckPose(accepted);
            Check(_navigator.Velocity == (_blocked ? Vector2d.Zero : input * (Fixed64)4), "Unexpected accepted velocity.");
            Check(_sideView
                ? _navigator.IsGrounded && _navigator.Support is { } support && support.Normal == Vector2d.Forward
                    && support.Point == new Vector2d(accepted.X, -Fixed64.Half)
                : _navigator.SupportStatus == NavigationSupportStatus2D.NotApplicable, "Unexpected support state.");
            Check(_navigator.LastCommittedCell?.Address == new NavigationCellAddress(MapId, new VoxelIndex(4, 1, 1)),
                "Unexpected committed cell.");
        }
    }

    private void Traverse(NavigationCellAddress source, NavigationCellAddress destination, bool ascending, bool verify)
    {
        Check(_navigator.ApplySideViewGuidedTrekRequest(source, destination, Policy, PathAlgorithm.AStar,
            Budget, allowTransitions: true) == NavigationGuideStatus.Success, "Ladder route acquisition failed.");
        AdvanceClocks();
        _navigator.PrepareFrameMotion();
        ResolveAndCommit();
        var action = _navigator.PendingTransition ?? throw new InvalidOperationException("The ladder source did not hold the action.");
        if (verify)
        {
            Check(action.SourceAddress == source && action.DestinationAddress == destination
                && action.Type == TraversalTransitionType.Climb && _navigator.IsGrounded, "Unexpected ladder instruction.");
            CheckPose(action.SourcePosition);
        }
        var corner = ascending
            ? new Vector2d(action.SourcePosition.X, action.DestinationPosition.Y)
            : new Vector2d(action.DestinationPosition.X, action.SourcePosition.Y);
        CommitAction(action, corner, verify);
        CommitAction(action, action.DestinationPosition, verify);
        Check(_navigator.CompletePendingTransition(action) == NavigationGuideStatus.Success, "Ladder completion failed.");
        if (verify)
            Check(_navigator.IsGrounded && _navigator.HasArrived && _navigator.LastCommittedCell?.Address == destination,
                "The ladder exit did not commit the supported destination.");
    }

    private void CommitAction(in NavigationTransitionInstruction2D action, Vector2d target, bool verify)
    {
        AdvanceClocks();
        Check(_navigator.PrepareActionFrameMotion(action, new NavigationPose2D(target, Fixed64.Zero))
            == NavigationGuideStatus.Success, "Ladder target preparation failed.");
        ResolveAndCommit();
        if (verify)
            CheckPose(target);
    }

    private void AdvanceClocks()
    {
        _navigation.Simulate();
        _physics.Simulate();
    }

    private void ResolveAndCommit()
    {
        _physics.LateSimulate();
        _navigator.CommitFrameMotion();
        _navigation.LateSimulate();
    }

    private void CheckPose(Vector2d accepted) =>
        Check(_navigator.Pose.Center == accepted && _navigator.Body.Position == accepted
            && _navigator.Body.Collider.Center == accepted
            && _navigator.Body.Agent.Transform.WorldPosition == accepted.ToVector3d(HostY)
            && _navigator.Body.Rotation == _navigator.Pose.RotationRadians, "Unexpected accepted native pose.");

    private void AddBox(Vector2d center, Vector2d size) =>
        new LSAABBoxCollider2D(size).InitializeWithNoBody(AgentAt(center));

    private MatterAgent AgentAt(Vector2d center) => new(_physics,
        new FixedTransform(center.ToVector3d(HostY), FixedQuaternion.Identity, Vector3d.One));

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class MatterAgent(GravitasWorldContext context, FixedTransform transform) : IMatterAgent
    {
        public GravitasWorldContext Context { get; } = context;
        public FixedTransform Transform { get; } = transform;
        public bool IsParent => true;
        public bool IsInteracting => false;
    }
}
