using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using System.Collections.Generic;
using System.Reflection;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation.Steering;
using Trailblazer.Tests.Navigation.Turning;
using Xunit;

namespace Trailblazer.Tests;

[Collection("TrailblazerCollection")]
public class TrailblazerWorldContextLifecycleTests : IDisposable
{
    private const int DefaultFrameRate = 32;

    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Simulate_ShouldInvokeOrderedHooksInAscendingOrder()
    {
        TestWorld.Setup();
        var calls = new List<string>();

        using IDisposable late = TestWorld.Context.RegisterOnSimulate(
            owner: "TrailblazerWorldContextLifecycleTests.Simulate.Late",
            order: 900,
            callback: () => calls.Add("late"));
        using IDisposable early = TestWorld.Context.RegisterOnSimulate(
            owner: "TrailblazerWorldContextLifecycleTests.Simulate.Early",
            order: -900,
            callback: () => calls.Add("early"));

        TestWorld.Context.Simulate();

        calls.Should().ContainInOrder("early", "late");
    }

    [Fact]
    public void Setup_ShouldReplaceTheSelectedTestContext()
    {
        TestWorld.Setup();
        TrailblazerWorldContext first = TestWorld.Context;
        using IDisposable ignored = first.RegisterOnSimulate("first", 0, () => { });

        TestWorld.Setup();

        TestWorld.Context.Should().NotBeSameAs(first);
        first.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void SetFrameRate_ShouldRefreshNavSteeringAutoStopThresholdAfterTypeInitialization()
    {
        TestWorld.Setup();
        var steering = new NavSteering(TestWorld.Context, Fixed64.One);
        var agent = new MockSteerAgent();

        TestWorld.Context.SetFrameRate(64);
        steering.PauseAutoStop();

        for (int i = 0; i < 7; i++)
            steering.GetHeading(agent);

        steering.CanAutoStop.Should().BeFalse();

        steering.GetHeading(agent);

        steering.CanAutoStop.Should().BeTrue();
    }

    [Fact]
    public void SetFrameRate_ShouldRefreshNavTurningCollisionThresholdAfterInitialization()
    {
        TestWorld.Setup();
        TestWorld.Context.SetFrameRate(DefaultFrameRate);

        var turning = new NavTurning(TestWorld.Context, Fixed64.One);
        var navigator = new MockTurnAgent
        {
            LastPosition = Vector3d.Zero,
            Position = new Vector3d(Fixed64.Zero, (Fixed64)0.01f, Fixed64.Zero),
            Forward = Vector3d.Right,
            Rotation = FixedQuaternion.Identity
        };

        TestWorld.Context.SetFrameRate(64);

        turning.NotifyCollision();
        turning.TrySimulateTurn(navigator.Position, navigator.LastPosition, navigator.Forward, navigator.Rotation, out _);
        turning.TrySimulateTurn(navigator.Position, navigator.LastPosition, navigator.Forward, navigator.Rotation, out _);

        turning.TargetReached.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetFrameRate_ShouldRejectNonPositiveValuesAndPreserveCurrentTiming(int invalidFrameRate)
    {
        TestWorld.Setup();
        TestWorld.Context.SetFrameRate(DefaultFrameRate);

        Action act = () => TestWorld.Context.SetFrameRate(invalidFrameRate);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("frameRate");
        TestWorld.Context.FrameRate.Should().Be(DefaultFrameRate);
        TestWorld.Context.DeltaTime.Should().Be(Fixed64.One / (Fixed64)DefaultFrameRate);
    }

    [Fact]
    public void LateSimulateAndVisualize_ShouldInvokeHooksAndTrackAccumulation()
    {
        TestWorld.Setup();
        int lateCalls = 0;
        int visualizeCalls = 0;

        using IDisposable late = TestWorld.Context.RegisterOnLateSimulate(
            owner: "TrailblazerWorldContextLifecycleTests.Late",
            order: 0,
            callback: () => lateCalls++);
        using IDisposable visualize = TestWorld.Context.RegisterOnVisualize(
            owner: "TrailblazerWorldContextLifecycleTests.Visualize",
            order: 0,
            callback: () => visualizeCalls++);

        TestWorld.Context.LateSimulate();

        lateCalls.Should().Be(1);
        TestWorld.Context.ResetAccumulation.Should().BeTrue();

        TestWorld.Context.Visualize();
        TestWorld.Context.Visualize();

        visualizeCalls.Should().Be(2);
        TestWorld.Context.ResetAccumulation.Should().BeFalse();
        TestWorld.Context.AccumulatedTime.Should().Be(TestWorld.Context.DeltaTime * 2);
        TestWorld.Context.ExpectedAccumulation.Should().Be((Fixed64)2);
    }

    [Fact]
    public void ResetAndFrameRateChanged_ShouldInvokeRegisteredHooks()
    {
        TestWorld.Setup();
        int resetCalls = 0;
        int frameRateCalls = 0;

        using IDisposable reset = TestWorld.Context.RegisterOnReset(
            owner: "TrailblazerWorldContextLifecycleTests.Reset",
            order: 0,
            callback: () => resetCalls++);
        using IDisposable frameRate = TestWorld.Context.RegisterOnFrameRateChanged(
            owner: "TrailblazerWorldContextLifecycleTests.FrameRate",
            order: 0,
            callback: () => frameRateCalls++);

        TestWorld.Context.Simulate();
        TestWorld.Context.SetFrameRate(48);

        frameRateCalls.Should().Be(1);
        TestWorld.Context.FrameRate.Should().Be(48);
        TestWorld.Context.DeltaTime.Should().Be(Fixed64.One / (Fixed64)48);

        TestWorld.Context.Reset();

        resetCalls.Should().Be(1);
        TestWorld.Context.FrameCount.Should().Be(0);
        TestWorld.Context.TotalTime.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void PathingReset_ShouldClearOnlyOwningPathingState()
    {
        typeof(TrailblazerPathingService)
            .GetMethod(nameof(TrailblazerPathingService.Reset), BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .NotBeNull("context.Pathing.Reset() is the host-facing pathing teardown API");

        using TrailblazerWorldContext contextA = CreateContextWithGrid();
        using TrailblazerWorldContext contextB = CreateContextWithGrid();
        RegisterSolidLine(contextA, "LifecycleResetChartA", Vector3d.Zero, 2);
        RegisterSolidLine(contextB, "LifecycleResetChartB", Vector3d.Zero, 2);
        RegisterSolidPoint(contextA, "LifecycleReachabilityStartA", new Vector3d(0, 2, 0));
        RegisterSolidPoint(contextA, "LifecycleReachabilityEndA", new Vector3d(3, 2, 0));
        RegisterSolidPoint(contextB, "LifecycleReachabilityStartB", new Vector3d(0, 2, 0));
        RegisterSolidPoint(contextB, "LifecycleReachabilityEndB", new Vector3d(3, 2, 0));
        contextA.Transitions.Register(CreateJumpTransition(contextA, "lifecycle-reset-transition-a"))
            .Should()
            .BeTrue();
        contextB.Transitions.Register(CreateJumpTransition(contextB, "lifecycle-reset-transition-b"))
            .Should()
            .BeTrue();
        contextA.VolumeRules.SetGasVoxelRule(static _ => true);
        contextB.VolumeRules.SetGasVoxelRule(static _ => true);
        contextA.Guides.TrySeedAStarCacheForBenchmark(1111, new[] { "LifecycleResetChartA" }, checkout: false)
            .Should()
            .BeTrue();
        contextB.Guides.TrySeedAStarCacheForBenchmark(2222, new[] { "LifecycleResetChartB" }, checkout: false)
            .Should()
            .BeTrue();

        contextA.Guides.RequestGuide(
            CreateAStarRequest(contextA, new Vector3d(0, 2, 0), new Vector3d(3, 2, 0)),
            out AStarGuide? contextAGuide).Should().BeFalse();
        contextB.Guides.RequestGuide(
            CreateAStarRequest(contextB, new Vector3d(0, 2, 0), new Vector3d(3, 2, 0)),
            out AStarGuide? contextBGuide).Should().BeFalse();
        contextAGuide.Should().BeNull();
        contextBGuide.Should().BeNull();
        SolidPartitionReachability.SolidPartitionReachabilityStats contextAReachabilityBefore =
            contextA.Guides.CaptureReachabilityStats();
        SolidPartitionReachability.SolidPartitionReachabilityStats contextBReachabilityBefore =
            contextB.Guides.CaptureReachabilityStats();

        contextA.Pathing.Reset();

        contextA.Pathing.IsChartRegistered("LifecycleResetChartA").Should().BeFalse();
        contextA.Transitions.IsRegistered("lifecycle-reset-transition-a").Should().BeFalse();
        contextA.VolumeRules.HasGasVoxelRule.Should().BeFalse();
        contextA.Guides.TotalAStarGuideCount.Should().Be(0);
        contextA.Guides.CaptureReachabilityStats().Version.Should().BeGreaterThan(contextAReachabilityBefore.Version);

        contextB.Pathing.IsChartRegistered("LifecycleResetChartB").Should().BeTrue();
        contextB.Transitions.IsRegistered("lifecycle-reset-transition-b").Should().BeTrue();
        contextB.VolumeRules.HasGasVoxelRule.Should().BeTrue();
        contextB.Guides.TotalAStarGuideCount.Should().Be(1);
        contextB.Guides.CaptureReachabilityStats().SnapshotBuildCount
            .Should()
            .Be(contextBReachabilityBefore.SnapshotBuildCount);
    }

    [Fact]
    public void Dispose_ShouldDisposePathingStateAndGuideCachesIdempotently()
    {
        TrailblazerWorldContext context = CreateContextWithGrid();
        PathingWorldState state = context.Pathing.State;
        AStarSurveyResult result = AStarSurveyResult.Create(
            context,
            new[] { new AStarWaypoint { Position = Vector3d.Zero, IsGoal = true } },
            new[] { "LifecycleDisposedCache" },
            key: 3333);
        context.Guides.TrySeedAStarCacheForBenchmark(3333, new[] { "LifecycleDisposedCache" }, checkout: false)
            .Should()
            .BeTrue();

        context.Dispose();
        context.Dispose();

        Action chartLockUse = () => state.NavigationChartMapLock.EnterReadLock();
        Action transitionLockUse = () => state.TransitionRegistryState.TransitionLock.EnterReadLock();

        chartLockUse.Should().Throw<ObjectDisposedException>();
        transitionLockUse.Should().Throw<ObjectDisposedException>();
        using (PathManager.EnterState(state))
        {
            Action guideCacheUse = () => state.GuideState.CachedAStarResults.TrySeed(result, checkout: false);
            guideCacheUse.Should().Throw<ObjectDisposedException>();
        }
    }

    [Fact]
    public void GetFrameFromTime_ShouldUseCurrentInverseDeltaTime()
    {
        TestWorld.Setup();
        TestWorld.Context.SetFrameRate(10);

        Fixed64 timestamp = (TestWorld.Context.DeltaTime * 3) + (TestWorld.Context.DeltaTime / 2);

        TestWorld.Context.GetFrameFromTime(timestamp).Should().Be(3);
    }

    [Fact]
    public void LifecycleHookRegistration_Dispose_ShouldBeIdempotent()
    {
        TestWorld.Setup();
        int callCount = 0;
        using IDisposable registration = TestWorld.Context.RegisterOnSimulate(
            owner: "TrailblazerWorldContextLifecycleTests.DoubleDispose",
            order: 0,
            callback: () => callCount++);

        registration.Dispose();
        registration.Dispose();
    }

    private static TrailblazerWorldContext CreateContextWithGrid()
    {
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        context.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        return context;
    }

    private static void RegisterSolidLine(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d minBounds,
        int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        context.Pathing.Register(NavigationChart.From3D(chartName, data, minBounds, Fixed64.One))
            .Should()
            .BeTrue();
    }

    private static void RegisterSolidPoint(
        TrailblazerWorldContext context,
        string chartName,
        Vector3d position)
    {
        var data = new bool[1, 1, 1]
        {
            {
                { true }
            }
        };

        context.Pathing.Register(NavigationChart.From3D(chartName, data, position, Fixed64.One))
            .Should()
            .BeTrue();
    }

    private static TraversalTransition CreateJumpTransition(TrailblazerWorldContext context, string id)
    {
        return new TraversalTransition(
            id,
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(RequireVoxel(context, Vector3d.Zero).WorldIndex),
            TraversalTransitionAnchor.Solid(RequireVoxel(context, new Vector3d(1, 0, 0)).WorldIndex),
            pathCostModifier: 1);
    }

    private static AStarPathRequest CreateAStarRequest(
        TrailblazerWorldContext context,
        Vector3d source,
        Vector3d destination)
    {
        return TestRequire.NotNull(AStarPathRequest.Create(context, source, destination, Fixed64.One));
    }

    private static Voxel RequireVoxel(TrailblazerWorldContext context, Vector3d position)
    {
        context.World.TryGetVoxel(position, out Voxel? voxel).Should().BeTrue();
        return TestRequire.NotNull(voxel);
    }
}
