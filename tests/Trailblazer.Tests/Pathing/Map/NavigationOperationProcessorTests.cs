using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationOperationProcessorTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        Fixed64.Zero,
        Fixed64.One,
        Fixed64.One);

    [Fact]
    public void CheckpointStamp_UsesValueEqualityAndStableRepeatedHashing()
    {
        var first = new NavigationMapCheckpointStamp("map", 10, 42);
        var second = new NavigationMapCheckpointStamp("map", 10, 42);

        second.Should().Be(first);
        second.GetHashCode().Should().Be(first.GetHashCode());
        first.GetHashCode().Should().Be(first.GetHashCode());
    }

    [Fact]
    public void RejectedLaterReplacement_DoesNotSupersedeEarlierValidInstall()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration sharedBinding = CreateBinding(Vector3d.Zero);
        NavigationMap firstMap = CreateMap("first", sharedBinding, new VoxelIndex(0, 0, 0));
        NavigationMap duplicateBindingMap = CreateMap("duplicate", sharedBinding, new VoxelIndex(0, 0, 0));
        NavigationMap validInstall = CreateMap(
            "installed",
            CreateBinding(new Vector3d(10, 0, 0)),
            new VoxelIndex(0, 0, 0));

        NavigationMapCommitOperation first = Commit(firstMap, 1, 0);
        NavigationMapCommitOperation valid = Commit(validInstall, 2, 1);
        NavigationMapCommitOperation invalid = Commit(duplicateBindingMap, 3, 1);

        processor.Admit(first).Should().BeTrue();
        processor.ProcessFrame(0);
        processor.Admit(valid).Should().BeTrue();
        processor.Admit(invalid).Should().BeTrue();
        processor.ProcessFrame(1);

        valid.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        invalid.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        invalid.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetMap("installed", out _).Should().BeTrue();
    }

    [Fact]
    public void CrossMapOverlay_ValidatesOneProspectiveCandidateAndPublishesAllOrNothing()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        VoxelIndex leftSource = new(2, 0, 0);
        NavigationMap left = CreateMap(
            "left",
            CreateBinding(Vector3d.Zero),
            leftSource);
        VoxelIndex witness = new(0, 0, 0);
        NavigationMap middle = CreateMap(
            "middle",
            CreateBinding(new Vector3d(3, 0, 0)),
            witness);
        NavigationMap right = CreateMap(
            "right",
            CreateBinding(new Vector3d(4, 0, 0)),
            new VoxelIndex(1, 0, 0));
        NavigationMapCommitOperation leftCommit = Commit(left, 1, 0);
        NavigationMapCommitOperation middleCommit = Commit(middle, 2, 0);
        NavigationMapCommitOperation rightCommit = Commit(right, 3, 0);
        processor.Admit(leftCommit).Should().BeTrue();
        processor.Admit(middleCommit).Should().BeTrue();
        processor.Admit(rightCommit).Should().BeTrue();
        processor.ProcessFrame(0);

        VoxelIndex created = new(0, 0, 0);
        Vector3d entry = GetCenter(left.GridBinding, leftSource);
        Vector3d exit = GetCenter(right.GridBinding, created);
        entry = new Vector3d(entry.X, Fixed64.Zero, entry.Z);
        exit = new Vector3d(exit.X, Fixed64.Zero, exit.Z);
        NavigationConnection link = new(
            "link",
            leftSource,
            new NavigationCellAddress("right", created),
            entry,
            exit,
            Fixed64.Zero,
            Fixed64.Half,
            new[] { new NavigationCellAddress("middle", witness) });
        var transaction = new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(
                "right",
                new[] { NavigationCellOverlayOperation.Set(created, SolidCell) }),
            new NavigationMapOverlayDelta(
                "left",
                connections: new[] { NavigationConnectionOverlayOperation.Upsert(link) })
        });
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(transaction),
            4,
            1);

        processor.Admit(overlay).Should().BeTrue();
        processor.ProcessFrame(1);

        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetOverlay("left", out NavigationMapOverlayState leftOverlay).Should().BeTrue();
        processor.Candidate.TryGetOverlay("right", out NavigationMapOverlayState rightOverlay).Should().BeTrue();
        leftOverlay.Connections.Should().ContainSingle();
        rightOverlay.Cells.Should().ContainSingle();
    }

    [Fact]
    public void CrossMapOverlay_RejectsNonContiguousWitnessCorridorWithoutMutation()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        VoxelIndex source = new(2, 0, 0);
        VoxelIndex witness = default;
        VoxelIndex destination = default;
        NavigationMap left = CreateMap("left", CreateBinding(Vector3d.Zero), source);
        NavigationMap middle = CreateMap("middle", CreateBinding(new Vector3d(5, 0, 0)), witness);
        NavigationMap right = CreateMap("right", CreateBinding(new Vector3d(6, 0, 0)), destination);
        processor.Admit(Commit(left, 1, 0)).Should().BeTrue();
        processor.Admit(Commit(middle, 2, 0)).Should().BeTrue();
        processor.Admit(Commit(right, 3, 0)).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationConnection invalid = new(
            "gap",
            source,
            new NavigationCellAddress("right", destination),
            GetFootAnchor(left.GridBinding, source),
            GetFootAnchor(right.GridBinding, destination),
            Fixed64.Zero,
            Fixed64.Half,
            new[] { new NavigationCellAddress("middle", witness) });
        var delta = new NavigationMapOverlayDelta(
            "left",
            connections: new[] { NavigationConnectionOverlayOperation.Upsert(invalid) });
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[] { delta })),
            4,
            1);

        processor.Admit(operation).Should().BeTrue();
        processor.ProcessFrame(1);

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetOverlay("left", out NavigationMapOverlayState overlay).Should().BeTrue();
        overlay.Connections.Should().BeEmpty();
    }

    [Fact]
    public void InstallingPreviouslyAbsentTarget_RejectsDormantDanglingConnectionTransactionally()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration leftBinding = CreateBinding(Vector3d.Zero);
        VoxelIndex source = new(0, 0, 0);
        NavigationConnection dormant = new(
            "dormant",
            source,
            new NavigationCellAddress("right", new VoxelIndex(1, 0, 0)),
            GetCenter(leftBinding, source),
            new Vector3d((Fixed64)11, Fixed64.One / 2, Fixed64.One / 2),
            Fixed64.One,
            Fixed64.One);
        NavigationMap left = new NavigationMapBuilder("left", leftBinding)
            .AddCell(source, SolidCell)
            .AddConnection(dormant)
            .Build();
        NavigationMap right = CreateMap(
            "right",
            CreateBinding(new Vector3d(10, 0, 0)),
            new VoxelIndex(0, 0, 0));
        NavigationMapCommitOperation leftCommit = Commit(left, 1, 0);
        NavigationMapCommitOperation rightCommit = Commit(right, 2, 1);

        processor.Admit(leftCommit).Should().BeTrue();
        processor.ProcessFrame(0);
        processor.Admit(rightCommit).Should().BeTrue();
        processor.ProcessFrame(1);

        leftCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        rightCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rightCommit.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetMap("right", out _).Should().BeFalse();
    }

    [Fact]
    public void AdmissionAndFrameBatchLimitsCompleteReceiptsDeterministically()
    {
        NavigationOperationLimits limits = CreateLimits(maxPendingOperations: 1, maxBatchItems: 1);
        var processor = new NavigationOperationProcessor(limits);
        NavigationMap firstMap = CreateMap("first", CreateBinding(Vector3d.Zero), default);
        NavigationMap secondMap = CreateMap("second", CreateBinding(new Vector3d(10, 0, 0)), default);
        NavigationMapCommitOperation first = Commit(firstMap, 1, 0);
        NavigationMapCommitOperation rejectedAtAdmission = Commit(secondMap, 2, 0);

        processor.Admit(first).Should().BeTrue();
        processor.Admit(rejectedAtAdmission).Should().BeFalse();
        rejectedAtAdmission.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.ProcessFrame(0);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        first.Receipt.PublishedFrame.Should().Be(0);
    }

    [Fact]
    public void ReAdmittingSamePendingOperation_DoesNotCompleteOrDoubleApplyItsReceipt()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMapCommitOperation operation = Commit(
            CreateMap("map", CreateBinding(Vector3d.Zero), default),
            1,
            0);

        processor.Admit(operation).Should().BeTrue();
        processor.Admit(operation).Should().BeFalse();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        Action process = () => processor.ProcessFrame(0);
        process.Should().NotThrow();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.MapCount.Should().Be(1);
    }

    [Fact]
    public void CapacityRejectedSubmission_BurnsSequenceAndCannotBeReadmitted()
    {
        var processor = new NavigationOperationProcessor(CreateLimits(maxPendingOperations: 1));
        NavigationMapCommitOperation admitted = Commit(
            CreateMap("first", CreateBinding(Vector3d.Zero), default),
            1,
            0);
        NavigationMapCommitOperation rejected = Commit(
            CreateMap("second", CreateBinding(new Vector3d(10, 0, 0)), default),
            2,
            1);

        processor.Admit(admitted).Should().BeTrue();
        processor.Admit(rejected).Should().BeFalse();
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.ProcessFrame(0);

        processor.Admit(rejected).Should().BeFalse();
        var reusedSequence = new NavigationMapRemoveOperation("first", 2, 1);
        processor.Admit(reusedSequence).Should().BeFalse();
        reusedSequence.Receipt.Rejection.Should().Be(NavigationOperationRejection.DuplicateSequence);
        processor.Candidate.MapCount.Should().Be(1);
    }

    [Fact]
    public void OverlayAdmission_RejectsScratchLargerThanConfiguredBatchCapacity()
    {
        var processor = new NavigationOperationProcessor(CreateLimits(
            maxBatchItems: 1,
            maxBatchSortScratchBytes: 2_200));
        processor.Admit(Commit(
            CreateMap("map", CreateBinding(Vector3d.Zero), default),
            1,
            0)).Should().BeTrue();
        processor.ProcessFrame(0);

        var delta = new NavigationMapOverlayDelta(
            "map",
            new[]
            {
                NavigationCellOverlayOperation.Suppress(default),
                NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0))
            });
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[] { delta })),
            2,
            1);

        processor.Admit(operation).Should().BeFalse();
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
    }

    [Fact]
    public void BatchLimit_CarriesCanonicalSuffixToLaterFrame()
    {
        var processor = new NavigationOperationProcessor(CreateLimits(maxBatchItems: 1));
        NavigationMapCommitOperation first = Commit(
            CreateMap("first", CreateBinding(Vector3d.Zero), default),
            1,
            0);
        NavigationMapCommitOperation second = Commit(
            CreateMap("second", CreateBinding(new Vector3d(10, 0, 0)), default),
            2,
            0);

        processor.Admit(first).Should().BeTrue();
        processor.Admit(second).Should().BeTrue();
        processor.ProcessFrame(0);

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        processor.ProcessFrame(1);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.PublishedFrame.Should().Be(1);
    }

    [Fact]
    public void SuccessfulInstallThenRemove_MarksInstallSupersededAndLeavesMapAbsent()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMap map = CreateMap("map", CreateBinding(Vector3d.Zero), default);
        NavigationMapCommitOperation install = Commit(map, 1, 0);
        var remove = new NavigationMapRemoveOperation("map", 2, 0);

        processor.Admit(install).Should().BeTrue();
        processor.Admit(remove).Should().BeTrue();
        processor.ProcessFrame(0);

        install.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetMap("map", out _).Should().BeFalse();
    }

    [Fact]
    public void LaterExactOverlayKeys_MarkEarlierSuccessfulOverlaySuperseded()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMap map = CreateMap("map", CreateBinding(Vector3d.Zero), default);
        NavigationMapCommitOperation install = Commit(map, 1, 0);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationOverlayCommitOperation first = Overlay(
            "map",
            NavigationCellOverlayOperation.Suppress(default),
            sequence: 2,
            frame: 1);
        NavigationOverlayCommitOperation last = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(default, SolidCell),
            sequence: 3,
            frame: 1);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(last).Should().BeTrue();
        processor.ProcessFrame(1);

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState overlay).Should().BeTrue();
        overlay.Cells.Should().ContainSingle();
        overlay.Cells[0].Kind.Should().Be(NavigationCellOverlayOperationKind.Set);
    }

    [Fact]
    public void InterveningUnrelatedOverlay_DoesNotHideLaterSupersedingOverlay()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        processor.Admit(Commit(
            CreateMap("map", CreateBinding(Vector3d.Zero), default),
            1,
            0)).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationOverlayCommitOperation first = Overlay(
            "map",
            NavigationCellOverlayOperation.Suppress(default),
            sequence: 2,
            frame: 1);
        NavigationOverlayCommitOperation unrelated = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), SolidCell),
            sequence: 3,
            frame: 1);
        NavigationOverlayCommitOperation last = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(default, SolidCell),
            sequence: 4,
            frame: 1);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(unrelated).Should().BeTrue();
        processor.Admit(last).Should().BeTrue();

        processor.ProcessFrame(1);

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        unrelated.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void SplitLaterKeyCoverage_MarksEarlierMultiKeyOverlaySuperseded()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        processor.Admit(Commit(
            CreateMap("map", CreateBinding(Vector3d.Zero), default),
            1,
            0)).Should().BeTrue();
        processor.ProcessFrame(0);

        var firstDelta = new NavigationMapOverlayDelta(
            "map",
            new[]
            {
                NavigationCellOverlayOperation.Suppress(default),
                NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0))
            });
        var first = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[] { firstDelta })),
            2,
            1);
        NavigationOverlayCommitOperation replaceFirst = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(default, SolidCell),
            3,
            1);
        NavigationOverlayCommitOperation unrelated = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), SolidCell),
            4,
            1);
        NavigationOverlayCommitOperation replaceSecond = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), SolidCell),
            5,
            1);

        processor.Admit(first).Should().BeTrue();
        processor.Admit(replaceFirst).Should().BeTrue();
        processor.Admit(unrelated).Should().BeTrue();
        processor.Admit(replaceSecond).Should().BeTrue();
        processor.ProcessFrame(1);

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        replaceFirst.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        unrelated.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        replaceSecond.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void InterveningSameMapOverlay_DoesNotHideLaterRemoveSupersedingInstall()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMapCommitOperation install = Commit(
            CreateMap("map", CreateBinding(Vector3d.Zero), default),
            1,
            0);
        NavigationOverlayCommitOperation overlay = Overlay(
            "map",
            NavigationCellOverlayOperation.Suppress(default),
            sequence: 2,
            frame: 0);
        var remove = new NavigationMapRemoveOperation("map", 3, 0);
        processor.Admit(install).Should().BeTrue();
        processor.Admit(overlay).Should().BeTrue();
        processor.Admit(remove).Should().BeTrue();

        processor.ProcessFrame(0);

        install.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void EmptyProcessFrame_DoesNotAllocateAfterWarmup()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        processor.ProcessFrame(0);

        long before = System.GC.GetAllocatedBytesForCurrentThread();
        processor.ProcessFrame(1);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
    }

    [Fact]
    public void DuplicateRegressingAndLateSchedulesRejectWithDistinctReasons()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMap map = CreateMap("map", CreateBinding(Vector3d.Zero), default);
        NavigationMapCommitOperation admitted = Commit(map, 2, 0);
        NavigationMapRemoveOperation duplicate = new("map", 2, 0);
        NavigationMapRemoveOperation regressing = new("map", 1, 0);

        processor.Admit(admitted).Should().BeTrue();
        processor.Admit(duplicate).Should().BeFalse();
        processor.Admit(regressing).Should().BeFalse();
        duplicate.Receipt.Rejection.Should().Be(NavigationOperationRejection.DuplicateSequence);
        regressing.Receipt.Rejection.Should().Be(NavigationOperationRejection.RegressingSequence);
        processor.ProcessFrame(0);

        NavigationMapRemoveOperation late = new("map", 3, 0);
        processor.Admit(late).Should().BeFalse();
        late.Receipt.Rejection.Should().Be(NavigationOperationRejection.LateEffectiveFrame);
    }

    [Fact]
    public void ClearCheckpointCommit_RejectsWhenOverlayHighWaterMoved()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NavigationMap map = CreateMap("map", binding, default);
        NavigationMapCommitOperation install = Commit(map, 1, 0, bakeVersion: 10);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);

        var delta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Suppress(default) });
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[] { delta })),
            2,
            1);
        processor.Admit(overlay).Should().BeTrue();
        processor.ProcessFrame(1);

        var staleStamp = new NavigationMapCheckpointStamp("map", 10, 0);
        var prepared = new PreparedNavigationMap(map, 11, staleStamp);
        var stale = new NavigationMapCommitOperation(prepared, OverlayReplacementPolicy.Clear, 3, 2);
        processor.Admit(stale).Should().BeTrue();
        processor.ProcessFrame(2);

        stale.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        stale.Receipt.Rejection.Should().Be(NavigationOperationRejection.Stale);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState state).Should().BeTrue();
        state.Cells.Should().ContainSingle();
    }

    [Fact]
    public void ReplacementWithReusedBakeVersion_IsRejectedAsStale()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NavigationMap original = CreateMap("map", binding, default);
        NavigationMap replacement = CreateMap("map", binding, new VoxelIndex(1, 0, 0));
        NavigationMapCommitOperation install = Commit(original, 1, 0, bakeVersion: 10);
        NavigationMapCommitOperation reusedVersion = Commit(replacement, 2, 1, bakeVersion: 10);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);
        processor.Admit(reusedVersion).Should().BeTrue();

        processor.ProcessFrame(1);

        reusedVersion.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        reusedVersion.Receipt.Rejection.Should().Be(NavigationOperationRejection.Stale);
        processor.Candidate.TryGetMap("map", out NavigationMap published).Should().BeTrue();
        published.Should().BeSameAs(original);
    }

    [Fact]
    public void RemoveAndReinstallWithReusedBakeVersion_IsRejectedAsStale()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NavigationMap map = CreateMap("map", binding, default);
        NavigationMapCommitOperation install = Commit(map, 1, 0, bakeVersion: 10);
        var remove = new NavigationMapRemoveOperation("map", 2, 1);
        NavigationMapCommitOperation reusedVersion = Commit(map, 3, 2, bakeVersion: 10);

        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);
        processor.Admit(remove).Should().BeTrue();
        processor.ProcessFrame(1);
        processor.Admit(reusedVersion).Should().BeTrue();
        processor.ProcessFrame(2);

        reusedVersion.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        reusedVersion.Receipt.Rejection.Should().Be(NavigationOperationRejection.Stale);
        processor.Candidate.MapCount.Should().Be(0);
    }

    [Fact]
    public void RetainedMapIdentityCapacity_BoundsUniqueMapIdChurn()
    {
        var processor = new NavigationOperationProcessor(CreateLimits(maxMaps: 1, maxRetainedMapIdentities: 1));
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NavigationMap first = CreateMap("first", binding, default);
        NavigationMap second = CreateMap("second", binding, default);
        NavigationMapCommitOperation installFirst = Commit(first, 1, 0);
        var removeFirst = new NavigationMapRemoveOperation("first", 2, 1);
        NavigationMapCommitOperation installSecond = Commit(second, 3, 2);

        processor.Admit(installFirst).Should().BeTrue();
        processor.ProcessFrame(0);
        processor.Admit(removeFirst).Should().BeTrue();
        processor.ProcessFrame(1);
        processor.Admit(installSecond).Should().BeTrue();
        processor.ProcessFrame(2);

        installSecond.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        installSecond.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.Candidate.MapCount.Should().Be(0);
    }

    [Fact]
    public void OperationLimits_RejectBatchSizeAboveDeterministicCoalescingCeiling()
    {
        Action create = () => _ = CreateLimits(
            maxBatchItems: NavigationOperationLimits.MaximumBatchItems + 1);

        create.Should().Throw<ArgumentException>();
    }

    private static NavigationMapCommitOperation Commit(
        NavigationMap map,
        long sequence,
        int frame,
        long bakeVersion = 1) => new(
            new PreparedNavigationMap(map, bakeVersion),
            OverlayReplacementPolicy.Clear,
            sequence,
            frame);

    private static NavigationOverlayCommitOperation Overlay(
        string mapId,
        NavigationCellOverlayOperation operation,
        long sequence,
        int frame) => new(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta(mapId, new[] { operation }) })),
            sequence,
            frame);

    private static NavigationMap CreateMap(
        string mapId,
        NormalizedGridConfiguration binding,
        VoxelIndex cell) => new NavigationMapBuilder(mapId, binding)
            .AddCell(cell, SolidCell)
            .Build();

    private static NormalizedGridConfiguration CreateBinding(Vector3d minimum)
    {
        var configuration = new GridConfiguration(
            minimum,
            minimum + new Vector3d(3, 2, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static Vector3d GetCenter(NormalizedGridConfiguration binding, VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return prism.Center;
    }

    private static Vector3d GetFootAnchor(NormalizedGridConfiguration binding, VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private static NavigationOperationLimits CreateLimits(
        int maxPendingOperations = 32,
        int maxBatchItems = 32,
        int maxMaps = 16,
        int maxRetainedMapIdentities = 32,
        long maxBatchSortScratchBytes = 1_000_000) => new(
            maxPendingOperations,
            maxPendingDescriptorBytes: 1_000_000,
            maxPreparedMapBytes: 1_000_000,
            maxBatchItems,
            maxBatchDescriptorBytes: 1_000_000,
            maxBatchSortScratchBytes,
            maxCorridorCells: 64,
            maxMaps,
            maxRetainedMapIdentities,
            maxOverlayCellsPerMap: 1_000,
            maxOverlayConnectionsPerMap: 1_000,
            maxOverlayTransitionsPerMap: 1_000,
            maxOverlayCells: 10_000,
            maxOverlayConnections: 10_000,
            maxOverlayTransitions: 10_000);
}
