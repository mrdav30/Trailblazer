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
        lease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        record.IsActive.Should().BeTrue();
        record.Source.Should().Be(new NavigationCellAddress("left", sourceIndex));
        record.Destination.Should().Be(new NavigationCellAddress("right", destinationIndex));
        record.CorridorCost.Should().Be(Fixed64.One);
        record.IsLowerBoundCertified.Should().BeTrue();
        record.PortalWaypoints.Length.Should().Be(1);
        record.PortalWaypoints[0].Should().Be(
            new Vector3d((Fixed64)2.5m, (Fixed64)(-0.5m), Fixed64.Zero));
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
        oldLease.Graph.Composition.GetIncidentEdgeCount(0).Should().Be(1);
        oldLease.Graph.Composition.GetIncidentEdgeCount(1).Should().Be(1);

        CommitCell(context, "right", NavigationCellOverlayOperation.Suppress(destinationIndex), 3);
        context.Simulate();
        using (NavigationWorldGraphLease dormantLease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormantLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord dormant)
                .Should().BeTrue();
            dormant.IsActive.Should().BeFalse();
            dormant.PortalWaypoints.Length.Should().Be(0);
            dormantLease.Graph.Composition.GetIncidentEdgeCount(0).Should().Be(0);
            dormantLease.Graph.Composition.GetIncidentEdgeCount(1).Should().Be(0);
        }
        oldLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord old)
            .Should().BeTrue();
        old.IsActive.Should().BeTrue();

        CommitCell(context, "right", NavigationCellOverlayOperation.RevertToBake(destinationIndex), 4);
        context.Simulate();
        using NavigationWorldGraphLease revivedLease = context.Pathing.TryAcquireNavigationGraph()!;
        revivedLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord revived)
            .Should().BeTrue();
        revived.IsActive.Should().BeTrue();
        revived.PortalWaypoints.Length.Should().Be(1);
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
        Admit(
            context,
            new NavigationMapBuilder("left", left)
                .AddCell(sourceIndex, SolidCell)
                .AddConnection(connection)
                .Build(),
            3);
        context.Simulate();
        var owner = new NavigationConnectionOwnerKey("left", "bridge");

        CommitCell(context, "middle", NavigationCellOverlayOperation.Suppress(default), 4);
        context.Simulate();
        using (NavigationWorldGraphLease dormantLease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormantLease.Graph.TryGetExplicitConnection(
                    owner,
                    out NavigationExplicitConnectionRecord dormant)
                .Should().BeTrue();
            dormant.IsActive.Should().BeFalse();
            ReadOnlySpan<NavigationConnectionOwnerKey> incident =
                dormantLease.Graph.ExplicitConnections.GetIncidentOwners(witnessAddress);
            incident.Length.Should().Be(1);
            incident[0].Should().Be(owner);
        }

        CommitCell(context, "middle", NavigationCellOverlayOperation.RevertToBake(default), 5);
        context.Simulate();
        using NavigationWorldGraphLease revivedLease = context.Pathing.TryAcquireNavigationGraph()!;
        revivedLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord revived)
            .Should().BeTrue();
        revived.IsActive.Should().BeTrue();
        revived.PortalWaypoints.Length.Should().Be(2);
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
            dormantLease.Graph.TryGetExplicitConnection(
                    owner,
                    out NavigationExplicitConnectionRecord dormant)
                .Should().BeTrue();
            dormant.IsActive.Should().BeFalse();
            dormantLease.Graph.ExplicitConnections.GetIncidentOwners(destinationAddress)
                .Length.Should().Be(1);
        }
        oldLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord old)
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
        revivedLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord revived)
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
            suppressed.Graph.TryGetExplicitConnection(owner, out _).Should().BeFalse();
            suppressed.Graph.ExplicitConnections.GetIncidentOwners(destinationAddress)
                .Length.Should().Be(0);
        }
        oldLease.Graph.TryGetExplicitConnection(owner, out _).Should().BeTrue();

        CommitConnection(
            context,
            "left",
            NavigationConnectionOverlayOperation.RevertToBake("bridge"),
            4);
        context.Simulate();
        using (NavigationWorldGraphLease reverted = context.Pathing.TryAcquireNavigationGraph()!)
        {
            reverted.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord record)
                .Should().BeTrue();
            record.Definition.Should().BeSameAs(baked);
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
        updated.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord replacement)
            .Should().BeTrue();
        replacement.Definition.Should().BeSameAs(upsert);
        updated.Graph.ExplicitConnections.GetIncidentOwners(destinationAddress).Length.Should().Be(1);
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
            replaced.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord record)
                .Should().BeTrue();
            record.Definition.Should().BeSameAs(replacement);
        }
        oldLease.Graph.TryGetExplicitConnection(owner, out NavigationExplicitConnectionRecord old)
            .Should().BeTrue();
        old.Definition.AdditionalCost.Should().Be(Fixed64.Zero);

        var remove = new NavigationMapRemoveOperation(
            "left",
            operationSequence: 4,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease removed = context.Pathing.TryAcquireNavigationGraph()!;
        removed.Graph.TryGetExplicitConnection(owner, out _).Should().BeFalse();
        removed.Graph.ExplicitConnections.GetIncidentOwners(destinationAddress).Length.Should().Be(0);
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
        var candidate = new NavigationOperationCandidate(navigationAreaCount: 1);
        candidate.ApplyMap(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                TrailblazerWorldContextSettings.Default.OperationLimits,
                new GridCellPrism[4],
                new Vector3d[6])
            .Should().Be(NavigationOperationRejection.None);
        var captured = new GridCellPrism[4];
        var work = candidate.BeginExplicitConnectionRefresh(
            "map",
            captured,
            new Vector3d[6]);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 1,
            maxBaselineAddresses: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 16));
        VoxelIndex[] expected = { source, firstWitness, secondWitness, destination };

        for (int step = 0; step < expected.Length; step++)
        {
            work.Advance(meter).Should().Be(step == expected.Length - 1);
            meter.ExplicitEdges.Should().Be(1);
            binding.TryGetCellPrism(expected[step], out GridCellPrism prism).Should().BeTrue();
            captured[step].Should().Be(prism,
                "each successful explicit debit must immediately capture that semantic cell");
            if (step + 1 < captured.Length)
                captured[step + 1].Should().Be(default(GridCellPrism));
            meter.Reset();
        }

        candidate.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("map", "corridor"),
                out NavigationExplicitConnectionRecord record)
            .Should().BeTrue();
        record.IsActive.Should().BeTrue();
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
        var candidate = new NavigationOperationCandidate(navigationAreaCount: 1);
        candidate.ApplyMap(
                new PreparedNavigationMap(map, bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                TrailblazerWorldContextSettings.Default.OperationLimits,
                new GridCellPrism[2],
                new Vector3d[2])
            .Should().Be(NavigationOperationRejection.None);
        var work = candidate.BeginExplicitConnectionRefresh(
            "map",
            new GridCellPrism[2],
            new Vector3d[2]);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(1, 1, 1, 1, 16, 1));
        var sourceAddress = new NavigationCellAddress("map", source);
        var destinationAddress = new NavigationCellAddress("map", destination);

        work.Advance(meter).Should().BeFalse();
        candidate.ExplicitConnections.GetIncidentOwners(sourceAddress).Length.Should().Be(0);
        candidate.ExplicitConnections.GetIncidentOwners(destinationAddress).Length.Should().Be(0);

        meter.Reset();
        work.Advance(meter).Should().BeFalse();
        meter.DependencyEntries.Should().Be(1);
        candidate.ExplicitConnections.GetIncidentOwners(sourceAddress).Length.Should().Be(1);
        candidate.ExplicitConnections.GetIncidentOwners(destinationAddress).Length.Should().Be(0);

        meter.Reset();
        work.Advance(meter).Should().BeTrue();
        meter.DependencyEntries.Should().Be(1);
        candidate.ExplicitConnections.GetIncidentOwners(sourceAddress).Length.Should().Be(1);
        candidate.ExplicitConnections.GetIncidentOwners(destinationAddress).Length.Should().Be(1);
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

        kinds.Should().Equal(NavigationGraphEdgeKind.Explicit, NavigationGraphEdgeKind.Native);
        endpoints.Should().Equal(
            new NavigationCellAddress("a-destination", explicitIndex),
            new NavigationCellAddress("z-source", nativeIndex));
    }

    [Fact]
    public void ExplicitEvaluation_ShouldUseCertifiedCostInclusiveCapacityAndOneWayDirection()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NormalizedGridConfiguration binding = AddGrid(context, 0);
        VoxelIndex sourceIndex = default;
        VoxelIndex witnessIndex = new(1, 0, 0);
        VoxelIndex destinationIndex = new(2, 0, 0);
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
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            GetFoot(binding, sourceIndex),
            GetFoot(binding, destinationIndex),
            Fixed64.Half,
            Fixed64.One,
            new[] { new NavigationCellAddress("map", witnessIndex) },
            additionalCost: (Fixed64)3);
        Admit(
            context,
            new NavigationMapBuilder("map", binding)
                .AddCell(sourceIndex, cell)
                .AddCell(witnessIndex, cell)
                .AddCell(destinationIndex, destinationCell)
                .AddConnection(connection)
                .Build(),
            1);
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("map", sourceIndex),
                out NavigationNodeRef source)
            .Should().BeTrue();
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("map", destinationIndex),
                out NavigationNodeRef destination)
            .Should().BeTrue();
        NavigationGraphEdge edge = FindExplicitEdge(lease.Graph, source, "shortcut");
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("explicit", 1),
            new[] { new NavigationAreaRule(isAllowed: true, additionalEnterCost: (Fixed64)4) });
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            profile,
            policy,
            TraversalMedium.Solid);

        evaluator.EvaluateEdge(source, edge, out Fixed64 cost)
            .Should().Be(TraversalEvaluationStatus.Passable);
        cost.Should().Be((Fixed64)11);
        evaluator.EvaluateEdge(destination, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Impassable);
        var oversized = new TraversalEvaluator(
            lease.Graph,
            new NavigationAgentProfile(
                new KinematicBodyShape((Fixed64)0.5001m, Fixed64.One, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None),
            policy,
            TraversalMedium.Solid);
        oversized.EvaluateEdge(source, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Impassable);

        NavigationSurfaceEdgeEnumerator reverse = lease.Graph.EnumerateSurfaceEdges(destination);
        while (reverse.MoveNext())
            reverse.Current.Kind.Should().NotBe(NavigationGraphEdgeKind.Explicit);

        VoxelGrid grid = context.World.ActiveGrids[0];
        grid.TryGetVoxel(witnessIndex, out Voxel? witness).Should().BeTrue();
        GridForge.ObstacleToken obstacle = context.World.AllocateObstacleToken();
        grid.TryAddObstacle(witness!, obstacle).Should().BeTrue();
        context.Simulate();
        using (NavigationWorldGraphLease blockedLease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            blockedLease.Graph.TryGetNodeRef(
                    new NavigationCellAddress("map", sourceIndex),
                    out NavigationNodeRef blockedSource)
                .Should().BeTrue();
            NavigationGraphEdge blocked = FindExplicitEdge(
                blockedLease.Graph,
                blockedSource,
                "shortcut");
            new TraversalEvaluator(
                    blockedLease.Graph,
                    profile,
                    policy,
                    TraversalMedium.Solid)
                .EvaluateEdge(blockedSource, blocked, out _)
                .Should().Be(TraversalEvaluationStatus.Impassable);
        }

        grid.TryRemoveObstacle(witness!, obstacle).Should().BeTrue();
        CommitCell(
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
        context.Simulate();
        using NavigationWorldGraphLease overflowLease = context.Pathing.TryAcquireNavigationGraph()!;
        overflowLease.Graph.TryGetNodeRef(
                new NavigationCellAddress("map", sourceIndex),
                out NavigationNodeRef overflowSource)
            .Should().BeTrue();
        NavigationGraphEdge overflowEdge = FindExplicitEdge(
            overflowLease.Graph,
            overflowSource,
            "shortcut");
        new TraversalEvaluator(
                overflowLease.Graph,
                profile,
                policy,
                TraversalMedium.Solid)
            .EvaluateEdge(overflowSource, overflowEdge, out Fixed64 overflowCost)
            .Should().Be(TraversalEvaluationStatus.CostOverflow);
        overflowCost.Should().Be(Fixed64.Zero);
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
        lease.Graph.ExplicitConnections.GetIncidentOwners(destinationAddress).Length.Should().Be(2);
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
        System.Action enumerateAndEvaluate = () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                NavigationSurfaceEdgeEnumerator edges = lease.Graph.EnumerateSurfaceEdges(source);
                while (edges.MoveNext())
                {
                    checksum += (int)evaluator.EvaluateEdge(source, edges.Current, out Fixed64 cost);
                    checksum += cost.GetHashCode();
                }
            }
        };
        enumerateAndEvaluate();

        AllocationTestUtility.MeasureAllocatedBytes(enumerateAndEvaluate).Should().Be(0);
        checksum.Should().NotBe(0);
    }

    private static NormalizedGridConfiguration AddGrid(
        TrailblazerWorldContext context,
        int minimumX)
    {
        var minimum = new Vector3d((Fixed64)minimumX, Fixed64.Zero, Fixed64.Zero);
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

    private static Vector3d GetFoot(NormalizedGridConfiguration binding, VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private static void Admit(
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
    }

    private static void CommitCell(
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
