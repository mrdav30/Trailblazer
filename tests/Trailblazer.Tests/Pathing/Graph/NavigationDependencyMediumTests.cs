using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationDependencyMediumTests
{
    private const string MapId = "shared-representative";
    private const int RemoteCellX = 128;
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("dependency-medium", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void Recording_ShouldRetainEachMediumAtOneRepresentativeAndDeduplicateExactKeys()
    {
        var workspace = new NavigationDependencyWorkspace(0, 3);
        NavigationSurfaceComponentKey solid = Component(TraversalMedium.Solid);
        NavigationSurfaceComponentKey gas = Component(TraversalMedium.Gas);
        NavigationSurfaceComponentKey liquid = Component(TraversalMedium.Liquid);

        workspace.TryRecordComponent(solid).Should().BeTrue();
        workspace.TryRecordComponent(gas).Should().BeTrue();
        workspace.TryRecordComponent(liquid).Should().BeTrue();
        workspace.ComponentCount.Should().Be(3);
        workspace.Components.Should().Equal(solid, gas, liquid);

        workspace.TryRecordComponent(gas).Should().BeTrue();
        workspace.TryRecordComponent(liquid).Should().BeTrue();
        workspace.TryRecordComponent(solid).Should().BeTrue();
        workspace.ComponentCount.Should().Be(3,
            "only an exact address-medium duplicate is free at capacity");

        workspace.Reset();
        workspace.TryRecordComponent(liquid).Should().BeTrue();
        workspace.ComponentCount.Should().Be(1);
        workspace.Components[0].Should().Be(liquid);
    }

    [Theory]
    [InlineData(TraversalMedium.Solid, TraversalMedium.Gas)]
    [InlineData(TraversalMedium.Gas, TraversalMedium.Liquid)]
    [InlineData(TraversalMedium.Liquid, TraversalMedium.Solid)]
    public void FullCapacity_ShouldRejectAnotherMediumWithoutReplacingTheRecordedKey(
        TraversalMedium recordedMedium,
        TraversalMedium additionalMedium)
    {
        var workspace = new NavigationDependencyWorkspace(0, 1);
        NavigationSurfaceComponentKey recorded = Component(recordedMedium);
        workspace.TryRecordComponent(recorded).Should().BeTrue();

        workspace.TryRecordComponent(Component(additionalMedium)).Should().BeFalse(
            "a different medium is another component even at the same representative");

        workspace.TryRecordComponent(recorded).Should().BeTrue();
        workspace.ComponentCount.Should().Be(1);
        workspace.Components[0].Should().Be(recorded);
    }

    [Fact]
    public void Union_ShouldCountAndCommitAnotherMediumAtTheSameRepresentative()
    {
        var target = new NavigationDependencyWorkspace(0, 2);
        var source = new NavigationDependencyWorkspace(0, 1);
        NavigationSurfaceComponentKey solid = Component(TraversalMedium.Solid);
        NavigationSurfaceComponentKey gas = Component(TraversalMedium.Gas);
        target.TryRecordComponent(solid).Should().BeTrue();
        source.TryRecordComponent(gas).Should().BeTrue();
        var meter = new NavigationWorkMeter(LookupBudget(1));

        target.TryCountMissing(source, meter, out int components, out int pages)
            .Should().BeTrue();

        components.Should().Be(1);
        pages.Should().Be(0);
        target.CanFit(components, pages).Should().BeTrue();
        target.CommitMerge(source);
        target.ComponentCount.Should().Be(2);
        target.Components.Should().Equal(solid, gas);
    }

    [Fact]
    public void Union_ShouldRejectAnUnfittableMediumBeforeMutatingTarget()
    {
        var target = new NavigationDependencyWorkspace(0, 1);
        var source = new NavigationDependencyWorkspace(0, 1);
        NavigationSurfaceComponentKey solid = Component(TraversalMedium.Solid);
        target.TryRecordComponent(solid).Should().BeTrue();
        source.TryRecordComponent(Component(TraversalMedium.Gas)).Should().BeTrue();
        var meter = new NavigationWorkMeter(LookupBudget(16));

        NavigationSearchFinalizationRules.TryPrepareDependencyMerge(
                epochIsCurrent: true,
                target,
                source,
                meter,
                priorLookupReservation: 0,
                out _)
            .Should().BeFalse("the second medium requires its own component slot");

        target.ComponentCount.Should().Be(1);
        target.Components[0].Should().Be(solid);
    }

    [Fact]
    public void AStarGuide_ShouldInvalidateWhenOnlyTheSharedRepresentativeGasComponentChanges()
    {
        using TrailblazerWorldContext context = CreateContext(out NormalizedGridConfiguration binding);
        PathQuery query = Query(binding, PathAlgorithm.AStar);
        context.Guides.RequestGuide(query, out NavigationGuideLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        acquired.HasValue.Should().BeTrue();
        using NavigationGuideLease guide = acquired!.Value;
        NavigationGuideStep transition = GuidedPathTestScene.AdvanceToTransition(guide);
        transition.Transition.DestinationMedium.Should().Be(TraversalMedium.Gas);

        PublishRemoteGasChange(context);

        guide.TryGetCurrentStep(out _).Should().Be(NavigationGuideStatus.Stale,
            "the Gas component generation changed even though the Solid component shares its representative");
    }

    [Fact]
    public void FlowGuide_ShouldInvalidateWhenOnlyTheSharedRepresentativeGasComponentChanges()
    {
        using TrailblazerWorldContext context = CreateContext(out NormalizedGridConfiguration binding);
        PathQuery query = Query(binding, PathAlgorithm.FlowField);
        context.Guides.RequestFlowField(query, out NavigationFlowFieldLease? acquired)
            .Should().Be(NavigationGuideStatus.Success);
        acquired.HasValue.Should().BeTrue();
        using NavigationFlowFieldLease guide = acquired!.Value;
        guide.OriginIntegrationCost.Should().Be(Fixed64.One);
        guide.Status.Should().Be(NavigationGuideStatus.Success);

        PublishRemoteGasChange(context);

        guide.Status.Should().Be(NavigationGuideStatus.Stale,
            "bounded Flow coverage still depends on the full exact Gas component");
    }

    private static NavigationSurfaceComponentKey Component(TraversalMedium medium) =>
        new(new NavigationCellAddress(MapId, default), medium);

    private static NavigationWorkBudget LookupBudget(int probes) =>
        new(probes, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static TrailblazerWorldContext CreateContext(
        out NormalizedGridConfiguration binding)
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        try
        {
            var configuration = new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(RemoteCellX, 0, 0),
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            context.World.TryAddGrid(configuration, out _).Should().BeTrue();
            configuration.TryNormalize(out binding).Should().BeTrue();
            var builder = new NavigationMapBuilder(MapId, binding)
                .AddCell(default, GuidedPathTestScene.Cell(TraversalMedia.Solid | TraversalMedia.Gas));
            for (int x = 1; x <= RemoteCellX; x++)
                builder.AddCell(new VoxelIndex(x, 0, 0), GuidedPathTestScene.Cell(TraversalMedia.Gas));
            builder.AddTransition(new TraversalTransitionDefinition(
                "takeoff",
                TraversalTransitionType.Takeoff,
                default,
                TraversalMedium.Solid,
                new NavigationCellAddress(MapId, new VoxelIndex(1, 0, 0)),
                TraversalMedium.Gas,
                actionCost: Fixed64.One));
            GuidedPathTestScene.PublishMapAndPolicy(
                context,
                builder.Build(),
                bakeVersion: 1,
                OverlayReplacementPolicy.Clear,
                mapSequence: 1,
                Policy,
                policySequence: 2);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static PathQuery Query(NormalizedGridConfiguration binding, PathAlgorithm algorithm)
    {
        Vector3d start = GuidedPathTestScene.Anchor(binding, default);
        binding.TryGetCellPrism(new VoxelIndex(1, 0, 0), out GridCellPrism destination)
            .Should().BeTrue();
        var end = new Vector3d(
            destination.Center.X,
            destination.Center.Y - Fixed64.Quarter,
            destination.Center.Z);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.Half, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Gas,
            TraversalCapability.None);
        return new PathQuery(
            new NavigationEndpoint(start, MapId),
            new NavigationEndpoint(end, MapId),
            profile,
            Policy.Key,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            algorithm,
            new NavigationWorkBudget(
                maxLookupProbes: 8_192,
                maxEndpointCandidates: 32,
                maxExpandedNodes: 32,
                maxEvaluatedEdges: 1_024,
                maxConnectionLegs: 1_024,
                maxTransitionCandidates: 1_024,
                maxTransitionPairs: 1_024,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 1_024,
                maxCoveredVoxelIntervals: 1_024,
                maxSimplificationRays: 0),
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.Zero));
    }

    private static void PublishRemoteGasChange(TrailblazerWorldContext context)
    {
        ulong worldSequence = context.World.ChangeSequence;
        GraphComponentDependency solid;
        GraphComponentDependency gas;
        GraphPageDependency localPage;
        long transitionRules;
        using (NavigationWorldGraphLease before = context.Pathing.TryAcquireNavigationGraph()!)
        {
            before.Graph.TryGetComponentDependency(Component(TraversalMedium.Solid), out solid)
                .Should().BeTrue();
            before.Graph.TryGetComponentDependency(Component(TraversalMedium.Gas), out gas)
                .Should().BeTrue("both media have the same representative in this fixture");
            before.Graph.TryGetPageDependency(
                    new GraphPageDependencyAddress(MapId, 0),
                    includeTransitionPage: true,
                    out localPage)
                .Should().BeTrue();
            transitionRules = before.Graph.TransitionRules.Version;
        }

        // The changed cell is on semantic page 2, far beyond either short query's coverage.
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(MapId, new[]
                {
                    NavigationCellOverlayOperation.Suppress(new VoxelIndex(RemoteCellX, 0, 0))
                })
            })),
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, overlay.Receipt);

        using NavigationWorldGraphLease after = context.Pathing.TryAcquireNavigationGraph()!;
        after.Graph.TryGetComponentDependency(solid.Key, out GraphComponentDependency currentSolid)
            .Should().BeTrue();
        after.Graph.TryGetComponentDependency(gas.Key, out GraphComponentDependency currentGas)
            .Should().BeTrue();
        currentSolid.Should().Be(solid);
        currentGas.Should().NotBe(gas,
            "removing the distant Gas member must rebuild its exact component");
        after.Graph.TryGetPageDependency(
                new GraphPageDependencyAddress(MapId, 0),
                includeTransitionPage: true,
                out GraphPageDependency currentLocalPage)
            .Should().BeTrue();
        currentLocalPage.Should().Be(localPage,
            "semantic, physical and transition-page checks must not mask the component regression");
        after.Graph.TransitionRules.Version.Should().Be(transitionRules);
        context.World.ChangeSequence.Should().Be(worldSequence,
            "this semantic overlay must not be masked by native-world invalidation");
    }
}
