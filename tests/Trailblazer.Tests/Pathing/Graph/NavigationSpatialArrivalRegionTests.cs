using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationSpatialArrivalRegionTests
{
    private const string Map = "mine";
    private static readonly NavigationAreaPolicyKey PolicyKey = new(Map, 1);
    private static readonly NavigationWorkBudget Budget = new(4096, 128, 64, 128, 64, 0, 0, 0, 256, 256, 0);

    [Theory]
    [InlineData(PathAlgorithm.AStar, false, TraversalMedium.Solid)]
    [InlineData(PathAlgorithm.AStar, true, TraversalMedium.Solid)]
    [InlineData(PathAlgorithm.FlowField, false, TraversalMedium.Solid)]
    [InlineData(PathAlgorithm.FlowField, true, TraversalMedium.Solid)]
    [InlineData(PathAlgorithm.FlowField, false, TraversalMedium.Gas)]
    public void BlockedCenterSelectsReachableBoundaryInsteadOfCloserIsolatedGoal(
        PathAlgorithm algorithm, bool scheduled, TraversalMedium medium)
    {
        using var context = TrailblazerWorldContext.CreateOwned(settings: PlanarSchedulingTestSettings.Create());
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(5, 0, 0));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out var binding).Should().BeTrue();
        TraversalMedia media = medium == TraversalMedium.Solid ? TraversalMedia.Solid : TraversalMedia.Gas;
        var cell = GuidedPathTestScene.Cell(media);
        var startIndex = new VoxelIndex(0, 0, 0);
        var boundaryIndex = new VoxelIndex(1, 0, 0);
        var blockedIndex = new VoxelIndex(3, 0, 0);
        var isolatedIndex = new VoxelIndex(4, 0, 0);
        var builder = new NavigationMapBuilder(Map, binding)
            .AddCell(startIndex, cell).AddCell(boundaryIndex, cell).AddCell(isolatedIndex, cell);
        Publish(context, builder);
        var start = Anchor(startIndex);
        var end = Anchor(blockedIndex);
        var boundary = Anchor(boundaryIndex);
        PathQuery Query(Fixed64? radius) => CreateQuery(start, end, algorithm, medium, media, radius);

        AssertQuery(Query(null), NavigationGuideStatus.InvalidEnd);
        AssertQuery(Query(Fixed64.Zero), NavigationGuideStatus.InvalidEnd);
        AssertQuery(Query(Fixed64.Two - Fixed64.MinIncrement), NavigationGuideStatus.NoPath);
        AssertQuery(Query(Fixed64.Two), NavigationGuideStatus.Success);
        if (medium == TraversalMedium.Gas)
        {
            context.Guides.RequestFlowField(Query(Fixed64.Two + Fixed64.Half + Fixed64.Quarter), out var wider)
                .Should().Be(NavigationGuideStatus.Success);
            using var guide = wider!.Value;
            Vector3d inRegionOnPreviousCell = boundary - Vector3d.Right * (Fixed64.Half + Fixed64.Eighth);
            var exactQuery = CreateQuery(start, boundary, algorithm, medium, media, radius: null);
            context.Guides.RequestFlowField(exactQuery, out var exactResult).Should().Be(NavigationGuideStatus.Success);
            using var exact = exactResult!.Value;
            var exactBudget = context.Settings.GuideSampleBudget;
            var exactMeter = new GuideSampleWorkMeter(exactBudget);
            exact.TrySample(inRegionOnPreviousCell, ref exactMeter, out _).Should().Be(NavigationGuideStatus.Success);
            int traceIntervals = exactBudget.MaxTraceIntervals - exactMeter.GetTraceIntervalAllowance();
            var oneLegBudget = new GuideSampleWorkBudget(128, 128, 8, 32, 32, traceIntervals, 1);
            guide.TrySample(inRegionOnPreviousCell, oneLegBudget, out var approach)
                .Should().Be(NavigationGuideStatus.Success);
            approach.IsGoalApproach.Should().BeTrue();
            approach.IsGoalReached.Should().BeFalse();
        }

        Vector3d Anchor(VoxelIndex index)
        {
            binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
            Fixed64 footY = medium == TraversalMedium.Solid ? prism.VerticalMin : prism.Center.Y - Fixed64.Quarter;
            return new Vector3d(prism.Center.X, footY, prism.Center.Z);
        }

        void AssertQuery(PathQuery query, NavigationGuideStatus expected)
        {
            NavigationGuideLease? aStar = null;
            NavigationFlowFieldLease? flow = null;
            if (scheduled)
            {
                context.Guides.Schedule(query, out var queued).Should().Be(NavigationGuideStatus.Pending);
                using var request = queued!;
                for (int frame = 0; frame < 1024 && request.Status == NavigationGuideStatus.Pending; frame++)
                {
                    context.Simulate();
                    context.LateSimulate();
                }
                request.Status.Should().Be(expected);
                if (expected != NavigationGuideStatus.Success)
                    return;
                if (algorithm == PathAlgorithm.AStar)
                    request.TryTakeGuide(out aStar).Should().BeTrue();
                else
                    request.TryTakeFlowField(out flow).Should().BeTrue();
            }
            else if (algorithm == PathAlgorithm.AStar)
                context.Guides.RequestGuide(query, out aStar).Should().Be(expected);
            else
                context.Guides.RequestFlowField(query, out flow).Should().Be(expected);

            using var aStarLease = aStar;
            using var flowLease = flow;
            if (aStarLease is NavigationGuideLease guide)
            {
                for (int i = 1; i < guide.StepCount; i++)
                    guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
                guide.TryGetCurrentStep(out var step).Should().Be(NavigationGuideStatus.Success);
                step.Position.Should().Be(boundary);
            }
            if (flowLease is NavigationFlowFieldLease field)
            {
                var sampleBudget = context.Settings.GuideSampleBudget;
                if (medium == TraversalMedium.Gas)
                {
                    Vector3d insideFoot = boundary + Vector3d.Right * Fixed64.Eighth;
                    var noPrismChecks = new GuideSampleWorkBudget(128, 128, 8, 32, 0, 32, 1);
                    field.TrySample(insideFoot, noPrismChecks, out var unproved)
                        .Should().Be(NavigationGuideStatus.BudgetExceeded);
                    unproved.IsGoalReached.Should().BeFalse();
                    var noTrace = new GuideSampleWorkBudget(128, 128, 8, 32, 32, 0, 1);
                    field.TrySample(insideFoot, noTrace, out var exhausted).Should().Be(NavigationGuideStatus.BudgetExceeded);
                    exhausted.IsGoalReached.Should().BeFalse();
                    field.TrySample(insideFoot, sampleBudget, out var inside)
                        .Should().Be(NavigationGuideStatus.Success);
                    inside.Target.Should().Be(boundary);
                    inside.IsGoalReached.Should().BeTrue();
                    inside.Heading.Should().Be(Vector3d.Zero);
                    field.TrySample(boundary, sampleBudget, out var atAnchor).Should().Be(NavigationGuideStatus.Success);
                    atAnchor.IsGoalReached.Should().BeTrue();
                    field.TrySample(boundary - Vector3d.Right * Fixed64.Eighth, sampleBudget, out var outsideRegion)
                        .Should().Be(NavigationGuideStatus.Success);
                    outsideRegion.IsGoalReached.Should().BeFalse();
                    return;
                }
                field.TrySample(boundary - Vector3d.Right * Fixed64.MinIncrement, sampleBudget, out var outside)
                    .Should().Be(NavigationGuideStatus.Success);
                outside.Heading.Should().Be(Vector3d.Right);
                field.TrySample(boundary, sampleBudget, out var onBoundary).Should().Be(NavigationGuideStatus.Success);
                onBoundary.Target.Should().Be(boundary);
                onBoundary.Heading.Should().Be(Vector3d.Zero);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactFlowKeepsGuidingOneRawUnitShortOfItsAnchor(bool useZero)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out var binding).Should().BeTrue();
        var destination = new VoxelIndex(1, 0, 0);
        var cell = GuidedPathTestScene.Cell(TraversalMedia.Solid);
        Publish(context, new NavigationMapBuilder(Map, binding).AddCell(default, cell).AddCell(destination, cell));
        Vector3d start = GuidedPathTestScene.Anchor(binding, default);
        Vector3d target = GuidedPathTestScene.Anchor(binding, destination);
        PathQuery query = CreateQuery(start, target, PathAlgorithm.FlowField, TraversalMedium.Solid,
            TraversalMedia.Solid, useZero ? Fixed64.Zero : null, bodyRadius: Fixed64.Half);
        context.Guides.RequestFlowField(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var flow = acquired!.Value;
        flow.TrySample(target - Vector3d.Right * Fixed64.MinIncrement, context.Settings.GuideSampleBudget, out var shortSample)
            .Should().Be(NavigationGuideStatus.Success);
        shortSample.IsGoalApproach.Should().BeTrue();
        shortSample.IsGoalReached.Should().BeFalse();
        shortSample.Target.Should().Be(target);
        flow.TrySample(target, context.Settings.GuideSampleBudget, out var reached).Should().Be(NavigationGuideStatus.Success);
        reached.IsGoalReached.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitFlowAcceptsOnlyInRangeFeetAfterItsExit(bool reachesGoal)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(4, 0, 0));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out var binding).Should().BeTrue();
        VoxelIndex origin = default;
        var firstWitness = new VoxelIndex(1, 0, 0);
        var secondWitness = new VoxelIndex(2, 0, 0);
        var destination = new VoxelIndex(3, 0, 0);
        Vector3d start = GuidedPathTestScene.Anchor(binding, origin);
        Vector3d goal = GuidedPathTestScene.Anchor(binding, destination);
        Vector3d exit = goal - Vector3d.Right * Fixed64.Quarter;
        var connection = new NavigationConnection("shortcut", origin, new NavigationCellAddress(Map, destination),
            start + Vector3d.Right * Fixed64.Quarter, exit, Fixed64.Quarter, Fixed64.One,
            [new NavigationCellAddress(Map, firstWitness), new NavigationCellAddress(Map, secondWitness)]);
        var cell = GuidedPathTestScene.Cell(TraversalMedia.Solid);
        var witness = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            (Fixed64)100, Fixed64.One, Fixed64.One);
        Publish(context, new NavigationMapBuilder(Map, binding).AddCell(origin, cell).AddCell(firstWitness, witness)
            .AddCell(secondWitness, witness).AddCell(destination, cell).AddConnection(connection));
        Vector3d requestedCenter = GuidedPathTestScene.Anchor(binding, new VoxelIndex(4, 0, 0));
        PathQuery query = CreateQuery(start, requestedCenter, PathAlgorithm.FlowField, TraversalMedium.Solid,
            TraversalMedia.Solid, reachesGoal ? Fixed64.One + Fixed64.Half : Fixed64.One);
        context.Guides.RequestFlowField(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var flow = acquired!.Value;
        flow.TrySample(exit - Vector3d.Right * Fixed64.MinIncrement, context.Settings.GuideSampleBudget, out var before)
            .Should().Be(NavigationGuideStatus.Success);
        before.IsGoalApproach.Should().BeTrue();
        before.IsGoalReached.Should().BeFalse();
        before.Target.Should().Be(exit);
        flow.TrySample(goal - Vector3d.Right * Fixed64.Eighth, context.Settings.GuideSampleBudget, out var after)
            .Should().Be(NavigationGuideStatus.Success);
        after.IsGoalReached.Should().Be(reachesGoal);
        after.Target.Should().Be(goal);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, 1, 8, false, false)]
    [InlineData(PathAlgorithm.AStar, 2, 1, false, false)]
    [InlineData(PathAlgorithm.AStar, 2, 8, false, true)]
    [InlineData(PathAlgorithm.FlowField, 1, 8, true, true)]
    public void RegionalGoalsRespectAdmissionAndMediumStateCapacity(PathAlgorithm algorithm, int nodeCapacity,
        int componentCapacity, bool dualMedium, bool admits)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(3, 0, 0));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out var binding).Should().BeTrue();
        var startIndex = new VoxelIndex(0, 0, 0);
        var firstGoal = new VoxelIndex(1, 0, 0);
        var secondGoal = new VoxelIndex(3, 0, 0);
        TraversalMedia media = dualMedium ? TraversalMedia.Solid | TraversalMedia.Gas : TraversalMedia.Solid;
        var builder = new NavigationMapBuilder(Map, binding)
            .AddCell(startIndex, GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .AddCell(firstGoal, GuidedPathTestScene.Cell(media));
        if (!dualMedium)
            builder.AddCell(secondGoal, GuidedPathTestScene.Cell(media));
        Publish(context, builder);
        var target = dualMedium ? firstGoal : new VoxelIndex(2, 0, 0);
        PathQuery query = CreateQuery(GuidedPathTestScene.Anchor(binding, startIndex), GuidedPathTestScene.Anchor(binding, target),
            algorithm, TraversalMedium.Solid, media, dualMedium ? Fixed64.Half : Fixed64.One, allowTransitions: dualMedium);
        var aStar = algorithm == PathAlgorithm.AStar
            ? new NavigationAStarWorkspace(1, 8, componentCapacity, nodeCapacity, 16, 16, 4)
            : null;
        var flow = algorithm == PathAlgorithm.FlowField
            ? new NavigationFlowFieldWorkspace(1, 8, componentCapacity, nodeCapacity, 16, 16)
            : null;
        var store = context.Pathing.NavigationGraphStore;
        var endpointWorkspace = aStar?.EndpointWorkspace ?? flow!.EndpointWorkspace;
        var rayWorkspace = aStar?.RayWorkspace ?? flow!.RayWorkspace;
        using var admission = new NavigationQueryAdmissionWork(context.World, store, endpointWorkspace, rayWorkspace, algorithm);
        admission.Begin(store.TryAcquire()!, query, TraversalMedium.Solid, media);
        for (int i = 0; i < 128 && admission.Status == NavigationQueryAdmissionStatus.Pending; i++)
            admission.Advance(64, 1);

        if (!admits)
        {
            admission.Status.Should().Be(NavigationQueryAdmissionStatus.CapacityExceeded);
            endpointWorkspace.Goals[0].Address.MapId.Should().BeNull("failed admission releases its retained goal slots");
            return;
        }

        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var resolved = admission.Result;
        if (aStar != null)
        {
            var work = new NavigationSurfaceAStarWork(context.World, store, resolved, aStar, admission.RayWork, long.MaxValue);
            work.Advance(64, 1, 64, 64).Should().Be(NavigationSurfaceAStarStatus.Pending);
            work.Advance(64, 1, 64, 64).Should().Be(NavigationSurfaceAStarStatus.CapacityExceeded);
        }
        else
        {
            resolved.Goals[0].Media.Should().Be(media);
            var work = new NavigationFlowFieldWork(context.World, resolved, flow!);
            work.Advance(64, 1, 64, 64).Should().Be(NavigationFlowFieldStatus.CapacityExceeded);
        }
    }

    private static PathQuery CreateQuery(Vector3d start, Vector3d target, PathAlgorithm algorithm, TraversalMedium medium,
        TraversalMedia media, Fixed64? radius, bool allowTransitions = false, Fixed64? bodyRadius = null) =>
        new(new NavigationEndpoint(start, Map), new NavigationEndpoint(target, Map), new NavigationAgentProfile(
            new KinematicBodyShape(bodyRadius ?? Fixed64.Eighth, Fixed64.Half, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, media, TraversalCapability.None), PolicyKey, new TraversalIntent(medium, media),
            algorithm, Budget, allowTransitions: allowTransitions, arrivalRadius: radius);

    private static void Publish(TrailblazerWorldContext context, NavigationMapBuilder builder) =>
        GuidedPathTestScene.PublishMapAndPolicy(context, builder.Build(), 1, OverlayReplacementPolicy.Clear, 2,
            new NavigationAreaPolicy(PolicyKey, [new NavigationAreaRule(true, Fixed64.Zero)]), 1);
}
