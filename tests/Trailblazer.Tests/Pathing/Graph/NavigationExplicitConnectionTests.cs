//=======================================================================
// NavigationExplicitConnectionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
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
public sealed class NavigationExplicitConnectionTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void BakedCrossMapConnection_ShouldRetainCompiledCorridorCertificate()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration right = AddGrid(context, 3);
        VoxelIndex sourceIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        Vector3d sourceFoot = GetFoot(left, sourceIndex);
        Vector3d destinationFoot = GetFoot(right, destinationIndex);
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", destinationIndex),
            sourceFoot,
            destinationFoot,
            Fixed64.Zero,
            Fixed64.One,
            isLowerBoundCertified: true);
        NavigationMap rightMap = new NavigationMapBuilder("right", right)
            .AddCell(destinationIndex, SolidCell)
            .Build();
        NavigationMap leftMap = new NavigationMapBuilder("left", left)
            .AddCell(sourceIndex, SolidCell)
            .AddConnection(connection)
            .Build();

        Admit(context, rightMap, 1);
        Admit(context, leftMap, 2);
        context.Simulate();

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var owner = new NavigationConnectionOwnerKey("left", "bridge");
        lease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        record.IsActive.Should().BeTrue();
        record.Source.Should().Be(new NavigationCellAddress("left", sourceIndex));
        record.Destination.Should().Be(new NavigationCellAddress("right", destinationIndex));
        record.CorridorCost.Should().Be(Fixed64.One);
        record.IsLowerBoundCertified.Should().BeTrue();
        record.NavigationPortals.Count.Should().Be(1);
        record.NavigationPortals[0].IsValid.Should().BeTrue();
    }

    [Fact]
    public void DestinationSuppressAndRevert_ShouldDormantAndReviveOnlyTheNewRoot()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration right = AddGrid(context, 3);
        VoxelIndex sourceIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", destinationIndex),
            GetFoot(left, sourceIndex),
            GetFoot(right, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("right", right)
                .AddCell(destinationIndex, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            2);
        context.Simulate();
        var owner = new NavigationConnectionOwnerKey("left", "bridge");
        using NavigationWorldGraphLease oldLease = context.Pathing.TryAcquireNavigationGraph()!;
        oldLease.Graph.ExplicitConnections.GetActiveIncidentEdgeCount("left").Should().Be(1);
        oldLease.Graph.ExplicitConnections.GetActiveIncidentEdgeCount("right").Should().Be(1);

        CommitCell(context, "right", NavigationCellOverlayOperation.Suppress(destinationIndex), 3);
        context.Simulate();
        using (NavigationWorldGraphLease dormantLease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormantLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord dormant)
                .Should().BeTrue();
            dormant.IsActive.Should().BeFalse();
            dormant.NavigationPortals.Count.Should().Be(0);
            dormantLease.Graph.ExplicitConnections.GetActiveIncidentEdgeCount("left").Should().Be(0);
            dormantLease.Graph.ExplicitConnections.GetActiveIncidentEdgeCount("right").Should().Be(0);
        }
        oldLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord old)
            .Should().BeTrue();
        old.IsActive.Should().BeTrue();

        CommitCell(context, "right", NavigationCellOverlayOperation.RevertToBake(destinationIndex), 4);
        context.Simulate();
        using NavigationWorldGraphLease revivedLease = context.Pathing.TryAcquireNavigationGraph()!;
        revivedLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord revived)
            .Should().BeTrue();
        revived.IsActive.Should().BeTrue();
        revived.NavigationPortals.Count.Should().Be(1);
    }

    [Fact]
    public void WitnessSuppressAndRevert_ShouldDormantAndReviveIncidentOwner()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration middle = AddGrid(context, 3);
        NormalizedGridConfiguration right = AddGrid(context, 4);
        VoxelIndex sourceIndex = new(2, 0, 0);
        var witnessAddress = new NavigationCellAddress("middle", default);
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", default),
            GetFoot(left, sourceIndex),
            GetFoot(right, default),
            Fixed64.Zero,
            Fixed64.One,
            new[] { witnessAddress });
        Admit(
            context,
            new NavigationMapBuilder("right", right)
                .AddCell(default, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("middle", middle)
                .AddCell(default, SolidCell)
                .Build(),
            2);
        NavigationMapCommitOperation leftCommit = Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            3);
        SimulateUntilTerminal(context, leftCommit.Receipt);
        leftCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var owner = new NavigationConnectionOwnerKey("left", "bridge");
        NavigationMapInstance priorLeft;
        long priorComponentVersion;
        using (NavigationWorldGraphLease installed = context.Pathing.TryAcquireNavigationGraph()!)
        {
            priorLeft = FindInstance(installed.Graph, "left");
            installed.Graph.SurfaceComponents.TryGet(
                    new NavigationCellAddress("left", sourceIndex),
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponent component)
                .Should().BeTrue();
            priorComponentVersion = component.Version;
        }

        CommitCell(context, "middle", NavigationCellOverlayOperation.Suppress(default), 4);
        context.Simulate();
        using (NavigationWorldGraphLease dormantLease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormantLease.Graph.ExplicitConnections.TryGet(
                    owner,
                    out NavigationExplicitConnectionRecord dormant)
                .Should().BeTrue();
            dormant.IsActive.Should().BeFalse();
            NavigationPagedSequence<NavigationConnectionOwnerKey> witnessOwners =
                dormantLease.Graph.ExplicitConnections.GetIncidentOwnerRow(witnessAddress);
            witnessOwners.Count.Should().Be(1);
            witnessOwners[0].Should().Be(owner);
            NavigationMapInstance dormantLeft = FindInstance(dormantLease.Graph, "left");
            dormantLeft.Should().BeSameAs(priorLeft,
                "a witness-only change must not rebuild the external source map instance");
            dormantLeft.InstanceVersion.Should().Be(priorLeft.InstanceVersion);
            dormantLease.Graph.SurfaceComponents.TryGet(
                    new NavigationCellAddress("left", sourceIndex),
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponent component)
                .Should().BeTrue();
            component.Version
                .Should().BeGreaterThan(priorComponentVersion,
                    "the external source's structural component must still be recomputed");
        }

        CommitCell(context, "middle", NavigationCellOverlayOperation.RevertToBake(default), 5);
        context.Simulate();
        using NavigationWorldGraphLease revivedLease = context.Pathing.TryAcquireNavigationGraph()!;
        revivedLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord revived)
            .Should().BeTrue();
        revived.IsActive.Should().BeTrue();
        revived.NavigationPortals.Count.Should().Be(2);
    }

    [Fact]
    public void DeferredWitnessSuppression_ShouldFailCloseAffectedExplicitComponent()
    {
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(1);
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration middle = AddGrid(context, 4);
        NormalizedGridConfiguration right = AddGrid(context, 8);
        VoxelIndex sourceIndex = new(3, 0, 0);
        NavigationCellAddress[] witnesses = CreateInteriorWitnessCorridor("middle");
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", default),
            GetFoot(left, sourceIndex),
            GetFoot(right, default),
            Fixed64.Zero,
            Fixed64.One,
            witnesses);
        NavigationMapCommitOperation rightCommit = Admit(
            context,
            new NavigationMapBuilder("right", right).AddCell(default, SolidCell).Build(),
            1);
        NavigationMapCommitOperation middleCommit = Admit(
            context,
            CreateCorridorMap("middle", middle),
            2);
        NavigationMapCommitOperation leftCommit = Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell).AddConnection(connection).Build(),
            3);
        SimulateUntilTerminal(context, leftCommit.Receipt);
        rightCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        middleCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        leftCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationOverlayCommitOperation suppression = CommitCell(
            context,
            "middle",
            NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0)),
            4);
        context.Simulate();

        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.TryGetNavigationGraphCellState(
                "left",
                sourceIndex,
                out NavigationGraphCellState sourceState)
            .Should().BeTrue();
        sourceState.IsMaterialized.Should().BeFalse();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("left", sourceIndex),
                out NavigationNodeRef source)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator pendingEdges = lease.Graph.EnumerateSurfaceEdges(source);
        while (pendingEdges.MoveNext())
        {
            pendingEdges.Current.Kind.Should().NotBe(NavigationGraphEdgeKind.Explicit,
                "the old explicit edge cannot remain queryable while witness suppression is deferred");
        }

        SimulateUntilTerminal(context, suppression.Receipt);
        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease dormantLease = context.Pathing.TryAcquireNavigationGraph()!;
        dormantLease.Graph.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("left", "bridge"),
                out NavigationExplicitConnectionRecord dormant)
            .Should().BeTrue();
        dormant.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PreGatherWitnessSuppression_ShouldCloseAllUntilAtomicExactPublication()
    {
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            maxExplicitEdges: 1,
            maxOverlaySlots: 1);
        const int unrelatedCount = 8;
        var unrelatedMapIds = new string[unrelatedCount];
        long sequence = 0;
        for (int i = 0; i < unrelatedMapIds.Length; i++)
        {
            string mapId = $"unrelated-{i}";
            unrelatedMapIds[i] = mapId;
            NormalizedGridConfiguration binding = AddGrid(context, 100 + (i * 10));
            Admit(
                context,
                new NavigationMapBuilder(mapId, binding).AddCell(default, SolidCell).Build(),
                ++sequence);
        }
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration middle = AddGrid(context, 4);
        NormalizedGridConfiguration right = AddGrid(context, 8);
        VoxelIndex sourceIndex = new(3, 0, 0);
        VoxelIndex firstMiddle = default;
        VoxelIndex witnessIndex = new(1, 0, 0);
        NavigationCellAddress[] witnesses = CreateInteriorWitnessCorridor("middle");
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", default),
            GetFoot(left, sourceIndex),
            GetFoot(right, default),
            Fixed64.Zero,
            Fixed64.One,
            witnesses);
        NavigationMapCommitOperation rightCommit = Admit(
            context,
            new NavigationMapBuilder("right", right).AddCell(default, SolidCell).Build(),
            ++sequence);
        NavigationMapCommitOperation middleCommit = Admit(
            context,
            new NavigationMapBuilder("middle", middle)
                .AddCell(firstMiddle, SolidCell)
                .AddCell(witnessIndex, SolidCell)
                .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
                .AddCell(new VoxelIndex(3, 0, 0), SolidCell)
                .Build(),
            ++sequence);
        NavigationMapCommitOperation leftCommit = Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            ++sequence);
        SimulateUntilTerminal(context, leftCommit.Receipt);
        rightCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        middleCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        leftCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var suppression = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "middle",
                    new[]
                    {
                        NavigationCellOverlayOperation.Set(firstMiddle, SolidCell),
                        NavigationCellOverlayOperation.Suppress(witnessIndex)
                    })
            })),
            operationSequence: ++sequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(suppression).Should().BeTrue();

        context.Simulate();

        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.NavigationMaintenanceMeter.OverlaySlots.Should().Be(1);
        context.Pathing.TryGetNavigationGraphCellState(
                "left",
                sourceIndex,
                out NavigationGraphCellState sourceState)
            .Should().BeTrue();
        sourceState.IsMaterialized.Should().BeFalse(
            "the source component must close before explicit incidence gather begins");
        for (int i = 0; i < unrelatedMapIds.Length; i++)
        {
            context.Pathing.TryGetNavigationGraphCellState(
                    unrelatedMapIds[i],
                    default,
                    out NavigationGraphCellState unrelatedState)
                .Should().BeTrue();
            unrelatedState.IsMaterialized.Should().BeFalse(
                "unknown explicit incidence must conservatively close every structural component");
        }

        for (int frame = 0; frame < 64 && suppression.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            context.Simulate();
        }

        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        for (int i = 0; i < unrelatedMapIds.Length; i++)
        {
            context.Pathing.TryGetNavigationGraphCellState(
                    unrelatedMapIds[i],
                    default,
                    out NavigationGraphCellState unrelatedState)
                .Should().BeTrue();
            unrelatedState.IsMaterialized.Should().BeTrue();
        }
        context.Pathing.TryGetNavigationGraphCellState(
                "left",
                sourceIndex,
                out sourceState)
            .Should().BeTrue();
        sourceState.IsMaterialized.Should().BeTrue();
    }

    [Fact]
    public void ExactNarrowing_ShouldRetryAfterRetiredSnapshotPressure()
    {
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            maxExplicitEdges: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxDependencyEntries: 3,
            maxRetiredSnapshots: 1);
        long sequence = 0;
        NormalizedGridConfiguration unrelated = AddGrid(context, 100);
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration middle = AddGrid(context, 4);
        NormalizedGridConfiguration right = AddGrid(context, 8);
        var sourceIndex = new VoxelIndex(3, 0, 0);
        var witnessIndex = new VoxelIndex(1, 0, 0);
        Admit(
            context,
            new NavigationMapBuilder("unrelated", unrelated).AddCell(default, SolidCell).Build(),
            ++sequence);
        Admit(
            context,
            new NavigationMapBuilder("right", right).AddCell(default, SolidCell).Build(),
            ++sequence);
        Admit(
            context,
            new NavigationMapBuilder("middle", middle)
                .AddCell(default, SolidCell)
                .AddCell(witnessIndex, SolidCell)
                .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
                .AddCell(new VoxelIndex(3, 0, 0), SolidCell)
                .Build(),
            ++sequence);
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", default),
            GetFoot(left, sourceIndex),
            GetFoot(right, default),
            Fixed64.Zero,
            Fixed64.One,
            CreateInteriorWitnessCorridor("middle"));
        NavigationMapCommitOperation leftCommit = Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            ++sequence);
        SimulateUntilTerminal(context, leftCommit.Receipt);
        leftCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        using NavigationWorldGraphLease retiredLease =
            context.Pathing.TryAcquireNavigationGraph()!;
        var suppression = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "middle",
                    new[]
                    {
                        NavigationCellOverlayOperation.Set(default, SolidCell),
                        NavigationCellOverlayOperation.Suppress(witnessIndex)
                    })
            })),
            ++sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(suppression).Should().BeTrue();

        context.Simulate();

        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        GetGraphCell(context, "unrelated").IsMaterialized.Should().BeFalse();
        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;
        store.RetiredGenerationCount.Should().Be(1);
        NavigationWorldGraph closed = store.Current;
        closed.Checkout();
        try
        {
            context.Simulate();

            suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
            (context.Pathing.RetainedCompositionWorkCount
                + context.Pathing.RetainedOperationWorkCount).Should().BeGreaterThan(0);
            GetGraphCell(context, "unrelated").IsMaterialized.Should().BeFalse(
                "the exact narrowing handoff must remain blocked while the store cannot publish");
        }
        finally
        {
            closed.Return();
            retiredLease.Dispose();
        }

        for (int frame = 0;
             frame < 128 && suppression.Receipt.Status == NavigationOperationStatus.Pending
                 && !GetGraphCell(context, "unrelated").IsMaterialized;
             frame++)
        {
            context.Simulate();
        }

        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        GetGraphCell(context, "unrelated").IsMaterialized.Should().BeTrue();
        GetGraphCell(context, "left", sourceIndex).IsMaterialized.Should().BeFalse();
    }

    [Fact]
    public void DeferredExplicitNarrowing_ShouldChargeEveryDiscoveredSource()
    {
        const int ownerCount = 4;
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            maxExplicitEdges: defaults.MaintenanceBudget.MaxExplicitEdges,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxDependencyEntries: 3);
        long sequence = 0;
        NormalizedGridConfiguration unrelated = AddGrid(context, 100);
        Admit(
            context,
            new NavigationMapBuilder("unrelated", unrelated).AddCell(default, SolidCell).Build(),
            ++sequence);
        NormalizedGridConfiguration middle = AddGrid(context, 4);
        NormalizedGridConfiguration right = AddGrid(context, 8);
        var sourceIndex = new VoxelIndex(3, 0, 0);
        var witnessIndex = new VoxelIndex(1, 0, 0);
        Admit(
            context,
            new NavigationMapBuilder("right", right).AddCell(default, SolidCell).Build(),
            ++sequence);
        Admit(
            context,
            new NavigationMapBuilder("middle", middle)
                .AddCell(default, SolidCell)
                .AddCell(witnessIndex, SolidCell)
                .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
                .AddCell(new VoxelIndex(3, 0, 0), SolidCell)
                .Build(),
            ++sequence);
        NavigationMapCommitOperation last = default;
        for (int i = 0; i < ownerCount; i++)
        {
            string mapId = $"source-{i}";
            NormalizedGridConfiguration sourceBinding = AddGridWithExtent(
                context,
                -i,
                3 + i);
            var ownerSourceIndex = new VoxelIndex(3 + i, 0, 0);
            var connection = new NavigationConnection(
                "bridge",
                ownerSourceIndex,
                new NavigationCellAddress("right", default),
                GetFoot(sourceBinding, ownerSourceIndex),
                GetFoot(right, default),
                Fixed64.Zero,
                Fixed64.One,
                CreateInteriorWitnessCorridor("middle"));
            last = Admit(
                context,
                new NavigationMapBuilder(mapId, sourceBinding)
                    .AddCell(ownerSourceIndex, SolidCell)
                    .AddConnection(connection)
                    .Build(),
                ++sequence);
        }
        SimulateUntilTerminal(context, last.Receipt);
        last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var suppression = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "middle",
                    new[]
                    {
                        NavigationCellOverlayOperation.Set(default, SolidCell),
                        NavigationCellOverlayOperation.Suppress(witnessIndex)
                    })
            })),
            ++sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(suppression).Should().BeTrue();
        context.Simulate();
        GetGraphCell(context, "unrelated").IsMaterialized.Should().BeFalse();

        int dependencyUnits = context.Pathing.NavigationMaintenanceMeter.DependencyEntries;
        int componentUnits = context.Pathing.NavigationMaintenanceMeter.ComponentNodes;
        bool narrowedWhilePending = false;
        for (int frame = 0; frame < 256 && suppression.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            context.Simulate();
            dependencyUnits += context.Pathing.NavigationMaintenanceMeter.DependencyEntries;
            componentUnits += context.Pathing.NavigationMaintenanceMeter.ComponentNodes;
            if (suppression.Receipt.Status == NavigationOperationStatus.Pending
                && GetGraphCell(context, "unrelated").IsMaterialized)
            {
                narrowedWhilePending = true;
                break;
            }
        }

        narrowedWhilePending.Should().BeTrue();
        componentUnits.Should().BeGreaterThanOrEqualTo(ownerCount,
            "every narrowed source-to-component lookup/root write must be charged");
        dependencyUnits.Should().BeGreaterThanOrEqualTo(ownerCount,
            "safety narrowing must charge every incident source component write");
        GetGraphCell(context, "source-0", sourceIndex).IsMaterialized.Should().BeFalse();
    }

    [Fact]
    public void HighDegreeSharedWitnessRefresh_ShouldChargeLinearDependencyWork()
    {
        const int ownerCount = 8;
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            defaults.MaintenanceBudget.MaxExplicitEdges);
        long sequence = 0;
        NormalizedGridConfiguration middle = AddGrid(context, 4);
        NormalizedGridConfiguration right = AddGrid(context, 8);
        var sourceIndex = new VoxelIndex(3, 0, 0);
        var witnessIndex = new VoxelIndex(1, 0, 0);
        Admit(
            context,
            new NavigationMapBuilder("right", right).AddCell(default, SolidCell).Build(),
            ++sequence);
        Admit(
            context,
            new NavigationMapBuilder("middle", middle)
                .AddCell(default, SolidCell)
                .AddCell(witnessIndex, SolidCell)
                .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
                .AddCell(new VoxelIndex(3, 0, 0), SolidCell)
                .Build(),
            ++sequence);
        NavigationMapCommitOperation last = default;
        for (int i = 0; i < ownerCount; i++)
        {
            string mapId = $"source-{i:D2}";
            NormalizedGridConfiguration binding = AddGridWithExtent(
                context,
                -i,
                3 + i);
            var ownerSourceIndex = new VoxelIndex(3 + i, 0, 0);
            var connection = new NavigationConnection(
                "bridge",
                ownerSourceIndex,
                new NavigationCellAddress("right", default),
                GetFoot(binding, ownerSourceIndex),
                GetFoot(right, default),
                Fixed64.Zero,
                Fixed64.One,
                CreateInteriorWitnessCorridor("middle"));
            last = Admit(
                context,
                new NavigationMapBuilder(mapId, binding)
                    .AddCell(ownerSourceIndex, SolidCell)
                    .AddConnection(connection)
                    .Build(),
                ++sequence);
        }
        SimulateUntilTerminal(context, last.Receipt);
        last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var suppression = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "middle",
                    new[] { NavigationCellOverlayOperation.Suppress(witnessIndex) })
            })),
            ++sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(suppression).Should().BeTrue();
        int dependencyWork = 0;
        for (int frame = 0; frame < 256 && suppression.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            context.Simulate();
            dependencyWork += context.Pathing.NavigationMaintenanceMeter.DependencyEntries;
        }

        suppression.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        dependencyWork.Should().BeLessThanOrEqualTo(ownerCount * 20,
            "unchanged canonical incidence membership must not be recopied once per owner");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public void SharedRowMapReplacement_ShouldRebuildEachIncidenceRowOnce(int ownerCount)
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 1);
        NavigationMap BuildMap()
        {
            var builder = new NavigationMapBuilder("map", binding)
                .AddCell(default, SolidCell)
                .AddCell(new VoxelIndex(1, 0, 0), SolidCell);
            for (int i = 0; i < ownerCount; i++)
            {
                builder.AddConnection(new NavigationConnection(
                    $"edge-{i:D2}",
                    default,
                    new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
                    GetFoot(binding, default),
                    GetFoot(binding, new VoxelIndex(1, 0, 0)),
                    Fixed64.Zero,
                    Fixed64.One));
            }
            return builder.Build();
        }

        NavigationOperationCandidate source = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(BuildMap(), 1));
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        int capacity = defaults.OperationLimits.MaxCorridorCells;
        var replacement = new NavigationMapFoldWork(
            source,
            new PreparedNavigationMap(BuildMap(), 2),
            OverlayReplacementPolicy.Clear,
            defaults.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            maxDependencyEntries: 1));
        int dependencyWork = 0;
        bool complete = false;

        for (int frame = 0; frame < 4096 && !complete; frame++)
        {
            complete = replacement.Advance(meter, out NavigationOperationRejection rejection);
            rejection.Should().Be(NavigationOperationRejection.None);
            dependencyWork += meter.DependencyEntries;
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            meter.Reset();
        }

        complete.Should().BeTrue();
        dependencyWork.Should().Be((19 * ownerCount) + 4,
            "dependency rows rebuild once and their endpoint subsets append and commit once");
    }

    [Fact]
    public void SharedWitnessFanIn_ShouldNotEnterEndpointEnumerationRows()
    {
        const int ownerCount = 64;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration binding = AddGridWithExtent(context, 0, 2);
        VoxelIndex sourceIndex = default;
        VoxelIndex witnessIndex = new(1, 0, 0);
        VoxelIndex destinationIndex = new(2, 0, 0);
        var witnessAddress = new NavigationCellAddress("map", witnessIndex);
        var builder = new NavigationMapBuilder("map", binding)
            .AddCell(sourceIndex, SolidCell)
            .AddCell(witnessIndex, SolidCell)
            .AddCell(destinationIndex, SolidCell);
        for (int i = 0; i < ownerCount; i++)
        {
            builder.AddConnection(new NavigationConnection(
                $"edge-{i:D2}",
                sourceIndex,
                new NavigationCellAddress("map", destinationIndex),
                GetFoot(binding, sourceIndex),
                GetFoot(binding, destinationIndex),
                Fixed64.Zero,
                Fixed64.One,
                new[] { witnessAddress }));
        }
        NavigationMapCommitOperation operation = Admit(context, builder.Build(), 1);
        SimulateUntilTerminal(context, operation.Receipt);
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationExplicitConnectionIndex index = lease.Graph.ExplicitConnections;

        index.GetIncidentOwnerRow(witnessAddress).Count.Should().Be(ownerCount);
        index.GetEndpointOwnerRow(witnessAddress).Count.Should().Be(0,
            "witness dependency fan-in is not query incidence");
        lease.Graph.TryGetNodeRef(witnessAddress, out NavigationNodeRef witness).Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = lease.Graph.EnumerateSurfaceEdges(witness);
        int explicitEdgeCount = 0;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
                explicitEdgeCount++;
        }
        explicitEdgeCount.Should().Be(0,
            "the empty endpoint row must produce no explicit witness edges");
    }

    [Fact]
    public void ActiveIncidenceJournal_ShouldRetainItsTreeNodeExactly()
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 1);
        NavigationMap BuildMap() => new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddConnection(new NavigationConnection(
                "edge",
                default,
                new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
                GetFoot(binding, default),
                GetFoot(binding, new VoxelIndex(1, 0, 0)),
                Fixed64.Zero,
                Fixed64.One))
            .Build();
        NavigationOperationCandidate source = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(BuildMap(), 1));
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        NavigationMapFoldWork CreateWork() => new(
            source,
            new PreparedNavigationMap(BuildMap(), 2),
            OverlayReplacementPolicy.Clear,
            defaults.OperationLimits,
            new GridCellPrism[defaults.OperationLimits.MaxCorridorCells],
            new Vector3d[(defaults.OperationLimits.MaxCorridorCells * 2) - 2],
            new NavigationCellAddress[defaults.OperationLimits.MaxCorridorCells],
            new NavigationAddressStampSet(defaults.OperationLimits.MaxCorridorCells));
        MaintenanceWorkMeter CreateMeter(int dependencyEntries) => new(
            new MaintenanceWorkBudget(
                defaults.MaintenanceBudget.MaxConsumedEnvelopes,
                defaults.MaintenanceBudget.MaxBaselineAddresses,
                defaults.MaintenanceBudget.MaxOverlaySlots,
                defaults.MaintenanceBudget.MaxComponentNodes,
                defaults.MaintenanceBudget.MaxSeamCandidateProbes,
                defaults.MaintenanceBudget.MaxExplicitEdges,
                dependencyEntries));
        NavigationMapFoldWork beforeTree = CreateWork();
        MaintenanceWorkMeter beforeMeter = CreateMeter(8);
        beforeTree.Advance(beforeMeter, out NavigationOperationRejection beforeRejection)
            .Should().BeFalse();
        beforeRejection.Should().Be(NavigationOperationRejection.None);
        beforeMeter.DependencyEntries.Should().Be(8);
        NavigationMapFoldWork withTree = CreateWork();
        MaintenanceWorkMeter withMeter = CreateMeter(9);
        withTree.Advance(withMeter, out NavigationOperationRejection withRejection)
            .Should().BeFalse();
        withRejection.Should().Be(NavigationOperationRejection.None);
        withMeter.DependencyEntries.Should().Be(9);

        (withTree.RetainedBytes - beforeTree.RetainedBytes).Should().Be(64);
        (withTree.PersistentPageCount - beforeTree.PersistentPageCount).Should().Be(1);
    }

    [Fact]
    public void MapReplacement_ShouldPublishFinalCanonicalIncidenceOrdering()
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 2);
        VoxelIndex source = default;
        VoxelIndex firstDestination = new(1, 0, 0);
        VoxelIndex secondDestination = new(2, 0, 0);
        NavigationMap BuildMap(bool replacement)
        {
            var builder = new NavigationMapBuilder("map", binding)
                .AddCell(source, SolidCell)
                .AddCell(firstDestination, SolidCell)
                .AddCell(secondDestination, SolidCell);
            (string Id, VoxelIndex Destination)[] definitions = replacement
                ? new[]
                {
                    ("edge-a", firstDestination),
                    ("edge-b", firstDestination),
                    ("edge-c", secondDestination)
                }
                : new[]
                {
                    ("edge-a", secondDestination),
                    ("edge-b", firstDestination),
                    ("edge-c", firstDestination)
                };
            for (int i = 0; i < definitions.Length; i++)
            {
                NavigationCellAddress[] witnesses = definitions[i].Destination == secondDestination
                    ? new[] { new NavigationCellAddress("map", firstDestination) }
                    : Array.Empty<NavigationCellAddress>();
                builder.AddConnection(new NavigationConnection(
                    definitions[i].Id,
                    source,
                    new NavigationCellAddress("map", definitions[i].Destination),
                    GetFoot(binding, source),
                    GetFoot(binding, definitions[i].Destination),
                    Fixed64.Zero,
                    Fixed64.One,
                    witnesses));
            }
            return builder.Build();
        }

        NavigationOperationCandidate candidate = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(BuildMap(replacement: false), 1));
        candidate = FoldMapCandidate(
            candidate,
            new PreparedNavigationMap(BuildMap(replacement: true), 2));

        NavigationExplicitConnectionIndex index = candidate.ExplicitConnections;
        var sourceAddress = new NavigationCellAddress("map", source);
        NavigationPagedSequence<NavigationConnectionOwnerKey> sourceOwners =
            index.GetIncidentOwnerRow(sourceAddress);
        sourceOwners.Count.Should().Be(3);
        sourceOwners[0].ConnectionId.Should().Be("edge-a");
        sourceOwners[1].ConnectionId.Should().Be("edge-b");
        sourceOwners[2].ConnectionId.Should().Be("edge-c");
        var firstAddress = new NavigationCellAddress("map", firstDestination);
        NavigationPagedSequence<NavigationConnectionOwnerKey> firstOwners =
            index.GetIncidentOwnerRow(firstAddress);
        firstOwners.Count.Should().Be(3);
        firstOwners[0].ConnectionId.Should().Be("edge-a");
        firstOwners[1].ConnectionId.Should().Be("edge-b");
        firstOwners[2].ConnectionId.Should().Be("edge-c");
        NavigationPagedSequence<NavigationConnectionOwnerKey> firstEndpoints =
            index.GetEndpointOwnerRow(firstAddress);
        firstEndpoints.Count.Should().Be(2);
        firstEndpoints[0].ConnectionId.Should().Be("edge-a");
        firstEndpoints[1].ConnectionId.Should().Be("edge-b");
        var secondAddress = new NavigationCellAddress("map", secondDestination);
        NavigationPagedSequence<NavigationConnectionOwnerKey> secondOwners =
            index.GetIncidentOwnerRow(secondAddress);
        secondOwners.Count.Should().Be(1);
        secondOwners[0].ConnectionId.Should().Be("edge-c");
    }

    [Fact]
    public void DuplicateSemanticAddresses_ShouldPublishOneOwnerPerIncidentRow()
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 0);
        var source = new NavigationCellAddress("map", default(VoxelIndex));
        var missing = new NavigationCellAddress("missing", default(VoxelIndex));
        var connection = new NavigationConnection(
            "edge",
            source.Index,
            missing,
            GetFoot(binding, source.Index),
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One,
            new[] { source });
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source.Index, SolidCell)
            .Build();

        NavigationOperationCandidate candidate = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, 1));
        candidate = FoldOverlayCandidate(
            candidate,
            new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "map",
                    connections: new[] { NavigationConnectionOverlayOperation.Upsert(connection) })
            }),
            sequence: 1);

        candidate.ExplicitConnections.GetIncidentOwnerRow(source).Count.Should().Be(1);
        candidate.ExplicitConnections.GetIncidentOwnerRow(missing).Count.Should().Be(1);
    }

    [Fact]
    public void CellOnlyRefresh_ShouldReuseRefEqualIncidenceRows()
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 1);
        var source = new NavigationCellAddress("map", default(VoxelIndex));
        var destination = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var definition = new NavigationConnection(
            "edge",
            source.Index,
            destination,
            GetFoot(binding, source.Index),
            GetFoot(binding, destination.Index),
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source.Index, SolidCell)
            .AddCell(destination.Index, SolidCell)
            .AddConnection(definition)
            .Build();
        NavigationOperationCandidate candidate = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, 1));
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorSource =
            candidate.ExplicitConnections.GetIncidentOwnerRow(source);
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorDestination =
            candidate.ExplicitConnections.GetIncidentOwnerRow(destination);
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorSourceEndpoints =
            candidate.ExplicitConnections.GetEndpointOwnerRow(source);
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorDestinationEndpoints =
            candidate.ExplicitConnections.GetEndpointOwnerRow(destination);

        candidate = FoldOverlayCandidate(
            candidate,
            new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "map",
                    new[] { NavigationCellOverlayOperation.Set(source.Index, SolidCell) })
            }),
            sequence: 1);

        candidate.ExplicitConnections.GetIncidentOwnerRow(source)
            .Should().BeSameAs(priorSource);
        candidate.ExplicitConnections.GetIncidentOwnerRow(destination)
            .Should().BeSameAs(priorDestination);
        candidate.ExplicitConnections.GetEndpointOwnerRow(source)
            .Should().BeSameAs(priorSourceEndpoints);
        candidate.ExplicitConnections.GetEndpointOwnerRow(destination)
            .Should().BeSameAs(priorDestinationEndpoints);
    }

    [Fact]
    public void DeferredTransitionOnlyOverlay_ShouldCloseAllThenNarrowToExactEndpoints()
    {
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            maxExplicitEdges: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1);
        NormalizedGridConfiguration sourceBinding = AddGrid(context, 0);
        NormalizedGridConfiguration destinationBinding = AddGrid(context, 10);
        NormalizedGridConfiguration unrelatedBinding = AddGrid(context, 20);
        var first = new TraversalTransitionDefinition(
            "first",
            TraversalTransitionType.Climb,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("destination", default),
            TraversalMedium.Solid,
            TraversalCapability.Climb);
        var second = new TraversalTransitionDefinition(
            "second",
            TraversalTransitionType.Climb,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("destination", default),
            TraversalMedium.Solid,
            TraversalCapability.Climb);
        long sequence = 0;
        NavigationMapCommitOperation destination = Admit(
            context,
            new NavigationMapBuilder("destination", destinationBinding)
                .AddCell(default, SolidCell)
                .Build(),
            ++sequence);
        NavigationMapCommitOperation unrelated = Admit(
            context,
            new NavigationMapBuilder("unrelated", unrelatedBinding)
                .AddCell(default, SolidCell)
                .Build(),
            ++sequence);
        NavigationMapCommitOperation source = Admit(
            context,
            new NavigationMapBuilder("source", sourceBinding)
                .AddCell(default, SolidCell)
                .AddTransition(first)
                .AddTransition(second)
                .Build(),
            ++sequence);
        SimulateUntilTerminal(context, source.Receipt);
        destination.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        unrelated.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        source.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "source",
                    transitions: new[]
                    {
                        TraversalTransitionOverlayOperation.Suppress("first"),
                        TraversalTransitionOverlayOperation.Suppress("second")
                    })
            })),
            ++sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();

        context.Simulate();

        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        GetGraphCell(context, "unrelated").IsMaterialized.Should().BeFalse(
            "operation folding owns an all-close safety root until exact endpoints are known");
        GetGraphCell(context, "source").IsMaterialized.Should().BeFalse();
        GetGraphCell(context, "destination").IsMaterialized.Should().BeFalse();

        for (int frame = 0;
             frame < 64 && overlay.Receipt.Status == NavigationOperationStatus.Pending
                 && !GetGraphCell(context, "unrelated").IsMaterialized;
             frame++)
        {
            context.Simulate();
        }
        GetGraphCell(context, "unrelated").IsMaterialized.Should().BeTrue();
    }

    [Fact]
    public void ExactClosureCapacityRejection_ShouldReopenEveryOwnedComponentOnce()
    {
        long exactPeak = 0;
        long installationPeak;
        using (TrailblazerWorldContext probe = CreateClosureCursorCapacityScenario(
            maxActiveSnapshotBytes: null,
            out NavigationOverlayCommitOperation probeOperation,
            out installationPeak,
            maxRetiredSnapshots: null))
        {
            for (int frame = 0;
                 frame < 512 && probeOperation.Receipt.Status == NavigationOperationStatus.Pending;
                 frame++)
            {
                probe.Simulate();
                long activeBytes =
                    probe.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes;
                if (activeBytes > exactPeak)
                    exactPeak = activeBytes;
            }
            probeOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
        exactPeak.Should().BeGreaterThan(installationPeak,
            "the multi-map exact rebuild must exceed the sequential installation peak");

        int rejectionFrame = -1;
        using (TrailblazerWorldContext timing = CreateClosureCursorCapacityScenario(
            exactPeak - 1,
            out NavigationOverlayCommitOperation timingOperation,
            out _,
            maxRetiredSnapshots: null))
        {
            for (int frame = 0;
                 frame < 512 && timingOperation.Receipt.Status == NavigationOperationStatus.Pending;
                 frame++)
            {
                timing.Simulate();
                if (timingOperation.Receipt.Status == NavigationOperationStatus.Rejected)
                    rejectionFrame = frame;
            }
            timingOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
            timingOperation.Receipt.Rejection.Should().Be(
                NavigationOperationRejection.CapacityExceeded);
        }
        rejectionFrame.Should().BeGreaterThan(0);

        using TrailblazerWorldContext context = CreateClosureCursorCapacityScenario(
            exactPeak - 1,
            out NavigationOverlayCommitOperation operation,
            out _,
            maxRetiredSnapshots: 1);
        using NavigationWorldGraphLease retainedSource =
            context.Pathing.TryAcquireNavigationGraph()!;
        long previousVersion = context.Pathing.GetNavigationGraphDiagnostics().GraphVersion;
        for (int frame = 0; frame < rejectionFrame; frame++)
        {
            context.Simulate();
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
            long version = context.Pathing.GetNavigationGraphDiagnostics().GraphVersion;
            version.Should().BeLessThanOrEqualTo(previousVersion + 1,
                "maintenance may publish at most one safety root per frame");
            previousVersion = version;
        }

        GetGraphCell(context, "map-0").IsMaterialized.Should().BeFalse();
        NavigationWorldGraph rollbackSource = context.Pathing.NavigationGraphStore.Current;
        rollbackSource.Checkout();
        try
        {
            context.Simulate();

            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending,
                "capacity rejection must wait until the owned closure can be rolled back");
            GetGraphCell(context, "map-0").IsMaterialized.Should().BeFalse(
                "failed rollback publication must retain the owned safety closure");
        }
        finally
        {
            rollbackSource.Return();
        }

        retainedSource.Dispose();
        for (int frame = 0;
             frame < 16 && operation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            context.Simulate();
        }
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(
            NavigationOperationRejection.CapacityExceeded);
        for (int i = 0; i < 16; i++)
            GetGraphCell(context, $"map-{i}").IsMaterialized.Should().BeTrue();
        context.Pathing.RetainedOperationWorkCount.Should().Be(0);
    }

    [Fact]
    public void ExternalDestinationRemoveAndInstall_ShouldRetainDormantOwnerIncidence()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration right = AddGrid(context, 3);
        VoxelIndex sourceIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        var destinationAddress = new NavigationCellAddress("right", destinationIndex);
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            destinationAddress,
            GetFoot(left, sourceIndex),
            GetFoot(right, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap destination = new NavigationMapBuilder("right", right)
            .AddCell(destinationIndex, SolidCell)
            .Build();
        Admit(context, destination, 1);
        Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            2);
        context.Simulate();
        var owner = new NavigationConnectionOwnerKey("left", "bridge");
        using NavigationWorldGraphLease oldLease = context.Pathing.TryAcquireNavigationGraph()!;

        var remove = new NavigationMapRemoveOperation(
            "right",
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        context.Simulate();
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using (NavigationWorldGraphLease dormantLease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormantLease.Graph.ExplicitConnections.TryGet(
                    owner,
                    out NavigationExplicitConnectionRecord dormant)
                .Should().BeTrue();
            dormant.IsActive.Should().BeFalse();
            dormantLease.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress).Count
                .Should().Be(1);
        }
        oldLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord old)
            .Should().BeTrue();
        old.IsActive.Should().BeTrue();

        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(destination, bakeVersion: 2),
            OverlayReplacementPolicy.Clear,
            operationSequence: 4,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(install).Should().BeTrue();
        context.Simulate();
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease revivedLease = context.Pathing.TryAcquireNavigationGraph()!;
        revivedLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord revived)
            .Should().BeTrue();
        revived.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ConnectionSuppressRevertAndUpsert_ShouldReplaceOnlyTheCandidateOwner()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration right = AddGrid(context, 3);
        VoxelIndex sourceIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        var destinationAddress = new NavigationCellAddress("right", destinationIndex);
        var baked = new NavigationConnection(
            "bridge",
            sourceIndex,
            destinationAddress,
            GetFoot(left, sourceIndex),
            GetFoot(right, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("right", right)
                .AddCell(destinationIndex, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(baked)
                .Build(),
            2);
        context.Simulate();
        var owner = new NavigationConnectionOwnerKey("left", "bridge");
        using NavigationWorldGraphLease oldLease = context.Pathing.TryAcquireNavigationGraph()!;

        CommitConnection(
            context,
            "left",
            NavigationConnectionOverlayOperation.Suppress("bridge"),
            3);
        context.Simulate();
        using (NavigationWorldGraphLease suppressed = context.Pathing.TryAcquireNavigationGraph()!)
        {
            suppressed.Graph.ExplicitConnections.TryGet(owner, out _).Should().BeFalse();
            suppressed.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress).Count
                .Should().Be(0);
        }
        oldLease.Graph.ExplicitConnections.TryGet(owner, out _).Should().BeTrue();

        CommitConnection(
            context,
            "left",
            NavigationConnectionOverlayOperation.RevertToBake("bridge"),
            4);
        context.Simulate();
        long revertedComponentVersion;
        using (NavigationWorldGraphLease reverted = context.Pathing.TryAcquireNavigationGraph()!)
        {
            reverted.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord record)
                .Should().BeTrue();
            record.Definition.Should().BeSameAs(baked);
            reverted.Graph.SurfaceComponents.TryGet(
                    new NavigationCellAddress("left", sourceIndex),
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponent component)
                .Should().BeTrue();
            revertedComponentVersion = component.Version;
        }

        var upsert = new NavigationConnection(
            "bridge",
            sourceIndex,
            destinationAddress,
            GetFoot(left, sourceIndex),
            GetFoot(right, destinationIndex),
            Fixed64.Zero,
            Fixed64.One,
            additionalCost: (Fixed64)2);
        CommitConnection(
            context,
            "left",
            NavigationConnectionOverlayOperation.Upsert(upsert),
            5);
        context.Simulate();
        using NavigationWorldGraphLease updated = context.Pathing.TryAcquireNavigationGraph()!;
        updated.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord replacement)
            .Should().BeTrue();
        replacement.Definition.Should().BeSameAs(upsert);
        updated.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress).Count.Should().Be(1);
        updated.Graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("left", sourceIndex),
                TraversalMedium.Solid,
                out NavigationSurfaceComponent updatedComponent)
            .Should().BeTrue();
        updatedComponent.Version.Should().BeGreaterThan(revertedComponentVersion,
            "same-endpoint cost changes must invalidate component-stamped cached paths");
    }

    [Fact]
    public void SourceReplacementAndRemoval_ShouldReplaceThenDeleteOwnedRecord()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration right = AddGrid(context, 3);
        VoxelIndex sourceIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        var destinationAddress = new NavigationCellAddress("right", destinationIndex);
        NavigationConnection Connection(Fixed64 additionalCost) => new(
            "bridge",
            sourceIndex,
            destinationAddress,
            GetFoot(left, sourceIndex),
            GetFoot(right, destinationIndex),
            Fixed64.Zero,
            Fixed64.One,
            additionalCost: additionalCost);
        Admit(
            context,
            new NavigationMapBuilder("right", right)
                .AddCell(destinationIndex, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(Connection(Fixed64.Zero))
                .Build(),
            2);
        context.Simulate();
        var owner = new NavigationConnectionOwnerKey("left", "bridge");
        using NavigationWorldGraphLease oldLease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationConnection replacement = Connection((Fixed64)2);
        var replace = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("left", left)
                    .AddCell(sourceIndex, SolidCell)
                    .AddConnection(replacement)
                    .Build(),
                bakeVersion: 2),
            OverlayReplacementPolicy.Clear,
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(replace).Should().BeTrue();
        context.Simulate();
        using (NavigationWorldGraphLease replaced = context.Pathing.TryAcquireNavigationGraph()!)
        {
            replaced.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord record)
                .Should().BeTrue();
            record.Definition.Should().BeSameAs(replacement);
        }
        oldLease.Graph.ExplicitConnections.TryGet(owner, out NavigationExplicitConnectionRecord old)
            .Should().BeTrue();
        old.Definition.AdditionalCost.Should().Be(Fixed64.Zero);

        var remove = new NavigationMapRemoveOperation(
            "left",
            operationSequence: 4,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease removed = context.Pathing.TryAcquireNavigationGraph()!;
        removed.Graph.ExplicitConnections.TryGet(owner, out _).Should().BeFalse();
        removed.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress).Count.Should().Be(0);
        removed.Graph.ExplicitConnections.GetEndpointOwnerRow(destinationAddress).Count.Should().Be(0);
    }

    [Fact]
    public void DeferredIncidenceRebuild_ShouldPublishAllRowsAtomically()
    {
        using TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            maxExplicitEdges: 16,
            maxDependencyEntries: 3);
        NormalizedGridConfiguration binding = AddGrid(context, 0);
        VoxelIndex sourceIndex = default;
        VoxelIndex destinationIndex = new(1, 0, 0);
        var sourceAddress = new NavigationCellAddress("map", sourceIndex);
        var destinationAddress = new NavigationCellAddress("map", destinationIndex);
        NavigationMap BuildMap(Fixed64 additionalCost) => new NavigationMapBuilder("map", binding)
            .AddCell(sourceIndex, SolidCell)
            .AddCell(destinationIndex, SolidCell)
            .AddConnection(new NavigationConnection(
                "edge",
                sourceIndex,
                destinationAddress,
                GetFoot(binding, sourceIndex),
                GetFoot(binding, destinationIndex),
                Fixed64.Zero,
                Fixed64.One,
                additionalCost: additionalCost))
            .Build();
        NavigationMapCommitOperation initial = Admit(context, BuildMap(Fixed64.Zero), 1);
        SimulateUntilTerminal(context, initial.Receipt);
        initial.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorSource;
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorDestination;
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorSourceEndpoints;
        NavigationPagedSequence<NavigationConnectionOwnerKey> priorDestinationEndpoints;
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            priorSource = lease.Graph.ExplicitConnections.GetIncidentOwnerRow(sourceAddress);
            priorDestination = lease.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress);
            priorSourceEndpoints = lease.Graph.ExplicitConnections.GetEndpointOwnerRow(sourceAddress);
            priorDestinationEndpoints =
                lease.Graph.ExplicitConnections.GetEndpointOwnerRow(destinationAddress);
        }

        var replacement = new NavigationMapCommitOperation(
            new PreparedNavigationMap(BuildMap(Fixed64.One), bakeVersion: 2),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(replacement).Should().BeTrue();
        bool observedDeferred = false;
        for (int frame = 0; frame < 4096 && replacement.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            context.Simulate();
            if (replacement.Receipt.Status != NavigationOperationStatus.Pending)
                break;
            observedDeferred = true;
            using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
            lease.Graph.ExplicitConnections.GetIncidentOwnerRow(sourceAddress)
                .Should().BeSameAs(priorSource);
            lease.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress)
                .Should().BeSameAs(priorDestination);
            lease.Graph.ExplicitConnections.GetEndpointOwnerRow(sourceAddress)
                .Should().BeSameAs(priorSourceEndpoints);
            lease.Graph.ExplicitConnections.GetEndpointOwnerRow(destinationAddress)
                .Should().BeSameAs(priorDestinationEndpoints);
        }

        observedDeferred.Should().BeTrue();
        replacement.Receipt.Status.Should().Be(
            NavigationOperationStatus.Applied,
            $"rejection={replacement.Receipt.Rejection}");
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.ExplicitConnections.GetIncidentOwnerRow(sourceAddress)
            .Should().NotBeSameAs(priorSource);
        published.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress)
            .Should().NotBeSameAs(priorDestination);
        published.Graph.ExplicitConnections.GetEndpointOwnerRow(sourceAddress)
            .Should().NotBeSameAs(priorSourceEndpoints);
        published.Graph.ExplicitConnections.GetEndpointOwnerRow(destinationAddress)
            .Should().NotBeSameAs(priorDestinationEndpoints);
    }

    [Fact]
    public void MultiWitnessCompilation_ShouldCaptureOneSemanticPrismPerExplicitDebit()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(4, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var source = new VoxelIndex(0, 0, 0);
        var firstWitness = new VoxelIndex(1, 0, 0);
        var secondWitness = new VoxelIndex(2, 0, 0);
        var destination = new VoxelIndex(3, 0, 0);
        var connection = new NavigationConnection(
            "corridor",
            source,
            new NavigationCellAddress("map", destination),
            GetFoot(binding, source),
            GetFoot(binding, destination),
            Fixed64.Zero,
            Fixed64.One,
            new[]
            {
                new NavigationCellAddress("map", firstWitness),
                new NavigationCellAddress("map", secondWitness)
            });
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(firstWitness, SolidCell)
            .AddCell(secondWitness, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(connection)
            .Build();
        var captured = new GridCellPrism[4];
        var work = new NavigationMapFoldWork(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            TrailblazerWorldContextSettings.Default.OperationLimits,
            captured,
            new Vector3d[6],
            new NavigationCellAddress[4],
            new NavigationAddressStampSet(4));
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 1,
            maxBaselineAddresses: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 16));
        VoxelIndex[] expected = { source, firstWitness, secondWitness, destination };

        int semanticStep = 0;
        int explicitUnits = 0;
        bool complete = false;
        for (int frame = 0; frame < 128 && !complete; frame++)
        {
            complete = work.Advance(meter, out NavigationOperationRejection rejection);
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.ExplicitEdges.Should().BeLessThanOrEqualTo(1);
            explicitUnits += meter.ExplicitEdges;
            if (semanticStep < expected.Length && captured[semanticStep].FootprintVertexCount != 0)
            {
                binding.TryGetCellPrism(expected[semanticStep], out GridCellPrism prism)
                    .Should().BeTrue();
                captured[semanticStep].Should().Be(prism,
                    "each semantic-cell debit must immediately capture that exact prism");
                meter.ExplicitEdges.Should().Be(1);
                semanticStep++;
                if (semanticStep < captured.Length)
                    captured[semanticStep].Should().Be(default(GridCellPrism));
            }
            meter.Reset();
        }

        complete.Should().BeTrue();
        semanticStep.Should().Be(expected.Length);
        work.Candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "corridor"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        record.IsActive.Should().BeTrue();
        record.NavigationPortals.Count.Should().Be(expected.Length - 1,
            "the retained certificate sequence must contain exactly one cursor-validated portal per adjacent semantic pair");
        explicitUnits.Should().Be(14,
            "each adjacent portal certificate is retained during its cursor validation debit");
    }

    [Fact]
    public void IncidenceMutation_ShouldPublishOneCandidateRootStepPerDependencyDebit()
    {
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var source = new VoxelIndex(0, 0, 0);
        var destination = new VoxelIndex(1, 0, 0);
        var connection = new NavigationConnection(
            "edge",
            source,
            new NavigationCellAddress("map", destination),
            GetFoot(binding, source),
            GetFoot(binding, destination),
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(connection)
            .Build();
        var work = new NavigationMapFoldWork(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            TrailblazerWorldContextSettings.Default.OperationLimits,
            new GridCellPrism[2],
            new Vector3d[2],
            new NavigationCellAddress[2],
            new NavigationAddressStampSet(2));
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 1, 1, 1, 16, 16, 1));
        var sourceAddress = new NavigationCellAddress("map", source);
        var destinationAddress = new NavigationCellAddress("map", destination);

        bool complete = false;
        bool sawSourceOnly = false;
        for (int frame = 0; frame < 64 && !complete; frame++)
        {
            complete = work.Advance(meter, out NavigationOperationRejection rejection);
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            int sourceCount = work.Candidate.ExplicitConnections
                .GetIncidentOwnerRow(sourceAddress).Count;
            int destinationCount = work.Candidate.ExplicitConnections
                .GetIncidentOwnerRow(destinationAddress).Count;
            if (sourceCount == 1 && destinationCount == 0)
                sawSourceOnly = true;
            destinationCount.Should().BeLessThanOrEqualTo(sourceCount,
                "a later incidence row cannot publish ahead of the prior source row");
            meter.Reset();
        }

        complete.Should().BeTrue();
        sawSourceOnly.Should().BeTrue();
        work.Candidate.ExplicitConnections.GetIncidentOwnerRow(sourceAddress).Count.Should().Be(1);
        work.Candidate.ExplicitConnections.GetIncidentOwnerRow(destinationAddress).Count.Should().Be(1);
        work.Candidate.ExplicitConnections.GetEndpointOwnerRow(sourceAddress).Count.Should().Be(1);
        work.Candidate.ExplicitConnections.GetEndpointOwnerRow(destinationAddress).Count.Should().Be(1);
    }

    [Fact]
    public void AddressStampReset_ShouldDiscardAbandonedOwnerKeys()
    {
        var addresses = new NavigationAddressStampSet(64);
        var first = new NavigationCellAddress("map", default);
        var second = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        addresses.Add(first).Should().BeTrue();
        addresses.Add(second).Should().BeTrue();

        addresses.Reset();

        addresses.Add(first).Should().BeTrue(
            "an abandoned refresh generation must not leak keys into the next owner");
        addresses.Add(first).Should().BeFalse();
    }

    [Fact]
    public void ExplicitRefreshConstruction_ShouldAccountForEveryOwnedObject()
    {
        const int Iterations = 256;
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var candidate = new NavigationOperationCandidate(navigationAreaCount: 1);
        var prisms = new GridCellPrism[capacity];
        var waypoints = new Vector3d[(capacity * 2) - 2];
        var addresses = new NavigationCellAddress[capacity];
        var addressSet = new NavigationAddressStampSet(capacity);
        NavigationOperationCandidate.ExplicitConnectionRefreshWork warmup =
            candidate.BeginExplicitConnectionRefresh(
                "missing",
                candidate.ExplicitConnections,
                prisms,
                waypoints,
                addresses,
                addressSet);
        GC.KeepAlive(warmup);
        long retained = 0;

        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < Iterations; i++)
            {
                NavigationOperationCandidate.ExplicitConnectionRefreshWork work =
                    candidate.BeginExplicitConnectionRefresh(
                        "missing",
                        candidate.ExplicitConnections,
                        prisms,
                        waypoints,
                        addresses,
                        addressSet);
                retained += work.RetainedBytes;
                GC.KeepAlive(work);
            }
        });

        retained.Should().Be(allocated + (Iterations * 64L),
            "the exact refresh allocation and its two logical persistent-root wrappers are retained");
    }

    [Fact]
    public void RepeatedSameRowReplacement_ShouldRetainOnlyLiveExplicitPayloadOwnership()
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 2);
        VoxelIndex sourceIndex = default;
        VoxelIndex destinationIndex = new(1, 0, 0);
        NavigationConnection baked = new(
            "edge",
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            GetFoot(binding, sourceIndex),
            GetFoot(binding, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(sourceIndex, SolidCell)
            .AddCell(destinationIndex, SolidCell)
            .AddConnection(baked)
            .Build();
        NavigationOperationCandidate candidate = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, 1));
        candidate.ResetWorkCopiedPersistentOwnership();
        NavigationConnection replacement = new(
            "edge",
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            GetFoot(binding, sourceIndex),
            GetFoot(binding, destinationIndex),
            Fixed64.Zero,
            Fixed64.One,
            additionalCost: Fixed64.One);
        candidate = FoldOverlayCandidate(
            candidate,
            new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "map",
                    connections: new[] { NavigationConnectionOverlayOperation.Upsert(replacement) })
            }),
            sequence: 1);
        candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "edge"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        long onceBytes = candidate.WorkOwnedExplicitPayloadBytes;
        int oncePages = candidate.WorkOwnedExplicitPayloadPages;
        var sourceAddress = new NavigationCellAddress("map", sourceIndex);
        var destinationAddress = new NavigationCellAddress("map", destinationIndex);
        NavigationPagedSequence<NavigationConnectionOwnerKey>[] liveRows =
        {
            candidate.ExplicitConnections.GetIncidentOwnerRow(sourceAddress),
            candidate.ExplicitConnections.GetIncidentOwnerRow(destinationAddress),
            candidate.ExplicitConnections.GetEndpointOwnerRow(sourceAddress),
            candidate.ExplicitConnections.GetEndpointOwnerRow(destinationAddress)
        };
        long rowBytes = 0;
        int rowPages = 0;
        for (int i = 0; i < liveRows.Length; i++)
        {
            rowBytes += liveRows[i].RetainedBytes;
            rowPages += liveRows[i].PersistentPageCount;
        }
        onceBytes.Should().Be(record.RetainedBytes + rowBytes,
            "the live record and its dependency and endpoint rows are the only payload replacements");
        oncePages.Should().Be(record.PersistentPageCount + rowPages);

        candidate = FoldOverlayCandidate(
            candidate,
            new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "map",
                    new[] { NavigationCellOverlayOperation.Set(sourceIndex, SolidCell) })
            }),
            sequence: 2);

        candidate.WorkOwnedExplicitPayloadBytes.Should().Be(onceBytes,
            "replacing the same record and rows twice must overwrite the live ownership ledger");
        candidate.WorkOwnedExplicitPayloadPages.Should().Be(oncePages);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    public void VariableSizeReplacement_ShouldChargeCurrentAndDisplacedPayloadExactly(
        int sourceWitnessCount,
        int targetWitnessCount)
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 1);
        var source = new VoxelIndex(0, 0, 0);
        NavigationConnection CreateConnection(int witnessCount, Fixed64 additionalCost) => new(
            "edge",
            source,
            new NavigationCellAddress("missing-destination", default),
            GetFoot(binding, source),
            GetFoot(binding, source),
            Fixed64.Zero,
            Fixed64.One,
            witnessCount switch
            {
                0 => null,
                1 => new[] { new NavigationCellAddress("missing-witness-1", default) },
                _ => new[]
                {
                    new NavigationCellAddress("missing-witness-1", default),
                    new NavigationCellAddress("missing-witness-2", default)
                }
            },
            additionalCost);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddConnection(CreateConnection(1, Fixed64.Zero))
            .Build();
        NavigationOperationCandidate published = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, 1));
        published.ResetWorkCopiedPersistentOwnership();
        NavigationOperationCandidate first = FoldOverlayCandidate(
            published,
            ConnectionTransaction(CreateConnection(sourceWitnessCount, Fixed64.One)),
            sequence: 1);
        long sourceBytes = first.WorkOwnedExplicitPayloadBytes;
        int sourcePages = first.WorkOwnedExplicitPayloadPages;

        NavigationOverlayFoldWork replacement = FoldOverlayWork(
            first,
            ConnectionTransaction(CreateConnection(targetWitnessCount, (Fixed64)2)),
            sequence: 2);

        replacement.DisplacedExplicitPayloadBytes.Should().Be(sourceBytes);
        replacement.DisplacedExplicitPayloadPages.Should().Be(sourcePages);
        replacement.Candidate.WorkOwnedExplicitPayloadBytes.Should().NotBe(sourceBytes,
            "the target payload cardinality differs from the displaced source");
    }

    [Fact]
    public void DifferentOwnerReplacement_ShouldNotChargeSharedSourcePayloadTwice()
    {
        NormalizedGridConfiguration binding = CreateBinding(0, 4);
        var firstSource = new VoxelIndex(0, 0, 0);
        var firstDestination = new VoxelIndex(1, 0, 0);
        var secondSource = new VoxelIndex(2, 0, 0);
        var secondDestination = new VoxelIndex(3, 0, 0);
        NavigationConnection CreateConnection(
            string id,
            VoxelIndex source,
            VoxelIndex destination,
            Fixed64 additionalCost) => new(
                id,
                source,
                new NavigationCellAddress("map", destination),
                GetFoot(binding, source),
                GetFoot(binding, destination),
                Fixed64.Zero,
                Fixed64.One,
                additionalCost: additionalCost);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(firstSource, SolidCell)
            .AddCell(firstDestination, SolidCell)
            .AddCell(secondSource, SolidCell)
            .AddCell(secondDestination, SolidCell)
            .AddConnection(CreateConnection("x", firstSource, firstDestination, Fixed64.Zero))
            .AddConnection(CreateConnection("y", secondSource, secondDestination, Fixed64.Zero))
            .Build();
        NavigationOperationCandidate published = FoldMapCandidate(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, 1));
        published.ResetWorkCopiedPersistentOwnership();
        NavigationOperationCandidate first = FoldOverlayCandidate(
            published,
            ConnectionTransaction(CreateConnection(
                "x",
                firstSource,
                firstDestination,
                Fixed64.One)),
            sequence: 1);
        long firstBytes = first.WorkOwnedExplicitPayloadBytes;
        int firstPages = first.WorkOwnedExplicitPayloadPages;

        NavigationOverlayFoldWork second = FoldOverlayWork(
            first,
            ConnectionTransaction(CreateConnection(
                "y",
                secondSource,
                secondDestination,
                Fixed64.One)),
            sequence: 2);

        second.DisplacedExplicitPayloadBytes.Should().Be(0);
        second.DisplacedExplicitPayloadPages.Should().Be(0);
        second.Candidate.WorkOwnedExplicitPayloadBytes.Should().Be(firstBytes * 2);
        second.Candidate.WorkOwnedExplicitPayloadPages.Should().Be(firstPages * 2);
    }

    [Fact]
    public void SurfaceEnumeration_ShouldMergeByDurableEndpointBeforeEdgeKind()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration destinationBinding = AddGrid(context, -3);
        NormalizedGridConfiguration sourceBinding = AddGrid(context, 0);
        VoxelIndex sourceIndex = default;
        VoxelIndex nativeIndex = new(1, 0, 0);
        VoxelIndex explicitIndex = new(2, 0, 0);
        var connection = new NavigationConnection(
            "cross",
            sourceIndex,
            new NavigationCellAddress("a-destination", explicitIndex),
            GetFoot(sourceBinding, sourceIndex),
            GetFoot(destinationBinding, explicitIndex),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("a-destination", destinationBinding)
                .AddCell(explicitIndex, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("z-source", sourceBinding)
                .AddCell(sourceIndex, SolidCell)
                .AddCell(nativeIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            2);
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("z-source", sourceIndex),
                out NavigationNodeRef source)
            .Should().BeTrue();
        var kinds = new List<NavigationGraphEdgeKind>();
        var endpoints = new List<NavigationCellAddress>();

        NavigationSurfaceEdgeEnumerator edges = lease.Graph.EnumerateSurfaceEdges(source);
        while (edges.MoveNext())
        {
            kinds.Add(edges.Current.Kind);
            lease.Graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress address)
                .Should().BeTrue();
            endpoints.Add(address);
        }

        kinds.Should().Equal(
            NavigationGraphEdgeKind.Explicit,
            NavigationGraphEdgeKind.Seam,
            NavigationGraphEdgeKind.Native);
        endpoints.Should().Equal(
            new NavigationCellAddress("a-destination", explicitIndex),
            new NavigationCellAddress("a-destination", explicitIndex),
            new NavigationCellAddress("z-source", nativeIndex));
    }

    [Fact]
    public void SurfaceEnumeration_ShouldOrderExplicitHeadsByDestinationBeforeConnectionId()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration destinationBinding = AddGrid(context, -3);
        NormalizedGridConfiguration sourceBinding = AddGrid(context, 0);
        VoxelIndex sourceIndex = default;
        VoxelIndex nativeIndex = new(1, 0, 0);
        VoxelIndex firstDestination = new(1, 0, 0);
        VoxelIndex secondDestination = new(2, 0, 0);
        var first = new NavigationConnection(
            "z-first-endpoint",
            sourceIndex,
            new NavigationCellAddress("a-destination", firstDestination),
            GetFoot(sourceBinding, sourceIndex),
            GetFoot(destinationBinding, firstDestination),
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("a-destination", secondDestination) });
        var second = new NavigationConnection(
            "a-second-endpoint",
            sourceIndex,
            new NavigationCellAddress("a-destination", secondDestination),
            GetFoot(sourceBinding, sourceIndex),
            GetFoot(destinationBinding, secondDestination),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("a-destination", destinationBinding)
                .AddCell(firstDestination, SolidCell)
                .AddCell(secondDestination, SolidCell)
                .Build(),
            1);
        NavigationMapCommitOperation sourceCommit = Admit(
            context,
            new NavigationMapBuilder("z-source", sourceBinding)
                .AddCell(sourceIndex, SolidCell)
                .AddCell(nativeIndex, SolidCell)
                .AddConnection(first)
                .AddConnection(second)
                .Build(),
            2);
        SimulateUntilTerminal(context, sourceCommit.Receipt);
        sourceCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var sourceAddress = new NavigationCellAddress("z-source", sourceIndex);
        lease.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef source).Should().BeTrue();
        NavigationPagedSequence<NavigationConnectionOwnerKey> endpointOwners =
            lease.Graph.ExplicitConnections.GetEndpointOwnerRow(sourceAddress);
        endpointOwners.Count.Should().Be(2);
        endpointOwners[0].ConnectionId.Should().Be("z-first-endpoint");
        endpointOwners[1].ConnectionId.Should().Be("a-second-endpoint");
        var kinds = new List<NavigationGraphEdgeKind>();
        var endpoints = new List<NavigationCellAddress>();
        var connectionIds = new List<string?>();

        NavigationSurfaceEdgeEnumerator edges = lease.Graph.EnumerateSurfaceEdges(source);
        while (edges.MoveNext())
        {
            kinds.Add(edges.Current.Kind);
            lease.Graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress endpoint)
                .Should().BeTrue();
            endpoints.Add(endpoint);
            connectionIds.Add(edges.Current.Kind == NavigationGraphEdgeKind.Explicit
                ? edges.Current.ExplicitConnection.Owner.ConnectionId
                : null);
        }

        kinds.Should().Equal(
            NavigationGraphEdgeKind.Explicit,
            NavigationGraphEdgeKind.Explicit,
            NavigationGraphEdgeKind.Seam,
            NavigationGraphEdgeKind.Native);
        endpoints.Should().Equal(
            new NavigationCellAddress("a-destination", firstDestination),
            new NavigationCellAddress("a-destination", secondDestination),
            new NavigationCellAddress("a-destination", secondDestination),
            new NavigationCellAddress("z-source", nativeIndex));
        connectionIds.Should().Equal(
            "z-first-endpoint",
            "a-second-endpoint",
            null,
            null);
    }

    [Fact]
    public void ExplicitEvaluation_ShouldUseCertifiedCostInclusiveCapacityAndOneWayDirection()
    {
        using TrailblazerWorldContext context = CreateExplicitEvaluationContext();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef source = ResolveNode(lease.Graph, default);
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("map", new VoxelIndex(2, 0, 0)),
                out NavigationNodeRef destination)
            .Should().BeTrue();
        NavigationGraphEdge edge = FindExplicitEdge(lease.Graph, source, "shortcut");
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            CreateExplicitEvaluationProfile(),
            CreateExplicitEvaluationPolicy(),
            TraversalMedium.Solid);

        evaluator.EvaluateEdge(source, edge, out TraversalEdgeEvidence evidence)
            .Should().Be(TraversalEvaluationStatus.Passable);
        evidence.Cost.Should().Be((Fixed64)11);
        evaluator.EvaluateEdge(destination, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Stale,
                "using a directed explicit certificate from the wrong source is structural misuse");
        var oversized = new TraversalEvaluator(
                lease.Graph,
                new NavigationAgentProfile(
                new KinematicBodyShape((Fixed64)0.5001m, Fixed64.One, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None),
            CreateExplicitEvaluationPolicy(),
            TraversalMedium.Solid);
        oversized.EvaluateEdge(source, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Impassable);

        NavigationSurfaceEdgeEnumerator reverse = lease.Graph.EnumerateSurfaceEdges(destination);
        while (reverse.MoveNext())
            reverse.Current.Kind.Should().NotBe(NavigationGraphEdgeKind.Explicit);
    }

    [Fact]
    public void ExplicitEvaluation_ShouldRejectBlockedWitnessBeforeCostOverflow()
    {
        using TrailblazerWorldContext context = CreateExplicitEvaluationContext();
        VoxelIndex witnessIndex = new(1, 0, 0);
        VoxelIndex destinationIndex = new(2, 0, 0);
        NavigationOverlayCommitOperation costChange = CommitCell(
            context,
            "map",
            NavigationCellOverlayOperation.Set(
                destinationIndex,
                new NavigationCell(
                    TraversalMedia.Solid,
                    TraversalCapability.None,
                    default,
                    Fixed64.MaxValue,
                    Fixed64.Half,
                    Fixed64.One)),
            2);
        SimulateUntilTerminal(context, costChange.Receipt);
        costChange.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(witnessIndex, out Voxel? witness).Should().BeTrue();
        GridForge.ObstacleToken obstacle = context.World.AllocateObstacleToken();
        grid.TryAddObstacle(witness!, obstacle).Should().BeTrue();
        context.Simulate();
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            NavigationNodeRef source = ResolveNode(lease.Graph, default);
            NavigationGraphEdge edge = FindExplicitEdge(lease.Graph, source, "shortcut");
            new TraversalEvaluator(
                    lease.Graph,
                    CreateExplicitEvaluationProfile(),
                    CreateExplicitEvaluationPolicy(),
                    TraversalMedium.Solid)
                .EvaluateEdge(source, edge, out _)
                .Should().Be(TraversalEvaluationStatus.Impassable);
        }

        grid.TryRemoveObstacle(witness!, obstacle).Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease overflowLease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationNodeRef overflowSource = ResolveNode(overflowLease.Graph, default);
        NavigationGraphEdge overflowEdge = FindExplicitEdge(
            overflowLease.Graph,
            overflowSource,
            "shortcut");
        new TraversalEvaluator(
            overflowLease.Graph,
            CreateExplicitEvaluationProfile(),
            CreateExplicitEvaluationPolicy(),
            TraversalMedium.Solid)
            .EvaluateEdge(overflowSource, overflowEdge, out TraversalEdgeEvidence overflow)
            .Should().Be(TraversalEvaluationStatus.CostOverflow);
        overflow.Cost.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void ExplicitEvaluation_ShouldApplyStepLimitToEachSemanticLeg()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration sourceBinding = AddGrid(context, 0, Fixed64.Zero);
        NormalizedGridConfiguration witnessBinding = AddGrid(context, 0, Fixed64.One);
        NormalizedGridConfiguration destinationBinding = AddGrid(context, 0, (Fixed64)2);
        VoxelIndex sourceIndex = default;
        var connection = new NavigationConnection(
            "steps",
            sourceIndex,
            new NavigationCellAddress("destination", default),
            GetFoot(sourceBinding, sourceIndex),
            GetFoot(destinationBinding, default),
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("witness", default) });
        Admit(context, new NavigationMapBuilder("destination", destinationBinding)
            .AddCell(default, SolidCell).Build(), 1);
        Admit(context, new NavigationMapBuilder("witness", witnessBinding)
            .AddCell(default, SolidCell).Build(), 2);
        NavigationMapCommitOperation sourceCommit = Admit(
            context,
            new NavigationMapBuilder("source", sourceBinding)
                .AddCell(sourceIndex, SolidCell).AddConnection(connection).Build(),
            3);
        context.Simulate();
        sourceCommit.Receipt.Rejection.Should().Be(NavigationOperationRejection.None);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
            new NavigationCellAddress("source", sourceIndex), out NavigationNodeRef source).Should().BeTrue();
        NavigationGraphEdge edge = FindExplicitEdge(lease.Graph, source, "steps");

        CreateEvaluator(lease.Graph, maxStepUp: Fixed64.One, maxDropDown: Fixed64.Zero)
            .EvaluateEdge(source, edge, out _).Should().Be(TraversalEvaluationStatus.Passable,
                "two one-unit steps are valid even though the total rise is two units");
        CreateEvaluator(
                lease.Graph,
                maxStepUp: Fixed64.One - Fixed64.FromRaw(1),
                maxDropDown: Fixed64.Zero)
            .EvaluateEdge(source, edge, out _).Should().Be(TraversalEvaluationStatus.Impassable);
    }

    [Fact]
    public void ExplicitEvaluation_ShouldApplyDropLimitToEachSemanticLeg()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration sourceBinding = AddGrid(context, 0, (Fixed64)2);
        NormalizedGridConfiguration witnessBinding = AddGrid(context, 0, Fixed64.One);
        NormalizedGridConfiguration destinationBinding = AddGrid(context, 0, Fixed64.Zero);
        VoxelIndex sourceIndex = default;
        var connection = new NavigationConnection(
            "drops",
            sourceIndex,
            new NavigationCellAddress("destination", default),
            GetFoot(sourceBinding, sourceIndex),
            GetFoot(destinationBinding, default),
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("witness", default) });
        Admit(context, new NavigationMapBuilder("destination", destinationBinding)
            .AddCell(default, SolidCell).Build(), 1);
        Admit(context, new NavigationMapBuilder("witness", witnessBinding)
            .AddCell(default, SolidCell).Build(), 2);
        NavigationMapCommitOperation sourceCommit = Admit(
            context,
            new NavigationMapBuilder("source", sourceBinding)
                .AddCell(sourceIndex, SolidCell).AddConnection(connection).Build(),
            3);
        context.Simulate();
        sourceCommit.Receipt.Rejection.Should().Be(NavigationOperationRejection.None);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
            new NavigationCellAddress("source", sourceIndex), out NavigationNodeRef source).Should().BeTrue();
        NavigationGraphEdge edge = FindExplicitEdge(lease.Graph, source, "drops");

        CreateEvaluator(lease.Graph, maxStepUp: Fixed64.Zero, maxDropDown: Fixed64.One)
            .EvaluateEdge(source, edge, out _).Should().Be(TraversalEvaluationStatus.Passable);
        CreateEvaluator(
                lease.Graph,
                maxStepUp: Fixed64.Zero,
                maxDropDown: Fixed64.One - Fixed64.FromRaw(1))
            .EvaluateEdge(source, edge, out _).Should().Be(TraversalEvaluationStatus.Impassable);
    }

    [Fact]
    public void IncomingRows_ShouldOrderSameLocalIdByDurableSourceWithoutDuplicates()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration destinationBinding = AddGrid(context, 3);
        NormalizedGridConfiguration right = AddGrid(context, 4);
        VoxelIndex leftIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        VoxelIndex rightIndex = default;
        NavigationConnection LeftConnection() => new(
            "shared",
            leftIndex,
            new NavigationCellAddress("destination", destinationIndex),
            GetFoot(left, leftIndex),
            GetFoot(destinationBinding, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        NavigationConnection RightConnection() => new(
            "shared",
            rightIndex,
            new NavigationCellAddress("destination", destinationIndex),
            GetFoot(right, rightIndex),
            GetFoot(destinationBinding, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("destination", destinationBinding)
                .AddCell(destinationIndex, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("a-source", left)
                .AddCell(leftIndex, SolidCell)
                .AddConnection(LeftConnection())
                .Build(),
            2);
        Admit(
            context,
            new NavigationMapBuilder("z-source", right)
                .AddCell(rightIndex, SolidCell)
                .AddConnection(RightConnection())
                .Build(),
            3);
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var destinationAddress = new NavigationCellAddress("destination", destinationIndex);
        lease.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destination)
            .Should().BeTrue();
        lease.Graph.ExplicitConnections.GetIncidentOwnerRow(destinationAddress).Count.Should().Be(2);
        var sources = new List<NavigationCellAddress>();

        NavigationSurfaceEdgeEnumerator incoming =
            lease.Graph.EnumerateIncomingExplicitSurfaceEdges(destination);
        while (incoming.MoveNext())
        {
            lease.Graph.TryGetNodeAddress(incoming.Current.Target, out NavigationCellAddress source)
                .Should().BeTrue();
            sources.Add(source);
        }

        sources.Should().Equal(
            new NavigationCellAddress("a-source", leftIndex),
            new NavigationCellAddress("z-source", rightIndex));
    }

    [Fact]
    public void IncomingExplicitEnumeration_ShouldOrderHeadsBySourceBeforeConnectionId()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration sourceBinding = AddGrid(context, 0);
        NormalizedGridConfiguration destinationBinding = AddGrid(context, 3);
        VoxelIndex firstSource = new(1, 0, 0);
        VoxelIndex secondSource = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        var first = new NavigationConnection(
            "z-first-source",
            firstSource,
            new NavigationCellAddress("destination", destinationIndex),
            GetFoot(sourceBinding, firstSource),
            GetFoot(destinationBinding, destinationIndex),
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("source", secondSource) });
        var second = new NavigationConnection(
            "a-second-source",
            secondSource,
            new NavigationCellAddress("destination", destinationIndex),
            GetFoot(sourceBinding, secondSource),
            GetFoot(destinationBinding, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("destination", destinationBinding)
                .AddCell(destinationIndex, SolidCell)
                .Build(),
            1);
        NavigationMapCommitOperation sourceCommit = Admit(
            context,
            new NavigationMapBuilder("source", sourceBinding)
                .AddCell(firstSource, SolidCell)
                .AddCell(secondSource, SolidCell)
                .AddConnection(first)
                .AddConnection(second)
                .Build(),
            2);
        SimulateUntilTerminal(context, sourceCommit.Receipt);
        sourceCommit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var destinationAddress = new NavigationCellAddress("destination", destinationIndex);
        lease.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destination)
            .Should().BeTrue();
        NavigationPagedSequence<NavigationConnectionOwnerKey> endpointOwners =
            lease.Graph.ExplicitConnections.GetEndpointOwnerRow(destinationAddress);
        endpointOwners.Count.Should().Be(2);
        endpointOwners[0].ConnectionId.Should().Be("z-first-source");
        endpointOwners[1].ConnectionId.Should().Be("a-second-source");
        var sources = new List<NavigationCellAddress>();
        var connectionIds = new List<string>();

        NavigationSurfaceEdgeEnumerator incoming =
            lease.Graph.EnumerateIncomingExplicitSurfaceEdges(destination);
        while (incoming.MoveNext())
        {
            lease.Graph.TryGetNodeAddress(incoming.Current.Target, out NavigationCellAddress source)
                .Should().BeTrue();
            sources.Add(source);
            connectionIds.Add(incoming.Current.ExplicitConnection.Owner.ConnectionId);
        }

        sources.Should().Equal(
            new NavigationCellAddress("source", firstSource),
            new NavigationCellAddress("source", secondSource));
        connectionIds.Should().Equal("z-first-source", "a-second-source");
    }

    [Fact]
    public void WarmedSurfaceEnumerationAndExplicitEvaluation_ShouldAllocateZeroBytes()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration left = AddGrid(context, 0);
        NormalizedGridConfiguration right = AddGrid(context, 3);
        VoxelIndex sourceIndex = new(2, 0, 0);
        VoxelIndex destinationIndex = default;
        var connection = new NavigationConnection(
            "bridge",
            sourceIndex,
            new NavigationCellAddress("right", destinationIndex),
            GetFoot(left, sourceIndex),
            GetFoot(right, destinationIndex),
            Fixed64.Zero,
            Fixed64.One);
        Admit(
            context,
            new NavigationMapBuilder("right", right)
                .AddCell(destinationIndex, SolidCell)
                .Build(),
            1);
        Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            2);
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("left", sourceIndex),
                out NavigationNodeRef source)
            .Should().BeTrue();
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None),
            new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("allocation", 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            TraversalMedium.Solid);
        int checksum = 0;
        NavigationGraphEdge seamEdge = default;
        NavigationSurfaceEdgeEnumerator initialEdges = lease.Graph.EnumerateSurfaceEdges(source);
        while (initialEdges.MoveNext())
        {
            if (initialEdges.Current.Kind == NavigationGraphEdgeKind.Seam)
                seamEdge = initialEdges.Current;
        }
        seamEdge.Kind.Should().Be(NavigationGraphEdgeKind.Seam);
        context.Pathing.GetNavigationGraphDiagnostics().Maps.Should().OnlyContain(
            map => map.IncidentExplicitEdgeCount == 1,
            "automatic seams must not inflate the explicit-only diagnostic");
        System.Action checkSeamActive = () =>
        {
            for (int i = 0; i < 10_000; i++)
                checksum += lease.Graph.AutomaticSeams.IsActive(seamEdge.AutomaticSeam) ? 1 : 0;
        };
        System.Action evaluateSeam = () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                checksum += (int)evaluator.EvaluateEdge(source, seamEdge, out TraversalEdgeEvidence evidence);
                checksum += evidence.Cost.GetHashCode();
            }
        };
        System.Action enumerateAndEvaluate = () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                NavigationSurfaceEdgeEnumerator edges = lease.Graph.EnumerateSurfaceEdges(source);
                while (edges.MoveNext())
                {
                    checksum += (int)evaluator.EvaluateEdge(source, edges.Current, out TraversalEdgeEvidence evidence);
                    checksum += evidence.Cost.GetHashCode();
                }
            }
        };
        checkSeamActive();
        evaluateSeam();
        enumerateAndEvaluate();

        AllocationTestUtility.MeasureAllocatedBytes(checkSeamActive).Should().Be(0,
            "active seam authentication is part of the warmed surface hot path");
        AllocationTestUtility.MeasureAllocatedBytes(evaluateSeam).Should().Be(0,
            "automatic seam evaluation is part of the warmed surface hot path");
        AllocationTestUtility.MeasureAllocatedBytes(enumerateAndEvaluate).Should().Be(0);
        checksum.Should().NotBe(0);
    }

    private static NormalizedGridConfiguration AddGrid(
        TrailblazerWorldContext context,
        int minimumX) => AddGrid(context, minimumX, Fixed64.Zero);

    private static NormalizedGridConfiguration AddGrid(
        TrailblazerWorldContext context,
        int minimumX,
        Fixed64 minimumY)
    {
        var minimum = new Vector3d((Fixed64)minimumX, minimumY, Fixed64.Zero);
        var configuration = new GridConfiguration(
            minimum,
            minimum + new Vector3d(3, 1, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static NormalizedGridConfiguration AddGridWithExtent(
        TrailblazerWorldContext context,
        int minimumX,
        int extentX)
    {
        var minimum = new Vector3d((Fixed64)minimumX, Fixed64.Zero, Fixed64.Zero);
        var configuration = new GridConfiguration(
            minimum,
            minimum + new Vector3d((Fixed64)extentX, Fixed64.One, (Fixed64)2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static NavigationCellAddress[] CreateInteriorWitnessCorridor(string mapId) =>
        new[]
        {
            new NavigationCellAddress(mapId, default),
            new NavigationCellAddress(mapId, new VoxelIndex(1, 0, 0)),
            new NavigationCellAddress(mapId, new VoxelIndex(2, 0, 0)),
            new NavigationCellAddress(mapId, new VoxelIndex(3, 0, 0))
        };

    private static NavigationMap CreateCorridorMap(
        string mapId,
        NormalizedGridConfiguration binding) => new NavigationMapBuilder(mapId, binding)
        .AddCell(default, SolidCell)
        .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
        .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
        .AddCell(new VoxelIndex(3, 0, 0), SolidCell)
        .Build();

    private static NormalizedGridConfiguration CreateBinding(int minimumX, int maximumX)
    {
        var configuration = new GridConfiguration(
            new Vector3d(minimumX, 0, 0),
            new Vector3d(maximumX, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static NavigationOperationCandidate FoldMapCandidate(
        NavigationOperationCandidate source,
        PreparedNavigationMap prepared)
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationMapFoldWork(
            source,
            prepared,
            OverlayReplacementPolicy.Clear,
            settings.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        AdvanceFold(work, settings.MaintenanceBudget);
        return work.Candidate;
    }

    private static NavigationOperationCandidate FoldOverlayCandidate(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        long sequence)
    {
        return FoldOverlayWork(source, transaction, sequence).Candidate;
    }

    private static NavigationOverlayFoldWork FoldOverlayWork(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        long sequence)
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationOverlayFoldWork(
            source,
            transaction,
            sequence,
            settings.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        for (int i = 0; i < 4096; i++)
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

    private static NavigationOverlayTransaction ConnectionTransaction(
        NavigationConnection connection) => new(new[]
        {
            new NavigationMapOverlayDelta(
                "map",
                connections: new[] { NavigationConnectionOverlayOperation.Upsert(connection) })
        });

    private static void AdvanceFold(
        NavigationMapFoldWork work,
        MaintenanceWorkBudget budget)
    {
        var meter = new MaintenanceWorkMeter(budget);
        for (int i = 0; i < 4096; i++)
        {
            if (work.Advance(meter, out NavigationOperationRejection rejection))
            {
                rejection.Should().Be(NavigationOperationRejection.None);
                return;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Map fold did not complete.");
    }

    private static TraversalEvaluator CreateEvaluator(
        NavigationWorldGraph graph,
        Fixed64 maxStepUp,
        Fixed64 maxDropDown) => new(
            graph,
            new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                maxStepUp,
                maxDropDown,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None),
            new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("explicit-step-drop", 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            TraversalMedium.Solid);

    private static TrailblazerWorldContext CreateExplicitEvaluationContext()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            NormalizedGridConfiguration binding = AddGrid(context, 0);
            VoxelIndex source = default;
            VoxelIndex witness = new(1, 0, 0);
            VoxelIndex destination = new(2, 0, 0);
            NavigationCell cell = new(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.Half,
                Fixed64.One);
            NavigationCell destinationCell = new(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                (Fixed64)2,
                Fixed64.Half,
                Fixed64.One);
            var connection = new NavigationConnection(
                "shortcut",
                source,
                new NavigationCellAddress("map", destination),
                GetFoot(binding, source),
                GetFoot(binding, destination),
                Fixed64.Half,
                Fixed64.One,
                new[] { new NavigationCellAddress("map", witness) },
                additionalCost: (Fixed64)3);
            NavigationMapCommitOperation operation = Admit(
                context,
                new NavigationMapBuilder("map", binding)
                    .AddCell(source, cell)
                    .AddCell(witness, cell)
                    .AddCell(destination, destinationCell)
                    .AddConnection(connection)
                    .Build(),
                1);
            SimulateUntilTerminal(context, operation.Receipt);
            operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationAgentProfile CreateExplicitEvaluationProfile() => new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.Zero,
        TraversalMedia.Solid,
        TraversalCapability.None);

    private static NavigationAreaPolicy CreateExplicitEvaluationPolicy() => new(
        new NavigationAreaPolicyKey("explicit", 1),
        new[] { new NavigationAreaRule(isAllowed: true, additionalEnterCost: (Fixed64)4) });

    private static NavigationNodeRef ResolveNode(
        NavigationWorldGraph graph,
        VoxelIndex index)
    {
        graph.TryGetNodeRef(new NavigationCellAddress("map", index), out NavigationNodeRef node)
            .Should().BeTrue();
        return node;
    }

    private static Vector3d GetFoot(NormalizedGridConfiguration binding, VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private static NavigationMapCommitOperation Admit(
        TrailblazerWorldContext context,
        NavigationMap map,
        long sequence)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            sequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static NavigationOverlayCommitOperation CommitCell(
        TrailblazerWorldContext context,
        string mapId,
        NavigationCellOverlayOperation cell,
        long sequence)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(mapId, new[] { cell })
            })),
            sequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static TrailblazerWorldContext CreateContextWithExplicitBudget(
        int maxExplicitEdges,
        int? maxOverlaySlots = null,
        int? maxComponentNodes = null,
        int? maxDependencyEntries = null,
        long? maxActiveSnapshotBytes = null,
        int? maxRetiredSnapshots = null)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots ?? defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes ?? defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            maxExplicitEdges,
            maxDependencyEntries ?? defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            maxActiveSnapshotBytes ?? defaults.MaxActiveSnapshotBytes,
            maxRetiredSnapshots ?? defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            maxDependencyEntries.HasValue ? 1 : defaults.NavigationAreaCount,
            maxDependencyEntries.HasValue ? 1 : defaults.MaxAreaPolicies,
            maxDependencyEntries.HasValue ? 1 : defaults.MaxAreaRulesPerPolicy,
            maxDependencyEntries.HasValue ? 1 : defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        return TrailblazerWorldContext.CreateOwned(settings: settings);
    }

    private static TrailblazerWorldContext CreateClosureCursorCapacityScenario(
        long? maxActiveSnapshotBytes,
        out NavigationOverlayCommitOperation operation,
        out long installationPeak,
        int? maxRetiredSnapshots)
    {
        TrailblazerWorldContext context = CreateContextWithExplicitBudget(
            maxExplicitEdges: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxDependencyEntries: 3,
            maxActiveSnapshotBytes,
            maxRetiredSnapshots);
        try
        {
            installationPeak = context.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes;
            long sequence = 0;
            NavigationMapCommitOperation last = default;
            var deltas = new NavigationMapOverlayDelta[16];
            for (int i = 0; i < deltas.Length; i++)
            {
                string mapId = $"map-{i}";
                NormalizedGridConfiguration binding = AddGrid(context, i * 10);
                last = Admit(
                    context,
                    new NavigationMapBuilder(mapId, binding)
                        .AddCell(default, SolidCell)
                        .Build(),
                    ++sequence);
                for (int frame = 0;
                     frame < 512 && last.Receipt.Status == NavigationOperationStatus.Pending;
                     frame++)
                {
                    context.Simulate();
                    installationPeak = Math.Max(
                        installationPeak,
                        context.Pathing.GetNavigationGraphDiagnostics().ActiveSnapshotBytes);
                }
                last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
                deltas[i] = new NavigationMapOverlayDelta(
                    mapId,
                    new[]
                    {
                        NavigationCellOverlayOperation.Set(
                            new VoxelIndex(1, 0, 0),
                            SolidCell)
                    });
            }
            operation = new NavigationOverlayCommitOperation(
                new PreparedNavigationOverlay(new NavigationOverlayTransaction(deltas)),
                ++sequence,
                context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static NavigationGraphCellState GetGraphCell(
        TrailblazerWorldContext context,
        string mapId,
        VoxelIndex index = default)
    {
        context.Pathing.TryGetNavigationGraphCellState(mapId, index, out NavigationGraphCellState state)
            .Should().BeTrue();
        return state;
    }

    private static NavigationMapInstance FindInstance(
        NavigationWorldGraph graph,
        string mapId)
    {
        for (int i = 0; i < graph.MapCount; i++)
        {
            NavigationMapInstance instance = graph.GetInstance(i);
            if (string.Equals(instance.MapId, mapId, StringComparison.Ordinal))
                return instance;
        }
        throw new Xunit.Sdk.XunitException($"Expected map instance '{mapId}'.");
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int i = 0; i < 4096 && receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
    }

    private static void CommitConnection(
        TrailblazerWorldContext context,
        string mapId,
        NavigationConnectionOverlayOperation connection,
        long sequence)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(mapId, connections: new[] { connection })
            })),
            sequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
    }

    private static NavigationGraphEdge FindExplicitEdge(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        string connectionId)
    {
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(source);
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit
                && edges.Current.ExplicitConnection.Owner.ConnectionId == connectionId)
            {
                return edges.Current;
            }
        }
        throw new System.InvalidOperationException("Expected explicit edge was not enumerated.");
    }

}
