using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using GridForge.Grids.Topology;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using System.Linq;
using System.Reflection;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationRayTests
{
    [Fact]
    public void WorkMeter_ShouldDebitEachRayCategoryExactly()
    {
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            maxLookupProbes: 0,
            maxEndpointCandidates: 0,
            maxExpandedNodes: 0,
            maxEvaluatedEdges: 0,
            maxConnectionLegs: 0,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 2,
            maxCoveredVoxelIntervals: 3,
            maxSimplificationRays: 1));

        meter.TryConsumeTraceIntervals(2).Should().BeTrue();
        meter.TryConsumeTraceIntervals(1).Should().BeFalse();
        meter.TraceIntervals.Should().Be(2);
        meter.TryConsumeCoveredVoxelIntervals(3).Should().BeTrue();
        meter.TryConsumeCoveredVoxelIntervals(1).Should().BeFalse();
        meter.CoveredVoxelIntervals.Should().Be(3);
        meter.TryConsumeSimplificationRays(1).Should().BeTrue();
        meter.TryConsumeSimplificationRays(1).Should().BeFalse();
        meter.SimplificationRays.Should().Be(1);

        meter.Reset(new NavigationWorkBudget(0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1));
        meter.TraceIntervals.Should().Be(0);
        meter.CoveredVoxelIntervals.Should().Be(0);
        meter.SimplificationRays.Should().Be(0);
    }

    [Fact]
    public void Workspace_ShouldDeriveEveryBufferFromExplicitCeilings()
    {
        var workspace = new NavigationRayWorkspace(
            mapCapacity: 2,
            pageCapacity: 3,
            componentCapacity: 5,
            coveredAddressCapacity: 13,
            traceIntervalCapacity: 11);

        workspace.TraceIntervals.Capacity.Should().BeGreaterThanOrEqualTo(11);
        workspace.TraceIntervalCapacity.Should().Be(11);
        workspace.ChainRecords.Should().HaveCount(11);
        workspace.Dependencies.Pages.Should().HaveCount(3);
        workspace.Dependencies.Components.Should().HaveCount(5);
        workspace.CoveredAddressCapacity.Should().Be(13);
        workspace.MapCapacity.Should().Be(2);
    }

    [Fact]
    public void Context_ShouldOwnOneImmediateRayWorkspace()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();

        context.Pathing.ImmediateRayWorkspace.SyncRoot.Should().NotBeNull();
        context.Pathing.ImmediateRayWorkspace.Workspace.Should().NotBeNull();
    }

    [Fact]
    public void WorkspaceContracts_ShouldNotRetainCompatibilityOrForwardingSurface()
    {
        ConstructorInfo aStarConstructor = typeof(NavigationAStarWorkspace)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        ConstructorInfo flowConstructor = typeof(NavigationFlowFieldWorkspace)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();

        aStarConstructor.GetParameters().Skip(4)
            .Should().OnlyContain(parameter => !parameter.HasDefaultValue);
        flowConstructor.GetParameters().Skip(4)
            .Should().OnlyContain(parameter => !parameter.HasDefaultValue);
        typeof(TrailblazerGuideService).GetProperty(
            "ImmediateRayWorkspace",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        typeof(NavigationRayWorkspace).GetProperty(
            "GenerationStamps",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        typeof(NavigationRayWorkspace).GetProperty(
            "IntervalAddresses",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        typeof(NavigationRayWorkspace).GetProperty(
            "IntervalNodes",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        typeof(NavigationRayWorkspace).GetProperty(
            "PredecessorOrdinals",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
        typeof(NavigationRayWorkspace).GetProperty(
            "EdgeOrdinals",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().BeNull();
    }

    [Fact]
    public void RayResult_ShouldRetainOnlyConsumerFacts()
    {
        NavigationCellAddress start = default;
        NavigationCellAddress end = default;
        var result = new NavigationRayResult(
            NavigationRayStatus.Success,
            start,
            end,
            Fixed64.One,
            isSemanticCostNeutral: true);

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.StartAddress.Should().Be(start);
        result.EndAddress.Should().Be(end);
        result.TraversalCost.Should().Be(Fixed64.One);
        result.IsSemanticCostNeutral.Should().BeTrue();
    }

    [Fact]
    public void OrderedRay_ShouldFollowTheExactGraphChainAndCost()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));

        work.Begin(request);

        work.Advance(meter).Should().Be(NavigationRayStatus.Success);
        work.Result.StartAddress.Should().Be(fixture.FarOrigin);
        work.Result.EndAddress.Should().Be(fixture.Near.Nodes[0].Address);
        work.Result.TraversalCost.Should().Be((Fixed64)3);
        work.Result.IsSemanticCostNeutral.Should().BeTrue();
        meter.LookupProbes.Should().Be(5);
        meter.CoveredVoxelIntervals.Should().Be(10);
        meter.EvaluatedEdges.Should().Be(6);
        meter.TraceIntervals.Should().Be(4);
        typeof(NavigationSurfaceEdgeEnumerator).GetProperty(
            "CurrentOrdinal",
            BindingFlags.Instance | BindingFlags.NonPublic).Should().NotBeNull();
    }

    [Theory]
    [InlineData(4, 10, 6, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(5, 10, 6, (int)NavigationRayStatus.Success)]
    [InlineData(5, 9, 6, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(5, 10, 5, (int)NavigationRayStatus.BudgetExceeded)]
    public void OrderedRay_ShouldHonorEachQueryMeterBoundary(
        int lookupProbes,
        int coveredAddresses,
        int evaluatedEdges,
        int expectedStatus)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);

        NavigationRayResult result = RunRay(
            CreateLineRequest(fixture),
            CreateRayBudget(
                traceCapacity: 4,
                coveredCapacity: coveredAddresses,
                lookupProbes: lookupProbes,
                evaluatedEdges: evaluatedEdges));

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
    }

    [Theory]
    [InlineData(0, (int)NavigationRayStatus.Success)]
    [InlineData(1, (int)NavigationRayStatus.Success)]
    [InlineData(2, (int)NavigationRayStatus.CostOverflow)]
    public void OrderedRay_ShouldReportCellAndAreaSurchargesWithoutWrapping(
        int surchargeKind,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationAreaId targetArea = surchargeKind == 1
            ? new NavigationAreaId(1)
            : default;
        Fixed64 enterCost = surchargeKind == 0
            ? Fixed64.Half
            : surchargeKind == 2 ? Fixed64.MaxValue : Fixed64.Zero;
        var targetCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            targetArea,
            enterCost,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAreaRule[] rules = surchargeKind == 1
            ? new[]
            {
                new NavigationAreaRule(true, Fixed64.Zero),
                new NavigationAreaRule(true, Fixed64.Half)
            }
            : new[] { new NavigationAreaRule(true, Fixed64.Zero) };
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("ray-cost", 1),
            rules);
        var destination = new VoxelIndex(1, 0, 0);
        var prepared = new PreparedNavigationMap(
            new NavigationMapBuilder("ray-cost", binding)
                .AddCell(default, NavigationAStarExitTestHarness.Cell)
                .AddCell(destination, targetCell)
                .Build(),
            1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationAreaCatalog.Empty.TryPublish(
                policy,
                maxPolicies: 1,
                requiredRuleCount: rules.Length,
                maxRulesPerPolicy: rules.Length,
                maxRules: rules.Length,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph = new NavigationWorldGraph(
            1,
            new[] { instance },
            areaCatalog: catalog);
        graph = graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);
        var request = new NavigationRayRequest(
            world,
            store,
            graph,
            NavigationAStarExitTestHarness.Profile(),
            policy,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            allowTransitions: false,
            NavigationAStarExitTestHarness.GetFoot(binding, default),
            NavigationAStarExitTestHarness.GetFoot(binding, destination),
            NavigationRayEndpointAllowance.None);

        NavigationRayResult result = RunRay(request);

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
        if (result.Status == NavigationRayStatus.Success)
        {
            result.TraversalCost.Should().Be(Fixed64.One + Fixed64.Half);
            result.IsSemanticCostNeutral.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(2, 16, (int)NavigationRayStatus.CapacityExceeded)]
    [InlineData(16, 2, (int)NavigationRayStatus.BudgetExceeded)]
    public void OrderedRay_ShouldDistinguishWorkspaceAndQueryTraceCeilings(
        int workspaceTraceCapacity,
        int budgetTraceCapacity,
        int expectedStatus)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var work = new NavigationRayWork(new NavigationRayWorkspace(
            1,
            8,
            8,
            16,
            workspaceTraceCapacity));
        var meter = new NavigationWorkMeter(CreateRayBudget(
            budgetTraceCapacity,
            coveredCapacity: 16));

        work.Begin(CreateLineRequest(fixture));

        NavigationRayStatus expected = (NavigationRayStatus)expectedStatus;
        work.Advance(meter).Should().Be(expected);
        work.Result.Status.Should().Be(expected);
    }

    [Fact]
    public void OrderedRay_ShouldHonorSourceOnlyAndExactSelectedEdgeConstraints()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationWorldGraph graph = fixture.Graph;
        graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        var selectedAddress = new NavigationCellAddress(
            fixture.FarOrigin.MapId,
            new VoxelIndex(2, 0, 0));
        graph.TryGetNodeRef(selectedAddress, out NavigationNodeRef selectedNode)
            .Should().BeTrue();
        graph.TryGetNodeState(sourceNode, out NavigationNodeState sourceState)
            .Should().BeTrue();
        graph.TryGetNodeState(selectedNode, out NavigationNodeState selectedState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
        edges.MoveNext().Should().BeTrue();
        graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress firstTarget)
            .Should().BeTrue();
        firstTarget.Should().Be(selectedAddress);
        int selectedOrdinal = edges.CurrentOrdinal;

        RunRay(CreateRequest(
                fixture.World,
                fixture.Store,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                sourceState.FootAnchor,
                selectedState.FootAnchor,
                NavigationRayChainConstraint.SourceOnly(fixture.FarOrigin)))
            .Status.Should().Be(NavigationRayStatus.Blocked);

        NavigationRayResult selected = RunRay(CreateRequest(
            fixture.World,
            fixture.Store,
            graph,
            NavigationAStarExitTestHarness.Profile(),
            sourceState.FootAnchor,
            selectedState.FootAnchor,
            NavigationRayChainConstraint.SelectedEdge(
                fixture.FarOrigin,
                selectedAddress,
                selectedOrdinal)));
        selected.Status.Should().Be(NavigationRayStatus.Success);
        selected.EndAddress.Should().Be(selectedAddress);

        RunRay(CreateRequest(
                fixture.World,
                fixture.Store,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                sourceState.FootAnchor,
                selectedState.FootAnchor,
                NavigationRayChainConstraint.SelectedEdge(
                    fixture.FarOrigin,
                    selectedAddress,
                    selectedOrdinal + 1)))
            .Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedRay_ShouldTraverseAutomaticSeamsInBothDirections(bool stacked)
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        NavigationAgentProfile profile = stacked
            ? new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                (Fixed64)2,
                (Fixed64)2,
                Fixed64.Zero,
                TraversalMedia.Solid,
                TraversalCapability.None)
            : fixture.DefaultProfile;

        NavigationRayResult forward = RunRay(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            profile,
            fixture.Start,
            fixture.End));
        NavigationRayResult reverse = RunRay(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            profile,
            fixture.End,
            fixture.Start));

        forward.Status.Should().Be(NavigationRayStatus.Success);
        reverse.Status.Should().Be(NavigationRayStatus.Success);
        forward.StartAddress.Should().Be(reverse.EndAddress);
        forward.EndAddress.Should().Be(reverse.StartAddress);
    }

    [Fact]
    public void OrderedRay_ShouldRevisitAnEarlierCanonicalRecordUnlockedByALaterSeamSource()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        edges.MoveNext().Should().BeTrue();
        edges.Current.Kind.Should().Be(NavigationGraphEdgeKind.Seam);
        int selectedOrdinal = edges.CurrentOrdinal;
        Vector3d seamPoint = Vector3d.Lerp(fixture.Start, fixture.End, Fixed64.Half);
        var workspace = new NavigationRayWorkspace(4, 16, 16, 32, 32);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(32, 32));

        work.Begin(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            seamPoint,
            fixture.End,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                selectedOrdinal)));
        work.Advance(meter).Should().Be(NavigationRayStatus.Success);
        NavigationRayResult result = work.Result;

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.StartAddress.Should().Be(sourceAddress);
        result.EndAddress.Should().Be(targetAddress);
    }

    [Fact]
    public void OrderedRay_ShouldPreserveTheTransitiveTieGroupAcrossAnUnmappedBridge()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        fixture.Context.World.TryAddGrid(
                new GridConfiguration(
                    Vector3d.Zero,
                    Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(16))),
                out _)
            .Should().BeTrue();
        var workspace = new NavigationRayWorkspace(4, 16, 16, 32, 32);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(32, 32));

        work.Begin(CreateRequest(
            fixture.Context.World,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            fixture.Start,
            fixture.End));
        work.Advance(meter).Should().Be(NavigationRayStatus.Success);

        workspace.TraceIntervals
            .GroupBy(interval => interval.TieGroupId)
            .Should().Contain(group => group.Count() >= 3);
    }

    [Theory]
    [InlineData(false, (int)NavigationRayStatus.Success)]
    [InlineData(true, (int)NavigationRayStatus.Blocked)]
    public void OrderedRay_ShouldRequireTheExactExplicitCorridor(
        bool offLineEntry,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-explicit",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "corridor",
                        source,
                        destination,
                        corridorCost: Fixed64.One,
                        radiusClearance: Fixed64.One,
                        entryOffset: offLineEntry
                            ? new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One / (Fixed64)4)
                            : default)
                });
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var sourceAddress = new NavigationCellAddress(fixture.MapId, source);
        var targetAddress = new NavigationCellAddress(fixture.MapId, destination);
        fixture.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeRef(targetAddress, out NavigationNodeRef targetNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(sourceNode, out NavigationNodeState sourceState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(targetNode, out NavigationNodeState targetState)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = fixture.Graph.EnumerateSurfaceEdges(sourceNode);
        int explicitOrdinal = -1;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
                explicitOrdinal = edges.CurrentOrdinal;
        }
        explicitOrdinal.Should().BeGreaterThanOrEqualTo(0);

        NavigationRayRequest request = CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            sourceState.FootAnchor,
            targetState.FootAnchor,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal));
        var workspace = new NavigationRayWorkspace(4, 32, 32, 64, 64);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateRayBudget(64, 64));
        work.Begin(request);
        work.Advance(meter);
        NavigationRayResult result = work.Result;

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
        if (!offLineEntry)
            result.TraversalCost.Should().Be(Fixed64.One);
        workspace.ChainRecords.Should().OnlyContain(record =>
            record.IncomingExplicitConnection == null);
    }

    [Theory]
    [InlineData((int)GridTopologyKind.RectangularPrism, (int)HexOrientation.PointyTop)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.PointyTop)]
    [InlineData((int)GridTopologyKind.HexPrism, (int)HexOrientation.FlatTop)]
    public void OrderedRay_ShouldFollowNativeChainsAcrossEveryTopology(
        int topologyValue,
        int orientationValue)
    {
        GridTopologyKind topology = (GridTopologyKind)topologyValue;
        HexOrientation orientation = (HexOrientation)orientationValue;
        using var world = new GridForge.Grids.GridWorld();
        GridTopologyMetrics metrics = topology == GridTopologyKind.RectangularPrism
            ? GridTopologyMetrics.Rectangular(Fixed64.One)
            : GridTopologyMetrics.Hex((Fixed64)2, Fixed64.One, orientation);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(12, 2, 12),
            topologyKind: topology,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex start;
        VoxelIndex middle;
        VoxelIndex end;
        if (topology == GridTopologyKind.RectangularPrism)
        {
            start = default;
            middle = new VoxelIndex(1, 0, 0);
            end = new VoxelIndex(2, 0, 0);
        }
        else
        {
            FindHexLine(binding, out start, out middle, out end);
        }
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { start, middle, end },
                topology == GridTopologyKind.RectangularPrism
                    ? "ray-rect"
                    : orientation == HexOrientation.PointyTop ? "ray-pointy" : "ray-flat");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(binding, start),
            NavigationAStarExitTestHarness.GetFoot(binding, end)));

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.StartAddress.Should().Be(new NavigationCellAddress(fixture.MapId, start));
        result.EndAddress.Should().Be(new NavigationCellAddress(fixture.MapId, end));
    }

    [Fact]
    public void OrderedRay_ShouldRejectAnInteriorSparseHole()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { start, end },
                "ray-hole");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);

        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                fixture.DefaultProfile,
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, start),
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, end)))
            .Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(false, (int)NavigationRayStatus.Success)]
    [InlineData(true, (int)NavigationRayStatus.Blocked)]
    public void OrderedRay_ShouldRejectPositiveRadiusWallClipping(
        bool nearWall,
        int expectedStatus)
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(2, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(3),
                new[] { start, new VoxelIndex(1, 0, 0), end },
                "ray-radius");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d offset = nearWall
            ? new Vector3d(Fixed64.Zero, Fixed64.Zero, (Fixed64)2 / (Fixed64)5)
            : default;
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                baseline.Shape.Height,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, start) + offset,
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, end) + offset))
            .Status.Should().Be((NavigationRayStatus)expectedStatus);
    }

    [Theory]
    [InlineData((int)NavigationRayEndpointAllowance.StartPrefix)]
    [InlineData((int)NavigationRayEndpointAllowance.DestinationSuffix)]
    public void OrderedRay_ShouldPermitOnlyTheRequestedEndpointBoundary(
        int allowanceValue)
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex cell = default;
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(1),
                new[] { cell },
                "ray-endpoint");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d foot = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cell);
        Vector3d outside = foot - new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        NavigationAgentProfile baseline = fixture.DefaultProfile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                baseline.Shape.Height,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);
        var address = new NavigationCellAddress(fixture.MapId, cell);
        NavigationRayEndpointAllowance allowance =
            (NavigationRayEndpointAllowance)allowanceValue;
        Vector3d start = allowance == NavigationRayEndpointAllowance.StartPrefix
            ? outside
            : foot;
        Vector3d end = allowance == NavigationRayEndpointAllowance.StartPrefix
            ? foot
            : outside;

        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                start,
                end,
                NavigationRayChainConstraint.SourceOnly(address),
                allowance))
            .Status.Should().Be(NavigationRayStatus.Success);
        RunRay(CreateRequest(
                world,
                store,
                fixture.Graph,
                profile,
                start,
                end,
                NavigationRayChainConstraint.SourceOnly(address)))
            .Status.Should().Be(NavigationRayStatus.Blocked);
    }

    [Fact]
    public void OrderedRay_ShouldReachTheFarthestValidDestinationSuffixCell()
    {
        using var world = new GridForge.Grids.GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "ray-destination-suffix");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d destinationFoot = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            destination);

        NavigationRayResult result = RunRay(CreateRequest(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source),
            destinationFoot + new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            endpointAllowance: NavigationRayEndpointAllowance.DestinationSuffix));

        result.Status.Should().Be(NavigationRayStatus.Success);
        result.EndAddress.Should().Be(
            new NavigationCellAddress(fixture.MapId, destination));
    }

    [Fact]
    public void OrderedRay_ShouldMeterAndValidateEveryExplicitWitnessLeg()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        VoxelIndex source = default;
        var firstWitness = new VoxelIndex(1, 0, 0);
        var secondWitness = new VoxelIndex(2, 0, 0);
        var destination = new VoxelIndex(3, 0, 0);
        Vector3d sourceFoot = NavigationAStarExitTestHarness.GetFoot(binding, source);
        Vector3d destinationFoot = NavigationAStarExitTestHarness.GetFoot(binding, destination);
        var connection = new NavigationConnection(
            "ray-multi",
            source,
            new NavigationCellAddress("ray-multi", destination),
            sourceFoot,
            destinationFoot,
            Fixed64.Zero,
            Fixed64.One,
            new[]
            {
                new NavigationCellAddress("ray-multi", firstWitness),
                new NavigationCellAddress("ray-multi", secondWitness)
            },
            additionalCost: Fixed64.Half);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        SimulateUntilTerminal(context, policyOperation.Receipt);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("ray-multi", binding)
                    .AddCell(source, NavigationAStarExitTestHarness.Cell)
                    .AddCell(firstWitness, NavigationAStarExitTestHarness.Cell)
                    .AddCell(secondWitness, NavigationAStarExitTestHarness.Cell)
                    .AddCell(destination, NavigationAStarExitTestHarness.Cell)
                    .AddConnection(connection)
                    .Build(),
                1),
            OverlayReplacementPolicy.Clear,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        SimulateUntilTerminal(context, mapOperation.Receipt);
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current;
        var sourceAddress = new NavigationCellAddress("ray-multi", source);
        var targetAddress = new NavigationCellAddress("ray-multi", destination);
        graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
        int explicitOrdinal = -1;
        while (edges.MoveNext())
        {
            if (edges.Current.Kind == NavigationGraphEdgeKind.Explicit)
                explicitOrdinal = edges.CurrentOrdinal;
        }
        explicitOrdinal.Should().BeGreaterThanOrEqualTo(0);
        NavigationRayRequest request = CreateRequest(
            context.World,
            context.Pathing.NavigationGraphStore,
            graph,
            NavigationAStarExitTestHarness.Profile(),
            sourceFoot,
            destinationFoot,
            NavigationRayChainConstraint.SelectedEdge(
                sourceAddress,
                targetAddress,
                explicitOrdinal));

        RunRay(request, CreateRayBudget(64, 64, connectionLegs: 2))
            .Status.Should().Be(NavigationRayStatus.BudgetExceeded);
        NavigationRayResult success = RunRay(
            request,
            CreateRayBudget(64, 64, connectionLegs: 3));
        success.Status.Should().Be(NavigationRayStatus.Success);
        success.StartAddress.Should().Be(sourceAddress);
        success.EndAddress.Should().Be(targetAddress);
        success.IsSemanticCostNeutral.Should().BeFalse();

        RunRay(CreateRequest(
                context.World,
                context.Pathing.NavigationGraphStore,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                sourceFoot - new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
                destinationFoot,
                NavigationRayChainConstraint.SelectedEdge(
                    sourceAddress,
                    targetAddress,
                    explicitOrdinal),
                NavigationRayEndpointAllowance.StartPrefix))
            .Status.Should().Be(NavigationRayStatus.Success);
    }

    [Theory]
    [InlineData(false, false, (int)NavigationRayStatus.Success)]
    [InlineData(true, false, (int)NavigationRayStatus.Blocked)]
    [InlineData(true, true, (int)NavigationRayStatus.Success)]
    public void OrderedRay_ShouldOrderConsecutiveExplicitEdgesAndKeepThemDirected(
        bool reverseSharedAnchors,
        bool includeEarlierAlternative,
        int expectedStatus)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular((Fixed64)2);
        GridConfiguration startConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        var middleCenter = new Vector3d(2, 0, 0);
        GridConfiguration middleConfiguration = new(
            middleCenter,
            middleCenter,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        var endCenter = new Vector3d(4, 0, 0);
        GridConfiguration endConfiguration = new(
            endCenter,
            endCenter,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(startConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(middleConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(endConfiguration, out _).Should().BeTrue();
        startConfiguration.TryNormalize(out NormalizedGridConfiguration startBinding)
            .Should().BeTrue();
        middleConfiguration.TryNormalize(out NormalizedGridConfiguration middleBinding)
            .Should().BeTrue();
        endConfiguration.TryNormalize(out NormalizedGridConfiguration endBinding)
            .Should().BeTrue();
        Vector3d startFoot = NavigationAStarExitTestHarness.GetFoot(
            startBinding,
            default);
        Vector3d middleFoot = NavigationAStarExitTestHarness.GetFoot(
            middleBinding,
            default);
        Vector3d endFoot = NavigationAStarExitTestHarness.GetFoot(endBinding, default);
        Vector3d quarter = new(Fixed64.One / (Fixed64)4, Fixed64.Zero, Fixed64.Zero);
        var first = new NavigationConnection(
            includeEarlierAlternative ? "a-late" : "first",
            default,
            new NavigationCellAddress("m-middle", default),
            startFoot,
            middleFoot + (reverseSharedAnchors ? quarter : -quarter),
            Fixed64.Zero,
            Fixed64.One);
        NavigationConnection? earlierAlternative = includeEarlierAlternative
            ? new NavigationConnection(
                "z-early",
                default,
                new NavigationCellAddress("m-middle", default),
                startFoot,
                middleFoot - quarter,
                Fixed64.Zero,
                Fixed64.One)
            : null;
        var next = new NavigationConnection(
            "next",
            default,
            new NavigationCellAddress("a-end", default),
            middleFoot + (reverseSharedAnchors ? Vector3d.Zero : quarter),
            endFoot,
            Fixed64.Zero,
            Fixed64.One);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            NavigationAStarExitTestHarness.Policy,
            1,
            context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        SimulateUntilTerminal(context, policyOperation.Receipt);
        var startBuilder = new NavigationMapBuilder("z-start", startBinding)
            .AddCell(default, NavigationAStarExitTestHarness.Cell)
            .AddConnection(first);
        if (earlierAlternative != null)
            startBuilder.AddConnection(earlierAlternative);
        NavigationMapCommitOperation[] maps =
        {
            new(
                new PreparedNavigationMap(
                    startBuilder.Build(),
                    1),
                OverlayReplacementPolicy.Clear,
                1,
                context.FrameCount + 1),
            new(
                new PreparedNavigationMap(
                    new NavigationMapBuilder("m-middle", middleBinding)
                        .AddCell(default, NavigationAStarExitTestHarness.Cell)
                        .AddConnection(next)
                        .Build(),
                    1),
                OverlayReplacementPolicy.Clear,
                2,
                context.FrameCount + 1),
            new(
                new PreparedNavigationMap(
                    new NavigationMapBuilder("a-end", endBinding)
                        .AddCell(default, NavigationAStarExitTestHarness.Cell)
                        .Build(),
                    1),
                OverlayReplacementPolicy.Clear,
                3,
                context.FrameCount + 1)
        };
        for (int i = 0; i < maps.Length; i++)
            context.Pathing.Admit(maps[i]).Should().BeTrue();
        SimulateUntilTerminal(context, maps[maps.Length - 1].Receipt);
        maps.Should().OnlyContain(map =>
            map.Receipt.Status == NavigationOperationStatus.Applied);
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current
            .WithAutomaticSeams(NavigationAutomaticSeamIndex.Empty);
        graph = graph.WithSurfaceComponents(
            NavigationSurfaceComponentTestFactory.Build(graph));
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(graph, 2);

        NavigationRayResult result = RunRay(CreateRequest(
                context.World,
                store,
                graph,
                NavigationAStarExitTestHarness.Profile(),
                startFoot,
                endFoot));

        result.Status.Should().Be((NavigationRayStatus)expectedStatus);
        if (!reverseSharedAnchors && !includeEarlierAlternative)
        {
            RunRay(CreateRequest(
                    context.World,
                    store,
                    graph,
                    NavigationAStarExitTestHarness.Profile(),
                    endFoot,
                    startFoot))
                .Status.Should().Be(NavigationRayStatus.Blocked);
        }
    }

    [Fact]
    public void OrderedRay_ShouldAcceptOnlyDependencyCompatiblePublications()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture compatible =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest compatibleRequest = CreateLineRequest(compatible);
        compatible.Store.TryPublish(
                compatible.Graph.WithGraphVersion(compatible.Graph.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);

        RunRay(compatibleRequest).Status.Should().Be(NavigationRayStatus.Success);

        using NavigationFlowFieldCacheTestHarness.LineFixture stale =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest staleRequest = CreateLineRequest(stale);
        var revisedPolicy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey(
                NavigationAStarExitTestHarness.Policy.Key.PolicyId,
                NavigationAStarExitTestHarness.Policy.Key.Revision + 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        NavigationAreaCatalog.Empty.TryPublish(
                revisedPolicy,
                1,
                1,
                1,
                1,
                out NavigationAreaCatalog revisedCatalog)
            .Should().Be(NavigationOperationRejection.None);
        stale.Store.TryPublish(stale.Graph.WithAreaCatalog(
                revisedCatalog,
                stale.Graph.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);

        RunRay(staleRequest).Status.Should().Be(NavigationRayStatus.Stale);
    }

    [Fact]
    public void OrderedRay_ShouldRejectPreTraceRawWorldMutationAsStale()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        fixture.Graph.TryGetMap(
                fixture.FarOrigin.MapId,
                out NavigationMapInstance? instance)
            .Should().BeTrue();
        instance.Should().NotBeNull();
        fixture.World.ActiveGrids[instance!.GridIdentity.GridIndex]
            .TryRemoveVoxel(new VoxelIndex(2, 0, 0))
            .Should().BeTrue();

        RunRay(request).Status.Should().Be(NavigationRayStatus.Stale);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    public void OrderedRay_ShouldFailClosedWhenDependencyScratchIsTooSmall(
        int pageCapacity,
        int componentCapacity)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        var work = new NavigationRayWork(new NavigationRayWorkspace(
            1,
            pageCapacity,
            componentCapacity,
            16,
            16));
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));

        work.Begin(CreateLineRequest(fixture));

        work.Advance(meter).Should().Be(NavigationRayStatus.CapacityExceeded);
    }

    [Fact]
    public void OrderedRay_ShouldAllocateZeroBytesAfterWarmup()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        var meter = new NavigationWorkMeter(CreateRayBudget(16, 16));
        GuideSampleWorkBudget guideBudget = new(
            maxCurrentNodeLookupProbes: 64,
            maxCursorLegScans: 64,
            maxCursorRebases: 0,
            maxPortalChecks: 64,
            maxPrismChecks: 64,
            maxTraceIntervals: 64,
            maxLocalRecoveryAttempts: 0);
        var guideMeter = new GuideSampleWorkMeter(guideBudget);
        for (int i = 0; i < 16; i++)
        {
            meter.Reset(CreateRayBudget(16, 16));
            work.Begin(request);
            work.Advance(meter).Should().Be(NavigationRayStatus.Success);
            guideMeter = new GuideSampleWorkMeter(guideBudget);
            work.Begin(request);
            work.Advance(ref guideMeter).Should().Be(NavigationRayStatus.Success);
        }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        NavigationRayStatus status = default;
        NavigationRayStatus guideStatus = default;
        for (int i = 0; i < 256; i++)
        {
            meter.Reset(CreateRayBudget(16, 16));
            work.Begin(request);
            status = work.Advance(meter);
            guideMeter = new GuideSampleWorkMeter(guideBudget);
            work.Begin(request);
            guideStatus = work.Advance(ref guideMeter);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationRayStatus.Success);
        guideStatus.Should().Be(NavigationRayStatus.Success);
        allocated.Should().Be(0);
    }

    [Theory]
    [InlineData(14, 64, 64, 64, 64, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(15, 64, 64, 64, 64, (int)NavigationRayStatus.Success)]
    [InlineData(64, 5, 64, 64, 64, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(64, 6, 64, 64, 64, (int)NavigationRayStatus.Success)]
    [InlineData(64, 64, 8, 64, 64, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(64, 64, 9, 64, 64, (int)NavigationRayStatus.Success)]
    [InlineData(64, 64, 64, 4, 64, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(64, 64, 64, 5, 64, (int)NavigationRayStatus.Success)]
    [InlineData(64, 64, 64, 64, 3, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(64, 64, 64, 64, 4, (int)NavigationRayStatus.Success)]
    public void OrderedRay_ShouldShareOneFiniteGuideSampleMeter(
        int currentNodeLookups,
        int cursorLegScans,
        int portalChecks,
        int prismChecks,
        int traceIntervals,
        int expectedStatus)
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        NavigationRayRequest request = CreateLineRequest(fixture);
        var work = new NavigationRayWork(new NavigationRayWorkspace(1, 8, 8, 16, 16));
        var meter = new GuideSampleWorkMeter(new GuideSampleWorkBudget(
            maxCurrentNodeLookupProbes: currentNodeLookups,
            maxCursorLegScans: cursorLegScans,
            maxCursorRebases: 0,
            maxPortalChecks: portalChecks,
            maxPrismChecks: prismChecks,
            maxTraceIntervals: traceIntervals,
            maxLocalRecoveryAttempts: 0));

        work.Begin(request);
        work.Advance(ref meter).Should().Be((NavigationRayStatus)expectedStatus);
    }

    private static NavigationRayRequest CreateLineRequest(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef startNode)
            .Should().BeTrue();
        NavigationCellAddress endAddress = fixture.Near.Nodes[0].Address;
        fixture.Graph.TryGetNodeRef(endAddress, out NavigationNodeRef endNode)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(startNode, out NavigationNodeState startState)
            .Should().BeTrue();
        fixture.Graph.TryGetNodeState(endNode, out NavigationNodeState endState)
            .Should().BeTrue();
        return CreateRequest(
            fixture.World,
            fixture.Store,
            fixture.Graph,
            NavigationAStarExitTestHarness.Profile(),
            startState.FootAnchor,
            endState.FootAnchor);
    }

    private static NavigationRayRequest CreateRequest(
        GridForge.Grids.GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        Vector3d start,
        Vector3d end,
        NavigationRayChainConstraint constraint = default,
        NavigationRayEndpointAllowance endpointAllowance =
            NavigationRayEndpointAllowance.None) => new(
        world,
        store,
        graph,
        profile,
        NavigationAStarExitTestHarness.Policy,
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        allowTransitions: false,
        start,
        end,
        endpointAllowance,
        constraint);

    private static NavigationRayResult RunRay(NavigationRayRequest request)
        => RunRay(request, CreateRayBudget(64, 64));

    private static NavigationRayResult RunRay(
        NavigationRayRequest request,
        NavigationWorkBudget budget)
    {
        var work = new NavigationRayWork(new NavigationRayWorkspace(4, 32, 32, 64, 64));
        var meter = new NavigationWorkMeter(budget);
        work.Begin(request);
        work.Advance(meter);
        return work.Result;
    }

    private static NavigationWorkBudget CreateRayBudget(
        int traceCapacity,
        int coveredCapacity,
        int connectionLegs = 1_024,
        int lookupProbes = 1_024,
        int evaluatedEdges = 1_024) => new(
        maxLookupProbes: lookupProbes,
        maxEndpointCandidates: 0,
        maxExpandedNodes: 0,
        maxEvaluatedEdges: evaluatedEdges,
        maxConnectionLegs: connectionLegs,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: traceCapacity,
        maxCoveredVoxelIntervals: coveredCapacity,
        maxSimplificationRays: 0);

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int frame = 0;
            frame < 1_024 && receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
    }

    private static void FindHexLine(
        NormalizedGridConfiguration binding,
        out VoxelIndex start,
        out VoxelIndex middle,
        out VoxelIndex end)
    {
        HexDirection[] directions =
        {
            HexDirection.QNegative,
            HexDirection.QNegativeRPositive,
            HexDirection.RNegative,
            HexDirection.RPositive,
            HexDirection.QPositiveRNegative,
            HexDirection.QPositive
        };
        for (int q = 0; q < binding.Width; q++)
        {
            for (int r = 0; r < binding.Length; r++)
            {
                var candidate = new VoxelIndex(q, 0, r);
                for (int direction = 0; direction < directions.Length; direction++)
                {
                    VoxelIndex offset = HexDirectionUtility.GetOffset(directions[direction]);
                    var next = new VoxelIndex(
                        candidate.x + offset.x,
                        candidate.y + offset.y,
                        candidate.z + offset.z);
                    var last = new VoxelIndex(
                        next.x + offset.x,
                        next.y + offset.y,
                        next.z + offset.z);
                    if (!binding.IsValidIndex(candidate)
                        || !binding.IsValidIndex(next)
                        || !binding.IsValidIndex(last))
                    {
                        continue;
                    }
                    start = candidate;
                    middle = next;
                    end = last;
                    return;
                }
            }
        }
        start = default;
        middle = default;
        end = default;
        throw new System.InvalidOperationException("The test grid has no three-cell hex ray.");
    }
}
