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

public sealed class NavigationBaselineRebuildDeltaTests
{
    [Fact]
    public void CapturedGridIdentity_ShouldRequireEveryGenerationFieldToMatch()
    {
        const long WorldSpawnToken = 11;
        const ushort GridIndex = 12;
        const long GridSpawnToken = 13;
        const ulong GridLastChangeSequence = 14;

        Matches(WorldSpawnToken, GridIndex, GridSpawnToken, GridLastChangeSequence)
            .Should().BeTrue();
        Matches(WorldSpawnToken + 1, GridIndex, GridSpawnToken, GridLastChangeSequence)
            .Should().BeFalse();
        Matches(WorldSpawnToken, (ushort)(GridIndex + 1), GridSpawnToken, GridLastChangeSequence)
            .Should().BeFalse();
        Matches(WorldSpawnToken, GridIndex, GridSpawnToken + 1, GridLastChangeSequence)
            .Should().BeFalse();
        Matches(WorldSpawnToken, GridIndex, GridSpawnToken, GridLastChangeSequence + 1)
            .Should().BeFalse();

        static bool Matches(
            long worldSpawnToken,
            ushort gridIndex,
            long gridSpawnToken,
            ulong gridLastChangeSequence) =>
            NavigationBaselineRebuild.MatchesCapturedGridIdentity(
                worldSpawnToken,
                gridIndex,
                gridSpawnToken,
                gridLastChangeSequence,
                WorldSpawnToken,
                GridIndex,
                GridSpawnToken,
                GridLastChangeSequence);
    }

    [Fact]
    public void CoveredBaseline_ShouldRequireCapturePayloadRunAndGridIdentity()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out _);
        world.TryCaptureNavigationBaseline(
                source.Map.GridBinding.Key,
                new[] { physical },
                out GridNavigationBaseline? baseline)
            .Should().BeTrue();
        baseline.Should().NotBeNull();
        GridNavigationBaseline exact = baseline!;

        IsCurrent(captured: true, exact, exact.CapturedChangeSequence)
            .Should().BeTrue();
        IsCurrent(captured: false, exact, exact.CapturedChangeSequence)
            .Should().BeFalse();
        IsCurrent(captured: true, candidate: null, exact.CapturedChangeSequence)
            .Should().BeFalse();
        IsCurrent(captured: true, exact, exact.CapturedChangeSequence + 1)
            .Should().BeFalse();

        bool IsCurrent(
            bool captured,
            GridNavigationBaseline? candidate,
            ulong capturedChangeSequence) =>
            NavigationBaselineRebuild.IsCoveredBaselineCurrent(
                captured,
                candidate,
                capturedChangeSequence,
                exact.WorldSpawnToken,
                exact.GridIndex,
                exact.GridSpawnToken,
                exact.GridLastChangeSequence);
    }

    [Fact]
    public void StaleCoveredBatchFinalization_ShouldDiscardTheSeedAndRestartDiscovery()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out _);
        var rebuild = new NavigationBaselineRebuild(source);
        var addresses = new VoxelIndex[1];
        var covered = new GridCoveredAddress[1];

        rebuild.Advance(
            world,
            source,
            maximumAddresses: source.DynamicSlotCount,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture seedCapture,
            out bool seedCompleted).Should().Be(source.DynamicSlotCount);
        seedCompleted.Should().BeFalse();
        seedCapture.IsRequested.Should().BeFalse();
        world.TryCaptureNavigationBaseline(
                source.Map.GridBinding.Key,
                new[] { physical },
                out GridNavigationBaseline? baseline)
            .Should().BeTrue();
        baseline.Should().NotBeNull();

        rebuild.FinalizeCoveredAddressAdvance(
                GridCoveredAddressCursorStatus.More,
                addressProbes: 1,
                outputCount: 1,
                invalidated: false,
                captured: true,
                baseline,
                expectedCapturedChangeSequence: baseline!.CapturedChangeSequence + 1,
                expectedWorldSpawnToken: baseline.WorldSpawnToken,
                long.MaxValue,
                int.MaxValue,
                out NavigationGridBaselineCapture staleCapture,
                out bool staleCompleted)
            .Should().Be(1);

        staleCompleted.Should().BeFalse();
        staleCapture.IsRequested.Should().BeFalse();
        rebuild.IsCapacityBlocked.Should().BeFalse();
        rebuild.Advance(
            world,
            source,
            maximumAddresses: source.DynamicSlotCount,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture retryCapture,
            out bool retryCompleted).Should().Be(source.DynamicSlotCount,
                "stale finalization must restart from the retained default seed rather than append the prior batch");
        retryCompleted.Should().BeFalse();
        retryCapture.IsRequested.Should().BeFalse();
    }

    [Fact]
    public void BaselineRebuildRegistryFootprint_ShouldExcludeOnlyTheEmptyRegistryRoot()
    {
        PersistentStringMap<NavigationBaselineRebuild> empty =
            PersistentStringMap<NavigationBaselineRebuild>.Empty;

        NavigationWorldGraph.GetBaselineRebuildRegistryFootprint(
            empty,
            out long emptyBytes,
            out int emptyPages);

        emptyBytes.Should().Be(0);
        emptyPages.Should().Be(0);

        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        PersistentStringMap<NavigationBaselineRebuild> populated = empty.Set(
            source.MapId,
            new NavigationBaselineRebuild(source));

        NavigationWorldGraph.GetBaselineRebuildRegistryFootprint(
            populated,
            out long populatedBytes,
            out int populatedPages);

        populatedBytes.Should().Be(populated.RetainedBytes);
        populatedPages.Should().Be(1 + populated.PersistentNodeCount);
    }

    [Fact]
    public void GridEvents_ShouldRejectForeignIdentityAndInvalidateOnlyMatchingBroadChanges()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out _);
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        GridConfiguration configuration = source.Map.GridBinding.Configuration;
        ulong nextSequence = source.GridLastChangeSequence + 1;
        var unknown = new VoxelIndex(1, 0, 0);

        var foreignWorld = new GridEventInfo(
            identity.WorldSpawnToken + 1,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstacleAdded,
            voxelIndex: unknown,
            changeStamp: new GridChangeStamp(nextSequence, nextSequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: 1);
        source.Apply(identity.WorldSpawnToken, foreignWorld, instanceVersion: 2)
            .Should().BeSameAs(source);

        var reusedGridSlot = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken + 1,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstacleAdded,
            voxelIndex: unknown,
            changeStamp: new GridChangeStamp(nextSequence, nextSequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: 1);
        source.Apply(identity.WorldSpawnToken, reusedGridSlot, instanceVersion: 2)
            .Should().BeSameAs(source);

        var unknownAddress = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstacleAdded,
            voxelIndex: unknown,
            changeStamp: new GridChangeStamp(nextSequence, nextSequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: 1);
        NavigationMapInstance advanced = source.Apply(
            identity.WorldSpawnToken,
            unknownAddress,
            instanceVersion: 2);
        advanced.Should().NotBeSameAs(source);
        advanced.GridLastChangeSequence.Should().Be(nextSequence);
        advanced.PhysicalVersion.Should().Be(source.PhysicalVersion);
        advanced.IsPhysicallyPresent(physical).Should().BeTrue();
        advanced.Apply(identity.WorldSpawnToken, unknownAddress, instanceVersion: 3)
            .Should().BeSameAs(advanced,
                "an already-accounted sequence must not create another snapshot");

        var mismatchedConfiguration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(3, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var unrelatedBroadChange = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            mismatchedConfiguration,
            gridVersion: 3,
            changeKind: GridEventKind.GridChanged);
        source.Apply(identity.WorldSpawnToken, unrelatedBroadChange, instanceVersion: 3)
            .Should().BeSameAs(source);

        var matchingAdded = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 3,
            changeKind: GridEventKind.GridAdded);
        source.Apply(identity.WorldSpawnToken, matchingAdded, instanceVersion: 3)
            .IsMaterialized.Should().BeFalse(
                "a matching grid addition represents a new physical generation");

        var matchingBroadChange = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 3,
            changeKind: GridEventKind.GridChanged);
        NavigationMapInstance dormant = source.Apply(
            identity.WorldSpawnToken,
            matchingBroadChange,
            instanceVersion: 3);
        dormant.IsMaterialized.Should().BeFalse();
        dormant.IsPhysicallyPresent(physical).Should().BeFalse();
        dormant.LastCopiedPhysicalPages.Should().BeGreaterThan(0);
        dormant.Apply(identity.WorldSpawnToken, unknownAddress, instanceVersion: 4)
            .Should().BeSameAs(dormant,
                "voxel events cannot rematerialize a map after its grid lifecycle retired");

        ulong payloadlessSequence = nextSequence + 1;
        var payloadlessVoxelChange = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 4,
            changeKind: GridEventKind.ObstaclesCleared,
            voxelIndex: physical,
            changeStamp: new GridChangeStamp(payloadlessSequence, payloadlessSequence),
            hasVoxelState: false,
            isVoxelPresent: false,
            obstacleCount: 0);
        NavigationMapInstance sequenceOnly = source.Apply(
            identity.WorldSpawnToken,
            payloadlessVoxelChange,
            instanceVersion: 4);
        sequenceOnly.GridLastChangeSequence.Should().Be(payloadlessSequence);
        sequenceOnly.PhysicalVersion.Should().Be(source.PhysicalVersion);
        sequenceOnly.IsPhysicallyPresent(physical).Should().BeTrue();

        NavigationMapInstance resnapshot = source.ApplyBatch(
            identity.WorldSpawnToken,
            System.ReadOnlySpan<GridEventInfo>.Empty,
            resnapshotAll: true,
            instanceVersion: 5);
        resnapshot.IsMaterialized.Should().BeFalse(
            "resnapshot invalidation applies even when no event envelope is retained");
        resnapshot.IsPhysicallyPresent(physical).Should().BeFalse();
        source.IsMaterialized.Should().BeTrue("the prior immutable snapshot remains published");
    }

    [Fact]
    public void IdenticalPhysicalEvent_ShouldAdvanceOnlyTheObservedGridSequence()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out _);
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        ulong nextSequence = source.GridLastChangeSequence + 1;
        var unchanged = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            source.Map.GridBinding.Configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstaclesCleared,
            voxelIndex: physical,
            changeStamp: new GridChangeStamp(nextSequence, nextSequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: 0);

        NavigationMapInstance advanced = source.Apply(
            identity.WorldSpawnToken,
            unchanged,
            instanceVersion: 2);

        advanced.Should().NotBeSameAs(source);
        advanced.GridLastChangeSequence.Should().Be(nextSequence);
        advanced.PhysicalVersion.Should().Be(source.PhysicalVersion,
            "an identical physical state must not publish a new physical page version");
        advanced.LastCopiedPhysicalPages.Should().Be(0);
        advanced.IsPhysicallyPresent(physical).Should().BeTrue();
    }

    [Fact]
    public void PhysicalBatch_ShouldIgnoreStateAlreadyCoveredByItsBaseline()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out _);
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        var stale = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            source.Map.GridBinding.Configuration,
            gridVersion: 2,
            changeKind: GridEventKind.SparseVoxelRemoved,
            voxelIndex: physical,
            changeStamp: new GridChangeStamp(
                source.BaselineCapturedChangeSequence,
                source.BaselineCapturedChangeSequence),
            hasVoxelState: true,
            isVoxelPresent: false,
            obstacleCount: 0);

        NavigationMapInstance result = source.ApplyBatch(
            identity.WorldSpawnToken,
            new[] { stale },
            resnapshotAll: false,
            instanceVersion: 2);

        result.Should().BeSameAs(source,
            "an event included in the captured baseline must not overwrite its physical state");
        result.IsPhysicallyPresent(physical).Should().BeTrue();
    }

    [Fact]
    public void PhysicalBatch_ShouldAdvanceGridSequenceWithoutInventingAnOutOfMapSlot()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        ulong nextSequence = source.GridLastChangeSequence + 1;
        var outside = new VoxelIndex(99, 0, 0);
        var state = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            source.Map.GridBinding.Configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstacleAdded,
            voxelIndex: outside,
            changeStamp: new GridChangeStamp(nextSequence, nextSequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: 1);

        NavigationMapInstance result = source.ApplyBatch(
            identity.WorldSpawnToken,
            new[] { state },
            resnapshotAll: false,
            instanceVersion: 2);

        result.GridLastChangeSequence.Should().Be(nextSequence,
            "matching grid progress remains authoritative even when the voxel is outside this map");
        result.PhysicalVersion.Should().Be(source.PhysicalVersion);
        result.LastCopiedPhysicalPages.Should().Be(0);
        result.TryGetSlot(outside, out _).Should().BeFalse();
    }

    [Fact]
    public void PhysicalBatch_ShouldAdvanceSequenceWithoutApplyingPayloadlessState()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out _);
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        ulong nextSequence = source.GridLastChangeSequence + 1;
        var payloadless = new GridEventInfo(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            source.Map.GridBinding.Configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstaclesCleared,
            voxelIndex: physical,
            changeStamp: new GridChangeStamp(nextSequence, nextSequence),
            hasVoxelState: false,
            isVoxelPresent: false,
            obstacleCount: 0);

        NavigationMapInstance result = source.ApplyBatch(
            identity.WorldSpawnToken,
            new[] { payloadless },
            resnapshotAll: false,
            instanceVersion: 2);

        result.GridLastChangeSequence.Should().Be(nextSequence);
        result.PhysicalVersion.Should().Be(source.PhysicalVersion);
        result.LastCopiedPhysicalPages.Should().Be(0);
        result.IsPhysicallyPresent(physical).Should().BeTrue(
            "an event without exact voxel state can advance observation only");
    }

    [Fact]
    public void PhysicalBatch_ShouldCopyEachTouchedPageOnlyOnce()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out VoxelIndex physical,
            out VoxelIndex authored);
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        ulong firstSequence = source.BaselineCapturedChangeSequence + 1;
        GridEventInfo Event(VoxelIndex index, ulong sequence, byte obstacleCount) => new(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            source.Map.GridBinding.Configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstacleAdded,
            voxelIndex: index,
            changeStamp: new GridChangeStamp(sequence, sequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: obstacleCount);

        NavigationMapInstance result = source.ApplyBatch(
            identity.WorldSpawnToken,
            new[]
            {
                Event(authored, firstSequence, 1),
                Event(authored, firstSequence + 1, 2)
            },
            resnapshotAll: false,
            instanceVersion: 2);

        result.LastCopiedPhysicalPages.Should().Be(1,
            "later events in the same new page mutate the frame-owned copy");
        result.IsPhysicallyPresent(authored).Should().BeTrue();
        result.TryGetSlot(authored, out int slot).Should().BeTrue();
        result.TryGetPhysicalState(slot, out bool present, out byte obstacleCount)
            .Should().BeTrue();
        present.Should().BeTrue();
        obstacleCount.Should().Be(2);
        result.GridLastChangeSequence.Should().Be(firstSequence + 1);

        NavigationMapInstance existingPage = source.ApplyBatch(
            identity.WorldSpawnToken,
            new[]
            {
                Event(physical, firstSequence, 1),
                Event(physical, firstSequence + 1, 2)
            },
            resnapshotAll: false,
            instanceVersion: 2);

        existingPage.LastCopiedPhysicalPages.Should().Be(1,
            "the first event clones the published page and later events mutate only that frame-owned copy");
        existingPage.TryGetSlot(physical, out int copiedSlot).Should().BeTrue();
        existingPage.TryGetPhysicalState(copiedSlot, out _, out byte copiedObstacleCount)
            .Should().BeTrue();
        copiedObstacleCount.Should().Be(2);
        source.TryGetSlot(physical, out int physicalSlot).Should().BeTrue();
        source.TryGetPhysicalState(physicalSlot, out _, out byte sourceObstacleCount)
            .Should().BeTrue();
        sourceObstacleCount.Should().Be(0,
            "batch copy-on-write must not mutate the prior immutable page");
    }

    [Fact]
    public void PhysicalBatch_ShouldOwnANewPhysicalPageAcrossTheSlotBoundary()
    {
        using var world = new GridWorld();
        var last = new VoxelIndex(NavigationPhysicalPage.SlotCount, 0, 0);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(NavigationPhysicalPage.SlotCount, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        world.TryAddGrid(configuration, new[] { default(VoxelIndex) }, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var builder = new NavigationMapBuilder("page-boundary", binding);
        for (int x = 0; x <= NavigationPhysicalPage.SlotCount; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), cell);
        NavigationMap map = builder.Build();
        var sourceState = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: 0,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0);
        NavigationMapInstance source = NavigationMapInstanceTestFactory.Compose(
            world,
            sourceState,
            previous: null,
            instanceVersion: 1);
        source.TryGetSlot(last, out int slot).Should().BeTrue();
        slot.Should().Be(NavigationPhysicalPage.SlotCount);
        source.TryGetPhysicalState(slot, out _, out _).Should().BeFalse(
            "the absent cell's page is omitted from the prior materialized snapshot");
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        ulong firstSequence = source.BaselineCapturedChangeSequence + 1;
        GridEventInfo Event(ulong sequence, byte obstacleCount) => new(
            identity.WorldSpawnToken,
            identity.GridIndex,
            identity.GridSpawnToken,
            configuration,
            gridVersion: 2,
            changeKind: GridEventKind.ObstacleAdded,
            voxelIndex: last,
            changeStamp: new GridChangeStamp(sequence, sequence),
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: obstacleCount);

        NavigationMapInstance result = source.ApplyBatch(
            identity.WorldSpawnToken,
            new[]
            {
                Event(firstSequence, 1),
                Event(firstSequence + 1, 2)
            },
            resnapshotAll: false,
            instanceVersion: 2);

        result.LastCopiedPhysicalPages.Should().Be(1);
        result.TryGetPhysicalState(slot, out bool present, out byte obstacleCount)
            .Should().BeTrue();
        present.Should().BeTrue();
        obstacleCount.Should().Be(2,
            "the second event mutates the batch-owned page instead of copying it again");
        source.TryGetPhysicalState(slot, out _, out _).Should().BeFalse(
            "the prior immutable snapshot must remain without that physical page");
    }

    [Fact]
    public void DeltaMaterialization_ShouldFailClosedWhenTheGridBaselineDisappears()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        var missingDelta = new NavigationGridBaselineCapture(
            source.AddressCount,
            baseline: null,
            isDelta: true);
        NavigationMapInstance missing = source.MaterializeDelta(
            source,
            missingDelta,
            instanceVersion: 4);
        missing.IsMaterialized.Should().BeFalse(
            "a delta whose grid baseline disappeared must retire the prior physical state");
    }

    [Fact]
    public void DeltaEntryPoint_ShouldDelegateUnrequestedAndFullCapturesToMaterialization()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        NavigationMapInstance unrequested = source.MaterializeDelta(
            source,
            default,
            instanceVersion: 4);
        var fullCapture = new NavigationGridBaselineCapture(
            source.AddressCount,
            PersistentIntMap<NavigationPhysicalPage>.Empty,
            source.BaselineCapturedChangeSequence,
            source.GridIdentity.WorldSpawnToken,
            source.GridIdentity.GridIndex,
            source.GridIdentity.GridSpawnToken,
            source.GridLastChangeSequence,
            source.Map.GridBinding.Key);

        NavigationMapInstance full = source.MaterializeDelta(
            source,
            fullCapture,
            instanceVersion: 5);

        unrequested.IsMaterialized.Should().BeFalse(
            "an absent capture follows the ordinary fail-closed materialization contract");
        full.IsMaterialized.Should().BeTrue(
            "a full capture must not be interpreted as an exact delta");
        full.GridIdentity.Should().Be(source.GridIdentity);
        full.GridLastChangeSequence.Should().Be(source.GridLastChangeSequence);
    }

    [Fact]
    public void CanonicalCopyAndDefaultSeed_ShouldRejectPartialAndOutOfRangeRequests()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        var sentinel = new VoxelIndex(99, 99, 99);
        var undersized = new VoxelIndex[source.AddressCount - 1];
        undersized[0] = sentinel;

        source.CopyCanonicalAddresses(undersized).Should().Be(0);
        undersized[0].Should().Be(sentinel,
            "an insufficient destination must not receive a partial canonical prefix");
        source.TryGetDefaultBaselineSeedSlot(-1, out _, out _).Should().BeFalse();
        source.TryGetDefaultBaselineSeedSlot(source.DynamicSlotCount, out _, out _)
            .Should().BeFalse();

        int retained = 0;
        for (int ordinal = 0; ordinal < source.DynamicSlotCount; ordinal++)
        {
            source.TryGetDefaultBaselineSeedSlot(ordinal, out _, out bool retain)
                .Should().BeTrue();
            if (retain)
                retained++;
        }
        retained.Should().Be(1,
            "only the authored dynamic address, not a physical-only address, seeds a resnapshot");

        NavigationMap map = new NavigationMapBuilder("without-default", source.Map.GridBinding)
            .AddCell(default, new NavigationCell(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One))
            .Build();
        var state = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: 0,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0);
        NavigationMapInstance withoutDefault = NavigationMapInstanceTestFactory.ComposeDetached(
            state,
            previous: null,
            instanceVersion: 1);
        withoutDefault.CreateDefaultBaselineSeed(
                instanceVersion: 2,
                PersistentVoxelIndexMap<NavigationDynamicCellSlot>.Empty,
                PersistentIntMap<VoxelIndex>.Empty)
            .Should().BeSameAs(withoutDefault,
                "a map without a default cell has no baseline seed state to replace");
    }

    [Fact]
    public void DefaultResnapshot_ShouldRetainAuthoredSlotWhileReplacingPhysicalOnlySlots()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out VoxelGrid grid,
            out VoxelIndex originalPhysical,
            out VoxelIndex authored);
        var replacementPhysical = new VoxelIndex(1, 0, 0);
        grid.TryRemoveVoxel(originalPhysical).Should().BeTrue();
        grid.TryAddVoxel(replacementPhysical, out _).Should().BeTrue();
        var rebuild = new NavigationBaselineRebuild(source);
        var addresses = new VoxelIndex[1];
        var covered = new GridCoveredAddress[1];
        NavigationGridBaselineCapture capture = default;
        bool completed = false;

        for (int slice = 0; slice < 64 && !completed; slice++)
        {
            rebuild.Advance(
                world,
                source,
                maximumAddresses: 1,
                long.MaxValue,
                int.MaxValue,
                addresses,
                covered,
                out capture,
                out completed);
        }

        completed.Should().BeTrue();
        NavigationMapInstance result = source.Materialize(capture, instanceVersion: 2);
        result.DynamicSlotCount.Should().Be(2);
        result.TryGetSlot(originalPhysical, out _).Should().BeFalse(
            "a vanished physical-only address has no semantic reason to remain");
        result.TryGetSlot(authored, out int authoredSlot).Should().BeTrue();
        result.TryGetEffectiveCell(authoredSlot, out NavigationCell authoredCell).Should().BeTrue();
        authoredCell.EnterCost.Should().Be(Fixed64.One,
            "the authored override must survive unrelated physical churn");
        result.IsPhysicallyPresent(authored).Should().BeFalse();
        result.TryGetSlot(replacementPhysical, out int replacementSlot).Should().BeTrue();
        result.TryGetEffectiveCell(replacementSlot, out NavigationCell replacementCell)
            .Should().BeTrue();
        replacementCell.EnterCost.Should().Be(Fixed64.Zero);
        result.IsPhysicallyPresent(replacementPhysical).Should().BeTrue();
    }

    [Fact]
    public void DefaultSeedPageCeiling_ShouldBlockWithoutPublishingAPartialCapture()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out VoxelIndex authored);
        var rebuild = new NavigationBaselineRebuild(source);
        var addresses = new VoxelIndex[1];
        var covered = new GridCoveredAddress[1];

        int firstConsumed = rebuild.Advance(
            world,
            source,
            maximumAddresses: 1,
            long.MaxValue,
            maximumPersistentPages: 1,
            addresses,
            covered,
            out NavigationGridBaselineCapture firstCapture,
            out bool firstCompleted);

        firstConsumed.Should().Be(1);
        firstCompleted.Should().BeFalse();
        firstCapture.IsRequested.Should().BeFalse();
        rebuild.IsCapacityBlocked.Should().BeTrue();

        int retryConsumed = rebuild.Advance(
            world,
            source,
            maximumAddresses: 1,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture retryCapture,
            out bool retryCompleted);

        retryConsumed.Should().Be(0,
            "a page-over-budget rebuild remains fail-closed instead of resuming partial state");
        retryCompleted.Should().BeFalse();
        retryCapture.IsRequested.Should().BeFalse();
        source.TryGetSlot(authored, out int authoredSlot).Should().BeTrue();
        source.TryGetEffectiveCell(authoredSlot, out NavigationCell authoredCell).Should().BeTrue();
        authoredCell.EnterCost.Should().Be(Fixed64.One,
            "capacity rejection must not mutate the source snapshot");
    }

    [Fact]
    public void DefaultSeedByteCeiling_ShouldAcceptExactRetainedPrefixAndRejectOneBelow()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        var addresses = new VoxelIndex[1];
        var covered = new GridCoveredAddress[1];
        var reference = new NavigationBaselineRebuild(source);

        reference.Advance(
            world,
            source,
            maximumAddresses: 1,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out _,
            out bool referenceCompleted).Should().Be(1);

        referenceCompleted.Should().BeFalse();
        long exactRetainedBytes = reference.RetainedBytes;

        var insufficient = new NavigationBaselineRebuild(source);
        insufficient.Advance(
            world,
            source,
            maximumAddresses: 1,
            exactRetainedBytes - 1,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture rejectedCapture,
            out bool rejectedCompleted).Should().Be(1);
        rejectedCompleted.Should().BeFalse();
        rejectedCapture.IsRequested.Should().BeFalse();
        insufficient.IsCapacityBlocked.Should().BeTrue(
            "a one-byte-short default seed cannot retain a partial prefix");

        var exact = new NavigationBaselineRebuild(source);
        exact.Advance(
            world,
            source,
            maximumAddresses: 1,
            exactRetainedBytes,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture exactCapture,
            out bool exactCompleted).Should().Be(1);
        exactCompleted.Should().BeFalse();
        exactCapture.IsRequested.Should().BeFalse();
        exact.IsCapacityBlocked.Should().BeFalse();
        exact.RetainedBytes.Should().Be(exactRetainedBytes,
            "the configured retained-byte ceiling is inclusive");
        PersistentStringMap<NavigationBaselineRebuild> rebuilds =
            PersistentStringMap<NavigationBaselineRebuild>.Empty
                .Set(insufficient.MapId, insufficient)
                .Set("unblocked", exact);
        NavigationBaselineRebuild.CountCapacityBlocked(rebuilds).Should().Be(1,
            "diagnostics must count the genuine one-below rebuild but not the exact-boundary rebuild");
    }

    [Fact]
    public void ChangedMapInstance_ShouldReplaceStaleChunkedBaselineWithExactDelta()
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(128, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var builder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 65; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), cell);
        NavigationMap map = builder.Build();
        var sourceState = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: 0,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0);
        NavigationMapInstance source = NavigationMapInstanceTestFactory.Compose(
            world,
            sourceState,
            previous: null,
            instanceVersion: 1);
        var sourceGraph = new NavigationWorldGraph(1, new[] { source });
        PersistentStringMap<NavigationBaselineRebuild> rebuilds =
            PersistentStringMap<NavigationBaselineRebuild>.Empty;

        Capture(
            world,
            sourceGraph,
            sourceGraph,
            resnapshotAll: true,
            changes: System.Array.Empty<NavigationOperationFrameChange>(),
            baselineAddressCapacity: 64,
            ref rebuilds,
            out NavigationGridBaselineCapture[] initialCaptures);

        rebuilds.Count.Should().Be(1);
        initialCaptures[0].IsRequested.Should().BeFalse(
            "the 64-address budget cannot finish a 65-address recovery baseline");
        NavigationBaselineRebuild stale = rebuilds.GetValueAt(0);
        stale.IsComplete.Should().BeFalse();
        long staleBytes = stale.RetainedBytes;
        int stalePages = stale.PersistentPageCount;

        VoxelIndex[] dynamicIndices =
        {
            new(127, 0, 0),
            new(128, 0, 0)
        };
        NavigationCellOverlayOperation[] sets =
        {
            NavigationCellOverlayOperation.Set(dynamicIndices[0], cell),
            NavigationCellOverlayOperation.Set(dynamicIndices[1], cell)
        };
        var delta = new NavigationMapOverlayDelta("map", sets);
        NavigationMapOverlayState overlay = NavigationMapOverlayState.Empty.Apply(delta, 2);
        PersistentVoxelIndexMap<byte> dynamicAddresses =
            PersistentVoxelIndexMap<byte>.Empty
                .Set(dynamicIndices[0], 0)
                .Set(dynamicIndices[1], 0);
        var changedState = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: 0,
            overlay,
            dynamicSlotGeneration: 1,
            dynamicAddresses);
        var compose = new NavigationMapInstance.ComposeWork(
            changedState,
            source,
            delta,
            version: 2);
        var composeMeter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 0; frame < 64 && !compose.Advance(composeMeter); frame++)
            composeMeter.Reset();
        NavigationMapInstance changed = compose.Result;
        changed.IsMaterialized.Should().BeFalse(
            "the newly introduced dynamic addresses still need their exact physical baseline");
        var changedGraph = new NavigationWorldGraph(2, new[] { changed });
        var preparedOverlay = new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(new[] { delta }));
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(preparedOverlay, operationSequence: 2)
        };

        Capture(
            world,
            changedGraph,
            sourceGraph,
            resnapshotAll: false,
            changes,
            baselineAddressCapacity: 64,
            ref rebuilds,
            out NavigationGridBaselineCapture[] deltaCaptures);

        deltaCaptures[0].IsRequested.Should().BeTrue();
        deltaCaptures[0].IsDelta.Should().BeTrue();
        deltaCaptures[0].AddressCount.Should().Be(2);
        rebuilds.Count.Should().Be(0,
            "the old instance's chunked rebuild must not survive exact delta capture");
        stale.RetainedBytes.Should().Be(staleBytes);
        stale.PersistentPageCount.Should().Be(stalePages,
            "discarding registry ownership must not mutate the immutable retained rebuild");

        NavigationMapInstance materialized = changed.MaterializeDelta(
            source,
            deltaCaptures[0],
            instanceVersion: 3);
        materialized.IsMaterialized.Should().BeTrue();
        materialized.LastCopiedPhysicalPages.Should().Be(1,
            "two exact-delta addresses in one dynamic slot page copy that page only once");
        dynamicIndices.Should().OnlyContain(index => materialized.IsPhysicallyPresent(index));
    }

    [Fact]
    public void OversizedNewAddressDelta_ShouldResumeOneFullBaselineRebuildWithoutTruncation()
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(128, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, cell)
            .Build();
        var sourceState = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: 0,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0);
        NavigationMapInstance source = NavigationMapInstanceTestFactory.Compose(
            world,
            sourceState,
            previous: null,
            instanceVersion: 1);
        var sourceGraph = new NavigationWorldGraph(1, new[] { source });
        var sets = new NavigationCellOverlayOperation[65];
        var dynamicAddresses = PersistentVoxelIndexMap<byte>.Empty;
        for (int x = 1; x <= sets.Length; x++)
        {
            VoxelIndex index = new(x, 0, 0);
            sets[x - 1] = NavigationCellOverlayOperation.Set(index, cell);
            dynamicAddresses = dynamicAddresses.Set(index, 0);
        }
        var delta = new NavigationMapOverlayDelta("map", sets);
        NavigationMapOverlayState overlay = NavigationMapOverlayState.Empty.Apply(delta, 2);
        var changedState = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: 0,
            overlay,
            dynamicSlotGeneration: 1,
            dynamicAddresses);
        var compose = new NavigationMapInstance.ComposeWork(changedState, source, delta, version: 2);
        var composeMeter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 0; frame < 64 && !compose.Advance(composeMeter); frame++)
            composeMeter.Reset();
        NavigationMapInstance changed = compose.Result;
        var changedGraph = new NavigationWorldGraph(2, new[] { changed });
        var preparedOverlay = new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(new[] { delta }));
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(preparedOverlay, operationSequence: 2)
        };
        PersistentStringMap<NavigationBaselineRebuild> rebuilds =
            PersistentStringMap<NavigationBaselineRebuild>.Empty;

        Capture(
            world,
            changedGraph,
            sourceGraph,
            resnapshotAll: false,
            changes,
            baselineAddressCapacity: 64,
            ref rebuilds,
            out NavigationGridBaselineCapture[] prefix);

        prefix[0].IsRequested.Should().BeFalse(
            "65 new addresses cannot be represented by a 64-slot exact delta");
        rebuilds.Count.Should().Be(1);
        NavigationBaselineRebuild retained = rebuilds.GetValueAt(0);
        retained.IsComplete.Should().BeFalse();

        Capture(
            world,
            changedGraph,
            sourceGraph,
            resnapshotAll: false,
            changes,
            baselineAddressCapacity: 64,
            ref rebuilds,
            out NavigationGridBaselineCapture[] completed);

        rebuilds.GetValueAt(0).Should().BeSameAs(retained,
            "the second frame must resume rather than replace the retained prefix");
        completed[0].IsRequested.Should().BeTrue();
        completed[0].IsDelta.Should().BeFalse();
        completed[0].AddressCount.Should().Be(66);
        retained.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void AuthoredBaseline_ShouldHonorItsExactRetainedByteBoundaryWithoutPublishingAPrefix()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateAuthoredSource(world, out _);
        var addresses = new VoxelIndex[1];
        var covered = new GridCoveredAddress[1];
        var reference = new NavigationBaselineRebuild(source);

        reference.Advance(
            world,
            source,
            maximumAddresses: 1,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture referenceCapture,
            out bool referenceCompleted).Should().Be(1);

        referenceCompleted.Should().BeTrue();
        referenceCapture.HasBaseline.Should().BeTrue();
        long exactRetainedBytes = reference.RetainedBytes;

        var insufficient = new NavigationBaselineRebuild(source);
        insufficient.Advance(
            world,
            source,
            maximumAddresses: 1,
            maximumRetainedBytes: exactRetainedBytes - 1,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture rejectedCapture,
            out bool rejectedCompleted).Should().Be(1);

        rejectedCompleted.Should().BeFalse();
        rejectedCapture.IsRequested.Should().BeFalse(
            "an over-budget physical page must not escape as a partial baseline");
        insufficient.IsCapacityBlocked.Should().BeTrue();
        insufficient.Advance(
            world,
            source,
            maximumAddresses: 1,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out _,
            out _).Should().Be(0,
                "a rejected retained prefix stays fail-closed for its owning rebuild");

        var exact = new NavigationBaselineRebuild(source);
        exact.Advance(
            world,
            source,
            maximumAddresses: 1,
            exactRetainedBytes,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture exactCapture,
            out bool exactCompleted).Should().Be(1);
        exactCompleted.Should().BeTrue();
        exactCapture.HasBaseline.Should().BeTrue(
            "the exact retained-byte ceiling is admissible rather than treated as over capacity");
    }

    [Fact]
    public void DefaultDiscovery_ShouldFailClosedWhenTheDiscoveredPhysicalSetExceedsSeedCapacity()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out VoxelGrid grid,
            out VoxelIndex originalPhysical,
            out VoxelIndex authored);
        var rebuild = new NavigationBaselineRebuild(source);
        var addresses = new VoxelIndex[3];
        var covered = new GridCoveredAddress[3];

        rebuild.Advance(
            world,
            source,
            maximumAddresses: source.DynamicSlotCount,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture seedCapture,
            out bool seedCompleted).Should().Be(source.DynamicSlotCount);

        seedCompleted.Should().BeFalse();
        seedCapture.IsRequested.Should().BeFalse();
        long seedRetainedBytes = rebuild.RetainedBytes;
        var newlyPhysical = new VoxelIndex(1, 0, 0);
        grid.TryAddVoxel(newlyPhysical, out _).Should().BeTrue();

        rebuild.Advance(
            world,
            source,
            maximumAddresses: addresses.Length,
            maximumRetainedBytes: seedRetainedBytes,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture rejectedCapture,
            out bool rejectedCompleted).Should().BeGreaterThan(0);

        rejectedCompleted.Should().BeFalse();
        rejectedCapture.IsRequested.Should().BeFalse(
            "newly discovered physical state cannot publish past the retained seed ceiling");
        rebuild.IsCapacityBlocked.Should().BeTrue();
        source.IsPhysicallyPresent(originalPhysical).Should().BeTrue();
        source.IsPhysicallyPresent(newlyPhysical).Should().BeFalse();
        source.TryGetSlot(authored, out _).Should().BeTrue(
            "capacity rejection must leave both physical and authored source ownership unchanged");
    }

    [Fact]
    public void RemovedGrid_ShouldCompleteBothBaselineKindsAsExplicitMissingCaptures()
    {
        using var authoredWorld = new GridWorld();
        NavigationMapInstance authored = CreateAuthoredSource(authoredWorld, out ushort authoredGrid);
        authoredWorld.TryRemoveGrid(authoredGrid).Should().BeTrue();

        var addresses = new VoxelIndex[2];
        var covered = new GridCoveredAddress[2];
        var authoredRebuild = new NavigationBaselineRebuild(authored);
        authoredRebuild.Advance(
            authoredWorld,
            authored,
            maximumAddresses: addresses.Length,
            long.MaxValue,
            int.MaxValue,
            addresses,
            covered,
            out NavigationGridBaselineCapture authoredCapture,
            out bool authoredCompleted).Should().Be(1);

        authoredCompleted.Should().BeTrue();
        authoredCapture.IsRequested.Should().BeTrue();
        authoredCapture.HasBaseline.Should().BeFalse();
        authored.Materialize(authoredCapture, instanceVersion: 2).IsMaterialized.Should().BeFalse(
            "grid removal must retire stale authored physical state rather than reopen it");

        using var defaultWorld = new GridWorld();
        NavigationMapInstance withDefault = CreateDefaultSource(
            defaultWorld,
            out VoxelGrid defaultGrid,
            out _,
            out _);
        defaultWorld.TryRemoveGrid(defaultGrid.GridIndex).Should().BeTrue();
        var defaultRebuild = new NavigationBaselineRebuild(withDefault);
        bool defaultCompleted = false;
        NavigationGridBaselineCapture defaultCapture = default;
        for (int step = 0; step < 4 && !defaultCompleted; step++)
        {
            defaultRebuild.Advance(
                defaultWorld,
                withDefault,
                maximumAddresses: addresses.Length,
                long.MaxValue,
                int.MaxValue,
                addresses,
                covered,
                out defaultCapture,
                out defaultCompleted);
        }

        defaultCompleted.Should().BeTrue();
        defaultCapture.IsRequested.Should().BeTrue();
        defaultCapture.HasBaseline.Should().BeFalse();
        withDefault.Materialize(defaultCapture, instanceVersion: 2).IsMaterialized.Should().BeFalse(
            "default discovery must also fail closed when its grid generation has disappeared");
    }

    [Fact]
    public void AffectedMapStampRollover_ShouldClearPriorVisitMarksBeforeCollection()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        var graph = new NavigationWorldGraph(1, new[] { source });
        NavigationGridGenerationIdentity identity = source.GridIdentity;
        var resnapshotScopes = new[]
        {
            new NavigationGridChangeScope(
                source.Map.GridBinding.Key,
                identity.WorldSpawnToken,
                identity.GridIndex,
                identity.GridSpawnToken)
        };
        var captures = new NavigationGridBaselineCapture[1];
        PersistentStringMap<NavigationBaselineRebuild> rebuilds =
            PersistentStringMap<NavigationBaselineRebuild>.Empty;
        var addresses = new VoxelIndex[8];
        var covered = new GridCoveredAddress[8];
        var deferred = new NavigationGridChangeScope[1];
        var ordinals = new int[1];
        var stamps = new[] { 1 };
        int stamp = -1;

        int affectedCount = graph.CaptureMaintenanceSnapshot(
            world,
            graph,
            System.ReadOnlySpan<GridEventInfo>.Empty,
            resnapshotScopes,
            resnapshotAll: false,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            blockAll: false,
            System.Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            maximumActiveSnapshotBytes: long.MaxValue,
            maximumPersistentGraphPages: int.MaxValue,
            captures,
            ref rebuilds,
            addresses,
            covered,
            deferred,
            out _,
            out bool deferAll,
            ordinals,
            stamps,
            ref stamp,
            out int collectedCount);

        stamp.Should().Be(1);
        affectedCount.Should().Be(1);
        collectedCount.Should().Be(1);
        rebuilds.Count.Should().Be(1,
            "the collected default-cell map must enter bounded baseline discovery");
        deferAll.Should().BeFalse();
    }

    [Fact]
    public void SnapshotCapture_ShouldEscalateWhenDeferredScopeOrRebuildCapacityIsUnavailable()
    {
        using var world = new GridWorld();
        NavigationMapInstance source = CreateDefaultSource(
            world,
            out _,
            out _,
            out _);
        var graph = new NavigationWorldGraph(1, new[] { source });
        var budget = new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 1,
            maxBaselineAddresses: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 1);
        var captures = new NavigationGridBaselineCapture[1];
        var addressScratch = new VoxelIndex[1];
        var coveredScratch = new GridCoveredAddress[1];
        var ordinals = new int[1];
        var stamps = new int[1];
        int stamp = 0;
        PersistentStringMap<NavigationBaselineRebuild> rebuilds =
            PersistentStringMap<NavigationBaselineRebuild>.Empty;

        graph.CaptureMaintenanceSnapshot(
            world,
            graph,
            System.ReadOnlySpan<GridEventInfo>.Empty,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            resnapshotAll: true,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            blockAll: false,
            System.Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0,
            new MaintenanceWorkMeter(budget),
            maximumActiveSnapshotBytes: long.MaxValue,
            maximumPersistentGraphPages: int.MaxValue,
            captures,
            ref rebuilds,
            addressScratch,
            coveredScratch,
            System.Array.Empty<NavigationGridChangeScope>(),
            out int deferredCount,
            out bool deferAll,
            ordinals,
            stamps,
            ref stamp,
            out _);

        captures[0].IsRequested.Should().BeFalse();
        rebuilds.Count.Should().Be(1,
            "the one-address prefix is retained for the next deterministic frame");
        deferredCount.Should().Be(0);
        deferAll.Should().BeTrue(
            "an unrepresentable deferred map scope must conservatively close every scope");

        captures[0] = default;
        rebuilds = PersistentStringMap<NavigationBaselineRebuild>.Empty;
        stamp = 0;
        var deferred = new NavigationGridChangeScope[1];
        graph.CaptureMaintenanceSnapshot(
            world,
            graph,
            System.ReadOnlySpan<GridEventInfo>.Empty,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            resnapshotAll: true,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            blockAll: false,
            System.Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0,
            new MaintenanceWorkMeter(budget),
            maximumActiveSnapshotBytes: graph.RetainedBytes,
            maximumPersistentGraphPages: graph.PersistentPageCount,
            captures,
            ref rebuilds,
            addressScratch,
            coveredScratch,
            deferred,
            out deferredCount,
            out deferAll,
            ordinals,
            stamps,
            ref stamp,
            out _);

        captures[0].IsRequested.Should().BeFalse();
        rebuilds.Count.Should().Be(0,
            "the rebuild object itself cannot be retained beyond the exact graph ceiling");
        deferredCount.Should().Be(1);
        deferAll.Should().BeFalse();
        deferred[0].ConfigurationKey.Should().Be(source.Map.GridBinding.Key);
    }

    private static void Capture(
        GridWorld world,
        NavigationWorldGraph graph,
        NavigationWorldGraph previous,
        bool resnapshotAll,
        NavigationOperationFrameChange[] changes,
        int baselineAddressCapacity,
        ref PersistentStringMap<NavigationBaselineRebuild> rebuilds,
        out NavigationGridBaselineCapture[] captures)
    {
        captures = new NavigationGridBaselineCapture[1];
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 8,
            maxBaselineAddresses: baselineAddressCapacity,
            maxOverlaySlots: 8,
            maxComponentNodes: 8,
            maxSeamCandidateProbes: 8,
            maxExplicitEdges: 8,
            maxDependencyEntries: 8));
        var addressScratch = new VoxelIndex[baselineAddressCapacity];
        var coveredScratch = new GridCoveredAddress[baselineAddressCapacity];
        var deferred = new NavigationGridChangeScope[1];
        var ordinals = new int[1];
        var stamps = new int[1];
        int stamp = 0;
        graph.CaptureMaintenanceSnapshot(
            world,
            previous,
            System.ReadOnlySpan<GridEventInfo>.Empty,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            resnapshotAll,
            System.ReadOnlySpan<NavigationGridChangeScope>.Empty,
            blockAll: false,
            changes,
            changes.Length,
            meter,
            maximumActiveSnapshotBytes: long.MaxValue,
            maximumPersistentGraphPages: int.MaxValue,
            captures,
            ref rebuilds,
            addressScratch,
            coveredScratch,
            deferred,
            out _,
            out _,
            ordinals,
            stamps,
            ref stamp,
            out _);
    }

    private static NavigationMapInstance CreateDefaultSource(
        GridWorld world,
        out VoxelGrid grid,
        out VoxelIndex originalPhysical,
        out VoxelIndex authored)
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        originalPhysical = default;
        world.TryAddGrid(configuration, new[] { originalPhysical }, out ushort gridIndex)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell defaultCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationCell authoredCell = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(defaultCell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        authored = new VoxelIndex(2, 0, 0);
        NavigationCellOverlayOperation set =
            NavigationCellOverlayOperation.Set(authored, authoredCell);
        NavigationMapOverlayState overlay = NavigationMapOverlayState.Empty.Apply(set, 1);
        PersistentVoxelIndexMap<byte> dynamicAddresses =
            PersistentVoxelIndexMap<byte>.Empty.Set(authored, 0);
        var state = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            prepared.RetainedBytes,
            overlay,
            dynamicSlotGeneration: 1,
            dynamicAddresses,
            prepared.BakedCellLookup);
        grid = world.ActiveGrids[gridIndex];
        return NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
    }

    private static NavigationMapInstance CreateAuthoredSource(
        GridWorld world,
        out ushort gridIndex)
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("authored", binding)
            .AddCell(default, new NavigationCell(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One))
            .Build();
        var state = new NavigationOperationCandidate.MapState(
            map,
            bakeVersion: 1,
            preparedMapRetainedBytes: new PreparedNavigationMap(map, bakeVersion: 1).RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0);
        return NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
    }
}
