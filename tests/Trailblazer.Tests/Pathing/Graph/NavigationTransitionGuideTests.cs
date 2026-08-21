//=======================================================================
// NavigationTransitionGuideTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Reflection;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationTransitionGuideTests
{
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("transition-guide", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void PayloadKey_ShouldIncludeResolvedStartAndTargetMedia()
    {
        PathQuery query = Query(
            Vector3d.Zero,
            Vector3d.One,
            TraversalMedia.Solid | TraversalMedia.Gas,
            allowTransitions: true);
        var address = new NavigationCellAddress("map", default);

        var solid = new NavigationAStarPayloadKey(
            query,
            address,
            address,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        var gas = new NavigationAStarPayloadKey(
            query,
            address,
            address,
            TraversalMedium.Gas,
            TraversalMedia.Gas);

        solid.Should().NotBe(gas);
        solid.GetHashCode().Should().NotBe(gas.GetHashCode());
    }

    [Theory]
    [InlineData((int)TraversalMedium.Gas)]
    [InlineData((int)TraversalMedium.Liquid)]
    public void AStar_ShouldTraverseOpenVolumeShortcutUsingUnifiedDispatcher(
        int mediumValue)
    {
        TraversalMedium medium = (TraversalMedium)mediumValue;
        using TrailblazerWorldContext context = CreateShortcutContext(medium);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, default);
        NavigationNodeRef target = Resolve(graph, new VoxelIndex(1, 0, 1));
        Vector3d start = GetVolumeAnchor(graph, source, Fixed64.One);
        Vector3d end = GetVolumeAnchor(graph, target, Fixed64.One);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(
                start,
                end,
                TraversalMedia.Gas | TraversalMedia.Liquid,
                allowTransitions: false),
            medium,
            TraversalMedia.Gas | TraversalMedia.Liquid);
        AdvanceAdmission(admission);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        AdvanceSearch(search);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Cost.Should().Be(Fixed64.FromRaw(42_212_773_072L));
        search.Result.WorldChangeSequence.Should().Be(context.World.ChangeSequence);
        search.Result.Key.StartMedium.Should().Be(medium);
        search.Result.Key.TargetMedia.Should().Be(
            TraversalMedia.Gas | TraversalMedia.Liquid);
        search.Result.GuidePoints[^1].Medium.Should().Be(medium);
    }

    [Fact]
    public void AStar_ShouldReconstructExplicitTransitionWithoutTreatingItAsMovement()
    {
        var sourceIndex = default(VoxelIndex);
        var targetIndex = new VoxelIndex(2, 0, 0);
        var transition = new TraversalTransitionDefinition(
            "takeoff",
            TraversalTransitionType.Takeoff,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Gas,
            actionCost: (Fixed64)5,
            locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);
        using TrailblazerWorldContext context = CreateTransitionContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Gas, (Fixed64)7),
            transition);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, sourceIndex);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Solid);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Gas);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(
                start,
                end,
                TraversalMedia.Solid | TraversalMedia.Gas,
                allowTransitions: true),
            TraversalMedium.Solid,
            TraversalMedia.Gas);
        AdvanceAdmission(admission);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        AdvanceSearch(search);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Cost.Should().Be((Fixed64)12);
        search.Result.Dependencies.HasTransitionRuleDependency.Should().BeTrue();
        NavigationAStarPayload.GetRetainedBytes(
                search.Result.GuidePoints.Length,
                search.Result.TransitionInstructions.Length,
                search.Result.Dependencies)
            .Should().Be(search.Result.RetainedBytes);
        var undersized = new NavigationAStarPayloadCache(
            context.World,
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes - 1,
            maxSinglePayloadBytes: search.Result.RetainedBytes - 1,
            maxActivePayloadBytes: search.Result.RetainedBytes - 1,
            maxActiveLeases: 1);
        undersized.TryReservePayload(search.Result.RetainedBytes, out _)
            .Should().BeFalse();
        var cache = new NavigationAStarPayloadCache(
            context.World,
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes,
            maxSinglePayloadBytes: search.Result.RetainedBytes,
            maxActivePayloadBytes: search.Result.RetainedBytes * 2,
            maxActiveLeases: 2);
        cache.TryReservePayload(
                search.Result.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        cache.TryPublish(
                search.Result,
                store,
                ref reservation,
                out NavigationAStarPayloadLease payloadLease)
            .Should().BeTrue();
        cache.TryCreateGuide(store, payloadLease, out NavigationAStarGuideLease? guide)
            .Should().Be(NavigationAStarQueryStatus.Success);
        cache.TryCheckout(
                search.Result.Key,
                graph,
                out NavigationAStarPayloadLease secondPayloadLease)
            .Should().BeTrue();
        cache.TryCreateGuide(
                store,
                secondPayloadLease,
                out NavigationAStarGuideLease? secondGuide)
            .Should().Be(NavigationAStarQueryStatus.Success);
        long generation = guide!.Generation;
        long secondGeneration = secondGuide!.Generation;
        var publicGuide = new NavigationGuideLease(guide);
        var secondPublicGuide = new NavigationGuideLease(secondGuide);
        generation.Should().Be(secondGeneration,
            "fresh guide objects both begin at acquisition generation one");
        guide.TryGetCurrentStep(generation, out NavigationGuideStep startStep)
            .Should().Be(NavigationAStarQueryStatus.Success);
        startStep.HasTransition.Should().BeFalse();
        startStep.Address.Should().Be(new NavigationCellAddress("map", sourceIndex));
        startStep.Medium.Should().Be(TraversalMedium.Solid);
        guide.TryAdvanceWaypoint(generation).Should().Be(NavigationAStarQueryStatus.Success);

        guide.TryGetCurrentStep(generation, out NavigationGuideStep transitionStep)
            .Should().Be(NavigationAStarQueryStatus.Success);
        transitionStep.HasTransition.Should().BeTrue();
        transitionStep.Address.Should().Be(new NavigationCellAddress("map", sourceIndex));
        transitionStep.Position.Should().Be(start);
        transitionStep.Medium.Should().Be(TraversalMedium.Solid);
        NavigationTransitionInstruction instruction = transitionStep.Transition;
        instruction.IdentityKind.Should().Be(NavigationTransitionIdentityKind.Definition);
        instruction.OwnerMapId.Should().Be("map");
        instruction.Id.Should().Be("takeoff");
        instruction.Type.Should().Be(TraversalTransitionType.Takeoff);
        instruction.SourceAddress.Should().Be(new NavigationCellAddress("map", sourceIndex));
        instruction.DestinationAddress.Should().Be(
            new NavigationCellAddress("map", targetIndex));
        instruction.SourceMedium.Should().Be(TraversalMedium.Solid);
        instruction.DestinationMedium.Should().Be(TraversalMedium.Gas);
        instruction.SourcePosition.Should().Be(start);
        instruction.DestinationPosition.Should().Be(end);
        instruction.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb);
        publicGuide.TryGetCurrentStep(out NavigationGuideStep publicTransitionStep)
            .Should().Be(NavigationGuideStatus.Success);
        publicTransitionStep.Transition.Should().Be(instruction);
        guide.TryGetCurrentStep(generation, out _)
            .Should().Be(NavigationAStarQueryStatus.Success);
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (guide.TryGetCurrentStep(generation, out _)
                != NavigationAStarQueryStatus.Success)
            {
                throw new InvalidOperationException("The active transition step became unavailable.");
            }
        }
        long allocationAfter = GC.GetAllocatedBytesForCurrentThread();
        (allocationAfter - allocationBefore).Should().Be(0);
        secondGuide.TryAdvanceWaypoint(secondGeneration)
            .Should().Be(NavigationAStarQueryStatus.Success);
        secondGuide.TryGetCurrentStep(
                secondGeneration,
                out NavigationGuideStep secondTransitionStep)
            .Should().Be(NavigationAStarQueryStatus.Success);
        secondPublicGuide.CompletePendingTransition(instruction)
            .Should().Be(NavigationGuideStatus.Stale,
                "an instruction is owned by the exact producing guide lease");
        secondGuide.GetCurrentWaypointOrdinal(secondGeneration).Should().Be(1);
        publicGuide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale,
            "the copied public lease observes the exact owner status");
        guide.TryAdvanceWaypoint(generation).Should().Be(NavigationAStarQueryStatus.Pending);
        guide.GetCurrentWaypointOrdinal(generation).Should().Be(1);
        guide.CompletePendingTransition(
                generation,
                search.Result.TransitionInstructions[0])
            .Should().Be(NavigationAStarQueryStatus.Stale,
                "the immutable cached instruction has no lease completion stamp");
        guide.GetCurrentWaypointOrdinal(generation).Should().Be(1);

        publicGuide.CompletePendingTransition(instruction)
            .Should().Be(NavigationGuideStatus.Success);
        publicGuide.TryGetCurrentStep(out NavigationGuideStep destinationStep)
            .Should().Be(NavigationGuideStatus.Success);
        destinationStep.HasTransition.Should().BeFalse();
        destinationStep.Address.Should().Be(
            new NavigationCellAddress("map", targetIndex));
        destinationStep.Position.Should().Be(end);
        destinationStep.Medium.Should().Be(TraversalMedium.Gas);
        secondGuide.CompletePendingTransition(
                secondGeneration,
                secondTransitionStep.Transition)
            .Should().Be(NavigationAStarQueryStatus.Success);
        guide.CompletePendingTransition(generation, instruction)
            .Should().Be(NavigationAStarQueryStatus.Stale,
                "one exact instruction can complete only once");
        secondGuide.Dispose(secondGeneration);
        guide.Dispose(generation);
        guide.TryGetCurrentStep(generation, out _)
            .Should().Be(NavigationAStarQueryStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.TryCheckout(
                search.Result.Key,
                graph,
                out NavigationAStarPayloadLease recycledPayloadLease)
            .Should().BeTrue();
        cache.TryCreateGuide(
                store,
                recycledPayloadLease,
                out NavigationAStarGuideLease? recycledGuide)
            .Should().Be(NavigationAStarQueryStatus.Success);
        recycledGuide.Should().BeSameAs(guide);
        long recycledGeneration = recycledGuide!.Generation;
        recycledGeneration.Should().BeGreaterThan(generation);
        recycledGuide.TryAdvanceWaypoint(recycledGeneration)
            .Should().Be(NavigationAStarQueryStatus.Success);
        recycledGuide.TryGetCurrentStep(
                recycledGeneration,
                out NavigationGuideStep recycledTransitionStep)
            .Should().Be(NavigationAStarQueryStatus.Success);
        recycledGuide.CompletePendingTransition(recycledGeneration, instruction)
            .Should().Be(NavigationAStarQueryStatus.Stale,
                "a pooled lease rejects an instruction from its prior generation");
        recycledGuide.CompletePendingTransition(
                recycledGeneration,
                recycledTransitionStep.Transition)
            .Should().Be(NavigationAStarQueryStatus.Success);
        recycledGuide.Dispose(recycledGeneration);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Flow_ShouldKeepEveryTargetMediumAsAnUnparentedZeroCostSeed()
    {
        VoxelIndex unused = default;
        var targetIndex = new VoxelIndex(2, 0, 0);
        var targetAddress = new NavigationCellAddress("map", targetIndex);
        TraversalTransitionDefinition[] transitions =
        {
            new(
                "gas-to-liquid",
                TraversalTransitionType.Custom,
                targetIndex,
                TraversalMedium.Gas,
                targetAddress,
                TraversalMedium.Liquid),
            new(
                "liquid-to-gas",
                TraversalTransitionType.Custom,
                targetIndex,
                TraversalMedium.Liquid,
                targetAddress,
                TraversalMedium.Gas)
        };
        using TrailblazerWorldContext context = CreateTransitionContext(
            unused,
            Cell(TraversalMedia.Gas),
            targetIndex,
            Cell(TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid),
            transitions);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        graph.TryGetNodeState(target, TraversalMedium.Liquid, out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                targetAddress,
                TraversalMedium.Liquid,
                out _,
                out _)
            .Should().BeTrue();
        Vector3d anchor = GetVolumeAnchor(graph, target, Fixed64.One);
        PathQuery astar = Query(
            anchor,
            anchor,
            TraversalMedia.Gas | TraversalMedia.Liquid,
            allowTransitions: true);
        var query = new PathQuery(
            astar.Start,
            astar.End,
            astar.Agent,
            astar.AreaPolicy,
            astar.Traversal,
            PathAlgorithm.FlowField,
            astar.Budget,
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            store.TryAcquire()!,
            query,
            new NavigationResolvedEndpoint(
                target,
                targetAddress,
                TraversalMedia.Gas | TraversalMedia.Liquid,
                TraversalMedium.Gas,
                anchor,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                targetAddress,
                TraversalMedia.Gas | TraversalMedia.Liquid,
                TraversalMedium.Gas,
                anchor,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Gas,
            TraversalMedia.Gas | TraversalMedia.Liquid,
            new NavigationWorkMeter(query.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        var workspace = new NavigationFlowFieldWorkspace(1, 4, 4, 2, 128, 16);
        using var work = new NavigationFlowFieldWork(
            context.World,
            resolved,
            workspace);
        for (int step = 0;
            step < 256 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(64, 64, 64, 64);
        }

        work.Status.Should().Be(NavigationFlowFieldStatus.Success);
        work.Result!.WorldChangeSequence.Should().Be(context.World.ChangeSequence);
        work.Result!.TryGetNode(
                targetAddress,
                TraversalMedium.Gas,
                out NavigationFlowFieldNode gas)
            .Should().BeTrue();
        work.Result.TryGetNode(
                targetAddress,
                TraversalMedium.Liquid,
                out NavigationFlowFieldNode liquid)
            .Should().BeTrue();
        gas.IntegrationCost.Should().Be(Fixed64.Zero);
        liquid.IntegrationCost.Should().Be(Fixed64.Zero);
        gas.SelectedEdge.IsValid.Should().BeFalse();
        liquid.SelectedEdge.IsValid.Should().BeFalse();

        Vector3d solidAnchor = GetGuideAnchor(
            graph,
            target,
            TraversalMedium.Solid);
        PathQuery exactAStar = Query(
            solidAnchor,
            solidAnchor,
            TraversalMedia.Solid | TraversalMedia.Gas,
            allowTransitions: true);
        var exactQuery = new PathQuery(
            exactAStar.Start,
            exactAStar.End,
            exactAStar.Agent,
            exactAStar.AreaPolicy,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.FlowField,
            exactAStar.Budget,
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
        var exactResolved = new NavigationResolvedPathQuery();
        exactResolved.Bind(
            store.TryAcquire()!,
            exactQuery,
            new NavigationResolvedEndpoint(
                target,
                targetAddress,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                solidAnchor,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                targetAddress,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                solidAnchor,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            TraversalMedia.Solid | TraversalMedia.Gas,
            new NavigationWorkMeter(exactQuery.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        using var exactWork = new NavigationFlowFieldWork(
            context.World,
            exactResolved,
            new NavigationFlowFieldWorkspace(1, 4, 4, 1, 128, 16));
        for (int step = 0;
            step < 256 && exactWork.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            exactWork.Advance(64, 64, 64, 64);
        }

        exactWork.Status.Should().Be(NavigationFlowFieldStatus.Success,
            "only the endpoint media qualified by admission consumes a target seed slot");
        exactWork.Result!.Nodes.Should().ContainSingle();
        exactWork.Result.Nodes[0].Medium.Should().Be(TraversalMedium.Solid);
        exactWork.Result.Key.TargetMedia.Should().Be(
            TraversalMedia.Solid | TraversalMedia.Gas,
            "the key retains the requested target mask for negative evidence");
    }

    [Fact]
    public void FlowGuide_ShouldSampleTheExactSelectedVolumeShortcut()
    {
        using TrailblazerWorldContext context = CreateShortcutContext(
            TraversalMedium.Gas);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 4);
        NavigationNodeRef source = Resolve(graph, default);
        NavigationNodeRef target = Resolve(graph, new VoxelIndex(1, 0, 1));
        var sourceAddress = new NavigationCellAddress("map", default);
        var targetAddress = new NavigationCellAddress("map", new VoxelIndex(1, 0, 1));
        Vector3d start = GetVolumeAnchor(graph, source, Fixed64.One);
        Vector3d end = GetVolumeAnchor(graph, target, Fixed64.One);
        PathQuery astar = Query(
            start,
            end,
            TraversalMedia.Gas,
            allowTransitions: false);
        var query = new PathQuery(
            astar.Start,
            astar.End,
            astar.Agent,
            astar.AreaPolicy,
            astar.Traversal,
            PathAlgorithm.FlowField,
            astar.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Zero));
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            store.TryAcquire()!,
            query,
            new NavigationResolvedEndpoint(
                source,
                sourceAddress,
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                start,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                targetAddress,
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                end,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Gas,
            TraversalMedia.Gas,
            new NavigationWorkMeter(query.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        var workspace = new NavigationFlowFieldWorkspace(1, 16, 16, 8, 128, 32);
        using var work = new NavigationFlowFieldWork(context.World, resolved, workspace);
        for (int step = 0;
            step < 512 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(128, 128, 128, 128);
        }
        work.Status.Should().Be(NavigationFlowFieldStatus.Success);
        work.Result!.TryGetNode(
                sourceAddress,
                TraversalMedium.Gas,
                out NavigationFlowFieldNode sourceNode)
            .Should().BeTrue();
        sourceNode.IntegrationCost.Should().Be(
            Fixed64.FromRaw(42_212_773_072L),
            "reverse Flow must select the same shortcut cost as forward A*");
        using var cache = new NavigationFlowFieldPayloadCache(
            context.World,
            maxEntries: 1,
            maxReusableBytes: work.Result!.RetainedBytes,
            maxSinglePayloadBytes: work.Result.RetainedBytes,
            maxActivePayloadBytes: work.Result.RetainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 4,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                work.Result.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                store,
                work.Result,
                sourceAddress,
                ref reservation,
                out NavigationFlowFieldPayloadLease payloadLease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(sourceAddress, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldGuideLease inner = GetInner(guide);

        Sample(
                inner,
                inner.Generation,
                start,
                GenerousSampleBudget,
                out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Success);

        sample.HasTransition.Should().BeFalse();
        sample.Medium.Should().Be(TraversalMedium.Gas);
        sample.Target.Should().Be(end);
        sample.Heading.Should().Be((end - start).Normalized);
        var measured = new GuideSampleWorkMeter(GenerousSampleBudget);
        inner.TrySample(
                inner.Generation,
                start,
                ref measured,
                out NavigationFlowSample _)
            .Should().Be(NavigationGuideStatus.Success);
        int lookups = 256 - measured.GetCurrentNodeLookupAllowance();
        int legs = 256 - measured.GetCursorLegScanAllowance();
        int portals = 64 - measured.GetPortalCheckAllowance();
        int prisms = 64 - measured.GetPrismCheckAllowance();
        int traces = 256 - measured.GetTraceIntervalAllowance();
        traces.Should().BeGreaterThan(0);
        var exactBudget = new GuideSampleWorkBudget(
            lookups,
            legs,
            maxCursorRebases: 0,
            portals,
            prisms,
            traces,
            maxLocalRecoveryAttempts: 0);
        var shortBudget = new GuideSampleWorkBudget(
            lookups,
            legs,
            maxCursorRebases: 0,
            portals,
            prisms,
            traces - 1,
            maxLocalRecoveryAttempts: 0);

        Sample(
                inner,
                inner.Generation,
                start,
                exactBudget,
                out NavigationFlowSample _)
            .Should().Be(NavigationGuideStatus.Success);
        Sample(
                inner,
                inner.Generation,
                start,
                shortBudget,
                out NavigationFlowSample _)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        Vector3d offCenter = start + new Vector3d(
            Fixed64.Zero,
            Fixed64.Zero,
            (Fixed64)1 / (Fixed64)4);
        Sample(
                inner,
                inner.Generation,
                offCenter,
                GenerousSampleBudget,
                out NavigationFlowSample rejoined)
            .Should().Be(NavigationGuideStatus.Success);
        rejoined.Medium.Should().Be(TraversalMedium.Gas);
        rejoined.Target.Should().Be(end);
        for (int i = 0; i < 8; i++)
        {
            Sample(
                    inner,
                    inner.Generation,
                    start,
                    GenerousSampleBudget,
                    out NavigationFlowSample _)
                .Should().Be(NavigationGuideStatus.Success);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool sampled = true;
        for (int i = 0; i < 64; i++)
        {
            if (Sample(
                    inner,
                    inner.Generation,
                    start,
                    GenerousSampleBudget,
                    out NavigationFlowSample _) != NavigationGuideStatus.Success)
            {
                sampled = false;
                break;
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        sampled.Should().BeTrue();
        allocated.Should().Be(0);
        guide.Dispose();
    }

    [Fact]
    public void Flow_ShouldRetainTheExactSelectedTransitionAction()
    {
        VoxelIndex sourceIndex = default;
        var targetIndex = new VoxelIndex(2, 0, 0);
        var transition = new TraversalTransitionDefinition(
            "takeoff",
            TraversalTransitionType.Takeoff,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Gas,
            actionCost: (Fixed64)5,
            locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);
        using TrailblazerWorldContext context = CreateTransitionContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Gas, (Fixed64)7),
            transition);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        NavigationNodeRef source = Resolve(graph, sourceIndex);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        var sourceAddress = new NavigationCellAddress("map", sourceIndex);
        var targetAddress = new NavigationCellAddress("map", targetIndex);
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Solid);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Gas);
        PathQuery astar = Query(
            start,
            end,
            TraversalMedia.Solid | TraversalMedia.Gas,
            allowTransitions: true);
        var query = new PathQuery(
            astar.Start,
            astar.End,
            astar.Agent,
            astar.AreaPolicy,
            astar.Traversal,
            PathAlgorithm.FlowField,
            astar.Budget,
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            store.TryAcquire()!,
            query,
            new NavigationResolvedEndpoint(
                source,
                sourceAddress,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                start,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                targetAddress,
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                end,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            TraversalMedia.Gas,
            new NavigationWorkMeter(query.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        var workspace = new NavigationFlowFieldWorkspace(1, 8, 8, 4, 128, 16);
        using var work = new NavigationFlowFieldWork(
            context.World,
            resolved,
            workspace);
        for (int step = 0;
            step < 256 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(64, 64, 64, 64);
        }

        work.Status.Should().Be(NavigationFlowFieldStatus.Success);
        work.Result!.TryGetNode(
                sourceAddress,
                TraversalMedium.Solid,
                out NavigationFlowFieldNode sourceNode)
            .Should().BeTrue();
        sourceNode.IntegrationCost.Should().Be((Fixed64)12);
        sourceNode.SelectedEdge.Target.Should().Be(targetAddress);
        sourceNode.SelectedEdge.TargetMedium.Should().Be(TraversalMedium.Gas);
        sourceNode.TransitionInstructionOrdinal.Should().Be(0);
        work.Result.TransitionInstructions.Should().ContainSingle();
        NavigationTransitionInstruction instruction =
            work.Result.TransitionInstructions[0];
        instruction.IdentityKind.Should().Be(NavigationTransitionIdentityKind.Definition);
        instruction.OwnerMapId.Should().Be("map");
        instruction.Id.Should().Be("takeoff");
        instruction.Type.Should().Be(TraversalTransitionType.Takeoff);
        instruction.SourceAddress.Should().Be(sourceAddress);
        instruction.DestinationAddress.Should().Be(targetAddress);
        instruction.SourceMedium.Should().Be(TraversalMedium.Solid);
        instruction.DestinationMedium.Should().Be(TraversalMedium.Gas);
        instruction.SourcePosition.Should().Be(start);
        instruction.DestinationPosition.Should().Be(end);
        instruction.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb);
        using var flowCache = new NavigationFlowFieldPayloadCache(
            context.World,
            maxEntries: 1,
            maxReusableBytes: work.Result.RetainedBytes,
            maxSinglePayloadBytes: work.Result.RetainedBytes,
            maxActivePayloadBytes: work.Result.RetainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 4,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        flowCache.TryReservePayload(
                work.Result.RetainedBytes,
                out NavigationFlowFieldReservation flowReservation)
            .Should().BeTrue();
        flowCache.TryPublishOrPromote(
                store,
                work.Result,
                sourceAddress,
                ref flowReservation,
                out NavigationFlowFieldPayloadLease flowPayloadLease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        flowCache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(sourceAddress, flowPayloadLease),
                out NavigationFlowFieldLease flowGuide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldGuideLease inner = GetInner(flowGuide);
        ulong flowGeneration = inner.Generation;

        var noLookupBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: 0,
            maxCursorLegScans: 256,
            maxCursorRebases: 16,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 256,
            maxLocalRecoveryAttempts: 4);
        Vector3d displaced = start + new Vector3d(
            Fixed64.Zero,
            Fixed64.Zero,
            (Fixed64)1 / (Fixed64)4);
        var measuredLookup = new GuideSampleWorkMeter(GenerousSampleBudget);
        inner.TrySample(
                flowGeneration,
                displaced,
                ref measuredLookup,
                out NavigationFlowSample measuredApproach)
            .Should().Be(NavigationGuideStatus.Success);
        measuredApproach.HasTransition.Should().BeFalse();
        int currentNodeLookups = 256
            - measuredLookup.GetCurrentNodeLookupAllowance();
        currentNodeLookups.Should().BeGreaterThan(0);
        var exactLookupBudget = new GuideSampleWorkBudget(
            currentNodeLookups,
            maxCursorLegScans: 256,
            maxCursorRebases: 16,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 256,
            maxLocalRecoveryAttempts: 4);
        Sample(
                inner,
                flowGeneration,
                displaced,
                exactLookupBudget,
                out NavigationFlowSample _)
            .Should().Be(NavigationGuideStatus.Success);
        var oneBelowLookupBudget = new GuideSampleWorkBudget(
            currentNodeLookups - 1,
            maxCursorLegScans: 256,
            maxCursorRebases: 16,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 256,
            maxLocalRecoveryAttempts: 4);
        Sample(
                inner,
                flowGeneration,
                displaced,
                oneBelowLookupBudget,
                out NavigationFlowSample _)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        Sample(
                inner,
                flowGeneration,
                start,
                noLookupBudget,
                out NavigationFlowSample blocked)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        blocked.Should().Be(default(NavigationFlowSample));
        Sample(
                inner,
                flowGeneration,
                displaced,
                GenerousSampleBudget,
                out NavigationFlowSample approach)
            .Should().Be(NavigationGuideStatus.Success);
        approach.HasTransition.Should().BeFalse();
        approach.Target.Should().Be(start);
        approach.Heading.Should().Be((start - displaced).Normalized);
        Sample(
                inner,
                flowGeneration,
                start,
                oneBelowLookupBudget,
                out NavigationFlowSample _)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);

        var exactTransitionLookupBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: currentNodeLookups,
            maxCursorLegScans: 256,
            maxCursorRebases: 16,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 256,
            maxLocalRecoveryAttempts: 4);
        flowGuide.TrySample(
                start,
                exactTransitionLookupBudget,
                out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Success);

        sample.HasTransition.Should().BeTrue();
        sample.Heading.Should().Be(Vector3d.Zero);
        sample.Target.Should().Be(start);
        sample.Medium.Should().Be(TraversalMedium.Solid);
        sample.Transition.Id.Should().Be("takeoff");
        flowGuide.TrySample(
                end,
                GenerousSampleBudget,
                out NavigationFlowSample pending)
            .Should().Be(NavigationGuideStatus.Success);
        pending.HasTransition.Should().BeTrue();
        pending.Heading.Should().Be(Vector3d.Zero);
        pending.Target.Should().Be(start);
        pending.Transition.Should().Be(sample.Transition,
            "a pending action remains the same occurrence until completion");
        flowCache.TryCheckout(
                store,
                store.Current,
                work.Result.Key,
                sourceAddress,
                out NavigationFlowFieldPayloadLease secondPayloadLease,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        flowCache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(sourceAddress, secondPayloadLease),
                out NavigationFlowFieldLease secondFlowGuide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldGuideLease secondInner = GetInner(secondFlowGuide);
        secondFlowGuide.TrySample(
                start,
                GenerousSampleBudget,
                out NavigationFlowSample secondSample)
            .Should().Be(NavigationGuideStatus.Success);

        secondFlowGuide.CompletePendingTransition(sample.Transition)
            .Should().Be(NavigationGuideStatus.Stale,
                "an instruction belongs to the exact producing lease");
        flowGuide.CompletePendingTransition(sample.Transition)
            .Should().Be(NavigationGuideStatus.Success);
        flowGuide.CompletePendingTransition(sample.Transition)
            .Should().Be(NavigationGuideStatus.Stale,
                "the completed sample ordinal cannot be replayed");
        secondFlowGuide.CompletePendingTransition(secondSample.Transition)
            .Should().Be(NavigationGuideStatus.Success);
        SetPrivateField(secondInner, "_currentSource", sourceAddress);
        SetPrivateField(secondInner, "_currentMedium", TraversalMedium.Solid);
        SetPrivateField(secondInner, "_hasPendingTransition", false);
        SetPrivateField(secondInner, "_sampleOrdinal", long.MaxValue);
        Sample(
                secondInner,
                secondInner.Generation,
                start,
                GenerousSampleBudget,
                out NavigationFlowSample exhausted)
            .Should().Be(NavigationGuideStatus.Stale,
                "completion ordinals retire before a same-lease wrap");
        exhausted.Should().Be(default(NavigationFlowSample));
        secondFlowGuide.Dispose();
        flowGuide.Dispose();

        flowCache.TryCheckout(
                store,
                store.Current,
                work.Result.Key,
                sourceAddress,
                out NavigationFlowFieldPayloadLease racePayloadLease,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        flowCache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(sourceAddress, racePayloadLease),
                out NavigationFlowFieldLease raceGuide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldGuideLease raceInner = GetInner(raceGuide);
        object guideSync = GetPrivateField<object>(raceInner, "_sync");
        object raySync = flowCache.ImmediateRayWorkspace.SyncRoot;
        NavigationGuideStatus raceStatus = NavigationGuideStatus.Success;
        NavigationFlowSample raceSample = default;
        Exception? sampleError = null;
        using var started = new ManualResetEventSlim();
        var sampleThread = new Thread(() =>
        {
            started.Set();
            try
            {
                raceStatus = Sample(
                    raceInner,
                    raceInner.Generation,
                    start,
                    GenerousSampleBudget,
                    out raceSample);
            }
            catch (Exception error)
            {
                sampleError = error;
            }
        })
        {
            IsBackground = true
        };
        Monitor.Enter(raySync);
        try
        {
            sampleThread.Start();
            started.Wait(5_000, TestContext.Current.CancellationToken)
                .Should().BeTrue();
            SpinWait.SpinUntil(() =>
            {
                if (!Monitor.TryEnter(guideSync))
                    return true;
                Monitor.Exit(guideSync);
                return false;
            }, 5_000).Should().BeTrue();
            VoxelGrid grid = context.World.ActiveGrids[0];
            grid.TryGetVoxel(sourceIndex, out Voxel? voxel).Should().BeTrue();
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken())
                .Should().BeTrue();
        }
        finally
        {
            Monitor.Exit(raySync);
        }
        sampleThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        sampleError.Should().BeNull();
        raceStatus.Should().Be(NavigationGuideStatus.Stale);
        raceSample.Should().Be(default(NavigationFlowSample));
        raceInner.GetStatus(raceInner.Generation)
            .Should().Be(NavigationGuideStatus.Stale,
                "ray staleness remains sticky for the lease generation");
        raceGuide.Dispose();
    }

    [Theory]
    [InlineData(TraversalMedium.Solid, TraversalMedium.Gas, "takeoff")]
    [InlineData(TraversalMedium.Gas, TraversalMedium.Solid, "land")]
    public void FlowGuide_ShouldHandMovementOffToTheSelectedTransition(
        TraversalMedium movementMedium,
        TraversalMedium destinationMedium,
        string transitionId)
    {
        using TrailblazerWorldContext context = CreateMovementTransitionContext(
            movementMedium,
            destinationMedium,
            transitionId);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var destinationIndex = default(VoxelIndex);
        var actionIndex = new VoxelIndex(1, 0, 0);
        var startIndex = new VoxelIndex(2, 0, 0);
        NavigationNodeRef destinationNode = Resolve(graph, destinationIndex);
        NavigationNodeRef actionNode = Resolve(graph, actionIndex);
        NavigationNodeRef startNode = Resolve(graph, startIndex);
        Vector3d destination = GetGuideAnchor(
            graph,
            destinationNode,
            destinationMedium);
        Vector3d action = GetGuideAnchor(graph, actionNode, movementMedium);
        Vector3d start = GetGuideAnchor(graph, startNode, movementMedium);
        TraversalMedia media = NavigationCell.ToMedia(movementMedium)
            | NavigationCell.ToMedia(destinationMedium);
        PathQuery astar = Query(start, destination, media, allowTransitions: true);
        var query = new PathQuery(
            astar.Start,
            astar.End,
            astar.Agent,
            astar.AreaPolicy,
            astar.Traversal,
            PathAlgorithm.FlowField,
            astar.Budget,
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var startAddress = new NavigationCellAddress("map", startIndex);
        var destinationAddress = new NavigationCellAddress("map", destinationIndex);
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            store.TryAcquire()!,
            query,
            new NavigationResolvedEndpoint(
                startNode,
                startAddress,
                NavigationCell.ToMedia(movementMedium),
                movementMedium,
                start,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                destinationNode,
                destinationAddress,
                NavigationCell.ToMedia(destinationMedium),
                destinationMedium,
                destination,
                Fixed64.Zero),
            policy!,
            movementMedium,
            NavigationCell.ToMedia(destinationMedium),
            new NavigationWorkMeter(query.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: movementMedium != TraversalMedium.Solid
                || destinationMedium != TraversalMedium.Solid);
        using var work = new NavigationFlowFieldWork(
            context.World,
            resolved,
            new NavigationFlowFieldWorkspace(1, 16, 16, 8, 128, 32));
        for (int step = 0;
            step < 512 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(64, 64, 64, 64);
        }
        work.Status.Should().Be(NavigationFlowFieldStatus.Success);
        work.Result!.TryGetNode(
                startAddress,
                movementMedium,
                out NavigationFlowFieldNode startFlowNode)
            .Should().BeTrue();
        startFlowNode.TransitionInstructionOrdinal.Should().Be(-1);
        work.Result.TryGetNode(
                new NavigationCellAddress("map", actionIndex),
                movementMedium,
                out NavigationFlowFieldNode actionFlowNode)
            .Should().BeTrue();
        actionFlowNode.TransitionInstructionOrdinal.Should().Be(0);

        using var cache = new NavigationFlowFieldPayloadCache(
            context.World,
            maxEntries: 1,
            maxReusableBytes: work.Result.RetainedBytes,
            maxSinglePayloadBytes: work.Result.RetainedBytes,
            maxActivePayloadBytes: work.Result.RetainedBytes,
            maxActiveLeases: 2,
            guideMapCapacity: 4,
            immediateRayWorkspace:
                NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());
        cache.TryReservePayload(
                work.Result.RetainedBytes,
                out NavigationFlowFieldReservation reservation)
            .Should().BeTrue();
        cache.TryPublishOrPromote(
                store,
                work.Result,
                startAddress,
                ref reservation,
                out NavigationFlowFieldPayloadLease payloadLease)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(startAddress, payloadLease),
                out NavigationFlowFieldLease guide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldGuideLease inner = GetInner(guide);
        cache.TryCheckout(
                store,
                store.Current,
                work.Result.Key,
                startAddress,
                out NavigationFlowFieldPayloadLease measurementLease,
                out _)
            .Should().Be(NavigationFlowFieldStatus.Success);
        cache.TryCreateGuide(
                store,
                new NavigationFlowQueryResult(startAddress, measurementLease),
                out NavigationFlowFieldLease measurementGuide)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldGuideLease measurementInner = GetInner(measurementGuide);
        var measurementMeter = new GuideSampleWorkMeter(GenerousSampleBudget);
        measurementInner.TrySample(
                measurementInner.Generation,
                action,
                ref measurementMeter,
                out NavigationFlowSample measured)
            .Should().Be(NavigationGuideStatus.Success);
        measured.HasTransition.Should().BeTrue();
        int actionLookups = 256
            - measurementMeter.GetCurrentNodeLookupAllowance();
        actionLookups.Should().BeGreaterThan(1);
        measurementGuide.Dispose();
        var oneBelowActionBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: actionLookups - 1,
            maxCursorLegScans: 256,
            maxCursorRebases: 16,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 256,
            maxLocalRecoveryAttempts: 4);
        Sample(
                inner,
                inner.Generation,
                action,
                oneBelowActionBudget,
                out NavigationFlowSample blocked)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        GetPrivateField<NavigationCellAddress>(inner, "_currentSource")
            .Should().Be(startAddress,
                "a failed action approach cannot commit its rebase");
        GetPrivateField<long>(inner, "_sampleOrdinal").Should().Be(0);
        var exactActionBudget = new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: actionLookups,
            maxCursorLegScans: 256,
            maxCursorRebases: 16,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 256,
            maxLocalRecoveryAttempts: 4);
        Sample(
                inner,
                inner.Generation,
                action,
                exactActionBudget,
                out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Success);
        sample.HasTransition.Should().BeTrue(
            "progress rebased onto the action node and must stop at its barrier");
        sample.Medium.Should().Be(movementMedium);
        sample.Target.Should().Be(action);
        sample.Transition.Id.Should().Be(transitionId);
        guide.Dispose();
    }

    [Fact]
    public void AStar_ShouldKeepSameMediumTransitionAsASimplificationBarrier()
    {
        var sourceIndex = default(VoxelIndex);
        var targetIndex = new VoxelIndex(2, 0, 0);
        var transition = new TraversalTransitionDefinition(
            "jump",
            TraversalTransitionType.Jump,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            TraversalMedium.Solid,
            actionCost: (Fixed64)5);
        using TrailblazerWorldContext context = CreateTransitionContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Solid, (Fixed64)7),
            transition);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, sourceIndex);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Solid);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Solid);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(start, end, TraversalMedia.Solid, allowTransitions: true),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        AdvanceAdmission(admission);
        NavigationWorkMeter meter = admission.Result.Meter;
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        AdvanceSearch(search);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.TransitionInstructions.Should().ContainSingle();
        search.Result.GuidePoints.Should().ContainSingle(point => point.HasTransition);
        meter.SimplificationRays.Should().Be(0,
            "a semantic action is a hard same-medium simplification barrier");
        workspace.NodeTable.TryGetSlot(
                new NavigationMediumStateRef(target, TraversalMedium.Solid),
                out int targetSlot)
            .Should().BeTrue();
        workspace.NodeTable.GetRecord(targetSlot).Parent.Should().Be(
            new NavigationMediumStateRef(source, TraversalMedium.Solid));
        workspace.NodeTable.GetRecord(targetSlot).ParentEdgeKind.Should().Be(
            NavigationTraversalEdgeKind.Transition);
    }

    [Fact]
    public void AStar_ShouldChooseCanonicalMediumAcrossEqualCostTransitions()
    {
        var sourceIndex = default(VoxelIndex);
        var targetIndex = new VoxelIndex(2, 0, 0);
        TraversalTransitionDefinition Transition(
            string id,
            TraversalMedium destinationMedium) => new(
            id,
            TraversalTransitionType.Takeoff,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", targetIndex),
            destinationMedium,
            actionCost: (Fixed64)5);
        using TrailblazerWorldContext context = CreateTransitionContext(
            sourceIndex,
            Cell(TraversalMedia.Solid),
            targetIndex,
            Cell(TraversalMedia.Gas | TraversalMedia.Liquid, (Fixed64)7),
            Transition("z-liquid", TraversalMedium.Liquid),
            Transition("a-gas", TraversalMedium.Gas));
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, sourceIndex);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Solid);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Gas);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(
                start,
                end,
                TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
                allowTransitions: true),
            TraversalMedium.Solid,
            TraversalMedia.Gas | TraversalMedia.Liquid);
        AdvanceAdmission(admission);
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        AdvanceSearch(search);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Cost.Should().Be((Fixed64)12);
        search.Result.TransitionInstructions.Should().ContainSingle();
        search.Result.TransitionInstructions[0].Id.Should().Be("a-gas");
        search.Result.TransitionInstructions[0].DestinationMedium.Should().Be(
            TraversalMedium.Gas);
        search.Result.GuidePoints[^1].Medium.Should().Be(TraversalMedium.Gas);
    }

    [Fact]
    public void AStar_ShouldRetainExactRuleActionInstruction()
    {
        var sourceIndex = default(VoxelIndex);
        var targetIndex = new VoxelIndex(1, 0, 0);
        var rule = new TraversalTransitionRule(
            "gas-to-liquid",
            TraversalTransitionType.SwimEntry,
            TraversalMedium.Gas,
            TraversalMedium.Liquid,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            actionCost: (Fixed64)5,
            TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);
        using TrailblazerWorldContext context = CreateTransitionContext(
            sourceIndex,
            Cell(TraversalMedia.Gas),
            targetIndex,
            Cell(TraversalMedia.Liquid, (Fixed64)7),
            Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, sourceIndex);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Gas);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Liquid);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(
                start,
                end,
                TraversalMedia.Gas | TraversalMedia.Liquid,
                allowTransitions: true),
            TraversalMedium.Gas,
            TraversalMedia.Liquid);
        AdvanceAdmission(admission);
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        AdvanceSearch(search);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.TransitionInstructions.Should().ContainSingle();
        NavigationTransitionInstruction instruction =
            search.Result.TransitionInstructions[0];
        NavigationDistanceMath.TryCeiling(
                start,
                instruction.SourcePosition,
                out Fixed64 sourceDistance)
            .Should().BeTrue();
        NavigationDistanceMath.TryCeiling(
                instruction.DestinationPosition,
                end,
                out Fixed64 destinationDistance)
            .Should().BeTrue();
        search.Result.Cost.Should().Be(
            sourceDistance + (Fixed64)5 + destinationDistance + (Fixed64)7);
        instruction.IdentityKind.Should().Be(NavigationTransitionIdentityKind.Rule);
        instruction.OwnerMapId.Should().BeEmpty();
        instruction.Id.Should().Be("gas-to-liquid");
        instruction.SourcePosition.Should().NotBe(start);
        instruction.DestinationPosition.Should().NotBe(end);
        instruction.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);

        var flowQuery = new PathQuery(
            admission.Result.Query.Start,
            admission.Result.Query.End,
            admission.Result.Query.Agent,
            admission.Result.Query.AreaPolicy,
            admission.Result.Query.Traversal,
            PathAlgorithm.FlowField,
            admission.Result.Query.Budget,
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
        var flowResolved = new NavigationResolvedPathQuery();
        flowResolved.Bind(
            store.TryAcquire()!,
            flowQuery,
            new NavigationResolvedEndpoint(
                source,
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                start,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                new NavigationCellAddress("map", targetIndex),
                TraversalMedia.Liquid,
                TraversalMedium.Liquid,
                end,
                Fixed64.Zero),
            admission.Result.AreaPolicy,
            TraversalMedium.Gas,
            TraversalMedia.Liquid,
            new NavigationWorkMeter(flowQuery.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        var flowWorkspace = new NavigationFlowFieldWorkspace(
            1,
            16,
            16,
            8,
            128,
            32);
        using var flow = new NavigationFlowFieldWork(
            context.World,
            flowResolved,
            flowWorkspace);
        for (int step = 0;
            step < 512 && flow.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            flow.Advance(128, 128, 128, 128);
        }

        flow.Status.Should().Be(NavigationFlowFieldStatus.Success);
        flow.Result!.TryGetNode(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Gas,
                out NavigationFlowFieldNode flowSource)
            .Should().BeTrue();
        flowSource.IntegrationCost.Should().Be(search.Result.Cost);
        flowSource.TransitionInstructionOrdinal.Should().Be(0);
        flow.Result.TransitionInstructions.Should().ContainSingle();
        NavigationTransitionInstruction flowInstruction =
            flow.Result.TransitionInstructions[0];
        flowInstruction.IdentityKind.Should().Be(instruction.IdentityKind);
        flowInstruction.Id.Should().Be(instruction.Id);
        flowInstruction.SourcePosition.Should().Be(instruction.SourcePosition);
        flowInstruction.DestinationPosition.Should().Be(instruction.DestinationPosition);
        flowInstruction.LocomotionHints.Should().Be(instruction.LocomotionHints);
    }

    [Fact]
    public void NoPath_ShouldIncludeDormantRuleDependencies()
    {
        var sourceIndex = default(VoxelIndex);
        var targetIndex = new VoxelIndex(1, 0, 0);
        var rule = new TraversalTransitionRule(
            "dormant-rule",
            TraversalTransitionType.SwimEntry,
            TraversalMedium.Gas,
            TraversalMedium.Liquid,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.None,
            actionCost: Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        using TrailblazerWorldContext context = CreateTransitionContext(
            sourceIndex,
            Cell(TraversalMedia.Gas),
            targetIndex,
            Cell(TraversalMedia.Solid),
            Array.Empty<TraversalTransitionDefinition>(),
            new[] { rule });
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationNodeRef source = Resolve(graph, sourceIndex);
        NavigationNodeRef target = Resolve(graph, targetIndex);
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Gas);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Solid);
        GraphDependencyStamp dependencies;
        GraphDependencyStamp flowDependencies;
        using (var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar))
        {
            admission.Begin(
                store.TryAcquire()!,
                Query(
                    start,
                    end,
                    TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
                    allowTransitions: true),
                TraversalMedium.Gas,
                TraversalMedia.Solid | TraversalMedia.Liquid);
            AdvanceAdmission(admission);
            using var search = new NavigationSurfaceAStarWork(
                context.World,
                store,
                admission.Result,
                workspace,
                admission.RayWork,
                long.MaxValue);

            AdvanceSearch(search);

            search.Status.Should().Be(NavigationSurfaceAStarStatus.NoPath);
            dependencies = search.Result.Dependencies;
            dependencies.HasTransitionRuleDependency.Should().BeTrue();
            graph.IsDependencyCurrent(dependencies).Should().BeTrue();
        }

        PathQuery flowSourceQuery = Query(
            start,
            end,
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
            allowTransitions: true);
        var flowQuery = new PathQuery(
            flowSourceQuery.Start,
            flowSourceQuery.End,
            flowSourceQuery.Agent,
            flowSourceQuery.AreaPolicy,
            flowSourceQuery.Traversal,
            PathAlgorithm.FlowField,
            flowSourceQuery.Budget,
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
        graph.AreaCatalog.TryGet(
                flowQuery.AreaPolicy,
                out NavigationAreaPolicy? flowPolicy)
            .Should().BeTrue();
        var flowResolved = new NavigationResolvedPathQuery();
        flowResolved.Bind(
            store.TryAcquire()!,
            flowQuery,
            new NavigationResolvedEndpoint(
                source,
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                start,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                new NavigationCellAddress("map", targetIndex),
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                end,
                Fixed64.Zero),
            flowPolicy!,
            TraversalMedium.Gas,
            TraversalMedia.Solid | TraversalMedia.Liquid,
            new NavigationWorkMeter(flowQuery.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        var flowWorkspace = new NavigationFlowFieldWorkspace(1, 16, 16, 8, 128, 32);
        using (var flow = new NavigationFlowFieldWork(
            context.World,
            flowResolved,
            flowWorkspace))
        {
            for (int step = 0;
                step < 512 && flow.Status == NavigationFlowFieldStatus.Pending;
                step++)
            {
                flow.Advance(128, 128, 128, 128);
            }
            flow.Status.Should().Be(NavigationFlowFieldStatus.NoPath);
            flow.Result.Should().NotBeNull();
            flowDependencies = flow.Result!.Dependencies;
            flowDependencies.HasTransitionRuleDependency.Should().BeTrue();
            flow.Result.IsComplete.Should().BeTrue();
            graph.IsDependencyCurrent(flowDependencies).Should().BeTrue();
        }

        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(
                        targetIndex,
                        Cell(TraversalMedia.Solid | TraversalMedia.Liquid))
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        while (overlay.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease changedLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph changed = WithPolicy(changedLease.Graph);

        changed.IsDependencyCurrent(dependencies).Should().BeFalse(
            "reactivating the previously wrong-medium target invalidates the negative proof");
        changed.IsDependencyCurrent(flowDependencies).Should().BeFalse(
            "Flow must retain the same blocked rule/page negative authority");
    }

    [Fact]
    public void AStar_ShouldRejectWorldMutationWhileVolumeSearchIsPending()
    {
        using TrailblazerWorldContext context = CreateShortcutContext(
            TraversalMedium.Gas);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, default);
        NavigationNodeRef target = Resolve(graph, new VoxelIndex(1, 0, 1));
        Vector3d start = GetVolumeAnchor(graph, source, Fixed64.One);
        Vector3d end = GetVolumeAnchor(graph, target, Fixed64.One);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(start, end, TraversalMedia.Gas, allowTransitions: false),
            TraversalMedium.Gas,
            TraversalMedia.Gas);
        AdvanceAdmission(admission);
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        search.Advance(lookupStepLimit: 1, nodeStepLimit: 1,
                edgeStepLimit: 0, connectionStepLimit: 0)
            .Should().Be(NavigationSurfaceAStarStatus.Pending);
        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        ulong before = context.World.ChangeSequence;
        grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken())
            .Should().BeTrue();
        context.World.ChangeSequence.Should().BeGreaterThan(before);

        search.Advance(1, 1, 1, 1).Should().Be(NavigationSurfaceAStarStatus.Stale);
        search.Result.Should().BeNull();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flow_ShouldRejectWorldMutationAcrossAdmissionAndSearch(
        bool mutateBeforeSearchConstruction)
    {
        using TrailblazerWorldContext context = CreateShortcutContext(
            TraversalMedium.Gas);
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        NavigationNodeRef source = Resolve(graph, default);
        NavigationNodeRef target = Resolve(graph, new VoxelIndex(1, 0, 1));
        Vector3d start = GetVolumeAnchor(graph, source, Fixed64.One);
        Vector3d end = GetVolumeAnchor(graph, target, Fixed64.One);
        PathQuery astar = Query(
            start,
            end,
            TraversalMedia.Gas,
            allowTransitions: false);
        var query = new PathQuery(
            astar.Start,
            astar.End,
            astar.Agent,
            astar.AreaPolicy,
            astar.Traversal,
            PathAlgorithm.FlowField,
            astar.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Zero));
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            store.TryAcquire()!,
            query,
            new NavigationResolvedEndpoint(
                source,
                new NavigationCellAddress("map", default),
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                start,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                target,
                new NavigationCellAddress("map", new VoxelIndex(1, 0, 1)),
                TraversalMedia.Gas,
                TraversalMedium.Gas,
                end,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Gas,
            TraversalMedia.Gas,
            new NavigationWorkMeter(query.Budget),
            context.World.ChangeSequence,
            requiresWorldStamp: true);
        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        if (mutateBeforeSearchConstruction)
        {
            ulong beforeConstruction = context.World.ChangeSequence;
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken())
                .Should().BeTrue();
            context.World.ChangeSequence.Should().BeGreaterThan(beforeConstruction);
        }
        var workspace = new NavigationFlowFieldWorkspace(1, 16, 16, 8, 128, 32);
        using var work = new NavigationFlowFieldWork(
            context.World,
            resolved,
            workspace);

        if (!mutateBeforeSearchConstruction)
        {
            work.Advance(lookupStepLimit: 1, nodeStepLimit: 1,
                    edgeStepLimit: 0, connectionStepLimit: 0)
                .Should().Be(NavigationFlowFieldStatus.Pending);
            ulong beforeAdvance = context.World.ChangeSequence;
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken())
                .Should().BeTrue();
            context.World.ChangeSequence.Should().BeGreaterThan(beforeAdvance);
        }

        work.Advance(1, 1, 1, 1).Should().Be(NavigationFlowFieldStatus.Stale);
        work.Result.Should().BeNull();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void AStar_ShouldReconstructSolidMovementAfterVolumeTransition()
    {
        using TrailblazerWorldContext context = CreateLandingContext();
        using NavigationWorldGraphLease sourceLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        NavigationWorldGraph graph = WithPolicy(sourceLease.Graph);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 16, 18, 8, 32, 32, 16);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        NavigationNodeRef source = Resolve(graph, default);
        NavigationNodeRef target = Resolve(graph, new VoxelIndex(2, 0, 0));
        Vector3d start = GetGuideAnchor(graph, source, TraversalMedium.Gas);
        Vector3d end = GetGuideAnchor(graph, target, TraversalMedium.Solid);
        using var admission = new NavigationQueryAdmissionWork(
            context.World,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            Query(
                start,
                end,
                TraversalMedia.Gas | TraversalMedia.Solid,
                allowTransitions: true),
            TraversalMedium.Gas,
            TraversalMedia.Solid);
        AdvanceAdmission(admission);
        using var search = new NavigationSurfaceAStarWork(
            context.World,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        AdvanceSearch(search);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Cost.Should().Be((Fixed64)14);
        search.Result.TransitionInstructions.Should().ContainSingle();
        search.Result.TransitionInstructions[0].SourceMedium.Should().Be(
            TraversalMedium.Gas);
        search.Result.TransitionInstructions[0].DestinationMedium.Should().Be(
            TraversalMedium.Solid);
        search.Result.GuidePoints[^1].Medium.Should().Be(TraversalMedium.Solid);
    }

    private static TrailblazerWorldContext CreateLandingContext()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(6, 2, 4),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)2,
                    (Fixed64)4),
                storageKind: GridStorageKind.Sparse);
            VoxelIndex[] indices =
            {
                default,
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(2, 0, 0)
            };
            context.World.TryAddGrid(configuration, indices, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var transition = new TraversalTransitionDefinition(
                "land",
                TraversalTransitionType.Landing,
                indices[0],
                TraversalMedium.Gas,
                new NavigationCellAddress("map", indices[1]),
                TraversalMedium.Solid,
                actionCost: (Fixed64)5);
            NavigationMap map = new NavigationMapBuilder("map", binding)
                .AddCell(indices[0], Cell(TraversalMedia.Gas))
                .AddCell(indices[1], Cell(TraversalMedia.Solid))
                .AddCell(indices[2], Cell(TraversalMedia.Solid, (Fixed64)7))
                .AddTransition(transition)
                .Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateMovementTransitionContext(
        TraversalMedium movementMedium,
        TraversalMedium destinationMedium,
        string transitionId)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(6, 2, 4),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)2,
                    (Fixed64)4),
                storageKind: GridStorageKind.Sparse);
            VoxelIndex[] indices =
            {
                default,
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(2, 0, 0)
            };
            context.World.TryAddGrid(configuration, indices, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var transition = new TraversalTransitionDefinition(
                transitionId,
                movementMedium == TraversalMedium.Solid
                    ? TraversalTransitionType.Takeoff
                    : TraversalTransitionType.Landing,
                indices[1],
                movementMedium,
                new NavigationCellAddress("map", indices[0]),
                destinationMedium,
                actionCost: (Fixed64)5);
            NavigationMap map = new NavigationMapBuilder("map", binding)
                .AddCell(indices[0], Cell(NavigationCell.ToMedia(destinationMedium)))
                .AddCell(indices[1], Cell(NavigationCell.ToMedia(movementMedium)))
                .AddCell(indices[2], Cell(NavigationCell.ToMedia(movementMedium)))
                .AddTransition(transition)
                .Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateShortcutContext(
        TraversalMedium medium)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(4, 2, 4),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)2),
                storageKind: GridStorageKind.Sparse);
            VoxelIndex[] indices =
            {
                default,
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(1, 0, 1)
            };
            context.World.TryAddGrid(configuration, indices, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding);
            TraversalMedia selectedMedia = NavigationCell.ToMedia(medium);
            TraversalMedia alternateMedia = medium == TraversalMedium.Gas
                ? TraversalMedia.Liquid
                : TraversalMedia.Gas;
            for (int i = 0; i < indices.Length; i++)
            {
                builder.AddCell(
                    indices[i],
                    Cell(
                        selectedMedia
                            | (indices[i] == new VoxelIndex(1, 0, 1)
                                ? alternateMedia
                                : TraversalMedia.None),
                        indices[i] == new VoxelIndex(1, 0, 1)
                            ? (Fixed64)7
                            : Fixed64.Zero));
            }
            NavigationMap map = builder.Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateTransitionContext(
        VoxelIndex sourceIndex,
        NavigationCell source,
        VoxelIndex targetIndex,
        NavigationCell target,
        params TraversalTransitionDefinition[] transitions) =>
        CreateTransitionContext(
            sourceIndex,
            source,
            targetIndex,
            target,
            transitions,
            Array.Empty<TraversalTransitionRule>());

    private static TrailblazerWorldContext CreateTransitionContext(
        VoxelIndex sourceIndex,
        NavigationCell source,
        VoxelIndex targetIndex,
        NavigationCell target,
        TraversalTransitionDefinition[] transitions,
        TraversalTransitionRule[] rules)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(8, 2, 4),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)2,
                    (Fixed64)4),
                storageKind: GridStorageKind.Sparse);
            context.World.TryAddGrid(
                    configuration,
                    new[] { sourceIndex, targetIndex },
                    out _)
                .Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var builder = new NavigationMapBuilder("map", binding)
                .AddCell(sourceIndex, source)
                .AddCell(targetIndex, target);
            for (int i = 0; i < transitions.Length; i++)
                builder.AddTransition(transitions[i]);
            for (int i = 0; i < rules.Length; i++)
                builder.AddTransitionRule(rules[i]);
            NavigationMap map = builder.Build();
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            while (operation.Receipt.Status == NavigationOperationStatus.Pending)
                context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationWorldGraph WithPolicy(NavigationWorldGraph graph)
    {
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        return graph.WithAreaCatalog(catalog, graph.GraphVersion);
    }

    private static NavigationCell Cell(
        TraversalMedia media,
        Fixed64 enterCost = default) => new(
        media,
        TraversalCapability.None,
        default,
        enterCost,
        (Fixed64)4,
        (Fixed64)4);

    private static NavigationAgentProfile Profile(TraversalMedia media) => new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        media,
        TraversalCapability.None);

    private static PathQuery Query(
        Vector3d start,
        Vector3d end,
        TraversalMedia media,
        bool allowTransitions) => new(
        new NavigationEndpoint(start, "map"),
        new NavigationEndpoint(end, "map"),
        Profile(media),
        Policy.Key,
        new TraversalIntent(
            media == TraversalMedia.Solid
                ? TraversalMedium.Solid
                : media == TraversalMedia.Gas
                    ? TraversalMedium.Gas
                    : TraversalMedium.Liquid,
            media),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(
            maxLookupProbes: 8_192,
            maxEndpointCandidates: 32,
            maxExpandedNodes: 128,
            maxEvaluatedEdges: 1_024,
            maxConnectionLegs: 1_024,
            maxTransitionCandidates: 1_024,
            maxTransitionPairs: 1_024,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: 1_024,
            maxSimplificationRays: 32),
        allowTransitions);

    private static NavigationNodeRef Resolve(
        NavigationWorldGraph graph,
        VoxelIndex index)
    {
        graph.TryGetNodeRef(new NavigationCellAddress("map", index), out NavigationNodeRef node)
            .Should().BeTrue();
        return node;
    }

    private static Vector3d GetVolumeAnchor(
        NavigationWorldGraph graph,
        NavigationNodeRef node,
        Fixed64 height)
    {
        graph.TryGetNodeState(node, out NavigationNodeState state).Should().BeTrue();
        state.TryGetCenteredVolumeFootAnchor(height, out Vector3d anchor).Should().BeTrue();
        return anchor;
    }

    private static Vector3d GetGuideAnchor(
        NavigationWorldGraph graph,
        NavigationNodeRef node,
        TraversalMedium medium)
    {
        graph.TryGetNodeState(node, medium, out NavigationNodeState state)
            .Should().BeTrue();
        if (medium == TraversalMedium.Solid)
            return state.FootAnchor;
        state.TryGetCenteredVolumeFootAnchor(Fixed64.One, out Vector3d anchor)
            .Should().BeTrue();
        return anchor;
    }

    private static void AdvanceAdmission(NavigationQueryAdmissionWork admission)
    {
        for (int step = 0;
            step < 1_024 && admission.Status == NavigationQueryAdmissionStatus.Pending;
            step++)
        {
            admission.Advance(256, 32);
        }
    }

    private static void AdvanceSearch(NavigationSurfaceAStarWork search)
    {
        for (int step = 0;
            step < 4_096 && search.Status == NavigationSurfaceAStarStatus.Pending;
            step++)
        {
            search.Advance(256, 256, 256, 256);
        }
    }

    private static NavigationFlowFieldGuideLease GetInner(
        NavigationFlowFieldLease lease) =>
        (NavigationFlowFieldGuideLease)typeof(NavigationFlowFieldLease)
            .GetField("_inner", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(lease)!;

    private static T GetPrivateField<T>(object instance, string name) =>
        (T)instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!;

    private static void SetPrivateField<T>(
        object instance,
        string name,
        T value) => instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static NavigationGuideStatus Sample(
        NavigationFlowFieldGuideLease lease,
        ulong generation,
        Vector3d actualFootPosition,
        GuideSampleWorkBudget budget,
        out NavigationFlowSample sample)
    {
        var meter = new GuideSampleWorkMeter(budget);
        return lease.TrySample(
            generation,
            actualFootPosition,
            ref meter,
            out sample);
    }

    private static GuideSampleWorkBudget GenerousSampleBudget => new(
        maxCurrentNodeLookupProbes: 256,
        maxCursorLegScans: 256,
        maxCursorRebases: 16,
        maxPortalChecks: 64,
        maxPrismChecks: 64,
        maxTraceIntervals: 256,
        maxLocalRecoveryAttempts: 4);
}
