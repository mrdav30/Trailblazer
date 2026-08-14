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
        default,
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
        leftOverlay.ConnectionCount.Should().Be(1);
        rightOverlay.CellCount.Should().Be(1);
    }

    [Fact]
    public void MultiWitnessConnection_ShouldConvergeWithOneExplicitEdgePerFrame()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        VoxelIndex source = new(2, 0, 0);
        NavigationMap left = CreateMap("left", CreateBinding(Vector3d.Zero), source);
        NavigationMap witnessA = CreateMap("witness-a", CreateBinding(new Vector3d(3, 0, 0)), default);
        NavigationMap witnessB = CreateMap("witness-b", CreateBinding(new Vector3d(4, 0, 0)), default);
        NavigationMap right = CreateMap("right", CreateBinding(new Vector3d(5, 0, 0)), default);
        processor.Admit(Commit(left, 1, 0)).Should().BeTrue();
        processor.Admit(Commit(witnessA, 2, 0)).Should().BeTrue();
        processor.Admit(Commit(witnessB, 3, 0)).Should().BeTrue();
        processor.Admit(Commit(right, 4, 0)).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationConnection connection = new(
            "multi-witness",
            source,
            new NavigationCellAddress("right", default),
            GetFootAnchor(left.GridBinding, source),
            GetFootAnchor(right.GridBinding, default),
            Fixed64.Zero,
            Fixed64.Half,
            new[]
            {
                new NavigationCellAddress("witness-a", default),
                new NavigationCellAddress("witness-b", default)
            });
        NavigationOverlayCommitOperation operation = new(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "left",
                    connections: new[] { NavigationConnectionOverlayOperation.Upsert(connection) })
            })),
            5,
            1);
        processor.Admit(operation).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 8, 8, 1, 8));

        int frame = 1;
        while (operation.Receipt.Status == NavigationOperationStatus.Pending && frame < 32)
        {
            meter.Reset();
            processor.ProcessFrame(
                frame++,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter);
            meter.ExplicitEdges.Should().BeLessThanOrEqualTo(1);
            if (operation.Receipt.Status == NavigationOperationStatus.Pending)
            {
                processor.Candidate.TryGetOverlay("left", out NavigationMapOverlayState current)
                    .Should().BeTrue();
                current.ConnectionCount.Should().Be(0,
                    "a partially validated corridor must remain unpublished");
            }
        }

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        operation.Receipt.PublishedFrame.Should().BeGreaterThan(1);
        processor.Candidate.TryGetOverlay("left", out NavigationMapOverlayState overlay)
            .Should().BeTrue();
        overlay.ConnectionCount.Should().Be(1);
    }

    [Fact]
    public void OversizedBakedConnection_ShouldRejectAtCorridorCapacity()
    {
        var witnesses = new NavigationCellAddress[63];
        for (int i = 0; i < witnesses.Length; i++)
            witnesses[i] = new NavigationCellAddress($"witness-{i:D2}", default);
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NavigationConnection connection = new(
            "oversized",
            default,
            new NavigationCellAddress("destination", default),
            GetFootAnchor(binding, default),
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.Half,
            witnesses);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddConnection(connection)
            .Build();
        var operation = Commit(map, 1, 0);
        var processor = new NavigationOperationProcessor(CreateLimits());

        processor.Admit(operation).Should().BeTrue();
        processor.ProcessFrame(0);

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
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
        overlay.ConnectionCount.Should().Be(0);
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
    public void SuppressingAndRestoringIncidentCell_ShouldKeepAuthoredLinksDormant()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        VoxelIndex source = default;
        VoxelIndex destination = new(1, 0, 0);
        NavigationConnection connection = new(
            "step",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.Half);
        TraversalTransitionDefinition transition = new(
            "ladder",
            TraversalTransitionType.Climb,
            source,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", destination),
            TraversalMedium.Solid,
            TraversalCapability.Climb);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(connection)
            .AddTransition(transition)
            .Build();
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationOverlayCommitOperation suppress = Overlay(
            "map",
            NavigationCellOverlayOperation.Suppress(source),
            2,
            1);
        processor.Admit(suppress).Should().BeTrue();
        processor.ProcessFrame(1);

        suppress.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationOverlayCommitOperation restore = Overlay(
            "map",
            NavigationCellOverlayOperation.RevertToBake(source),
            3,
            2);
        processor.Admit(restore).Should().BeTrue();
        processor.ProcessFrame(2);

        restore.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void NewLinkUpsert_WithSameTransactionSuppression_ShouldPublishDormantOwner()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        VoxelIndex source = default;
        VoxelIndex destination = new(1, 0, 0);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .Build();
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationConnection connection = new(
            "new-link",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.Half);
        var delta = new NavigationMapOverlayDelta(
            "map",
            new[] { NavigationCellOverlayOperation.Suppress(source) },
            new[] { NavigationConnectionOverlayOperation.Upsert(connection) });
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[] { delta })),
            2,
            1);

        processor.Admit(operation).Should().BeTrue();
        processor.ProcessFrame(1);

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState overlay).Should().BeTrue();
        overlay.CellCount.Should().Be(1);
        overlay.ConnectionCount.Should().Be(1);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "new-link"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        record.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PreserveReplacement_ShouldValidateNewBakedLinkBehindSuppressedSource()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NormalizedGridConfiguration otherBinding = CreateBinding(new Vector3d(10, 0, 0));
        VoxelIndex source = default;
        VoxelIndex destination = new(1, 0, 0);
        NavigationMap original = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .Build();
        processor.Admit(Commit(original, 1, 0, bakeVersion: 1)).Should().BeTrue();
        NavigationMap other = new NavigationMapBuilder("other", otherBinding)
            .AddCell(default, SolidCell)
            .Build();
        processor.Admit(Commit(other, 2, 0, bakeVersion: 1)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationOverlayCommitOperation suppress = Overlay(
            "map",
            NavigationCellOverlayOperation.Suppress(source),
            3,
            1);
        processor.Admit(suppress).Should().BeTrue();
        processor.ProcessFrame(1);

        NavigationConnection invalid = new(
            "invalid",
            source,
            new NavigationCellAddress("other", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(otherBinding, destination),
            Fixed64.Zero,
            Fixed64.Half);
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(invalid)
            .Build();
        NavigationMapCommitOperation operation = new(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            4,
            2);

        processor.Admit(operation).Should().BeTrue();
        processor.ProcessFrame(2);

        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetMap("map", out NavigationMap published).Should().BeTrue();
        published.Should().BeSameAs(original);
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
    public void MeteredMapWork_ShouldCarryCommitAndRemovalWithoutOvertaking()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 1, 8, 8, 1));
        NormalizedGridConfiguration firstBinding = CreateBinding(Vector3d.Zero);
        var firstBuilder = new NavigationMapBuilder("first", firstBinding)
            .AddCell(default, SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell);
        for (int i = 0; i < 3; i++)
        {
            firstBuilder.AddTransition(new TraversalTransitionDefinition(
                $"to-later-{i}",
                TraversalTransitionType.Climb,
                default,
                TraversalMedium.Solid,
                new NavigationCellAddress("later", default),
                TraversalMedium.Solid,
                TraversalCapability.Climb));
        }
        NavigationMap firstMap = firstBuilder.Build();
        NavigationMapCommitOperation first = Commit(firstMap, 1, 0);
        NavigationMapCommitOperation later = Commit(
            CreateMap("later", CreateBinding(new Vector3d(10, 0, 0)), default),
            2,
            0);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(later).Should().BeTrue();

        processor.ProcessFrame(0, static (_, _, _, _) => NavigationCandidatePublication.Published, meter)
            .Should().Be(NavigationOperationFrameResult.Deferred);

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        later.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        processor.Candidate.MapCount.Should().Be(0);
        processor.AdvanceDeferredStructuralClosure(
                NavigationWorldGraph.Empty,
                meter,
                static (_, _) => true,
                out _)
            .Should().Be(NavigationDeferredStructuralClosureStatus.CloseAll);
        int frame = 1;
        while (later.Receipt.Status == NavigationOperationStatus.Pending && frame < 32)
        {
            meter.Reset();
            processor.ProcessFrame(frame++, static (_, _, _, _) => NavigationCandidatePublication.Published, meter);
        }
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        later.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        first.Receipt.PublishedFrame.Should().Be(later.Receipt.PublishedFrame);
        processor.Candidate.MapCount.Should().Be(2);

        var remove = new NavigationMapRemoveOperation("first", 3, frame);
        NavigationMapCommitOperation afterRemove = Commit(
            CreateMap("after", CreateBinding(new Vector3d(20, 0, 0)), default),
            4,
            frame);
        processor.Admit(remove).Should().BeTrue();
        processor.Admit(afterRemove).Should().BeTrue();
        meter.Reset();
        processor.ProcessFrame(frame++, static (_, _, _, _) => NavigationCandidatePublication.Published, meter)
            .Should().Be(NavigationOperationFrameResult.Deferred);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        afterRemove.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        processor.Candidate.TryGetMap("first", out _).Should().BeTrue();
        while (afterRemove.Receipt.Status == NavigationOperationStatus.Pending && frame < 64)
        {
            meter.Reset();
            processor.ProcessFrame(frame++, static (_, _, _, _) => NavigationCandidatePublication.Published, meter);
        }
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        afterRemove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        remove.Receipt.PublishedFrame.Should().Be(afterRemove.Receipt.PublishedFrame);
        processor.Candidate.TryGetMap("first", out _).Should().BeFalse();
        processor.Candidate.TryGetMap("after", out _).Should().BeTrue();
    }

    [Fact]
    public void MeteredPreserveReplacement_ShouldRejectInvalidAuthoredEdgeHiddenBySuppress()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NormalizedGridConfiguration otherBinding = CreateBinding(new Vector3d(10, 0, 0));
        VoxelIndex source = default;
        VoxelIndex invalidDestination = new(1, 0, 0);
        NavigationMap original = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .Build();
        processor.Admit(Commit(original, 1, 0)).Should().BeTrue();
        processor.Admit(Commit(
            CreateMap("other", otherBinding, default), 2, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationOverlayCommitOperation suppress = Overlay(
            "map", NavigationCellOverlayOperation.Suppress(source), 3, 1);
        processor.Admit(suppress).Should().BeTrue();
        processor.ProcessFrame(1);
        NavigationConnection invalid = new(
            "hidden",
            source,
            new NavigationCellAddress("other", invalidDestination),
            GetFootAnchor(binding, source),
            GetFootAnchor(otherBinding, default),
            Fixed64.Zero,
            Fixed64.Half);
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddConnection(invalid)
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            4,
            2);
        processor.Admit(operation).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 1, 8, 1, 1));
        for (int frame = 2; frame < 32 && operation.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            meter.Reset();
            processor.ProcessFrame(frame, static (_, _, _, _) => NavigationCandidatePublication.Published, meter);
        }

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetMap("map", out NavigationMap published).Should().BeTrue();
        published.Should().BeSameAs(original);
    }

    [Fact]
    public void ResumedOverlayValidationFailure_ShouldRestoreFoldSourceAndKeepPriorPrefix()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        NavigationMap map = CreateMap("map", binding, default);
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationOverlayCommitOperation valid = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), SolidCell),
            2,
            1);
        NavigationConnection invalid = new(
            "invalid",
            default,
            new NavigationCellAddress("map", default),
            default,
            GetFootAnchor(binding, default),
            Fixed64.Zero,
            Fixed64.Half);
        NavigationOverlayCommitOperation rejected = ConnectionOverlay(invalid, 3, 1);
        processor.Admit(valid).Should().BeTrue();
        processor.Admit(rejected).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 1, 8, 1, 1));

        int frame = 1;
        while (rejected.Receipt.Status == NavigationOperationStatus.Pending && frame < 32)
        {
            meter.Reset();
            processor.ProcessFrame(
                frame++,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter);
        }

        valid.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState overlay).Should().BeTrue();
        overlay.CellCount.Should().Be(1, "the earlier successful prefix remains authoritative");
        overlay.ConnectionCount.Should().Be(0, "the rejected resumed fold cannot leak its partial root");
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "invalid"),
                out _)
            .Should().BeFalse("the rejected later operation cannot leak a compiled owner root");
    }

    [Fact]
    public void MeteredSupersedence_ShouldCarryCoverageWithoutPublishingFoldedStateEarly()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMap map = CreateMap("map", CreateBinding(Vector3d.Zero), default);
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), SolidCell),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), SolidCell),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(3, 0, 0), SolidCell)
                })
            })),
            2,
            1);
        processor.Admit(overlay).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 3, 8, 8, 8));

        int publicationCount = 0;
        NavigationCandidatePublication Publish(
            NavigationOperationCandidate _,
            int __,
            NavigationOperationFrameChange[] ___,
            int ____)
        {
            publicationCount++;
            return NavigationCandidatePublication.Published;
        }
        processor.ProcessFrame(1, Publish, meter)
            .Should().Be(NavigationOperationFrameResult.Deferred);

        meter.OverlaySlots.Should().Be(3);
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        publicationCount.Should().Be(0);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState current).Should().BeTrue();
        current.CellCount.Should().Be(0);
        int frame = 2;
        while (overlay.Receipt.Status == NavigationOperationStatus.Pending && frame < 16)
        {
            meter.Reset();
            processor.ProcessFrame(frame++, Publish, meter);
            meter.OverlaySlots.Should().BeLessThanOrEqualTo(3);
            if (overlay.Receipt.Status == NavigationOperationStatus.Pending)
            {
                publicationCount.Should().Be(0);
                processor.Candidate.TryGetOverlay("map", out current).Should().BeTrue();
                current.CellCount.Should().Be(0);
            }
        }
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        publicationCount.Should().Be(1);
        overlay.Receipt.PublishedFrame.Should().BeGreaterThan(1);
        processor.Candidate.TryGetOverlay("map", out current).Should().BeTrue();
        current.CellCount.Should().Be(3);
    }

    [Fact]
    public void RetainedGuard_ShouldChargePreparedMapGrowthDuringReplacement()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        var initialPrepared = new PreparedNavigationMap(
            new NavigationMapBuilder("map", binding)
                .AddCell(default, SolidCell)
                .Build(),
            1);
        var initial = new NavigationMapCommitOperation(
            initialPrepared,
            OverlayReplacementPolicy.Clear,
            1,
            0);
        processor.Admit(initial).Should().BeTrue();
        processor.ProcessFrame(0);

        var replacementPrepared = new PreparedNavigationMap(
            new NavigationMapBuilder("map", binding)
                .AddCell(default, SolidCell)
                .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
                .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
                .Build(),
            2);
        var replacement = new NavigationMapCommitOperation(
            replacementPrepared,
            OverlayReplacementPolicy.Clear,
            2,
            1);
        processor.Admit(replacement).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 1, 8, 8, 8));
        long maximumGuardBytes = 0;
        int frame = 1;
        while (replacement.Receipt.Status == NavigationOperationStatus.Pending && frame < 16)
        {
            meter.Reset();
            processor.ProcessFrame(
                frame++,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter,
                (bytes, _) =>
                {
                    maximumGuardBytes = Math.Max(maximumGuardBytes, bytes);
                    return true;
                });
        }

        replacement.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        maximumGuardBytes.Should().BeGreaterThanOrEqualTo(
            replacementPrepared.RetainedBytes - initialPrepared.RetainedBytes,
            "pending prepared-map growth must be charged before the replacement is attached");
    }

    [Fact]
    public void RetainedGuard_ShouldChargeSameCountPayloadGrowthAndClampShrinkToZero()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        processor.Admit(Commit(CreateMap("map", binding, default), 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);

        NavigationConnection small = new(
            "connection",
            default,
            new NavigationCellAddress("d", default),
            GetFootAnchor(binding, default),
            default,
            Fixed64.Zero,
            Fixed64.Half);
        var witnesses = new NavigationCellAddress[16];
        for (int i = 0; i < witnesses.Length; i++)
            witnesses[i] = new NavigationCellAddress($"witness-{i:D2}-{new string('x', 64)}", default);
        NavigationConnection large = new(
            "connection",
            default,
            new NavigationCellAddress(new string('d', 256), default),
            GetFootAnchor(binding, default),
            default,
            Fixed64.Zero,
            Fixed64.Half,
            witnesses);
        NavigationOverlayCommitOperation smallOperation = ConnectionOverlay(small, 2, 1);
        processor.Admit(smallOperation).Should().BeTrue();
        processor.ProcessFrame(1);
        long beforeGrowth = processor.Candidate.RetainedBytes;
        long beforeExplicitGrowth = processor.Candidate.ExplicitConnections.RetainedBytes;

        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 1, 8, 1, 8));
        long maximumGrowthGuardBytes = 0;
        NavigationOverlayCommitOperation growth = ConnectionOverlay(large, 3, 2);
        processor.Admit(growth).Should().BeTrue();
        int frame = 2;
        while (growth.Receipt.Status == NavigationOperationStatus.Pending && frame < 64)
        {
            meter.Reset();
            processor.ProcessFrame(
                frame++,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter,
                (bytes, pages) =>
                {
                    bytes.Should().BeGreaterThanOrEqualTo(0);
                    pages.Should().BeGreaterThanOrEqualTo(0);
                    maximumGrowthGuardBytes = Math.Max(maximumGrowthGuardBytes, bytes);
                    return true;
                });
        }
        growth.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.OverlayConnectionCount.Should().Be(1);
        processor.Candidate.ExplicitConnections.GetIncidentAddressCount("d").Should().Be(0);
        for (int i = 0; i < witnesses.Length; i++)
        {
            processor.Candidate.ExplicitConnections.GetIncidentAddressCount(witnesses[i].MapId)
                .Should().Be(1);
        }
        long payloadGrowth = processor.Candidate.RetainedBytes - beforeGrowth;
        payloadGrowth.Should().BePositive();
        maximumGrowthGuardBytes.Should().BeGreaterThanOrEqualTo(payloadGrowth);

        NavigationOverlayCommitOperation shrink = ConnectionOverlay(small, 4, frame);
        processor.Admit(shrink).Should().BeTrue();
        while (shrink.Receipt.Status == NavigationOperationStatus.Pending && frame < 96)
        {
            meter.Reset();
            processor.ProcessFrame(
                frame++,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter,
                (bytes, pages) =>
                {
                    bytes.Should().BeGreaterThanOrEqualTo(0,
                        "source-relative retained deltas clamp shrinkage instead of underflowing");
                    pages.Should().BeGreaterThanOrEqualTo(0);
                    return true;
                });
        }
        shrink.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.OverlayConnectionCount.Should().Be(1);
        for (int i = 0; i < witnesses.Length; i++)
        {
            processor.Candidate.ExplicitConnections.GetIncidentAddressCount(witnesses[i].MapId)
                .Should().Be(0);
        }
        processor.Candidate.ExplicitConnections.GetIncidentAddressCount(large.Destination.MapId)
            .Should().Be(0);
        processor.Candidate.ExplicitConnections.RetainedBytes.Should().Be(beforeExplicitGrowth);
        processor.Candidate.RetainedBytes.Should().Be(beforeGrowth);
    }

    [Fact]
    public void RetainedGuard_ShouldRejectSameSizedExplicitPayloadReplacementBelowExactPeak()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        VoxelIndex destination = new(1, 0, 0);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(destination, SolidCell)
            .Build();
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationConnection CreateConnection(Fixed64 additionalCost) => new(
            "connection",
            default,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, default),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.Half,
            additionalCost: additionalCost);
        NavigationOverlayCommitOperation initial = ConnectionOverlay(
            CreateConnection(Fixed64.Zero),
            2,
            1);
        processor.Admit(initial).Should().BeTrue();
        processor.ProcessFrame(1);
        initial.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        long sourceBytes = processor.Candidate.RetainedBytes;
        int sourcePages = processor.Candidate.PersistentPageCount;

        long exactPeakBytes = 0;
        int exactPeakPages = 0;
        NavigationOverlayCommitOperation firstReplacement = ConnectionOverlay(
            CreateConnection(Fixed64.One),
            3,
            2);
        processor.Admit(firstReplacement).Should().BeTrue();
        processor.ProcessFrame(
            2,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, pages) =>
            {
                exactPeakBytes = Math.Max(exactPeakBytes, bytes);
                exactPeakPages = Math.Max(exactPeakPages, pages);
                return true;
            });
        firstReplacement.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        long logicalByteDelta = Math.Max(0L, processor.Candidate.RetainedBytes - sourceBytes);
        int logicalPageDelta = Math.Max(0, processor.Candidate.PersistentPageCount - sourcePages);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "connection"),
                out NavigationExplicitConnectionRecord published)
            .Should().BeTrue();
        long replacementPayloadBytes = published.RetainedBytes
            + processor.Candidate.ExplicitConnections.GetIncidentOwnerRow(
                new NavigationCellAddress("map", default)).RetainedBytes
            + processor.Candidate.ExplicitConnections.GetIncidentOwnerRow(
                new NavigationCellAddress("map", destination)).RetainedBytes;
        int replacementPayloadPages = published.PersistentPageCount
            + processor.Candidate.ExplicitConnections.GetIncidentOwnerRow(
                new NavigationCellAddress("map", default)).PersistentPageCount
            + processor.Candidate.ExplicitConnections.GetIncidentOwnerRow(
                new NavigationCellAddress("map", destination)).PersistentPageCount;
        exactPeakBytes.Should().BeGreaterThanOrEqualTo(replacementPayloadBytes,
            "the new record and both fixed-page incidence rows coexist with the source payload");
        exactPeakPages.Should().BeGreaterThanOrEqualTo(replacementPayloadPages);
        exactPeakBytes.Should().BeGreaterThan(logicalByteDelta,
            "same-sized candidate totals alone exclude source/new payload coexistence");
        exactPeakPages.Should().BeGreaterThan(logicalPageDelta);
        long tightByteCap = exactPeakBytes - 1;
        tightByteCap.Should().BeGreaterThanOrEqualTo(logicalByteDelta);

        NavigationOverlayCommitOperation rejected = ConnectionOverlay(
            CreateConnection((Fixed64)2),
            4,
            3);
        processor.Admit(rejected).Should().BeTrue();
        processor.ProcessFrame(
            3,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, pages) => bytes <= tightByteCap && pages <= exactPeakPages);

        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "connection"),
                out NavigationExplicitConnectionRecord retained)
            .Should().BeTrue();
        retained.Definition.AdditionalCost.Should().Be(Fixed64.One,
            "capacity rejection must retain the prior successful explicit root");
    }

    [Fact]
    public void RetainedGuard_ShouldChargeIntermediateSameOwnerPayloadAcrossBatch()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        VoxelIndex destination = new(1, 0, 0);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(destination, SolidCell)
            .Build();
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationConnection CreateConnection(Fixed64 additionalCost) => new(
            "connection",
            default,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, default),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.Half,
            additionalCost: additionalCost);
        NavigationOverlayCommitOperation initial = ConnectionOverlay(
            CreateConnection(Fixed64.Zero),
            2,
            1);
        processor.Admit(initial).Should().BeTrue();
        processor.ProcessFrame(1);

        long oneFoldPeak = 0;
        NavigationOverlayCommitOperation measured = ConnectionOverlay(
            CreateConnection(Fixed64.One),
            3,
            2);
        processor.Admit(measured).Should().BeTrue();
        processor.ProcessFrame(
            2,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) =>
            {
                oneFoldPeak = Math.Max(oneFoldPeak, bytes);
                return true;
            });
        measured.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "connection"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        long onePayloadBytes = record.RetainedBytes
            + processor.Candidate.ExplicitConnections.GetIncidentOwnerRow(
                new NavigationCellAddress("map", default)).RetainedBytes
            + processor.Candidate.ExplicitConnections.GetIncidentOwnerRow(
                new NavigationCellAddress("map", destination)).RetainedBytes;
        onePayloadBytes.Should().Be(record.RetainedBytes + 432);

        NavigationOverlayCommitOperation first = ConnectionOverlay(
            CreateConnection((Fixed64)2),
            4,
            3);
        NavigationOverlayCommitOperation second = ConnectionOverlay(
            CreateConnection((Fixed64)3),
            5,
            3);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(second).Should().BeTrue();

        long pairPeak = 0;
        processor.ProcessFrame(
            3,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) =>
            {
                pairPeak = Math.Max(pairPeak, bytes);
                return true;
            });

        pairPeak.Should().BeGreaterThan(oneFoldPeak);
        first.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);
        second.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);

        NavigationOverlayCommitOperation rejectedFirst = ConnectionOverlay(
            CreateConnection((Fixed64)4),
            6,
            4);
        NavigationOverlayCommitOperation rejectedSecond = ConnectionOverlay(
            CreateConnection((Fixed64)5),
            7,
            4);
        processor.Admit(rejectedFirst).Should().BeTrue();
        processor.Admit(rejectedSecond).Should().BeTrue();
        processor.ProcessFrame(
            4,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) => bytes < pairPeak);

        rejectedFirst.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejectedFirst.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        rejectedSecond.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejectedSecond.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "connection"),
                out NavigationExplicitConnectionRecord retained)
            .Should().BeTrue();
        retained.Definition.AdditionalCost.Should().Be((Fixed64)3,
            "the rejected pair must retain the prior published owner");
    }

    [Fact]
    public void RetainedGuard_ShouldReleasePriorFoldPayloadAcrossThreeReplacements()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        VoxelIndex destination = new(1, 0, 0);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(destination, SolidCell)
            .Build();
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationConnection CreateConnection(Fixed64 additionalCost) => new(
            "connection",
            default,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, default),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.Half,
            additionalCost: additionalCost);
        NavigationOverlayCommitOperation initial = ConnectionOverlay(
            CreateConnection(Fixed64.Zero),
            2,
            1);
        processor.Admit(initial).Should().BeTrue();
        processor.ProcessFrame(1);

        long oneFoldPeak = 0;
        long copiedBytesPerFold = 0;
        NavigationOverlayCommitOperation measured = ConnectionOverlay(
            CreateConnection(Fixed64.One),
            3,
            2);
        processor.Admit(measured).Should().BeTrue();
        processor.ProcessFrame(
            2,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) =>
            {
                oneFoldPeak = Math.Max(oneFoldPeak, bytes);
                copiedBytesPerFold = Math.Max(
                    copiedBytesPerFold,
                    processor.Candidate.WorkCopiedPersistentBytes);
                return true;
            });
        measured.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "connection"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        long onePayloadBytes = record.RetainedBytes + 432;
        long correctedThreeFoldPeak = oneFoldPeak
            + (2 * copiedBytesPerFold)
            + onePayloadBytes;

        NavigationOverlayCommitOperation first = ConnectionOverlay(
            CreateConnection((Fixed64)2),
            4,
            3);
        NavigationOverlayCommitOperation second = ConnectionOverlay(
            CreateConnection((Fixed64)3),
            5,
            3);
        NavigationOverlayCommitOperation third = ConnectionOverlay(
            CreateConnection((Fixed64)4),
            6,
            3);
        processor.Admit(first).Should().BeTrue();
        processor.Admit(second).Should().BeTrue();
        processor.Admit(third).Should().BeTrue();

        long observedBatchPeak = 0;
        processor.ProcessFrame(
            3,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) =>
            {
                observedBatchPeak = Math.Max(observedBatchPeak, bytes);
                return bytes <= correctedThreeFoldPeak;
            });

        observedBatchPeak.Should().BeLessThanOrEqualTo(correctedThreeFoldPeak);
        first.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);
        second.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);
        third.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);
        processor.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "connection"),
                out NavigationExplicitConnectionRecord retained)
            .Should().BeTrue();
        retained.Definition.AdditionalCost.Should().Be((Fixed64)4);
    }

    [Fact]
    public void RetainedGuard_ShouldChargeDifferentOwnersOnceAtExactPeak()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        var firstSource = new VoxelIndex(0, 0, 0);
        var firstDestination = new VoxelIndex(1, 0, 0);
        var secondSource = new VoxelIndex(0, 0, 1);
        var secondDestination = new VoxelIndex(1, 0, 1);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(firstSource, SolidCell)
            .AddCell(firstDestination, SolidCell)
            .AddCell(secondSource, SolidCell)
            .AddCell(secondDestination, SolidCell)
            .Build();
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationConnection CreateConnection(
            string id,
            VoxelIndex source,
            VoxelIndex destination,
            Fixed64 additionalCost) => new(
                id,
                source,
                new NavigationCellAddress("map", destination),
                GetFootAnchor(binding, source),
                GetFootAnchor(binding, destination),
                Fixed64.Zero,
                Fixed64.Half,
                additionalCost: additionalCost);
        NavigationOverlayCommitOperation initialX = ConnectionOverlay(
            CreateConnection("x", firstSource, firstDestination, Fixed64.Zero),
            2,
            1);
        NavigationOverlayCommitOperation initialY = ConnectionOverlay(
            CreateConnection("y", secondSource, secondDestination, Fixed64.Zero),
            3,
            1);
        processor.Admit(initialX).Should().BeTrue();
        processor.Admit(initialY).Should().BeTrue();
        processor.ProcessFrame(1);
        initialX.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        initialY.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        long exactPeak = 0;
        NavigationOverlayCommitOperation measuredX = ConnectionOverlay(
            CreateConnection("x", firstSource, firstDestination, Fixed64.One),
            4,
            2);
        NavigationOverlayCommitOperation measuredY = ConnectionOverlay(
            CreateConnection("y", secondSource, secondDestination, Fixed64.One),
            5,
            2);
        processor.Admit(measuredX).Should().BeTrue();
        processor.Admit(measuredY).Should().BeTrue();
        processor.ProcessFrame(
            2,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) =>
            {
                exactPeak = Math.Max(exactPeak, bytes);
                return true;
            });
        measuredX.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        measuredY.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationOverlayCommitOperation exactX = ConnectionOverlay(
            CreateConnection("x", firstSource, firstDestination, (Fixed64)2),
            6,
            3);
        NavigationOverlayCommitOperation exactY = ConnectionOverlay(
            CreateConnection("y", secondSource, secondDestination, (Fixed64)2),
            7,
            3);
        processor.Admit(exactX).Should().BeTrue();
        processor.Admit(exactY).Should().BeTrue();
        processor.ProcessFrame(
            3,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) => bytes <= exactPeak);
        exactX.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);
        exactY.Receipt.Rejection.Should().NotBe(NavigationOperationRejection.CapacityExceeded);

        NavigationOverlayCommitOperation rejectedX = ConnectionOverlay(
            CreateConnection("x", firstSource, firstDestination, (Fixed64)3),
            8,
            4);
        NavigationOverlayCommitOperation rejectedY = ConnectionOverlay(
            CreateConnection("y", secondSource, secondDestination, (Fixed64)3),
            9,
            4);
        processor.Admit(rejectedX).Should().BeTrue();
        processor.Admit(rejectedY).Should().BeTrue();
        processor.ProcessFrame(
            4,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget),
            (bytes, _) => bytes < exactPeak);

        rejectedX.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejectedX.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        rejectedY.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejectedY.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
    }

    [Fact]
    public void RetainedGuard_ShouldRejectSameCountMultiMapOverlayCopyBeyondTightCap()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        NormalizedGridConfiguration firstBinding = CreateBinding(Vector3d.Zero);
        NormalizedGridConfiguration secondBinding = CreateBinding(new Vector3d(10, 0, 0));
        processor.Admit(Commit(CreateMap("first", firstBinding, default), 1, 0)).Should().BeTrue();
        processor.Admit(Commit(CreateMap("second", secondBinding, default), 2, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationMapOverlayDelta[] initialDeltas =
        {
            new("first", new[] { NavigationCellOverlayOperation.Set(default, SolidCell) }),
            new("second", new[] { NavigationCellOverlayOperation.Set(default, SolidCell) })
        };
        var initial = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(initialDeltas)),
            3,
            1);
        processor.Admit(initial).Should().BeTrue();
        processor.ProcessFrame(1);
        initial.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        long sourceBytes = processor.Candidate.RetainedBytes;
        int sourcePages = processor.Candidate.PersistentPageCount;

        NavigationCell replacement = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            new NavigationAreaId(0),
            Fixed64.One,
            Fixed64.One,
            Fixed64.One);
        NavigationMapOverlayDelta[] replacementDeltas =
        {
            new("first", new[] { NavigationCellOverlayOperation.Set(default, replacement) }),
            new("second", new[] { NavigationCellOverlayOperation.Set(default, replacement) })
        };
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(replacementDeltas)),
            4,
            2);
        processor.Admit(operation).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(
            new MaintenanceWorkBudget(8, 8, 1, 8, 8, 8));
        long tightByteCap = -1;
        int tightPageCap = -1;
        long maximumBytes = 0;
        int maximumPages = 0;

        bool Guard(long bytes, int pages)
        {
            maximumBytes = Math.Max(maximumBytes, bytes);
            maximumPages = Math.Max(maximumPages, pages);
            if (tightByteCap < 0)
            {
                tightByteCap = bytes;
                tightPageCap = pages;
                return true;
            }
            return bytes <= tightByteCap && pages <= tightPageCap;
        }

        processor.ProcessFrame(
            2,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            meter,
            Guard).Should().Be(NavigationOperationFrameResult.Deferred);
        for (int frame = 3;
             frame < 16 && operation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            meter.Reset();
            processor.ProcessFrame(
                frame,
                static (_, _, _, _) => NavigationCandidatePublication.Published,
                meter,
                Guard);
        }

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        (maximumBytes > tightByteCap || maximumPages > tightPageCap).Should().BeTrue(
            "same-count persistent replacements still retain copied tree paths");
        processor.Candidate.RetainedBytes.Should().Be(sourceBytes);
        processor.Candidate.PersistentPageCount.Should().Be(sourcePages);
    }

    [Fact]
    public void RetainedGuard_ShouldKeepCompletedSameCountCopiesAfterFoldCarryover()
    {
        NavigationOperationLimits limits = CreateLimits();
        var processor = new NavigationOperationProcessor(limits);
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        processor.Admit(Commit(CreateMap("map", binding, default), 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        var initial = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(default, SolidCell),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), SolidCell)
                })
            })),
            2,
            1);
        processor.Admit(initial).Should().BeTrue();
        processor.ProcessFrame(1);
        initial.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationCell replacement = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.One,
            Fixed64.One);
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(default, replacement),
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), replacement)
                })
            })),
            3,
            2);
        processor.Admit(operation).Should().BeTrue();
        var meter = new MaintenanceWorkMeter(
            new MaintenanceWorkBudget(8, 8, 1, 8, 8, 8));

        bool Guard(long bytes, int _) => bytes <= processor.CoverageScratchBytes;

        processor.ProcessFrame(
            2,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            meter,
            Guard)
            .Should().Be(NavigationOperationFrameResult.Deferred);
        meter.Reset();
        processor.ProcessFrame(
            3,
            static (_, _, _, _) => NavigationCandidatePublication.Published,
            meter,
            Guard);

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
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
        overlay.CellCount.Should().Be(1);
        overlay.GetCellAt(0).Kind.Should().Be(NavigationCellOverlayOperationKind.Set);
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
        state.CellCount.Should().Be(1);
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
    public void ConfiguredAreaLayout_ShouldRejectUnknownBakedAndOverlayAreas()
    {
        var processor = new NavigationOperationProcessor(
            CreateLimits(),
            navigationAreaCount: 1);
        NormalizedGridConfiguration binding = CreateBinding(Vector3d.Zero);
        var unknownAreaCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            new NavigationAreaId(1),
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMap invalidMap = new NavigationMapBuilder("invalid", binding)
            .AddCell(default, unknownAreaCell)
            .Build();
        NavigationMapCommitOperation invalidInstall = Commit(invalidMap, 1, 0);

        processor.Admit(invalidInstall).Should().BeTrue();
        processor.ProcessFrame(0);

        invalidInstall.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);

        NavigationMap validMap = CreateMap("valid", binding, default);
        NavigationMapCommitOperation validInstall = Commit(validMap, 2, 1);
        processor.Admit(validInstall).Should().BeTrue();
        processor.ProcessFrame(1);

        NavigationOverlayCommitOperation invalidOverlay = Overlay(
            "valid",
            NavigationCellOverlayOperation.Set(default, unknownAreaCell),
            3,
            2);
        processor.Admit(invalidOverlay).Should().BeTrue();
        processor.ProcessFrame(2);

        invalidOverlay.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetOverlay("valid", out NavigationMapOverlayState overlay).Should().BeTrue();
        overlay.CellCount.Should().Be(0);
    }

    [Fact]
    public void OperationLimits_RejectBatchSizeAboveDeterministicCoalescingCeiling()
    {
        Action create = () => _ = CreateLimits(
            maxBatchItems: NavigationOperationLimits.MaximumBatchItems + 1);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LargeOverlay_PointUpdatesCopyOnlyPersistentPathsAndKeepExactTotals()
    {
        const int existingCount = 511;
        var processor = new NavigationOperationProcessor(CreateLimits());
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(1024, 2, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = CreateMap("map", binding, default);
        processor.Admit(Commit(map, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);

        var initial = new NavigationCellOverlayOperation[existingCount];
        for (int i = 0; i < initial.Length; i++)
        {
            initial[i] = NavigationCellOverlayOperation.Set(
                new VoxelIndex(i + 1, 0, 0),
                SolidCell);
        }
        var install = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta("map", initial) })),
            2,
            1);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(1);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState before).Should().BeTrue();
        before.PersistentNodeCount.Should().Be(existingCount);

        var set = Overlay(
            "map",
            NavigationCellOverlayOperation.Set(new VoxelIndex(existingCount + 1, 0, 0), SolidCell),
            3,
            2);
        processor.Admit(set).Should().BeTrue();
        processor.ProcessFrame(2);
        set.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState afterSet).Should().BeTrue();
        afterSet.CellCount.Should().Be(existingCount + 1);
        afterSet.LastApplyCopiedNodeCount.Should().BeLessThan(32);
        (afterSet.RetainedBytes - before.RetainedBytes).Should().Be(128);
        processor.Candidate.OverlayCellCount.Should().Be(existingCount + 1);

        var suppress = Overlay(
            "map",
            NavigationCellOverlayOperation.Suppress(new VoxelIndex(existingCount / 2, 0, 0)),
            4,
            3);
        processor.Admit(suppress).Should().BeTrue();
        processor.ProcessFrame(3);
        suppress.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState afterSuppress).Should().BeTrue();
        afterSuppress.CellCount.Should().Be(existingCount + 1);
        afterSuppress.LastApplyCopiedNodeCount.Should().BeLessThan(32);
        afterSuppress.TryGetCell(
                new VoxelIndex(existingCount / 2, 0, 0),
                out NavigationCellOverlayOperation suppressed)
            .Should().BeTrue();
        suppressed.Kind.Should().Be(NavigationCellOverlayOperationKind.Suppress);
        processor.Candidate.OverlayCellCount.Should().Be(existingCount + 1);

        var revert = Overlay(
            "map",
            NavigationCellOverlayOperation.RevertToBake(new VoxelIndex(existingCount / 2, 0, 0)),
            5,
            4);
        processor.Admit(revert).Should().BeTrue();
        processor.ProcessFrame(4);
        revert.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        processor.Candidate.TryGetOverlay("map", out NavigationMapOverlayState afterRevert).Should().BeTrue();
        afterRevert.CellCount.Should().Be(existingCount);
        afterRevert.LastApplyCopiedNodeCount.Should().BeLessThan(32);
        (afterSuppress.RetainedBytes - afterRevert.RetainedBytes).Should().Be(128);
        processor.Candidate.OverlayCellCount.Should().Be(existingCount);
        afterRevert.GetCellAt(0).Index.Should().Be(new VoxelIndex(1, 0, 0));
        afterRevert.GetCellAt(existingCount - 1).Index.Should().Be(new VoxelIndex(existingCount + 1, 0, 0));
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

    private static NavigationOverlayCommitOperation ConnectionOverlay(
        NavigationConnection connection,
        long sequence,
        int frame) => new(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[]
                    {
                        new NavigationMapOverlayDelta(
                            "map",
                            connections: new[] { NavigationConnectionOverlayOperation.Upsert(connection) })
                    })),
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
