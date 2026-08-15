//=======================================================================
// NavigationIncomingSurfaceEdgeTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationIncomingSurfaceEdgeTests
{
    private static readonly NavigationCell SurfaceCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    [Fact]
    public void IncomingEdges_ShouldExposeCanonicalForwardEdgesAcrossNativeExplicitAndSeamKinds()
    {
        using TrailblazerWorldContext forward = CreateMixedContext(reverseInsertion: false);
        using TrailblazerWorldContext reverse = CreateMixedContext(reverseInsertion: true);

        List<IncomingSnapshot> expected = new()
        {
            new(
                new NavigationCellAddress("a-explicit", default),
                NavigationGraphEdgeKind.Explicit,
                new NavigationCellAddress("m-destination", default),
                CanonicalOutgoingOrdinal: 2,
                ExplicitConnectionId: "to-destination",
                NativeDirectionOrdinal: -1,
                SeamIsReverse: false),
            new(
                new NavigationCellAddress("a-explicit", default),
                NavigationGraphEdgeKind.Seam,
                new NavigationCellAddress("m-destination", default),
                CanonicalOutgoingOrdinal: 3,
                ExplicitConnectionId: string.Empty,
                NativeDirectionOrdinal: -1,
                SeamIsReverse: false),
            new(
                new NavigationCellAddress("b-seam", default),
                NavigationGraphEdgeKind.Seam,
                new NavigationCellAddress("m-destination", default),
                CanonicalOutgoingOrdinal: 0,
                ExplicitConnectionId: string.Empty,
                NativeDirectionOrdinal: -1,
                SeamIsReverse: false),
            new(
                new NavigationCellAddress("m-destination", new VoxelIndex(0, 0, 1)),
                NavigationGraphEdgeKind.Native,
                new NavigationCellAddress("m-destination", default),
                CanonicalOutgoingOrdinal: 0,
                ExplicitConnectionId: string.Empty,
                NativeDirectionOrdinal: 1,
                SeamIsReverse: false),
            new(
                new NavigationCellAddress("z-seam", default),
                NavigationGraphEdgeKind.Seam,
                new NavigationCellAddress("m-destination", default),
                CanonicalOutgoingOrdinal: 0,
                ExplicitConnectionId: string.Empty,
                NativeDirectionOrdinal: -1,
                SeamIsReverse: true)
        };

        ReadIncoming(forward).Should().Equal(expected);
        ReadIncoming(reverse).Should().Equal(expected);
    }

    [Fact]
    public void ParallelExplicitIncomingEdges_ShouldPreserveExactForwardRecordAndLocator()
    {
        using TrailblazerWorldContext forward =
            CreateParallelExplicitContext(reverseInsertion: false);
        using TrailblazerWorldContext reverse =
            CreateParallelExplicitContext(reverseInsertion: true);
        Fixed64 quarter = Fixed64.FromFraction(1, 4);
        Fixed64 footY = Fixed64.FromFraction(-1, 2);
        List<ParallelExplicitSnapshot> expected = new()
        {
            new(
                "alpha",
                new Vector3d(-quarter, footY, (Fixed64)(-1)),
                new Vector3d(-quarter, footY, Fixed64.Zero),
                CanonicalOutgoingOrdinal: 0),
            new(
                "zeta",
                new Vector3d(quarter, footY, (Fixed64)(-1)),
                new Vector3d(quarter, footY, Fixed64.Zero),
                CanonicalOutgoingOrdinal: 1)
        };

        ReadParallelExplicitIncoming(forward).Should().Equal(expected);
        ReadParallelExplicitIncoming(reverse).Should().Equal(expected);
    }

    [Fact]
    public void IncomingAdvance_ShouldDebitEveryInspectedCandidate_AndResumeWithoutLosingTheEdge()
    {
        using TrailblazerWorldContext context = CreateMixedContext(reverseInsertion: true);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var destinationAddress = new NavigationCellAddress("m-destination", default);
        lease.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destination)
            .Should().BeTrue();
        NavigationIncomingSurfaceEdgeEnumerator incoming =
            lease.Graph.EnumerateIncomingSurfaceEdges(destination);
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: 0,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 7,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: 0,
            maxSimplificationRays: 0));

        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Pending);
        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Pending);
        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Blocked);
        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Pending);
        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Blocked);
        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Blocked);
        AdvanceOne(ref incoming, meter).Should().Be(NavigationSurfaceEdgeAdvanceStatus.Edge);

        meter.EvaluatedEdges.Should().Be(7);
        lease.Graph.TryGetNodeAddress(
                incoming.Current.Predecessor,
                out NavigationCellAddress predecessor)
            .Should().BeTrue();
        predecessor.Should().Be(new NavigationCellAddress("a-explicit", default));
        incoming.Current.ForwardEdge.Kind.Should().Be(NavigationGraphEdgeKind.Explicit);
        incoming.Current.ForwardEdge.ExplicitConnection.Owner.ConnectionId
            .Should().Be("to-destination");
        incoming.Current.SelectedEdge.Should().Be(
            new NavigationSelectedEdgeRef(destinationAddress, canonicalOutgoingOrdinal: 2));
    }

    [Fact]
    public void WarmedIncomingEnumeration_ShouldAllocateZeroBytes()
    {
        using TrailblazerWorldContext context = CreateMixedContext(reverseInsertion: false);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var destinationAddress = new NavigationCellAddress("m-destination", default);
        lease.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destination)
            .Should().BeTrue();
        int checksum = 0;
        Action enumerate = () => checksum = ConsumeIncoming(
            lease.Graph,
            destination,
            repetitions: 10_000);
        enumerate();

        AllocationTestUtility.MeasureAllocatedBytes(enumerate).Should().Be(0);
        checksum.Should().NotBe(0);
    }

    private static NavigationSurfaceEdgeAdvanceStatus AdvanceOne(
        ref NavigationIncomingSurfaceEdgeEnumerator incoming,
        NavigationWorkMeter meter)
    {
        int edgeStepRemaining = 1;
        return incoming.AdvanceOne(meter, ref edgeStepRemaining);
    }

    private static List<IncomingSnapshot> ReadIncoming(TrailblazerWorldContext context)
    {
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var destinationAddress = new NavigationCellAddress("m-destination", default);
        lease.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destination)
            .Should().BeTrue();
        var result = new List<IncomingSnapshot>();
        NavigationIncomingSurfaceEdgeEnumerator incoming =
            lease.Graph.EnumerateIncomingSurfaceEdges(destination);
        while (incoming.MoveNext())
        {
            NavigationIncomingSurfaceEdge current = incoming.Current;
            lease.Graph.TryGetNodeAddress(
                    current.Predecessor,
                    out NavigationCellAddress predecessor)
                .Should().BeTrue();
            lease.Graph.TryGetNodeAddress(
                    current.ForwardEdge.Target,
                    out NavigationCellAddress target)
                .Should().BeTrue();
            current.SelectedEdge.Target.Should().Be(destinationAddress);
            current.SelectedEdge.CanonicalOutgoingOrdinal.Should().BeGreaterThanOrEqualTo(0);
            result.Add(new IncomingSnapshot(
                predecessor,
                current.ForwardEdge.Kind,
                target,
                current.SelectedEdge.CanonicalOutgoingOrdinal,
                current.ForwardEdge.Kind == NavigationGraphEdgeKind.Explicit
                    ? current.ForwardEdge.ExplicitConnection.Owner.ConnectionId
                    : string.Empty,
                current.ForwardEdge.NativeDirectionOrdinal,
                current.ForwardEdge.Kind == NavigationGraphEdgeKind.Seam
                    && current.ForwardEdge.AutomaticSeam.IsReverse));
        }
        return result;
    }

    private static List<ParallelExplicitSnapshot> ReadParallelExplicitIncoming(
        TrailblazerWorldContext context)
    {
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var destinationAddress = new NavigationCellAddress("destination", default);
        lease.Graph.TryGetNodeRef(destinationAddress, out NavigationNodeRef destination)
            .Should().BeTrue();
        var result = new List<ParallelExplicitSnapshot>();
        NavigationIncomingSurfaceEdgeEnumerator incoming =
            lease.Graph.EnumerateIncomingSurfaceEdges(destination);
        while (incoming.MoveNext())
        {
            NavigationIncomingSurfaceEdge current = incoming.Current;
            if (current.ForwardEdge.Kind != NavigationGraphEdgeKind.Explicit)
                continue;

            NavigationGraphEdge resolved = ResolveSelectedEdge(
                lease.Graph,
                current.Predecessor,
                current.SelectedEdge);
            ReferenceEquals(
                    resolved.ExplicitConnection,
                    current.ForwardEdge.ExplicitConnection)
                .Should().BeTrue("the durable locator must resolve the exact forward record");
            result.Add(new ParallelExplicitSnapshot(
                current.ForwardEdge.ExplicitConnection.Owner.ConnectionId,
                current.ForwardEdge.ExplicitConnection.Definition.EntryAnchor,
                current.ForwardEdge.ExplicitConnection.Definition.ExitAnchor,
                current.SelectedEdge.CanonicalOutgoingOrdinal));
        }
        return result;
    }

    private static NavigationGraphEdge ResolveSelectedEdge(
        NavigationWorldGraph graph,
        NavigationNodeRef predecessor,
        NavigationSelectedEdgeRef selected)
    {
        NavigationSurfaceEdgeEnumerator outgoing = graph.EnumerateSurfaceEdges(predecessor);
        for (int ordinal = 0; ordinal <= selected.CanonicalOutgoingOrdinal; ordinal++)
        {
            outgoing.MoveNext().Should().BeTrue();
            if (ordinal != selected.CanonicalOutgoingOrdinal)
                continue;
            graph.TryGetNodeAddress(outgoing.Current.Target, out NavigationCellAddress target)
                .Should().BeTrue();
            target.Should().Be(selected.Target);
            return outgoing.Current;
        }
        throw new Xunit.Sdk.XunitException("The selected edge ordinal was not present.");
    }

    private static int ConsumeIncoming(
        NavigationWorldGraph graph,
        NavigationNodeRef destination,
        int repetitions)
    {
        int checksum = 0;
        for (int iteration = 0; iteration < repetitions; iteration++)
        {
            NavigationIncomingSurfaceEdgeEnumerator incoming =
                graph.EnumerateIncomingSurfaceEdges(destination);
            while (incoming.MoveNext())
            {
                checksum += incoming.Current.Predecessor.GetHashCode();
                checksum += incoming.Current.SelectedEdge.GetHashCode();
            }
        }
        return checksum;
    }

    private static TrailblazerWorldContext CreateMixedContext(bool reverseInsertion)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration explicitConfiguration = CreateConfiguration(
                new Vector3d(0, 0, -1),
                new Vector3d(0, 0, -1));
            GridConfiguration otherConfiguration = CreateConfiguration(
                new Vector3d(0, 0, -2),
                new Vector3d(0, 0, -2));
            GridConfiguration leftSeamConfiguration = CreateConfiguration(
                new Vector3d(-1, 0, 0),
                new Vector3d(-1, 0, 0));
            GridConfiguration destinationConfiguration = CreateConfiguration(
                Vector3d.Zero,
                new Vector3d(0, 0, 1));
            GridConfiguration rightSeamConfiguration = CreateConfiguration(
                new Vector3d(1, 0, 0),
                new Vector3d(1, 0, 0));
            GridConfiguration[] configurations =
            {
                explicitConfiguration,
                otherConfiguration,
                leftSeamConfiguration,
                destinationConfiguration,
                rightSeamConfiguration
            };
            AddGrids(context, configurations, reverseInsertion);

            NormalizedGridConfiguration explicitBinding = Normalize(explicitConfiguration);
            NormalizedGridConfiguration otherBinding = Normalize(otherConfiguration);
            NormalizedGridConfiguration leftSeamBinding = Normalize(leftSeamConfiguration);
            NormalizedGridConfiguration destinationBinding = Normalize(destinationConfiguration);
            NormalizedGridConfiguration rightSeamBinding = Normalize(rightSeamConfiguration);
            var otherConnection = new NavigationConnection(
                "to-other",
                default,
                new NavigationCellAddress("c-other", default),
                GetFoot(explicitBinding, default),
                GetFoot(otherBinding, default),
                Fixed64.Zero,
                Fixed64.One);
            var destinationConnection = new NavigationConnection(
                "to-destination",
                default,
                new NavigationCellAddress("m-destination", default),
                GetFoot(explicitBinding, default),
                GetFoot(destinationBinding, default),
                Fixed64.Zero,
                Fixed64.One);
            var explicitBuilder = new NavigationMapBuilder("a-explicit", explicitBinding)
                .AddCell(default, SurfaceCell);
            if (reverseInsertion)
            {
                explicitBuilder.AddConnection(destinationConnection);
                explicitBuilder.AddConnection(otherConnection);
            }
            else
            {
                explicitBuilder.AddConnection(otherConnection);
                explicitBuilder.AddConnection(destinationConnection);
            }
            NavigationMap[] maps =
            {
                explicitBuilder.Build(),
                new NavigationMapBuilder("c-other", otherBinding)
                    .AddCell(default, SurfaceCell)
                    .Build(),
                new NavigationMapBuilder("b-seam", leftSeamBinding)
                    .AddCell(default, SurfaceCell)
                    .Build(),
                new NavigationMapBuilder("m-destination", destinationBinding)
                    .AddCell(default, SurfaceCell)
                    .AddCell(new VoxelIndex(0, 0, 1), SurfaceCell)
                    .Build(),
                new NavigationMapBuilder("z-seam", rightSeamBinding)
                    .AddCell(default, SurfaceCell)
                    .Build()
            };
            AdmitMaps(context, maps, reverseInsertion);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static TrailblazerWorldContext CreateParallelExplicitContext(
        bool reverseInsertion)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            GridConfiguration sourceConfiguration = CreateConfiguration(
                new Vector3d(0, 0, -1),
                new Vector3d(0, 0, -1));
            GridConfiguration destinationConfiguration = CreateConfiguration(
                Vector3d.Zero,
                Vector3d.Zero);
            GridConfiguration[] configurations =
            {
                destinationConfiguration,
                sourceConfiguration
            };
            AddGrids(context, configurations, reverseInsertion);
            NormalizedGridConfiguration sourceBinding = Normalize(sourceConfiguration);
            NormalizedGridConfiguration destinationBinding = Normalize(
                destinationConfiguration);
            Vector3d sourceFoot = GetFoot(sourceBinding, default);
            Vector3d destinationFoot = GetFoot(destinationBinding, default);
            Fixed64 quarter = Fixed64.FromFraction(1, 4);
            var alpha = new NavigationConnection(
                "alpha",
                default,
                new NavigationCellAddress("destination", default),
                sourceFoot + new Vector3d(-quarter, Fixed64.Zero, Fixed64.Zero),
                destinationFoot + new Vector3d(-quarter, Fixed64.Zero, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.One);
            var zeta = new NavigationConnection(
                "zeta",
                default,
                new NavigationCellAddress("destination", default),
                sourceFoot + new Vector3d(quarter, Fixed64.Zero, Fixed64.Zero),
                destinationFoot + new Vector3d(quarter, Fixed64.Zero, Fixed64.Zero),
                Fixed64.Zero,
                Fixed64.One);
            var sourceBuilder = new NavigationMapBuilder("source", sourceBinding)
                .AddCell(default, SurfaceCell);
            if (reverseInsertion)
            {
                sourceBuilder.AddConnection(zeta);
                sourceBuilder.AddConnection(alpha);
            }
            else
            {
                sourceBuilder.AddConnection(alpha);
                sourceBuilder.AddConnection(zeta);
            }
            NavigationMap[] maps =
            {
                new NavigationMapBuilder("destination", destinationBinding)
                    .AddCell(default, SurfaceCell)
                    .Build(),
                sourceBuilder.Build()
            };
            AdmitMaps(context, maps, reverseInsertion);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static void AddGrids(
        TrailblazerWorldContext context,
        GridConfiguration[] configurations,
        bool reverseInsertion)
    {
        for (int ordinal = 0; ordinal < configurations.Length; ordinal++)
        {
            int index = reverseInsertion
                ? configurations.Length - ordinal - 1
                : ordinal;
            context.World.TryAddGrid(configurations[index], out _).Should().BeTrue();
        }
    }

    private static void AdmitMaps(
        TrailblazerWorldContext context,
        NavigationMap[] maps,
        bool reverseInsertion)
    {
        var receipts = new NavigationOperationReceipt[maps.Length];
        for (int ordinal = 0; ordinal < maps.Length; ordinal++)
        {
            int index = reverseInsertion ? maps.Length - ordinal - 1 : ordinal;
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(maps[index], bakeVersion: 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: ordinal + 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            receipts[ordinal] = operation.Receipt;
        }
        for (int frame = 0;
            frame < 512 && receipts[maps.Length - 1].Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
        for (int ordinal = 0; ordinal < receipts.Length; ordinal++)
        {
            int index = reverseInsertion ? maps.Length - ordinal - 1 : ordinal;
            receipts[ordinal].Status.Should().Be(
                NavigationOperationStatus.Applied,
                $"map {maps[index].MapId} rejection was {receipts[ordinal].Rejection}");
        }
    }

    private static GridConfiguration CreateConfiguration(Vector3d minimum, Vector3d maximum) =>
        new(
            minimum,
            maximum,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));

    private static NormalizedGridConfiguration Normalize(GridConfiguration configuration)
    {
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private readonly record struct IncomingSnapshot(
        NavigationCellAddress Predecessor,
        NavigationGraphEdgeKind Kind,
        NavigationCellAddress Target,
        int CanonicalOutgoingOrdinal,
        string ExplicitConnectionId,
        int NativeDirectionOrdinal,
        bool SeamIsReverse);

    private readonly record struct ParallelExplicitSnapshot(
        string ConnectionId,
        Vector3d EntryAnchor,
        Vector3d ExitAnchor,
        int CanonicalOutgoingOrdinal);
}
