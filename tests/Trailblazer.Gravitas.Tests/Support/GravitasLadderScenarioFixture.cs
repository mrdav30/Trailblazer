using FixedMathSharp;
using FluentAssertions;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

namespace Trailblazer.Gravitas.Tests.Support;

internal sealed class GravitasLadderScenarioFixture : GravitasNavigatorScenarioFixture,
    IClimbAffordanceResolver
{
    internal const string MapId = "gravitas-ladder";
    internal const string TransitionId = "ladder-up";
    internal const int ActionFrameCount = 36;
    internal int ActionFrames { get; private set; }

    internal GravitasLadderScenarioFixture(bool obstructClimb = false)
        : base(addWall: false, addNavigation: false)
    {
        var configuration = new GridConfiguration(
            new Vector3d(-2, -2, -2), new Vector3d(6, 5, 3),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, Fixed64.Zero, (Fixed64)2, (Fixed64)2);
        var map = new NavigationMapBuilder(MapId, binding)
            .AddCell(new VoxelIndex(2, 2, 2), cell)
            .AddCell(new VoxelIndex(3, 2, 2), cell)
            .AddCell(new VoxelIndex(4, 4, 2), cell)
            .AddCell(new VoxelIndex(5, 4, 2), cell)
            .AddTransition(new TraversalTransitionDefinition(
                TransitionId, TraversalTransitionType.Climb,
                new VoxelIndex(3, 2, 2), TraversalMedium.Solid,
                new NavigationCellAddress(MapId, new VoxelIndex(4, 4, 2)),
                TraversalMedium.Solid, TraversalCapability.Climb,
                locomotionHints: TraversalTransitionLocomotionHints.RequestClimb))
            .Build();
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear, 3, Trailblazer.FrameCount + 1);
        Trailblazer.Pathing.Admit(operation).Should().BeTrue();
        SimulateUntilApplied(new[] { operation });
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.FromFraction(1, 8), Fixed64.Quarter, Fixed64.FromFraction(1, 8)),
            Fixed64.Zero, Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Gas, TraversalCapability.Climb);
        ConfigureScenario(profile, new Vector3d(Fixed64.Zero, -Fixed64.Half, Fixed64.Zero),
            new Vector3d((Fixed64)3, Fixed64.FromFraction(3, 2), Fixed64.Zero), MapId, MapId,
            PhysicsLayerMask.FromLayer(0), PhysicsLayerMask.None);
        // The ladder shaft is open. Four vertical action frames are followed
        // by 32 sideways dismount frames onto the upper landing at 1 unit/s.
        AddBox(new Vector3d(Fixed64.Zero, -Fixed64.One, Fixed64.Zero), new Vector3d(4, 1, 2), layer: 0);
        AddBox(new Vector3d((Fixed64)3, Fixed64.FromFraction(5, 4), Fixed64.Zero),
            new Vector3d((Fixed64)3, Fixed64.Half, (Fixed64)2), layer: 0);
        if (obstructClimb)
            AddBox(new Vector3d(Fixed64.One, Fixed64.Half, Fixed64.Zero),
                new Vector3d(Fixed64.One, Fixed64.Quarter, Fixed64.One));
    }

    internal PathQuery Query(PathAlgorithm algorithm) => new(
        new NavigationEndpoint(StartFoot, MapId), new NavigationEndpoint(EndFoot, MapId),
        Profile, new NavigationAreaPolicyKey("gravitas-proof", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), algorithm,
        new NavigationWorkBudget(8192, 128, 1024, 8192, 1024, 1024, 1024, 128, 1024, 1024, 1024),
        allowTransitions: true, arrivalRadius: ArrivalRadius);

    protected override void PrepareHostMotion(GravitasNavigatorActor actor)
    {
        if (!actor.Navigator.PendingTransition.HasValue || ActionFrames >= ActionFrameCount)
            return;
        ActionFrames++;
        Vector3d target = new(
            Fixed64.One + (Fixed64)System.Math.Max(0, ActionFrames - 4) / (Fixed64)32,
            Fixed64.FromFraction(-3, 8) + Fixed64.Half * (Fixed64)System.Math.Min(ActionFrames, 4),
            Fixed64.Zero);
        actor.Body.PositionTransform.LocalPosition = target;
    }

    public bool TryResolveClimbAffordance(TrekRequest request, TransitState currentState,
        out ClimbAffordanceSnapshot snapshot)
    {
        snapshot = new ClimbAffordanceSnapshot(ClimbAffordanceKind.Ladder,
            new Vector3d(Fixed64.One, Fixed64.FromFraction(-3, 8), Fixed64.Zero), Vector3d.Backward, Vector3d.Up,
            affordanceId: 41, allowLateralTraverse: false, allowDescent: false,
            allowMantle: false, allowDetachJump: false);
        return request.IsRequestingClimb;
    }
}
