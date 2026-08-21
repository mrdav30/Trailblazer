//=======================================================================
// NavigationTransitionGuideTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
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
            additionalCost: (Fixed64)5);
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
            TraversalTransitionLocomotionHints.None);
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
        secondGuide.CompletePendingTransition(secondGeneration, instruction)
            .Should().Be(NavigationAStarQueryStatus.Stale,
                "an instruction is owned by the exact producing guide lease");
        secondGuide.GetCurrentWaypointOrdinal(secondGeneration).Should().Be(1);
        guide.TryAdvanceWaypoint(generation).Should().Be(NavigationAStarQueryStatus.Pending);
        guide.GetCurrentWaypointOrdinal(generation).Should().Be(1);
        guide.CompletePendingTransition(
                generation,
                search.Result.TransitionInstructions[0])
            .Should().Be(NavigationAStarQueryStatus.Stale,
                "the immutable cached instruction has no lease completion stamp");
        guide.GetCurrentWaypointOrdinal(generation).Should().Be(1);

        guide.CompletePendingTransition(generation, instruction)
            .Should().Be(NavigationAStarQueryStatus.Success);
        guide.TryGetCurrentStep(generation, out NavigationGuideStep destinationStep)
            .Should().Be(NavigationAStarQueryStatus.Success);
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
            additionalCost: (Fixed64)5);
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
            additionalCost: (Fixed64)5);
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
                additionalCost: (Fixed64)5);
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
                ? TraversalDomain.Surface
                : TraversalDomain.Volume,
            media == TraversalMedia.Solid
                ? TraversalMedium.Solid
                : media == TraversalMedia.Gas
                    ? TraversalMedium.Gas
                    : TraversalMedium.Liquid,
            media == TraversalMedia.Solid
                ? TraversalDomain.Surface
                : TraversalDomain.Volume),
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
}
