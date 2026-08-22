using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationTransitionRuleTests
{
    private const string MapId = "phase7-duck";
    private const string ShapeMapId = "phase7-transition-shapes";
    private const string MutationMapId = "phase7-mutation";
    private const string UnaffectedMapId = "phase7-unaffected";

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("phase7-duck", 1),
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

    private static readonly GuideSampleWorkBudget SampleBudget = new(
        1_024,
        1_024,
        1_024,
        1_024,
        1_024,
        1_024,
        1_024);

    [Fact]
    public void DuckRule_ShouldServeMultipleWaterSurfacesAndRejectANonFlyingSwimmer()
    {
        var firstWater = new VoxelIndex(0, 0, 0);
        var firstAir = new VoxelIndex(1, 0, 0);
        var secondWater = new VoxelIndex(3, 0, 0);
        var secondAir = new VoxelIndex(4, 0, 0);
        using TrailblazerWorldContext context = CreateDuckContext(
            new[] { firstWater, firstAir, secondWater, secondAir },
            out NormalizedGridConfiguration binding);
        var contacts = new[]
        {
            (Water: firstWater, Air: firstAir),
            (Water: secondWater, Air: secondAir)
        };

        for (int i = 0; i < contacts.Length; i++)
        {
            PathQuery duck = Query(
                binding,
                contacts[i].Water,
                contacts[i].Air,
                TraversalCapability.Swim | TraversalCapability.Fly,
                PathAlgorithm.AStar);
            context.Guides.RequestGuide(duck, out NavigationGuideLease? acquired)
                .Should().Be(NavigationGuideStatus.Success);
            using NavigationGuideLease guide = acquired!.Value;
            NavigationGuideStep action = GuidedPathTestScene.AdvanceToTransition(guide);

            action.Transition.IdentityKind.Should().Be(
                NavigationTransitionIdentityKind.Rule);
            action.Transition.Id.Should().Be("duck-takeoff");
            action.Transition.Type.Should().Be(TraversalTransitionType.Takeoff);
            action.Transition.SourceAddress.Should().Be(
                new NavigationCellAddress(MapId, contacts[i].Water));
            action.Transition.DestinationAddress.Should().Be(
                new NavigationCellAddress(MapId, contacts[i].Air));
            action.Transition.SourceMedium.Should().Be(TraversalMedium.Liquid);
            action.Transition.DestinationMedium.Should().Be(TraversalMedium.Gas);
            guide.CompletePendingTransition(action.Transition)
                .Should().Be(NavigationGuideStatus.Success);
            guide.TryGetCurrentStep(out NavigationGuideStep destination)
                .Should().Be(NavigationGuideStatus.Success);
            destination.Address.Should().Be(
                new NavigationCellAddress(MapId, contacts[i].Air));
            destination.Medium.Should().Be(TraversalMedium.Gas);

            if (i == 0)
            {
                PathQuery swimmer = Query(
                    binding,
                    contacts[i].Water,
                    contacts[i].Air,
                    TraversalCapability.Swim,
                    PathAlgorithm.AStar);
                context.Guides.RequestGuide(swimmer, out NavigationGuideLease? blocked)
                    .Should().Be(NavigationGuideStatus.NoPath);
                blocked.Should().BeNull();
            }

            PathQuery flowDuck = Query(
                binding,
                contacts[i].Water,
                contacts[i].Air,
                TraversalCapability.Swim | TraversalCapability.Fly,
                PathAlgorithm.FlowField);
            context.Guides.RequestFlowField(
                    flowDuck,
                    out NavigationFlowFieldLease? flowAcquired)
                .Should().Be(NavigationGuideStatus.Success);
            using NavigationFlowFieldLease flow = flowAcquired!.Value;
            Vector3d waterAnchor = GuidedPathTestScene.Anchor(
                binding,
                contacts[i].Water);
            action.Transition.SourcePosition.Should().NotBe(waterAnchor);
            flow.TrySample(
                    action.Transition.SourcePosition,
                    SampleBudget,
                    out NavigationFlowSample takeoff)
                .Should().Be(NavigationGuideStatus.Success);
            takeoff.HasTransition.Should().BeTrue();
            takeoff.Transition.Id.Should().Be("duck-takeoff");
            flow.CompletePendingTransition(takeoff.Transition)
                .Should().Be(NavigationGuideStatus.Success);
            flow.TrySample(
                    GuidedPathTestScene.Anchor(
                        binding,
                        contacts[i].Air),
                    SampleBudget,
                    out NavigationFlowSample continued)
                .Should().Be(NavigationGuideStatus.Success);
            continued.HasTransition.Should().BeFalse();
            continued.Medium.Should().Be(TraversalMedium.Gas);
            continued.Heading.Should().Be(Vector3d.Zero);
        }
    }

    [Fact]
    public void TransitionShapes_ShouldCoverSameCellSameMediumAndDistantCustomActions()
    {
        VoxelIndex sameCell = default;
        var jumpSource = new VoxelIndex(2, 0, 0);
        var jumpTarget = new VoxelIndex(4, 0, 0);
        var climbSource = new VoxelIndex(6, 0, 0);
        var climbTarget = new VoxelIndex(8, 0, 0);
        var teleporterSource = new VoxelIndex(10, 0, 0);
        var teleporterTarget = new VoxelIndex(20, 0, 0);
        using TrailblazerWorldContext context = CreateShapeContext(
            sameCell,
            jumpSource,
            jumpTarget,
            climbSource,
            climbTarget,
            teleporterSource,
            teleporterTarget,
            out NormalizedGridConfiguration binding);

        PathQuery sameCellDisabled = ShapeQuery(
            binding,
            sameCell,
            TraversalMedium.Liquid,
            sameCell,
            TraversalMedium.Gas,
            allowTransitions: false);
        context.Guides.RequestGuide(
                sameCellDisabled,
                out NavigationGuideLease? disabled)
            .Should().Be(NavigationGuideStatus.NoPath);
        disabled.Should().BeNull();

        AssertTransition(
            context,
            ShapeQuery(
                binding,
                sameCell,
                TraversalMedium.Liquid,
                sameCell,
                TraversalMedium.Gas,
                allowTransitions: true),
            "same-cell-takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Gas,
            expectedCost: (Fixed64)2);
        AssertTransition(
            context,
            ShapeQuery(
                binding,
                jumpSource,
                TraversalMedium.Solid,
                jumpTarget,
                TraversalMedium.Solid,
                allowTransitions: true),
            "same-medium-jump",
            TraversalTransitionType.Jump,
            TraversalMedium.Solid,
            expectedCost: (Fixed64)3);
        AssertTransition(
            context,
            ShapeQuery(
                binding,
                climbSource,
                TraversalMedium.Solid,
                climbTarget,
                TraversalMedium.Solid,
                allowTransitions: true),
            "same-medium-climb",
            TraversalTransitionType.Climb,
            TraversalMedium.Solid,
            expectedCost: (Fixed64)4);
        AssertTransition(
            context,
            ShapeQuery(
                binding,
                teleporterSource,
                TraversalMedium.Solid,
                teleporterTarget,
                TraversalMedium.Solid,
                allowTransitions: true),
            "cheap-teleporter",
            TraversalTransitionType.Custom,
            TraversalMedium.Solid,
            expectedCost: (Fixed64)3);
    }

    [Fact]
    public void PublicMutations_ShouldInvalidateOnlyTheirExactDependencies()
    {
        VoxelIndex start = default;
        var middle = new VoxelIndex(1, 0, 0);
        var end = new VoxelIndex(2, 0, 0);
        var dualMedia = new VoxelIndex(3, 0, 0);
        using TrailblazerWorldContext context = CreateMutationContext(
            dualMedia,
            out NormalizedGridConfiguration affectedBinding,
            out NormalizedGridConfiguration unaffectedBinding);
        PathQuery affectedQuery = MutationQuery(
            MutationMapId,
            affectedBinding,
            start,
            end,
            TraversalMedium.Gas,
            TraversalMedium.Gas,
            allowTransitions: false);
        PathQuery unaffectedQuery = MutationQuery(
            UnaffectedMapId,
            unaffectedBinding,
            start,
            end,
            TraversalMedium.Solid,
            TraversalMedium.Solid,
            allowTransitions: false);
        RequestSettledGuide(
                context,
                affectedQuery,
                out NavigationGuideLease? affectedAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease affected = affectedAcquired!.Value;
        RequestSettledGuide(
                context,
                unaffectedQuery,
                out NavigationGuideLease? unaffectedAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease unaffected = unaffectedAcquired!.Value;

        PublishCellOverlay(
            context,
            operationSequence: 4,
            NavigationCellOverlayOperation.Set(
                middle,
                GuidedPathTestScene.Cell(TraversalMedia.Liquid)));

        affected.Status.Should().Be(NavigationGuideStatus.Stale);
        unaffected.Status.Should().Be(NavigationGuideStatus.Success);
        affected.Dispose();
        RequestSettledGuide(
                context,
                affectedQuery,
                out NavigationGuideLease? flooded)
            .Should().Be(NavigationGuideStatus.NoPath);
        flooded.Should().BeNull();

        PublishCellOverlay(
            context,
            operationSequence: 5,
            NavigationCellOverlayOperation.RevertToBake(middle));

        unaffected.Status.Should().Be(NavigationGuideStatus.Success);
        RequestSettledGuide(
                context,
                affectedQuery,
                out NavigationGuideLease? drainedAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease drained = drainedAcquired!.Value;

        PathQuery ruleQuery = MutationQuery(
            MutationMapId,
            affectedBinding,
            dualMedia,
            dualMedia,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            allowTransitions: true);
        RequestSettledGuide(context, ruleQuery, out NavigationGuideLease? ruleAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease originalRule = ruleAcquired!.Value;
        originalRule.TotalCost.Should().Be(Fixed64.One);

        CommitMutationMap(
            context,
            affectedBinding,
            dualMedia,
            actionCost: (Fixed64)5,
            bakeVersion: 2,
            operationSequence: 6,
            OverlayReplacementPolicy.PreserveAndRevalidate);

        originalRule.Status.Should().Be(NavigationGuideStatus.Stale);
        originalRule.Dispose();
        RequestSettledGuide(context, ruleQuery, out NavigationGuideLease? changedAcquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease changedRule = changedAcquired!.Value;
        changedRule.TotalCost.Should().Be((Fixed64)5);
    }

    private static void AssertTransition(
        TrailblazerWorldContext context,
        PathQuery query,
        string expectedId,
        TraversalTransitionType expectedType,
        TraversalMedium expectedDestinationMedium,
        Fixed64 expectedCost)
    {
        context.Guides.RequestGuide(query, out NavigationGuideLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationGuideLease guide = acquired!.Value;
        guide.TotalCost.Should().Be(expectedCost);
        NavigationGuideStep action = GuidedPathTestScene.AdvanceToTransition(guide);
        action.Transition.Id.Should().Be(expectedId);
        action.Transition.Type.Should().Be(expectedType);
        action.Transition.DestinationMedium.Should().Be(expectedDestinationMedium);
        action.Transition.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.None);
        guide.CompletePendingTransition(action.Transition)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out NavigationGuideStep destination)
            .Should().Be(NavigationGuideStatus.Success);
        destination.Address.Should().Be(action.Transition.DestinationAddress);
        destination.Medium.Should().Be(expectedDestinationMedium);
    }

    private static TrailblazerWorldContext CreateDuckContext(
        VoxelIndex[] cells,
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
                Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(configuration, cells, out _).Should().BeTrue();
        configuration.TryNormalize(out binding).Should().BeTrue();

        var builder = new NavigationMapBuilder(MapId, binding)
            .AddCell(cells[0], GuidedPathTestScene.Cell(TraversalMedia.Liquid))
            .AddCell(cells[1], GuidedPathTestScene.Cell(TraversalMedia.Gas))
            .AddCell(cells[2], GuidedPathTestScene.Cell(TraversalMedia.Liquid))
            .AddCell(cells[3], GuidedPathTestScene.Cell(TraversalMedia.Gas))
            .AddTransitionRule(new TraversalTransitionRule(
                "duck-takeoff",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.PositiveFaceContact,
                TraversalCapability.Swim | TraversalCapability.Fly,
                actionCost: (Fixed64)2,
                TraversalTransitionLocomotionHints.None));
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            builder.Build(),
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            Policy,
            policySequence: 2);
        return context;
    }

    private static TrailblazerWorldContext CreateShapeContext(
        VoxelIndex sameCell,
        VoxelIndex jumpSource,
        VoxelIndex jumpTarget,
        VoxelIndex climbSource,
        VoxelIndex climbTarget,
        VoxelIndex teleporterSource,
        VoxelIndex teleporterTarget,
        out NormalizedGridConfiguration binding)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(20, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out binding).Should().BeTrue();

        NavigationCell solid = GuidedPathTestScene.Cell(TraversalMedia.Solid);
        NavigationMap map = new NavigationMapBuilder(ShapeMapId, binding)
            .AddCell(
                sameCell,
                GuidedPathTestScene.Cell(
                    TraversalMedia.Liquid | TraversalMedia.Gas))
            .AddCell(jumpSource, solid)
            .AddCell(jumpTarget, solid)
            .AddCell(climbSource, solid)
            .AddCell(climbTarget, solid)
            .AddCell(teleporterSource, solid)
            .AddCell(teleporterTarget, new NavigationCell(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                enterCost: (Fixed64)2,
                radiusClearance: Fixed64.One,
                heightClearance: Fixed64.One))
            .AddTransitionRule(new TraversalTransitionRule(
                "same-cell-takeoff",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.Fly,
                actionCost: (Fixed64)2,
                TraversalTransitionLocomotionHints.None))
            .AddTransition(new TraversalTransitionDefinition(
                "same-medium-jump",
                TraversalTransitionType.Jump,
                jumpSource,
                TraversalMedium.Solid,
                new NavigationCellAddress(ShapeMapId, jumpTarget),
                TraversalMedium.Solid,
                TraversalCapability.Jump,
                actionCost: (Fixed64)3))
            .AddTransition(new TraversalTransitionDefinition(
                "same-medium-climb",
                TraversalTransitionType.Climb,
                climbSource,
                TraversalMedium.Solid,
                new NavigationCellAddress(ShapeMapId, climbTarget),
                TraversalMedium.Solid,
                TraversalCapability.Climb,
                actionCost: (Fixed64)4))
            .AddTransition(new TraversalTransitionDefinition(
                "cheap-teleporter",
                TraversalTransitionType.Custom,
                teleporterSource,
                TraversalMedium.Solid,
                new NavigationCellAddress(ShapeMapId, teleporterTarget),
                TraversalMedium.Solid,
                TraversalCapability.None,
                actionCost: Fixed64.One))
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

    private static TrailblazerWorldContext CreateMutationContext(
        VoxelIndex dualMedia,
        out NormalizedGridConfiguration affectedBinding,
        out NormalizedGridConfiguration unaffectedBinding)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var affectedConfiguration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(3, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        var unaffectedConfiguration = new GridConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(affectedConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(unaffectedConfiguration, out _).Should().BeTrue();
        affectedConfiguration.TryNormalize(out affectedBinding).Should().BeTrue();
        unaffectedConfiguration.TryNormalize(out unaffectedBinding).Should().BeTrue();
        NavigationOperationReceipt affectedReceipt = CommitMutationMap(
            context,
            affectedBinding,
            dualMedia,
            Fixed64.One,
            bakeVersion: 1,
            operationSequence: 1,
            OverlayReplacementPolicy.Clear,
            simulate: false);
        NavigationMap unaffected = new NavigationMapBuilder(
                UnaffectedMapId,
                unaffectedBinding)
            .AddCell(default, GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .AddCell(
                new VoxelIndex(1, 0, 0),
                GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .AddCell(
                new VoxelIndex(2, 0, 0),
                GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .Build();
        var unaffectedOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(unaffected, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            Policy,
            publicationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(unaffectedOperation).Should().BeTrue();
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(
            context,
            affectedReceipt,
            unaffectedOperation.Receipt,
            policyOperation.Receipt);
        return context;
    }

    private static NavigationOperationReceipt CommitMutationMap(
        TrailblazerWorldContext context,
        NormalizedGridConfiguration binding,
        VoxelIndex dualMedia,
        Fixed64 actionCost,
        int bakeVersion,
        long operationSequence,
        OverlayReplacementPolicy replacementPolicy,
        bool simulate = true)
    {
        NavigationMap map = new NavigationMapBuilder(MutationMapId, binding)
            .AddCell(default, GuidedPathTestScene.Cell(TraversalMedia.Gas))
            .AddCell(
                new VoxelIndex(1, 0, 0),
                new NavigationCell(
                    TraversalMedia.Gas,
                    TraversalCapability.None,
                    default,
                    Fixed64.One,
                    Fixed64.One,
                    Fixed64.One))
            .AddCell(
                new VoxelIndex(2, 0, 0),
                GuidedPathTestScene.Cell(TraversalMedia.Gas))
            .AddCell(
                dualMedia,
                GuidedPathTestScene.Cell(
                    TraversalMedia.Liquid | TraversalMedia.Gas))
            .AddTransitionRule(new TraversalTransitionRule(
                "mutable-takeoff",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.Fly,
                actionCost,
                TraversalTransitionLocomotionHints.None))
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion),
            replacementPolicy,
            operationSequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        if (simulate)
            GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
        return operation.Receipt;
    }

    private static void PublishCellOverlay(
        TrailblazerWorldContext context,
        long operationSequence,
        params NavigationCellOverlayOperation[] cells)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta(MutationMapId, cells) })),
            operationSequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }

    private static PathQuery ShapeQuery(
        NormalizedGridConfiguration binding,
        VoxelIndex start,
        TraversalMedium startMedium,
        VoxelIndex end,
        TraversalMedium endMedium,
        bool allowTransitions)
    {
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Liquid | TraversalMedia.Gas,
            TraversalCapability.Fly
                | TraversalCapability.Jump
                | TraversalCapability.Climb);
        return new PathQuery(
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, start),
                ShapeMapId),
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, end),
                ShapeMapId),
            profile,
            Policy.Key,
            new TraversalIntent(
                startMedium,
                endMedium switch
                {
                    TraversalMedium.Solid => TraversalMedia.Solid,
                    TraversalMedium.Gas => TraversalMedia.Gas,
                    TraversalMedium.Liquid => TraversalMedia.Liquid,
                    _ => throw new ArgumentOutOfRangeException(nameof(endMedium))
                }),
            PathAlgorithm.AStar,
            Budget,
            allowTransitions);
    }

    private static PathQuery MutationQuery(
        string mapId,
        NormalizedGridConfiguration binding,
        VoxelIndex start,
        VoxelIndex end,
        TraversalMedium startMedium,
        TraversalMedium endMedium,
        bool allowTransitions)
    {
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Liquid | TraversalMedia.Gas,
            TraversalCapability.Fly);
        return new PathQuery(
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, start),
                mapId),
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, end),
                mapId),
            profile,
            Policy.Key,
            new TraversalIntent(
                startMedium,
                endMedium switch
                {
                    TraversalMedium.Solid => TraversalMedia.Solid,
                    TraversalMedium.Gas => TraversalMedia.Gas,
                    TraversalMedium.Liquid => TraversalMedia.Liquid,
                    _ => throw new ArgumentOutOfRangeException(nameof(endMedium))
                }),
            PathAlgorithm.AStar,
            Budget,
            allowTransitions);
    }

    private static PathQuery Query(
        NormalizedGridConfiguration binding,
        VoxelIndex start,
        VoxelIndex end,
        TraversalCapability capabilities,
        PathAlgorithm algorithm)
    {
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Liquid | TraversalMedia.Gas,
            capabilities);
        return new PathQuery(
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(
                    binding,
                    start),
                MapId),
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, end),
                MapId),
            profile,
            Policy.Key,
            new TraversalIntent(TraversalMedium.Liquid, TraversalMedia.Gas),
            algorithm,
            Budget,
            allowTransitions: true,
            algorithm == PathAlgorithm.FlowField
                ? new FlowFieldQueryOptions(Fixed64.Zero)
                : default);
    }

    private static NavigationGuideStatus RequestSettledGuide(
        TrailblazerWorldContext context,
        PathQuery query,
        out NavigationGuideLease? guide)
    {
        NavigationGuideStatus status = NavigationGuideStatus.Stale;
        guide = null;
        for (int frame = 0; frame < 1_024 && status == NavigationGuideStatus.Stale; frame++)
        {
            context.Simulate();
            status = context.Guides.RequestGuide(query, out guide);
        }
        return status;
    }

}
