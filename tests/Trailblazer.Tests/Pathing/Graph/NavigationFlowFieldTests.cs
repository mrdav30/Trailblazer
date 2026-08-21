//=======================================================================
// NavigationFlowFieldTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationFlowFieldTests
{
    [Fact]
    public void Constructor_ShouldRejectStagedGasUntilMediumStateSearchIsPorted()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = { default, new VoxelIndex(1, 0, 0) };
        NavigationCell gas = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "gas-flow-gate",
                new[] { gas, gas });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        PathQuery surface = fixture.CreateQuery(cells[0], cells[1], profile);
        var query = new PathQuery(
            surface.Start,
            surface.End,
            surface.Agent,
            surface.AreaPolicy,
            surface.Traversal,
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(
                128, 16, 16, 64, 64, 0, 0, 0, 0, 32, 0),
            allowTransitions: false);
        var workspace = new NavigationFlowFieldWorkspace(1, 4, 6, 4, 16, 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.FlowField);
        admission.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Gas,
            TraversalMedia.Gas);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 8);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);

        Action construct = () =>
            _ = new NavigationFlowFieldWork(admission.Result, workspace);

        construct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReverseIntegration_ShouldPublishDestinationFirstWithExactForwardCosts()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)3, (Fixed64)2, (Fixed64)1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var start = new VoxelIndex(0, 0, 0);
        var middle = new VoxelIndex(1, 0, 0);
        var destination = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { start, middle, destination },
                "line");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(start, destination, fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, start),
            new NavigationCellAddress(fixture.MapId, destination));

        result.Status.Should().Be(NavigationFlowFieldStatus.Success);
        result.Payload.IsComplete.Should().BeTrue();
        result.Payload.Nodes.Should().HaveCount(3);
        result.Payload.Nodes[0].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, destination));
        result.Payload.Nodes[0].IntegrationCost.Should().Be(Fixed64.Zero);
        result.Payload.Nodes[1].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, middle));
        result.Payload.Nodes[1].IntegrationCost.Should().Be(Fixed64.One);
        result.Payload.Nodes[1].SelectedEdge.Target.Should().Be(
            new NavigationCellAddress(fixture.MapId, destination));
        result.Payload.Nodes[2].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, start));
        result.Payload.Nodes[2].IntegrationCost.Should().Be((Fixed64)2);
        result.Payload.Nodes[2].SelectedEdge.Target.Should().Be(
            new NavigationCellAddress(fixture.MapId, middle));
        result.Payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, new VoxelIndex(9, 0, 0)),
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ExplicitReverseIntegration_ShouldDebitTheTerminalConnectionLeg()
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { start, destination },
                "explicit",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "bridge",
                        start,
                        destination,
                        corridorCost: (Fixed64)4,
                        radiusClearance: (Fixed64)2)
                });
        PathQuery query = WithBudget(
            ToFlowField(
                fixture.CreateQuery(start, destination, fixture.DefaultProfile),
                Fixed64.Zero),
            maxConnectionLegs: 0);

        FlowResult blocked = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, start),
            new NavigationCellAddress(fixture.MapId, destination));
        FlowResult exact = RunFlow(
            fixture.Graph,
            WithBudget(query, maxConnectionLegs: 1),
            new NavigationCellAddress(fixture.MapId, start),
            new NavigationCellAddress(fixture.MapId, destination));

        blocked.Status.Should().Be(NavigationFlowFieldStatus.BudgetExceeded);
        exact.Status.Should().Be(NavigationFlowFieldStatus.Success);
        exact.ConnectionLegs.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitReverseIntegration_ShouldRejectStructuralPortalCertificatesAsStale(
        bool omitPortalCertificates)
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "explicit-stale",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "missing-certificate",
                        source,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.One,
                        omitPortalCertificates: omitPortalCertificates,
                        portalTranslation: omitPortalCertificates
                            ? default
                            : new Vector3d(
                                Fixed64.Zero,
                                Fixed64.Zero,
                                Fixed64.MinIncrement))
                });
        PathQuery query = ToFlowField(
            fixture.CreateQuery(source, destination, fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, source),
            new NavigationCellAddress(fixture.MapId, destination));

        result.Status.Should().Be(NavigationFlowFieldStatus.Stale);
        result.HasPayload.Should().BeFalse();
    }

    [Fact]
    public void ReverseIntegration_ShouldSortDependencyPagesBeforeCapture()
    {
        using var world = new GridWorld();
        const int CellCount = 66;
        var cells = new VoxelIndex[CellCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new VoxelIndex(i, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(CellCount),
                cells,
                "pages");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[CellCount - 1], fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, cells[0]),
            new NavigationCellAddress(fixture.MapId, cells[CellCount - 1]));

        result.Status.Should().Be(NavigationFlowFieldStatus.Success);
        result.Payload.Dependencies.Pages.Should().HaveCount(2);
        result.Payload.Dependencies.Pages[0].PageIndex.Should().Be(0);
        result.Payload.Dependencies.Pages[1].PageIndex.Should().Be(1);
    }

    [Fact]
    public void ThreeNodePayloadOrdering_ShouldRequireEightLookupProbes()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                cells,
                "lookup-budget");
        PathQuery baseline = ToFlowField(
            fixture.CreateQuery(cells[0], cells[2], fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult seven = RunFlow(
            fixture.Graph,
            WithBudget(baseline, maxLookupProbes: 7),
            new NavigationCellAddress(fixture.MapId, cells[0]),
            new NavigationCellAddress(fixture.MapId, cells[2]));
        FlowResult eight = RunFlow(
            fixture.Graph,
            WithBudget(baseline, maxLookupProbes: 8),
            new NavigationCellAddress(fixture.MapId, cells[0]),
            new NavigationCellAddress(fixture.MapId, cells[2]));

        seven.Status.Should().Be(NavigationFlowFieldStatus.BudgetExceeded);
        eight.Status.Should().Be(NavigationFlowFieldStatus.Success);
    }

    [Fact]
    public void OriginThreshold_ShouldIncludeTheCompleteEqualCostAddressGroup()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(4),
                cells,
                "prefix");
        PathQuery near = ToFlowField(
            fixture.CreateQuery(cells[0], cells[1], fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            near,
            new NavigationCellAddress(fixture.MapId, cells[0]),
            new NavigationCellAddress(fixture.MapId, cells[1]));

        result.Status.Should().Be(NavigationFlowFieldStatus.Success);
        result.Payload.IsComplete.Should().BeFalse();
        result.Payload.Nodes.Should().HaveCount(3);
        result.Payload.Nodes[0].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, cells[1]));
        result.Payload.Nodes[1].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, cells[0]));
        result.Payload.Nodes[2].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, cells[2]));
        result.Payload.Nodes[1].IntegrationCost.Should().Be(Fixed64.One);
        result.Payload.Nodes[2].IntegrationCost.Should().Be(Fixed64.One);
        result.Payload.LastSettledCost.Should().Be(Fixed64.One);
        result.Payload.LastSettledAddress.Should().Be(
            new NavigationCellAddress(fixture.MapId, cells[2]));
    }

    [Fact]
    public void ExtraIntegrationCost_ShouldExtendTheClosedPrefixThroughItsBoundary()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0),
            new(4, 0, 0),
            new(5, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(6),
                cells,
                "extended-prefix");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[1], fixture.DefaultProfile),
            Fixed64.One);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, cells[0]),
            new NavigationCellAddress(fixture.MapId, cells[1]));

        result.Status.Should().Be(NavigationFlowFieldStatus.Success);
        result.Payload.IsComplete.Should().BeFalse();
        result.Payload.Nodes.Should().HaveCount(4);
        result.Payload.LastSettledCost.Should().Be((Fixed64)2);
        result.Payload.LastSettledAddress.Should().Be(
            new NavigationCellAddress(fixture.MapId, cells[3]));
        result.Payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, cells[4]),
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void EqualIntegrationCandidates_ShouldKeepTheFirstCanonicalOutgoingEdge()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "tie",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "zeta",
                        origin,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: (Fixed64)2),
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "alpha",
                        origin,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: (Fixed64)2)
                });
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, origin),
            new NavigationCellAddress(fixture.MapId, destination));

        result.Payload.TryGetNode(
                new NavigationCellAddress(fixture.MapId, origin),
                out NavigationFlowFieldNode node)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                new NavigationCellAddress(fixture.MapId, origin),
                out NavigationNodeRef originNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(originNode);
        int canonicalExplicitOrdinal = -1;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit
                && edges.Current.ExplicitConnection.Owner.ConnectionId == "alpha")
            {
                canonicalExplicitOrdinal = edges.CurrentOrdinal;
            }
        }
        node.IntegrationCost.Should().Be(Fixed64.Zero);
        node.SelectedEdge.CanonicalOutgoingOrdinal.Should().Be(canonicalExplicitOrdinal);
    }

    [Fact]
    public void PayloadKey_ShouldExcludeOriginAndRetainTheExactDestinationEndpoint()
    {
        PathQuery baseline = ToFlowField(
            NavigationAStarExitTestHarness.Query(
                new Vector3d(0, 0, 0),
                "map",
                new Vector3d(2, 0, 0),
                "map",
                NavigationAStarExitTestHarness.Profile()),
            Fixed64.Zero);
        PathQuery differentOrigin = baseline.WithStartPosition(new Vector3d(1, 0, 0));
        PathQuery differentTerminal = new(
            baseline.Start,
            new NavigationEndpoint(
                baseline.End.Position + new Vector3d(
                    Fixed64.Half,
                    Fixed64.Zero,
                    Fixed64.Zero),
                baseline.End.MapId),
            baseline.Agent,
            baseline.AreaPolicy,
            baseline.Traversal,
            baseline.Algorithm,
            baseline.Budget,
            baseline.AllowTransitions,
            baseline.FlowField);
        var addressedDestination = new NavigationCellAddress(
            "map",
            new VoxelIndex(2, 0, 0));

        var first = new NavigationFlowFieldPayloadKey(
            baseline,
            addressedDestination,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        var sameField = new NavigationFlowFieldPayloadKey(
            differentOrigin,
            addressedDestination,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        var differentField = new NavigationFlowFieldPayloadKey(
            differentTerminal,
            addressedDestination,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        var differentMedia = new NavigationFlowFieldPayloadKey(
            baseline,
            addressedDestination,
            TraversalMedium.Gas,
            TraversalMedia.Gas | TraversalMedia.Liquid);

        sameField.Should().Be(first);
        sameField.GetHashCode().Should().Be(first.GetHashCode());
        differentField.Should().NotBe(first);
        differentMedia.Should().NotBe(first);
    }

    [Fact]
    public void PayloadKey_DefaultValue_ShouldHaveStableEqualityAndHashing()
    {
        NavigationFlowFieldPayloadKey first = default;
        NavigationFlowFieldPayloadKey second = default;

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals((object)second).Should().BeTrue();
    }

    [Fact]
    public void CoverageThresholdOverflow_ShouldFailWithoutPublishingAPayload()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "overflow");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile),
            Fixed64.MaxValue);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, origin),
            new NavigationCellAddress(fixture.MapId, destination));

        result.Status.Should().Be(NavigationFlowFieldStatus.CostOverflow);
    }

    [Fact]
    public void ExplicitRelaxationOverflow_ShouldFailWithoutPublishingAPayload()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var middle = new VoxelIndex(1, 0, 0);
        var destination = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { origin, middle, destination },
                "relaxation-overflow",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "overflow",
                        origin,
                        middle,
                        corridorCost: Fixed64.MaxValue,
                        radiusClearance: (Fixed64)2),
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "terminal",
                        middle,
                        destination,
                        corridorCost: Fixed64.One,
                        radiusClearance: (Fixed64)2)
                });
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, origin),
            new NavigationCellAddress(fixture.MapId, destination));

        result.Status.Should().Be(NavigationFlowFieldStatus.CostOverflow);
        result.HasPayload.Should().BeFalse();
    }

    [Fact]
    public void OneWayReverseIntegration_ShouldPublishACompleteDirectionalNoPathField()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var oneWayTarget = new VoxelIndex(4, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(8),
                new[] { source, oneWayTarget },
                "one-way",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "forward-only",
                        source,
                        oneWayTarget,
                        corridorCost: (Fixed64)4,
                        radiusClearance: (Fixed64)2)
                });
        PathQuery query = ToFlowField(
            fixture.CreateQuery(oneWayTarget, source, fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, oneWayTarget),
            new NavigationCellAddress(fixture.MapId, source));

        result.Status.Should().Be(NavigationFlowFieldStatus.NoPath);
        result.Payload.IsComplete.Should().BeTrue();
        result.Payload.Nodes.Should().ContainSingle();
        result.Payload.Nodes[0].Address.Should().Be(
            new NavigationCellAddress(fixture.MapId, source));
    }

    [Fact]
    public void PayloadByteAccounting_ShouldMatchTheExactMaximumAndRejectNegativeCounts()
    {
        Unsafe.SizeOf<NavigationFlowFieldPayloadKey>().Should().Be(216);
        Unsafe.SizeOf<NavigationFlowFieldNode>().Should().Be(64);
        ((Action)(() => NavigationFlowFieldPayload.GetMaximumRetainedBytes(-1, 0, 0)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => NavigationFlowFieldPayload.GetMaximumRetainedBytes(0, -1, 0)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => NavigationFlowFieldPayload.GetMaximumRetainedBytes(0, 0, -1)))
            .Should().Throw<ArgumentOutOfRangeException>();

        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                cells,
                "bytes");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[2], fixture.DefaultProfile),
            Fixed64.Zero);
        NavigationCellAddress origin = new(fixture.MapId, cells[0]);
        NavigationCellAddress destination = new(fixture.MapId, cells[2]);

        FlowResult baseline = RunFlow(
            fixture.Graph,
            query,
            origin,
            destination);
        FlowResult exact = RunFlow(
            fixture.Graph,
            query,
            origin,
            destination,
            maximumPayloadBytes: 720);
        FlowResult oneByteShort = RunFlow(
            fixture.Graph,
            query,
            origin,
            destination,
            maximumPayloadBytes: 719);

        baseline.Payload.RetainedBytes.Should().Be(720);
        NavigationFlowFieldPayload.GetMaximumRetainedBytes(3, 1, 1)
            .Should().Be(720);
        exact.Status.Should().Be(NavigationFlowFieldStatus.Success);
        oneByteShort.Status.Should().Be(NavigationFlowFieldStatus.CapacityExceeded);
    }

    [Fact]
    public void ComponentMismatch_ShouldReturnNoPathWithoutPublishingAField()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { origin, destination },
                "disconnected");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile),
            Fixed64.Zero);

        FlowResult result = RunFlow(
            fixture.Graph,
            query,
            new NavigationCellAddress(fixture.MapId, origin),
            new NavigationCellAddress(fixture.MapId, destination));

        result.Status.Should().Be(NavigationFlowFieldStatus.NoPath);
        result.HasPayload.Should().BeFalse();
    }

    [Fact]
    public void ThreeNodeLine_ShouldEnforceExactSearchAndWorkspaceBoundaries()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                cells,
                "boundaries");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[2], fixture.DefaultProfile),
            Fixed64.Zero);
        NavigationCellAddress origin = new(fixture.MapId, cells[0]);
        NavigationCellAddress destination = new(fixture.MapId, cells[2]);

        FlowResult exact = RunFlow(
            fixture.Graph,
            WithBudget(query, maxExpandedNodes: 3, maxEvaluatedEdges: 9),
            origin,
            destination,
            dependencyPageCapacity: 1,
            dependencyComponentCapacity: 1,
            nodeCapacity: 3);
        FlowResult nodeBudgetShort = RunFlow(
            fixture.Graph,
            WithBudget(query, maxExpandedNodes: 2),
            origin,
            destination);
        FlowResult edgeBudgetShort = RunFlow(
            fixture.Graph,
            WithBudget(query, maxEvaluatedEdges: 8),
            origin,
            destination);
        FlowResult nodeCapacityShort = RunFlow(
            fixture.Graph,
            query,
            origin,
            destination,
            nodeCapacity: 2);
        FlowResult pageCapacityShort = RunFlow(
            fixture.Graph,
            query,
            origin,
            destination,
            dependencyPageCapacity: 0);
        FlowResult componentCapacityShort = RunFlow(
            fixture.Graph,
            query,
            origin,
            destination,
            dependencyComponentCapacity: 0);

        exact.Status.Should().Be(NavigationFlowFieldStatus.Success);
        exact.ExpandedNodes.Should().Be(3);
        exact.EvaluatedEdges.Should().Be(9);
        exact.LookupProbes.Should().Be(8);
        nodeBudgetShort.Status.Should().Be(NavigationFlowFieldStatus.BudgetExceeded);
        edgeBudgetShort.Status.Should().Be(NavigationFlowFieldStatus.BudgetExceeded);
        nodeCapacityShort.Status.Should().Be(NavigationFlowFieldStatus.CapacityExceeded);
        pageCapacityShort.Status.Should().Be(NavigationFlowFieldStatus.CapacityExceeded);
        componentCapacityShort.Status.Should().Be(
            NavigationFlowFieldStatus.CapacityExceeded);
    }

    [Fact]
    public void WorkspaceReset_ShouldReleaseActiveReferencesWithoutAllocating()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destination },
                "reset");
        var originAddress = new NavigationCellAddress(fixture.MapId, origin);
        var destinationAddress = new NavigationCellAddress(
            fixture.MapId,
            destination);
        fixture.Graph.TryGetNodeRef(originAddress, out NavigationNodeRef originNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(
                destinationAddress,
                out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        fixture.Graph.TryGetSurfaceComponent(
                originAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey component,
                out _)
            .Should().BeTrue();
        var workspace = new NavigationFlowFieldWorkspace(0, 1, 1, 2, 2, 2);

        workspace.TryGetOrAdd(originNode, out int originSlot, out _)
            .Should().BeTrue();
        ref NavigationFlowFieldSearchNode originRecord =
            ref workspace.GetRecord(originSlot);
        originRecord.Address = originAddress;
        originRecord.SelectedEdge = new NavigationSelectedEdgeRef(
            destinationAddress,
            0);
        workspace.TryRecordPage(fixture.MapId, 0).Should().BeTrue();
        workspace.TryRecordComponent(component).Should().BeTrue();
        workspace.Reset();

        workspace.TryGetOrAdd(originNode, out _, out bool originReadded)
            .Should().BeTrue();
        originReadded.Should().BeTrue();
        workspace.GetRecord(originSlot).Address.Should().Be(
            default(NavigationCellAddress));
        workspace.GetRecord(originSlot).SelectedEdge.Should().Be(
            default(NavigationSelectedEdgeRef));
        workspace.DependencyPages[0].Should().Be(
            default(GraphPageDependencyAddress));
        workspace.DependencyComponents[0].Should().Be(
            default(NavigationSurfaceComponentKey));
        workspace.DependencyPageCount.Should().Be(0);
        workspace.DependencyComponentCount.Should().Be(0);

        workspace.TryGetOrAdd(destinationNode, out int destinationSlot, out _)
            .Should().BeTrue();
        workspace.GetRecord(destinationSlot).Address = destinationAddress;
        workspace.TryRecordPage(fixture.MapId, 0).Should().BeTrue();
        workspace.TryRecordComponent(component).Should().BeTrue();
        long before = GC.GetAllocatedBytesForCurrentThread();
        workspace.Reset();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        workspace.TryGetOrAdd(
                destinationNode,
                out int resetDestinationSlot,
                out bool destinationReadded)
            .Should().BeTrue();
        destinationReadded.Should().BeTrue();
        workspace.GetRecord(resetDestinationSlot).Address.Should().Be(
            default(NavigationCellAddress));
    }

    [Fact]
    public void SearchStart_ShouldPreserveEndpointDependenciesInThePublishedPayload()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destination = new VoxelIndex(1, 0, 0);
        var endpointCandidate = new VoxelIndex(64, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(65),
                new[] { origin, destination, endpointCandidate },
                "endpoint-dependency");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destination, fixture.DefaultProfile),
            Fixed64.Zero);
        var endpointAddress = new NavigationCellAddress(
            fixture.MapId,
            endpointCandidate);
        fixture.Graph.TryGetSurfaceComponent(
                endpointAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey endpointComponent,
                out _)
            .Should().BeTrue();
        var workspace = new NavigationFlowFieldWorkspace(0, 2, 2, 2, 2, 2);
        workspace.TryRecordPage(fixture.MapId, pageIndex: 1).Should().BeTrue();
        workspace.TryRecordComponent(endpointComponent).Should().BeTrue();
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        using var work = new NavigationFlowFieldWork(
            Resolve(
                store,
                fixture.Graph,
                query,
                new NavigationCellAddress(fixture.MapId, origin),
                new NavigationCellAddress(fixture.MapId, destination),
                out _),
            workspace);

        for (int step = 0;
            step < 128 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(32, 32, 32, 32);
        }

        work.Status.Should().Be(NavigationFlowFieldStatus.Success);
        work.Result!.Dependencies.Pages.Should().HaveCount(2);
        work.Result.Dependencies.Components.Should().HaveCount(2);
    }

    [Fact]
    public void SearchAdvanceBeforePayloadConstruction_ShouldAllocateNothing()
    {
        using var world = new GridWorld();
        const int CellCount = 16;
        var cells = new VoxelIndex[CellCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new VoxelIndex(i, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(CellCount),
                cells,
                "allocation");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[CellCount - 1], fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, cells[0]);
        var destination = new NavigationCellAddress(
            fixture.MapId,
            cells[CellCount - 1]);
        var workspace = new NavigationFlowFieldWorkspace(
            0, 2, 2, CellCount, CellCount, CellCount);

        using (NavigationWorldGraphStore warmStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph))
        using (NavigationFlowFieldWork warm = new(
            Resolve(warmStore, fixture.Graph, query, origin, destination, out _),
            workspace))
        {
            warm.Advance(1, 1, 1, 1);
            warm.Advance(1, 1, 1, 1);
        }

        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        using var work = new NavigationFlowFieldWork(
            Resolve(store, fixture.Graph, query, origin, destination, out _),
            workspace);
        work.Advance(1, 1, 1, 1).Should().Be(NavigationFlowFieldStatus.Pending);
        long before = GC.GetAllocatedBytesForCurrentThread();
        NavigationFlowFieldStatus status = work.Advance(1, 1, 1, 1);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationFlowFieldStatus.Pending);
        allocated.Should().Be(0);
        work.Result.Should().BeNull();
    }

    [Fact]
    public void DependencySortBoundary_ShouldAllocateNothingAfterSearch()
    {
        using var world = new GridWorld();
        const int CellCount = 66;
        var cells = new VoxelIndex[CellCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = new VoxelIndex(i, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(CellCount),
                cells,
                "sort-allocation");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[CellCount - 1], fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, cells[0]);
        var destination = new NavigationCellAddress(
            fixture.MapId,
            cells[CellCount - 1]);
        var workspace = new NavigationFlowFieldWorkspace(
            0, 2, 1, CellCount, CellCount, CellCount);

        using (NavigationWorldGraphStore warmStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph))
        using (NavigationFlowFieldWork warm = new(
            Resolve(warmStore, fixture.Graph, query, origin, destination, out _),
            workspace))
        {
            warm.Advance(0, CellCount, 512, 0);
        }

        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        using var work = new NavigationFlowFieldWork(
            Resolve(store, fixture.Graph, query, origin, destination, out _),
            workspace);
        long before = GC.GetAllocatedBytesForCurrentThread();
        NavigationFlowFieldStatus status = work.Advance(0, CellCount, 512, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationFlowFieldStatus.Pending);
        allocated.Should().Be(0);
        work.Result.Should().BeNull();
    }

    [Fact]
    public void PayloadCapacityPreflight_ShouldRejectWithoutAllocatingDependencyStamp()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                cells,
                "payload-preflight");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(cells[0], cells[2], fixture.DefaultProfile),
            Fixed64.Zero);
        var origin = new NavigationCellAddress(fixture.MapId, cells[0]);
        var destination = new NavigationCellAddress(fixture.MapId, cells[2]);
        var workspace = new NavigationFlowFieldWorkspace(0, 1, 1, 3, 3, 3);

        using (NavigationWorldGraphStore warmStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph))
        using (NavigationFlowFieldWork warm = new(
            Resolve(warmStore, fixture.Graph, query, origin, destination, out _),
            workspace,
            maximumPayloadBytes: 0))
        {
            warm.Advance(64, 64, 64, 64);
        }

        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        store.TryAcquire()!.Dispose();
        using var work = new NavigationFlowFieldWork(
            Resolve(store, fixture.Graph, query, origin, destination, out _),
            workspace,
            maximumPayloadBytes: 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        NavigationFlowFieldStatus status = work.Advance(64, 64, 64, 64);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationFlowFieldStatus.CapacityExceeded);
        allocated.Should().Be(0);
        work.Result.Should().BeNull();
    }

    [Fact]
    public void Work_ShouldReleaseItsGraphLeaseAtCompletionAndDispose()
    {
        using var world = new GridWorld();
        VoxelIndex origin = default;
        var destinationIndex = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { origin, destinationIndex },
                "lease");
        PathQuery query = ToFlowField(
            fixture.CreateQuery(origin, destinationIndex, fixture.DefaultProfile),
            Fixed64.Zero);
        var originAddress = new NavigationCellAddress(fixture.MapId, origin);
        var destinationAddress = new NavigationCellAddress(
            fixture.MapId,
            destinationIndex);
        NavigationFlowFieldPayload payload;

        using (var completedStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph))
        using (var completed = new NavigationFlowFieldWork(
            Resolve(
                completedStore,
                fixture.Graph,
                query,
                originAddress,
                destinationAddress,
                out _),
            new NavigationFlowFieldWorkspace(0, 2, 2, 2, 2, 2)))
        {
            completedStore.ActiveLeaseCount.Should().Be(1);
            for (int step = 0;
                step < 256 && completed.Status == NavigationFlowFieldStatus.Pending;
                step++)
            {
                completed.Advance(16, 16, 16, 16);
            }
            completed.Status.Should().Be(NavigationFlowFieldStatus.Success);
            completedStore.ActiveLeaseCount.Should().Be(0);
            payload = completed.Result!;
            AssertScratchReleased(completed);
        }

        payload.TryGetNode(originAddress, out NavigationFlowFieldNode node)
            .Should().BeTrue();
        node.IntegrationCost.Should().Be(Fixed64.One);

        PathQuery zeroLookupBudget = WithBudget(query, maxLookupProbes: 0);
        using (var failedStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph))
        using (var failed = new NavigationFlowFieldWork(
            Resolve(
                failedStore,
                fixture.Graph,
                zeroLookupBudget,
                originAddress,
                destinationAddress,
                out _),
            new NavigationFlowFieldWorkspace(0, 2, 2, 2, 2, 2)))
        {
            failed.Advance(16, 16, 16, 16).Should().Be(
                NavigationFlowFieldStatus.BudgetExceeded);
            failedStore.ActiveLeaseCount.Should().Be(0);
            AssertScratchReleased(failed);
        }

        using var abandonedStore =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        var abandoned = new NavigationFlowFieldWork(
            Resolve(
                abandonedStore,
                fixture.Graph,
                query,
                originAddress,
                destinationAddress,
                out _),
            new NavigationFlowFieldWorkspace(0, 2, 2, 2, 2, 2));
        abandonedStore.ActiveLeaseCount.Should().Be(1);

        abandoned.Dispose();

        abandonedStore.ActiveLeaseCount.Should().Be(0);
        AssertScratchReleased(abandoned);
    }

    private static void AssertScratchReleased(NavigationFlowFieldWork work)
    {
        const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        Type workType = typeof(NavigationFlowFieldWork);
        workType.GetField("_dependencyStamp", PrivateInstance)!
            .GetValue(work).Should().BeNull();
        workType.GetField("_payloadNodes", PrivateInstance)!
            .GetValue(work).Should().BeNull();
        workType.GetField("_payloadLookup", PrivateInstance)!
            .GetValue(work).Should().BeNull();

        object dependencySort = workType.GetField("_dependencySort", PrivateInstance)!
            .GetValue(work)!;
        dependencySort.GetType().GetField("_components", PrivateInstance)!
            .GetValue(dependencySort).Should().BeNull();
        dependencySort.GetType().GetField("_pages", PrivateInstance)!
            .GetValue(dependencySort).Should().BeNull();

        object payloadSort = workType.GetField("_payloadSort", PrivateInstance)!
            .GetValue(work)!;
        payloadSort.GetType().GetField("_nodes", PrivateInstance)!
            .GetValue(payloadSort).Should().BeNull();
        payloadSort.GetType().GetField("_lookup", PrivateInstance)!
            .GetValue(payloadSort).Should().BeNull();
    }

    private static FlowResult RunFlow(
        NavigationWorldGraph graph,
        PathQuery query,
        NavigationCellAddress start,
        NavigationCellAddress destination,
        long maximumPayloadBytes = long.MaxValue,
        int dependencyPageCapacity = 128,
        int dependencyComponentCapacity = 128,
        int nodeCapacity = 128)
    {
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        graph.TryGetNodeRef(start, out NavigationNodeRef startNode).Should().BeTrue();
        graph.TryGetNodeRef(destination, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        var meter = new NavigationWorkMeter(query.Budget);
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            lease,
            query,
            new NavigationResolvedEndpoint(
                startNode,
                start,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                Vector3d.Zero,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                destinationNode,
                destination,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                Vector3d.Zero,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            TraversalMedia.Solid,
            meter);
        var workspace = new NavigationFlowFieldWorkspace(
            mapCapacity: 0,
            dependencyPageCapacity,
            dependencyComponentCapacity,
            nodeCapacity,
            rayCoveredAddressCapacity: nodeCapacity,
            rayTraceIntervalCapacity: nodeCapacity);
        using var work = new NavigationFlowFieldWork(
            resolved,
            workspace,
            maximumPayloadBytes);
        for (int step = 0;
            step < 4_096 && work.Status == NavigationFlowFieldStatus.Pending;
            step++)
        {
            work.Advance(64, 64, 64, 64);
        }
        return new FlowResult(
            work.Status,
            work.Result!,
            work.Result != null,
            meter.LookupProbes,
            meter.ExpandedNodes,
            meter.EvaluatedEdges,
            meter.ConnectionLegs);
    }

    private static PathQuery ToFlowField(PathQuery query, Fixed64 extraIntegrationCost) =>
        new(
            query.Start,
            query.End,
            query.Agent,
            query.AreaPolicy,
            query.Traversal,
            PathAlgorithm.FlowField,
            query.Budget,
            query.AllowTransitions,
            new FlowFieldQueryOptions(extraIntegrationCost));

    private static PathQuery WithBudget(
        PathQuery query,
        int? maxLookupProbes = null,
        int? maxConnectionLegs = null,
        int? maxExpandedNodes = null,
        int? maxEvaluatedEdges = null) =>
        new(
            query.Start,
            query.End,
            query.Agent,
            query.AreaPolicy,
            query.Traversal,
            query.Algorithm,
            new NavigationWorkBudget(
                maxLookupProbes ?? query.Budget.MaxLookupProbes,
                query.Budget.MaxEndpointCandidates,
                maxExpandedNodes ?? query.Budget.MaxExpandedNodes,
                maxEvaluatedEdges ?? query.Budget.MaxEvaluatedEdges,
                maxConnectionLegs ?? query.Budget.MaxConnectionLegs,
                query.Budget.MaxTransitionCandidates,
                query.Budget.MaxTransitionPairs,
                query.Budget.MaxStagedLegAttempts,
                query.Budget.MaxTraceIntervals,
                query.Budget.MaxCoveredVoxelIntervals,
                query.Budget.MaxSimplificationRays),
            query.AllowTransitions,
            query.FlowField);

    private static NavigationResolvedPathQuery Resolve(
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        PathQuery query,
        NavigationCellAddress start,
        NavigationCellAddress destination,
        out NavigationWorkMeter meter)
    {
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        graph.TryGetNodeRef(start, out NavigationNodeRef startNode).Should().BeTrue();
        graph.TryGetNodeRef(destination, out NavigationNodeRef destinationNode)
            .Should().BeTrue();
        graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy? policy)
            .Should().BeTrue();
        meter = new NavigationWorkMeter(query.Budget);
        var resolved = new NavigationResolvedPathQuery();
        resolved.Bind(
            lease,
            query,
            new NavigationResolvedEndpoint(
                startNode,
                start,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                Vector3d.Zero,
                Fixed64.Zero),
            new NavigationResolvedEndpoint(
                destinationNode,
                destination,
                TraversalMedia.Solid,
                TraversalMedium.Solid,
                Vector3d.Zero,
                Fixed64.Zero),
            policy!,
            TraversalMedium.Solid,
            TraversalMedia.Solid,
            meter);
        return resolved;
    }

    private readonly record struct FlowResult(
        NavigationFlowFieldStatus Status,
        NavigationFlowFieldPayload Payload,
        bool HasPayload,
        int LookupProbes,
        int ExpandedNodes,
        int EvaluatedEdges,
        int ConnectionLegs);
}
