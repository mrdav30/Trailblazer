using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class FlowFieldTransitionFallbackTests : IDisposable
{
    public FlowFieldTransitionFallbackTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FlowFieldRequest_ShouldUseTransitionFallback_ForDisconnectedJumpLink()
    {
        RegisterTwoPointChart("JumpStart", Vector3d.Zero);
        RegisterTwoPointChart("JumpEnd", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "jump-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One));
        request.AllowTraversalTransitions = true;

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d toJumpSource).Should().BeTrue();
        toJumpSource.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 0, 0), out Vector3d acrossGap).Should().BeTrue();
        acrossGap.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d toGoal).Should().BeTrue();
        toGoal.X.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void FlowFieldRequest_ShouldReuseTransitionFallbackPlan_ForSecondIdenticalRequest()
    {
        RegisterTwoPointChart("WarmJumpStart", Vector3d.Zero);
        RegisterTwoPointChart("WarmJumpEnd", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "warm-jump-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        FlowFieldGuide firstGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdFirstGuide),
            createdFirstGuide);
        firstGuide.IsStaged.Should().BeTrue();
        PathGuideFactory.ReturnGuide(firstGuide);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        FlowFieldGuide secondGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdSecondGuide),
            createdSecondGuide);
        secondGuide.IsStaged.Should().BeTrue();
        PathGuideFactory.ReturnGuide(secondGuide);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(2_048);
    }

    [Fact]
    public void FlowFieldRequest_ShouldNotReuseFallbackPlan_FromDifferentOrigin()
    {
        RegisterTwoPointChart("OriginSpecificA", new Vector3d(-4, 0, 0));
        RegisterTwoPointChart("OriginSpecificB", Vector3d.Zero);
        RegisterTwoPointChart("OriginSpecificDestination", new Vector3d(4, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "origin-specific-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(-3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)))).Should().BeTrue();
        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "origin-specific-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)))).Should().BeTrue();

        FlowFieldPathRequest firstRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            new Vector3d(-4, 0, 0),
            new Vector3d(5, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));
        FlowFieldGuide firstGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(firstRequest, out FlowFieldGuide? createdFirstGuide),
            createdFirstGuide);
        PathGuideFactory.ReturnGuide(firstGuide);

        FlowFieldPathRequest secondRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(5, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));
        secondRequest.RequestCacheKey.Should().Be(firstRequest.RequestCacheKey,
            "flow-field survey equality remains destination-centric");
        FlowFieldGuide secondGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(secondRequest, out FlowFieldGuide? createdSecondGuide),
            createdSecondGuide);

        secondGuide.TryGetMovementDirection(Vector3d.Zero, out Vector3d towardOwnTransition).Should().BeTrue();
        towardOwnTransition.X.Should().BeGreaterThan(Fixed64.Zero);
        PathGuideFactory.TotalHybridRoutePlanCount.Should().Be(2);
    }

    [Fact]
    public void FlowFieldFallback_ShouldNotReusePlan_WhenExactPointsDifferWithinSameVoxels()
    {
        RegisterTwoPointChart("ExactPointStart", Vector3d.Zero);
        RegisterTwoPointChart("ExactPointEnd", new Vector3d(3, 0, 0));
        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "exact-point-gap",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)))).Should().BeTrue();

        Vector3d firstOrigin = Vector3d.FromDouble(0.1, 0, 0);
        Vector3d changedOrigin = Vector3d.FromDouble(0.2, 0, 0);
        Vector3d firstTarget = Vector3d.FromDouble(4.1, 0, 0);
        Vector3d changedTarget = Vector3d.FromDouble(4.2, 0, 0);
        FlowFieldPathRequest firstRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            firstOrigin,
            firstTarget,
            Fixed64.One,
            allowTraversalTransitions: true));
        FlowFieldPathRequest changedOriginRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            changedOrigin,
            firstTarget,
            Fixed64.One,
            allowTraversalTransitions: true));
        FlowFieldPathRequest changedTargetRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            firstOrigin,
            changedTarget,
            Fixed64.One,
            allowTraversalTransitions: true));

        changedOriginRequest.StartNode!.WorldIndex.Should().Be(firstRequest.StartNode!.WorldIndex);
        changedTargetRequest.EndNode!.WorldIndex.Should().Be(firstRequest.EndNode!.WorldIndex);
        changedTargetRequest.RequestCacheKey.Should().Be(firstRequest.RequestCacheKey,
            "normal flow-field survey equality remains voxel-based and destination-centric");
        changedOriginRequest.HybridFallbackCacheKey.Should().NotBe(firstRequest.HybridFallbackCacheKey);
        changedTargetRequest.HybridFallbackCacheKey.Should().NotBe(firstRequest.HybridFallbackCacheKey);

        HybridPathRequest firstHybrid = TestRequire.NotNull(HybridPathRequest.CreateFromFlowField(firstRequest));
        HybridPathRequest changedOriginHybrid = TestRequire.NotNull(
            HybridPathRequest.CreateFromFlowField(changedOriginRequest));
        HybridPathRequest changedTargetHybrid = TestRequire.NotNull(
            HybridPathRequest.CreateFromFlowField(changedTargetRequest));
        changedOriginHybrid.RequestCacheKey.Should().NotBe(firstHybrid.RequestCacheKey);
        changedTargetHybrid.RequestCacheKey.Should().NotBe(firstHybrid.RequestCacheKey);

        FlowFieldGuide firstGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(firstRequest, out FlowFieldGuide? createdFirstGuide),
            createdFirstGuide);
        PathGuideFactory.ReturnGuide(firstGuide);

        FlowFieldGuide changedOriginGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(changedOriginRequest, out FlowFieldGuide? createdOriginGuide),
            createdOriginGuide);
        HybridRoutePlan originPlan = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<HybridRoutePlan?>(changedOriginGuide, "_stagedPlan"));
        originPlan.Steps[0].SegmentRequest.Origin.Should().Be(changedOrigin);
        PathGuideFactory.ReturnGuide(changedOriginGuide);

        FlowFieldGuide changedTargetGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(changedTargetRequest, out FlowFieldGuide? createdTargetGuide),
            createdTargetGuide);
        HybridRoutePlan targetPlan = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<HybridRoutePlan?>(changedTargetGuide, "_stagedPlan"));
        targetPlan.Steps[^1].SegmentRequest.TargetPosition.Should().Be(changedTarget);
        PathGuideFactory.TotalHybridRoutePlanCount.Should().Be(3);
    }

    [Fact]
    public void HybridRequestKey_ShouldChange_WhenSameTransitionIdIsReregistered()
    {
        RegisterTwoPointChart("RegistryStart", Vector3d.Zero);
        RegisterTwoPointChart("RegistryEnd", new Vector3d(3, 0, 0));
        const string transitionId = "registry-sensitive";

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: transitionId,
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
        FlowFieldPathRequest firstFlowRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));
        HybridPathRequest firstHybridRequest = TestRequire.NotNull(
            HybridPathRequest.CreateFromFlowField(firstFlowRequest));
        PathRequestCacheKey firstKey = firstHybridRequest.RequestCacheKey;

        TraversalTransitionRegistry.Unregister(transitionId).Should().BeTrue();
        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: transitionId,
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 9)).Should().BeTrue();
        FlowFieldPathRequest secondFlowRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));
        HybridPathRequest secondHybridRequest = TestRequire.NotNull(
            HybridPathRequest.CreateFromFlowField(secondFlowRequest));

        firstHybridRequest.RoutePlan!.DirectedTransitions[0].Id.Should().Be(transitionId);
        secondHybridRequest.RoutePlan!.DirectedTransitions[0].Id.Should().Be(transitionId);
        firstHybridRequest.RequestCacheKey.Should().Be(firstKey,
            "a built hybrid request keeps its registry snapshot");
        secondHybridRequest.RequestCacheKey.Should().NotBe(firstKey);
    }

    [Fact]
    public void FlowFieldRequest_ShouldUseTransitionFallback_ForChartToWaterToChartRoute()
    {
        RegisterTwoPointChart("WaterStart", new Vector3d(-2, 0, 0));
        RegisterTwoPointChart("WaterEnd", new Vector3d(3, 0, 0));

        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(0, 0, 0));
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(1, 0, 0));
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(2, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "water-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(new Vector3d(-1, 0, 0)),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(0, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "water-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, new Vector3d(-2, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One));
        request.AllowTraversalTransitions = true;

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(new Vector3d(-2, 0, 0), out Vector3d toShoreline).Should().BeTrue();
        toShoreline.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(-1, 0, 0), out Vector3d intoWater).Should().BeTrue();
        intoWater.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(0, 0, 0), out Vector3d throughWater).Should().BeTrue();
        throughWater.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 0, 0), out Vector3d ontoChart).Should().BeTrue();
        ontoChart.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d toGoal).Should().BeTrue();
        toGoal.X.Should().BeGreaterThan(Fixed64.Zero);
    }

    [Fact]
    public void FlowFieldRequest_ShouldNotUseTransitionFallback_WhenOnlyAllowUnwalkableIsEnabled()
    {
        RegisterTwoPointChart("NoFallbackStart", Vector3d.Zero);
        RegisterTwoPointChart("NoFallbackEnd", new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "no-fallback-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            pathCostModifier: 4)).Should().BeTrue();

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        request.AllowTraversalTransitions.Should().BeFalse();

        PathGuideFactory.RequestGuide(request, out FlowFieldGuide? guide).Should().BeFalse();
        guide.Should().BeNull();
    }

    [Fact]
    public void FlowFieldRequest_ShouldUseTransitionFallback_ForGeneratedClimbTopology()
    {
        PathTestFactory.RegisterAuthoredClimbRoute(TestWorld.Context, "FlowFieldClimbFallback");

        FlowFieldPathRequest request = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(3, 0, 1),
            Fixed64.One,
            allowUnwalkableEndpoints: true));
        request.MaxClimbHeight = Fixed64.Zero;
        request.AllowTraversalTransitions = true;

        FlowFieldGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out FlowFieldGuide? createdGuide),
            createdGuide);
        guide.IsStaged.Should().BeTrue();

        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d toSeam).Should().BeTrue();
        toSeam.X.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 0, 0), out Vector3d upWall).Should().BeTrue();
        upWall.Y.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(1, 1, 0), out Vector3d aroundCorner).Should().BeTrue();
        aroundCorner.Z.Should().BeGreaterThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 1, 1), out Vector3d towardExit).Should().BeTrue();
        towardExit.Y.Should().BeLessThan(Fixed64.Zero);

        guide.TryGetMovementDirection(new Vector3d(2, 0, 1), out Vector3d toGoal).Should().BeTrue();
        toGoal.X.Should().BeGreaterThan(Fixed64.Zero);
    }

    private static void RegisterTwoPointChart(string chartName, Vector3d minBounds)
    {
        bool[,,] data = new bool[1, 2, 1];
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;
        PathTestFactory.RegisterFromData(TestWorld.Context, chartName, data, minBounds);
    }

}
