using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class NavigationTransitionSimulationTests
{
    private const string MapId = "phase7-ladder";

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("phase7-simulation", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    private static readonly NavigationWorkBudget Budget = new(
        8_192,
        64,
        1_024,
        8_192,
        1_024,
        1_024,
        1_024,
        64,
        1_024,
        1_024,
        1_024);

    [Fact]
    public void DroppedLadder_ShouldCreateMoveAndRemoveOneHeldBidirectionalTransition()
    {
        VoxelIndex cliff = default;
        var water = new VoxelIndex(2, 0, 0);
        var movedWater = new VoxelIndex(4, 0, 0);
        using TrailblazerWorldContext context = CreateLadderContext(
            cliff,
            water,
            movedWater,
            out NormalizedGridConfiguration binding);
        PathQuery originalQuery = Query(
            binding,
            cliff,
            TraversalMedium.Solid,
            water,
            TraversalMedium.Liquid,
            TraversalMedia.Liquid);

        context.Guides.RequestGuide(originalQuery, out NavigationGuideLease? absent)
            .Should().Be(NavigationGuideStatus.NoPath);
        absent.Should().BeNull();

        PublishOverlay(
            context,
            sequence: 3,
            TraversalTransitionOverlayOperation.Upsert(LadderDown(cliff, water)),
            TraversalTransitionOverlayOperation.Upsert(LadderUp(cliff, water)));

        PathQuery reverseQuery = Query(
            binding,
            water,
            TraversalMedium.Liquid,
            cliff,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        context.Guides.RequestGuide(reverseQuery, out NavigationGuideLease? reverseAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        using (NavigationGuideLease reverse = reverseAcquired!.Value)
        {
            NavigationTransitionInstruction climbUp =
                GuidedPathTestScene.AdvanceToTransition(reverse).Transition;
            climbUp.Id.Should().Be("ladder-up");
            climbUp.SourceMedium.Should().Be(TraversalMedium.Liquid);
            climbUp.DestinationMedium.Should().Be(TraversalMedium.Solid);
        }

        context.Guides.RequestGuide(originalQuery, out NavigationGuideLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease completed = acquired!.Value;
        NavigationGuideStep action = GuidedPathTestScene.AdvanceToTransition(completed);
        action.Transition.Type.Should().Be(TraversalTransitionType.Climb);
        action.Transition.SourceMedium.Should().Be(TraversalMedium.Solid);
        action.Transition.DestinationMedium.Should().Be(TraversalMedium.Liquid);
        action.Transition.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb);
        completed.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);

        completed.CompletePendingTransition(action.Transition)
            .Should().Be(NavigationGuideStatus.Success);
        completed.TryGetCurrentStep(out NavigationGuideStep destination)
            .Should().Be(NavigationGuideStatus.Success);
        destination.HasTransition.Should().BeFalse();
        destination.Address.Should().Be(new NavigationCellAddress(MapId, water));
        destination.Medium.Should().Be(TraversalMedium.Liquid);

        var navigator = new TestNavigator(context);
        navigator.Setup(
            GuidedPathTestScene.Anchor(binding, cliff),
            AgentProfile());
        navigator.Initialize(new TrekCondition { Medium = TraversalMedium.Solid });
        navigator.ApplyGuidedTrekRequest(originalQuery);
        navigator.Simulate();
        navigator.PendingTransition.Should().NotBeNull();
        NavigationTransitionInstruction heldInstruction =
            navigator.PendingTransition!.Value;
        navigator.CommitFrameMotion();

        PublishOverlay(
            context,
            sequence: 4,
            TraversalTransitionOverlayOperation.Upsert(LadderDown(cliff, movedWater)),
            TraversalTransitionOverlayOperation.Upsert(LadderUp(cliff, movedWater)));

        navigator.CompletePendingTransition(heldInstruction)
            .Should().Be(NavigationGuideStatus.Stale);
        navigator.PendingTransition.Should().Be(heldInstruction);
        navigator.Simulate();
        navigator.PendingTransition.Should().BeNull();
        context.Guides.RequestGuide(originalQuery, out NavigationGuideLease? movedAway)
            .Should().Be(NavigationGuideStatus.NoPath);
        movedAway.Should().BeNull();

        PathQuery movedQuery = Query(
            binding,
            cliff,
            TraversalMedium.Solid,
            movedWater,
            TraversalMedium.Liquid,
            TraversalMedia.Liquid);
        context.Guides.RequestGuide(movedQuery, out NavigationGuideLease? movedAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease moved = movedAcquired!.Value;
        NavigationTransitionInstruction movedInstruction =
            GuidedPathTestScene.AdvanceToTransition(moved).Transition;

        PublishOverlay(
            context,
            sequence: 5,
            TraversalTransitionOverlayOperation.Suppress("ladder-down"),
            TraversalTransitionOverlayOperation.Suppress("ladder-up"));

        moved.Status.Should().Be(NavigationGuideStatus.Stale);
        moved.CompletePendingTransition(movedInstruction)
            .Should().Be(NavigationGuideStatus.Stale);
        context.Guides.RequestGuide(movedQuery, out NavigationGuideLease? removed)
            .Should().Be(NavigationGuideStatus.NoPath);
        removed.Should().BeNull();
    }

    [Fact]
    public void ImportedClimbShore_ShouldDriveTheExactNavigatorLocomotionHints()
    {
        VoxelIndex water = default;
        var climbShore = new VoxelIndex(1, 0, 0);
        using TrailblazerWorldContext context = CreateImportedShoreContext(
            water,
            climbShore,
            out NormalizedGridConfiguration binding);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            TraversalCapability.Climb | TraversalCapability.Swim);
        Vector3d waterAnchor = GuidedPathTestScene.Anchor(binding, water);
        Vector3d shoreAnchor = GuidedPathTestScene.Anchor(binding, climbShore);
        var query = new PathQuery(
            new NavigationEndpoint(waterAnchor, "phase7-shore"),
            new NavigationEndpoint(shoreAnchor, "phase7-shore"),
            profile,
            Policy.Key,
            new TraversalIntent(TraversalMedium.Liquid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            Budget,
            allowTransitions: true);
        var navigator = new TestNavigator(context);
        navigator.Setup(waterAnchor, profile);
        navigator.Initialize(new TrekCondition { Medium = TraversalMedium.Liquid });
        navigator.ApplyGuidedTrekRequest(query);

        navigator.Simulate();

        navigator.PendingTransition.Should().NotBeNull();
        NavigationTransitionInstruction action = navigator.PendingTransition!.Value;
        action.Type.Should().Be(TraversalTransitionType.SwimExit);
        action.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb
            | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        navigator.CompletePendingTransition(action)
            .Should().Be(NavigationGuideStatus.Success);
        navigator.SetTestPosition(shoreAnchor);
        navigator.SetTrekCondition(
            medium: TraversalMedium.Solid,
            replaceGroundContact: false,
            updateMotorState: false);

        navigator.PendingTransition.Should().BeNull();
        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Solid);
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        navigator.Steering!.CurrentQuery!.Value.Traversal.StartMedium
            .Should().Be(TraversalMedium.Solid);
    }

    private static TrailblazerWorldContext CreateLadderContext(
        VoxelIndex cliff,
        VoxelIndex water,
        VoxelIndex movedWater,
        out NormalizedGridConfiguration binding)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                Fixed64.One,
                Fixed64.One,
                Fixed64.One));
        context.World.TryAddGrid(configuration, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out binding).Should().BeTrue();

        NavigationCell solid = GuidedPathTestScene.Cell(TraversalMedia.Solid);
        NavigationCell liquid = GuidedPathTestScene.Cell(TraversalMedia.Liquid);
        NavigationMap map = new NavigationMapBuilder(MapId, binding)
            .AddCell(cliff, solid)
            .AddCell(water, liquid)
            .AddCell(movedWater, liquid)
            .Build();
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            Policy,
            policySequence: 2);
        return context;
    }

    private static TrailblazerWorldContext CreateImportedShoreContext(
        VoxelIndex water,
        VoxelIndex climbShore,
        out NormalizedGridConfiguration binding)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Right,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out binding).Should().BeTrue();
        NavigationMap map = NavigationMapTokenImporter.ImportRectangular(
            "phase7-shore",
            configuration,
            new string[,,] { { { "L!" } }, { { "LC!" } } },
            NavigationTokenLegend.CreateBuiltIn(Fixed64.One, Fixed64.One));
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            Policy,
            policySequence: 2);
        return context;
    }

    private static TraversalTransitionDefinition LadderDown(
        VoxelIndex cliff,
        VoxelIndex water) => new(
        "ladder-down",
        TraversalTransitionType.Climb,
        cliff,
        TraversalMedium.Solid,
        new NavigationCellAddress(MapId, water),
        TraversalMedium.Liquid,
        TraversalCapability.Climb,
        locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);

    private static TraversalTransitionDefinition LadderUp(
        VoxelIndex cliff,
        VoxelIndex water) => new(
        "ladder-up",
        TraversalTransitionType.Climb,
        water,
        TraversalMedium.Liquid,
        new NavigationCellAddress(MapId, cliff),
        TraversalMedium.Solid,
        TraversalCapability.Climb,
        locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);

    private static PathQuery Query(
        NormalizedGridConfiguration binding,
        VoxelIndex start,
        TraversalMedium startMedium,
        VoxelIndex end,
        TraversalMedium endMedium,
        TraversalMedia targetMedia)
    {
        NavigationAgentProfile profile = AgentProfile();
        return new PathQuery(
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, start),
                MapId),
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, end),
                MapId),
            profile,
            Policy.Key,
            new TraversalIntent(startMedium, targetMedia),
            PathAlgorithm.AStar,
            Budget,
            allowTransitions: true);
    }

    private static NavigationAgentProfile AgentProfile() => new(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            TraversalCapability.Climb | TraversalCapability.Swim);

    private static void PublishOverlay(
        TrailblazerWorldContext context,
        long sequence,
        params TraversalTransitionOverlayOperation[] transitions)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[]
                    {
                        new NavigationMapOverlayDelta(
                            MapId,
                            transitions: transitions)
                    })),
            sequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }
}
