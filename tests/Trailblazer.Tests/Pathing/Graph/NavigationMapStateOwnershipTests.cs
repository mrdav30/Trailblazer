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

public sealed class NavigationMapStateOwnershipTests
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void ShrinkingReplacement_ShouldOwnTheEntireNewMapPayload()
    {
        PreparedNavigationMap large = PrepareMap("map", cellCount: 32, bakeVersion: 1);
        PreparedNavigationMap small = PrepareMap("map", cellCount: 1, bakeVersion: 2);
        NavigationOperationCandidate published = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            large).Candidate;
        published.ResetWorkCopiedPersistentOwnership();

        NavigationMapFoldWork replacement = FoldMap(published, small);

        replacement.Candidate.RetainedBytes.Should().BeLessThan(published.RetainedBytes);
        replacement.Candidate.WorkOwnedMapStatePayloadBytes.Should().Be(small.RetainedBytes);
        replacement.Candidate.WorkOwnedMapStatePayloadPages.Should().Be(0);
        replacement.DisplacedMapStatePayloadBytes.Should().Be(0);
        replacement.DisplacedMapStatePayloadPages.Should().Be(0);
    }

    [Fact]
    public void SecondReplacement_ShouldRetainTheDisplacedFoldSourceUntilAccepted()
    {
        PreparedNavigationMap original = PrepareMap("map", cellCount: 4, bakeVersion: 1);
        PreparedNavigationMap firstMap = PrepareMap("map", cellCount: 3, bakeVersion: 2);
        PreparedNavigationMap secondMap = PrepareMap("map", cellCount: 2, bakeVersion: 3);
        NavigationOperationCandidate published = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            original).Candidate;
        published.ResetWorkCopiedPersistentOwnership();
        NavigationMapFoldWork first = FoldMap(published, firstMap);

        NavigationMapFoldWork second = FoldMap(first.Candidate, secondMap);

        first.DisplacedMapStatePayloadBytes.Should().Be(0);
        second.DisplacedMapStatePayloadBytes.Should().Be(firstMap.RetainedBytes);
        second.DisplacedMapStatePayloadPages.Should().Be(0);
        second.Candidate.WorkOwnedMapStatePayloadBytes.Should().Be(secondMap.RetainedBytes);
        second.Candidate.WorkOwnedMapStatePayloadPages.Should().Be(0);
    }

    [Fact]
    public void ShrinkingReplacementPreflight_ShouldChargeTheFullPendingPayload()
    {
        PreparedNavigationMap large = PrepareMap("map", cellCount: 32, bakeVersion: 1);
        PreparedNavigationMap small = PrepareMap("map", cellCount: 1, bakeVersion: 2);
        long exactPreflightBytes = 144L + small.RetainedBytes;

        NavigationMapCommitOperation accepted = ProcessShrinkingReplacement(
            large,
            small,
            exactPreflightBytes,
            out long acceptedPreflightBytes);
        NavigationMapCommitOperation rejected = ProcessShrinkingReplacement(
            large,
            small,
            exactPreflightBytes - 1,
            out long rejectedPreflightBytes);

        acceptedPreflightBytes.Should().Be(exactPreflightBytes);
        rejectedPreflightBytes.Should().Be(exactPreflightBytes);
        accepted.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
    }

    [Fact]
    public void CompletedShrinkingFold_ShouldChargeOwnedAndDisplacedPayloadsExactly()
    {
        PreparedNavigationMap large = PrepareMap("map", cellCount: 32, bakeVersion: 1);
        PreparedNavigationMap small = PrepareMap("map", cellCount: 1, bakeVersion: 2);
        NavigationOperationCandidate published = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            large).Candidate;
        published.ResetWorkCopiedPersistentOwnership();
        NavigationMapFoldWork work = FoldMap(published, small);
        NavigationOperationCandidate candidate = work.Candidate;
        long exactCompletedBytes = checked(
            work.RetainedBytes
            + System.Math.Max(
                0L,
                candidate.NonPayloadRetainedBytes - published.NonPayloadRetainedBytes)
            + candidate.WorkOwnedExplicitPayloadBytes
            + candidate.WorkOwnedMapStatePayloadBytes
            + work.DisplacedExplicitPayloadBytes
            + work.DisplacedMapStatePayloadBytes);

        NavigationMapCommitOperation accepted = ProcessShrinkingReplacement(
            large,
            small,
            exactCompletedBytes,
            guardedCall: 2,
            out long acceptedCompletedBytes);
        NavigationMapCommitOperation rejected = ProcessShrinkingReplacement(
            large,
            small,
            exactCompletedBytes - 1,
            guardedCall: 2,
            out long rejectedCompletedBytes);

        acceptedCompletedBytes.Should().Be(exactCompletedBytes);
        rejectedCompletedBytes.Should().Be(exactCompletedBytes);
        accepted.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
    }

    [Fact]
    public void ComposeWork_ShouldExcludeCandidateOwnedStatePayload()
    {
        PreparedNavigationMap prepared = PrepareMap("map", cellCount: 1, bakeVersion: 1);
        var normal = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        var inflatedDescriptor = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes + 100_000,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        var normalWork = new NavigationMapInstance.ComposeWork(normal, previous: null, version: 1);
        var inflatedWork = new NavigationMapInstance.ComposeWork(
            inflatedDescriptor,
            previous: null,
            version: 1);

        inflatedWork.AdditionalExclusiveRetainedBytes
            .Should().Be(normalWork.AdditionalExclusiveRetainedBytes);
        inflatedWork.AdditionalExclusivePersistentPages
            .Should().Be(normalWork.AdditionalExclusivePersistentPages);
        inflatedWork.RetainedBytes.Should().Be(
            96L + inflatedWork.AdditionalExclusiveRetainedBytes);
        inflatedWork.PersistentPageCount.Should().Be(
            1 + inflatedWork.AdditionalExclusivePersistentPages);
    }

    [Fact]
    public void OverlayCompose_ShouldOwnChangedRootWrapperAtConstantLogicalSize()
    {
        PreparedNavigationMap prepared = PrepareMap("map", cellCount: 1, bakeVersion: 1);
        var initialDelta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Set(default, Cell) });
        NavigationMapOverlayState initialOverlay = NavigationMapOverlayState.Empty.Apply(
            initialDelta,
            operationSequence: 1);
        var initialState = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            initialOverlay,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance previous = NavigationMapInstanceTestFactory.ComposeDetached(
            initialState,
            previous: null,
            instanceVersion: 1);
        NavigationCell changedCell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        var delta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Set(default, changedCell) });
        NavigationMapOverlayState nextOverlay = initialOverlay.Apply(delta, operationSequence: 2);
        var nextState = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            nextOverlay,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        var work = new NavigationMapInstance.ComposeWork(nextState, previous, delta, version: 2);
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);

        work.Advance(meter).Should().BeTrue();

        work.AdditionalExclusiveRetainedBytes.Should().Be(200L + 32L + 72L + 4_400L);
        work.AdditionalExclusivePersistentPages.Should().Be(6 + 1 + 1);
    }

    [Fact]
    public void OverlaySequenceCompose_ShouldMeterEveryChangeAndCanonicalLookup()
    {
        PreparedNavigationMap prepared = PrepareMap("map", cellCount: 1, bakeVersion: 1);
        var sourceState = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance previous = NavigationMapInstanceTestFactory.ComposeDetached(
            sourceState,
            previous: null,
            instanceVersion: 1);
        var targetDelta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Set(default, Cell) });
        NavigationMapOverlayState targetOverlay = sourceState.Overlay.Apply(
            targetDelta,
            operationSequence: 5);
        var targetState = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            targetOverlay,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        var changes = new NavigationOperationFrameChange[5];
        for (int changeIndex = 0; changeIndex < changes.Length - 1; changeIndex++)
        {
            var maps = new NavigationMapOverlayDelta[8];
            for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
                maps[mapIndex] = new NavigationMapOverlayDelta(
                    $"other-{changeIndex:D2}-{mapIndex:D2}",
                    new[] { NavigationCellOverlayOperation.Set(default, Cell) });
            changes[changeIndex] = NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(new NavigationOverlayTransaction(maps)),
                operationSequence: changeIndex + 1);
        }
        changes[^1] = NavigationOperationFrameChange.Overlay(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[] { targetDelta })),
            operationSequence: 5);
        var work = new NavigationMapInstance.ComposeWork(
            targetState,
            previous,
            changes,
            changes.Length,
            "map",
            version: 2);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 1));
        int componentUnits = 0;
        int dependencyUnits = 0;
        int overlayUnits = 0;
        for (int frame = 0; frame < 32; frame++)
        {
            bool complete = work.Advance(meter);
            meter.ComponentNodes.Should().BeLessThanOrEqualTo(1);
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            meter.OverlaySlots.Should().BeLessThanOrEqualTo(1);
            componentUnits += meter.ComponentNodes;
            dependencyUnits += meter.DependencyEntries;
            overlayUnits += meter.OverlaySlots;
            if (complete)
                break;
            meter.Reset();
        }

        work.Result.Should().NotBeNull();
        componentUnits.Should().Be(changes.Length);
        dependencyUnits.Should().Be(changes.Length);
        overlayUnits.Should().Be(1);
    }

    [Fact]
    public void StructuralPreparation_ShouldCountCandidateSharedPayloadOnlyOnce()
    {
        PreparedNavigationMap original = PrepareMap("map", cellCount: 4, bakeVersion: 1);
        PreparedNavigationMap replacement = PrepareMap("map", cellCount: 4, bakeVersion: 2);
        NavigationOperationCandidate published = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            original).Candidate;
        published.ResetWorkCopiedPersistentOwnership();
        published.TryGetState("map", out NavigationOperationCandidate.MapState? originalState)
            .Should().BeTrue();
        NavigationMapInstance prior = NavigationMapInstanceTestFactory.ComposeDetached(
            originalState!,
            previous: null,
            instanceVersion: 1);
        var source = new NavigationWorldGraph(1, new[] { prior });
        NavigationOperationCandidate candidate = FoldMap(published, replacement).Candidate;
        candidate.TryGetState("map", out NavigationOperationCandidate.MapState? nextState)
            .Should().BeTrue();
        var compose = new NavigationMapInstance.ComposeWork(nextState!, prior, version: 2);
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        while (!compose.Advance(meter))
            meter.Reset();
        var changes = new[]
        {
            NavigationOperationFrameChange.MapCommit(
                replacement,
                OverlayReplacementPolicy.Clear,
                operationSequence: 2)
        };
        var preparation = new NavigationWorldGraph.StructuralPreparationWork(
            source,
            candidate,
            changes,
            changeCount: 1,
            PersistentStringMap<bool>.Empty.Set("map", true),
            version: 2);
        meter.Reset();
        while (!preparation.Advance(meter))
            meter.Reset();

        candidate.WorkOwnedMapStatePayloadBytes.Should().Be(replacement.RetainedBytes);
        preparation.RetainedBytes.Should().Be(
            192L + compose.AdditionalExclusiveRetainedBytes + 64L + 144L + 664L);
        preparation.PersistentPageCount.Should().Be(
            1 + compose.AdditionalExclusivePersistentPages + 4);
    }

    [Fact]
    public void StructuralPreparation_ShouldPreserveUnchangedWitnessSourceInstance()
    {
        PreparedNavigationMap prepared = PrepareMap("map", cellCount: 1, bakeVersion: 1);
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            prepared).Candidate;
        candidate.ResetWorkCopiedPersistentOwnership();
        candidate.TryGetState("map", out NavigationOperationCandidate.MapState? state)
            .Should().BeTrue();
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.ComposeDetached(
            state!,
            previous: null,
            instanceVersion: 1);
        var source = new NavigationWorldGraph(1, new[] { instance });
        var work = new NavigationWorldGraph.StructuralPreparationWork(
            source,
            candidate,
            Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0,
            PersistentStringMap<bool>.Empty.Set("map", true),
            version: 2);
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);

        work.Advance(meter).Should().BeTrue();

        work.Result.TryGetMap("map", out NavigationMapInstance? result).Should().BeTrue();
        result.Should().BeSameAs(instance);
        result!.InstanceVersion.Should().Be(1);
    }

    [Fact]
    public void StructuralPreparation_ShouldOwnOneFinalInstanceForRepeatedMapChanges()
    {
        PreparedNavigationMap prepared = PrepareMap("map", cellCount: 2, bakeVersion: 1);
        NavigationOperationCandidate published = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            prepared).Candidate;
        published.ResetWorkCopiedPersistentOwnership();
        published.TryGetState("map", out NavigationOperationCandidate.MapState? sourceState)
            .Should().BeTrue();
        NavigationMapInstance sourceInstance = NavigationMapInstanceTestFactory.ComposeDetached(
            sourceState!,
            previous: null,
            instanceVersion: 1);
        var sourceWithoutComponents = new NavigationWorldGraph(
            1,
            new[] { sourceInstance });
        NavigationWorldGraph source = sourceWithoutComponents.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(sourceWithoutComponents));
        NavigationCell firstCell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        NavigationCell secondCell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        var firstDelta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Set(default, firstCell) });
        var secondDelta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), secondCell) });
        var firstTransaction = new NavigationOverlayTransaction(new[] { firstDelta });
        var secondTransaction = new NavigationOverlayTransaction(new[] { secondDelta });
        var combinedTransaction = new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(
                "map",
                new[]
                {
                    NavigationCellOverlayOperation.Set(default, firstCell),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), secondCell)
                })
        });
        NavigationOperationCandidate sequentialCandidate = FoldOverlay(
            FoldOverlay(published, firstTransaction, operationSequence: 2).Candidate,
            secondTransaction,
            operationSequence: 3).Candidate;
        NavigationOperationCandidate combinedCandidate = FoldOverlay(
            published,
            combinedTransaction,
            operationSequence: 3).Candidate;
        var sequentialChanges = new[]
        {
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(firstTransaction),
                operationSequence: 2),
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(secondTransaction),
                operationSequence: 3)
        };
        var combinedChanges = new[]
        {
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(combinedTransaction),
                operationSequence: 3)
        };

        NavigationWorldGraph.StructuralPreparationWork sequential = PrepareStructural(
            source,
            sequentialCandidate,
            sequentialChanges);
        NavigationWorldGraph.StructuralPreparationWork combined = PrepareStructural(
            source,
            combinedCandidate,
            combinedChanges);

        sequential.RetainedBytes.Should().Be(5_912L);
        combined.RetainedBytes.Should().Be(5_912L);
        sequential.PersistentPageCount.Should().Be(combined.PersistentPageCount);
    }

    [Fact]
    public void RuntimeShrinkingReplacement_ShouldApplyAtExactPeakAndRollbackAtOneByteLess()
    {
        long initializationPeak;
        using (var probeWorld = new GridWorld())
        using (NavigationGraphRuntime probe = CreateShrinkingRuntimeScenario(
            probeWorld,
            maximumActiveBytes: null,
            out NavigationMapCommitOperation probeReplacement,
            out initializationPeak))
        {
            SimulateUntilTerminal(probe, probeReplacement);
            probeReplacement.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
        long lower = initializationPeak;
        long upper = TrailblazerWorldContextSettings.Default.MaxActiveSnapshotBytes;
        while (lower < upper)
        {
            long middle = lower + ((upper - lower) >> 1);
            using var calibrationWorld = new GridWorld();
            using NavigationGraphRuntime calibration = CreateShrinkingRuntimeScenario(
                calibrationWorld,
                middle,
                out NavigationMapCommitOperation operation,
                out _);
            SimulateUntilTerminal(calibration, operation);
            operation.Receipt.Status.Should().NotBe(NavigationOperationStatus.Pending);
            if (operation.Receipt.Status == NavigationOperationStatus.Applied)
                upper = middle;
            else
                lower = middle + 1;
        }
        long replacementPeak = lower;
        replacementPeak.Should().BeGreaterThan(initializationPeak,
            "the exact replacement ceiling must also admit the published source root");

        using (var exactWorld = new GridWorld())
        using (NavigationGraphRuntime exact = CreateShrinkingRuntimeScenario(
            exactWorld,
            replacementPeak,
            out NavigationMapCommitOperation accepted,
            out _))
        {
            SimulateUntilTerminal(exact, accepted);
            accepted.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }

        using var belowWorld = new GridWorld();
        using NavigationGraphRuntime below = CreateShrinkingRuntimeScenario(
            belowWorld,
            replacementPeak - 1,
            out NavigationMapCommitOperation rejected,
            out _);
        SimulateUntilTerminal(below, rejected);
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        for (int frame = 0; frame < 16 && !GetMapCell(below).IsMaterialized; frame++)
            below.Maintain(frame + 129);
        GetMapCell(below).IsMaterialized.Should().BeTrue();
        below.RetainedOperationWorkCount.Should().Be(0);
        below.RetainedCompositionWorkCount.Should().Be(0);
    }

    private static NavigationGraphRuntime CreateShrinkingRuntimeScenario(
        GridWorld world,
        long? maximumActiveBytes,
        out NavigationMapCommitOperation replacement,
        out long initializationPeak)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var maintenanceBudget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            maintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            maximumActiveBytes ?? defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(63, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var originalBuilder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 64; x++)
            originalBuilder.AddCell(new VoxelIndex(x, 0, 0), Cell);
        PreparedNavigationMap original = new(originalBuilder.Build(), 1);
        NavigationOperationCandidate published = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            original).Candidate;
        published.ResetWorkCopiedPersistentOwnership();
        published.TryGetState("map", out NavigationOperationCandidate.MapState? sourceState)
            .Should().BeTrue();
        NavigationMapInstance sourceInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            sourceState!,
            previous: null,
            instanceVersion: 1);
        var source = new NavigationWorldGraph(1, new[] { sourceInstance });
        var runtime = new NavigationGraphRuntime(world, settings);
        try
        {
            runtime.Store.TryPublish(source).Should().Be(NavigationCandidatePublication.Published);
            initializationPeak = GetActiveBytes(runtime);
            var smallerBuilder = new NavigationMapBuilder("map", binding);
            for (int x = 0; x < 63; x++)
                smallerBuilder.AddCell(new VoxelIndex(x, 0, 0), Cell);
            NavigationMap smaller = smallerBuilder.Build();
            replacement = new NavigationMapCommitOperation(
                new PreparedNavigationMap(smaller, 2),
                OverlayReplacementPolicy.Clear,
                operationSequence: 2,
                effectiveFrame: 1);
            runtime.Admit(replacement).Should().BeTrue();
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static void SimulateUntilTerminal(
        NavigationGraphRuntime runtime,
        NavigationMapCommitOperation operation)
    {
        for (int frame = 0;
             frame < 512 && operation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            runtime.Maintain(frame + 1);
        }
    }

    private static NavigationGraphCellState GetMapCell(NavigationGraphRuntime runtime)
    {
        runtime.TryGetCellState("map", default, out NavigationGraphCellState state)
            .Should().BeTrue();
        return state;
    }

    private static long GetActiveBytes(NavigationGraphRuntime runtime) => checked(
        runtime.Current.RetainedBytes
        + runtime.RetainedCompositionWorkBytes
        + runtime.RetainedOperationWorkBytes);

    private static NavigationMapCommitOperation ProcessShrinkingReplacement(
        PreparedNavigationMap large,
        PreparedNavigationMap small,
        long firstGuardLimit,
        out long firstGuardBytes)
    {
        var processor = new NavigationOperationProcessor(
            TrailblazerWorldContextSettings.Default.OperationLimits);
        var install = new NavigationMapCommitOperation(
            large,
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        processor.Admit(install).Should().BeTrue();
        AdvanceProcessor(processor, install, startFrame: 1);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var replacement = new NavigationMapCommitOperation(
            small,
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: 32);
        processor.Admit(replacement).Should().BeTrue();
        long captured = -1;
        bool first = true;
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 32; frame < 64 && replacement.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            processor.ProcessFrame(
                frame,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter,
                (bytes, _) =>
                {
                    if (!first)
                        return true;
                    first = false;
                    captured = bytes;
                    return bytes <= firstGuardLimit;
                });
            meter.Reset();
        }
        firstGuardBytes = captured;
        return replacement;
    }

    private static NavigationMapCommitOperation ProcessShrinkingReplacement(
        PreparedNavigationMap large,
        PreparedNavigationMap small,
        long guardedLimit,
        int guardedCall,
        out long guardedBytes)
    {
        var processor = new NavigationOperationProcessor(
            TrailblazerWorldContextSettings.Default.OperationLimits);
        var install = new NavigationMapCommitOperation(
            large,
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        processor.Admit(install).Should().BeTrue();
        AdvanceProcessor(processor, install, startFrame: 1);
        var replacement = new NavigationMapCommitOperation(
            small,
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: 32);
        processor.Admit(replacement).Should().BeTrue();
        long captured = -1;
        int call = 0;
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 32; frame < 64 && replacement.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            processor.ProcessFrame(
                frame,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter,
                (bytes, _) =>
                {
                    call++;
                    if (call != guardedCall)
                        return true;
                    captured = bytes;
                    return bytes <= guardedLimit;
                });
            meter.Reset();
        }
        guardedBytes = captured;
        return replacement;
    }

    private static void AdvanceProcessor(
        NavigationOperationProcessor processor,
        NavigationMapCommitOperation operation,
        int startFrame)
    {
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = startFrame;
             frame < startFrame + 31 && operation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            processor.ProcessFrame(
                frame,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter);
            meter.Reset();
        }
    }

    private static NavigationMapFoldWork FoldMap(
        NavigationOperationCandidate source,
        PreparedNavigationMap prepared)
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int corridorCapacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationMapFoldWork(
            source,
            prepared,
            OverlayReplacementPolicy.Clear,
            settings.OperationLimits,
            new GridCellPrism[corridorCapacity],
            new Vector3d[(corridorCapacity * 2) - 2],
            new NavigationCellAddress[corridorCapacity],
            new NavigationAddressStampSet(corridorCapacity));
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        for (int frame = 0; frame < 4_096; frame++)
        {
            if (work.Advance(meter, out NavigationOperationRejection rejection))
            {
                rejection.Should().Be(NavigationOperationRejection.None);
                return work;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Map fold did not complete.");
    }

    private static NavigationOverlayFoldWork FoldOverlay(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        long operationSequence)
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int corridorCapacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationOverlayFoldWork(
            source,
            transaction,
            operationSequence,
            settings.OperationLimits,
            new GridCellPrism[corridorCapacity],
            new Vector3d[(corridorCapacity * 2) - 2],
            new NavigationCellAddress[corridorCapacity],
            new NavigationAddressStampSet(corridorCapacity));
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        for (int frame = 0; frame < 4_096; frame++)
        {
            if (work.Advance(meter, out NavigationOperationRejection rejection))
            {
                rejection.Should().Be(NavigationOperationRejection.None);
                return work;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Overlay fold did not complete.");
    }

    private static NavigationWorldGraph.StructuralPreparationWork PrepareStructural(
        NavigationWorldGraph source,
        NavigationOperationCandidate candidate,
        NavigationOperationFrameChange[] changes)
    {
        var work = new NavigationWorldGraph.StructuralPreparationWork(
            source,
            candidate,
            changes,
            changes.Length,
            PersistentStringMap<bool>.Empty.Set("map", true),
            version: source.GraphVersion + 1);
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        while (!work.Advance(meter))
            meter.Reset();
        return work;
    }

    private static PreparedNavigationMap PrepareMap(
        string mapId,
        int cellCount,
        long bakeVersion)
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(cellCount - 1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int x = 0; x < cellCount; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), Cell);
        return new PreparedNavigationMap(builder.Build(), bakeVersion);
    }
}
