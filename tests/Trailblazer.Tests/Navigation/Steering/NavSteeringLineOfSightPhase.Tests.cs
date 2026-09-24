using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

[Collection("PathingCollection")]
public sealed partial class NavSteeringLineOfSightPhaseTests : IDisposable
{
    private const string MapId = "steering-los-phase";
    private static readonly Guid FirstReplayId =
        new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondReplayId =
        new("22222222-2222-2222-2222-222222222222");

    public NavSteeringLineOfSightPhaseTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
    }

    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, 1, 0, 2)]
    [InlineData(PathAlgorithm.FlowField, 1, 0, 2)]
    [InlineData(PathAlgorithm.AStar, 2, 0, 2)]
    [InlineData(PathAlgorithm.FlowField, 2, 0, 2)]
    [InlineData(PathAlgorithm.AStar, 2, 1, 3)]
    [InlineData(PathAlgorithm.FlowField, 2, 1, 3)]
    [InlineData(PathAlgorithm.AStar, 16, 0, 16)]
    [InlineData(PathAlgorithm.FlowField, 16, 15, 15)]
    public void ExplicitPhase_ShouldRefreshOnTheHandDerivedFutureSlot(
        PathAlgorithm algorithm,
        int interval,
        int phase,
        int expectedRefreshFrame)
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        TestWorld.Context.FrameCount.Should().Be(1);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(interval, phase);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, algorithm));

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.SetTestPosition(middle);

        while (TestWorld.Context.FrameCount < expectedRefreshFrame)
        {
            TestWorld.Context.Simulate();
            steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
            if (TestWorld.Context.FrameCount < expectedRefreshFrame)
            {
                steering.HasLineOfSightPath.Should().BeFalse();
                steering.HasNavigationGuidance.Should().BeTrue();
            }
        }

        TestWorld.Context.FrameCount.Should().Be(expectedRefreshFrame);
        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void ReconfiguringAnAlreadyDueSession_ShouldNotPostponeItsRefresh()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(2, 0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(2);

        steering.ConfigurePathRechecks(2, 0);
        steering.ConfigurePathRechecks(2, 1);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void EnablingAnAlreadyDueCountdownSession_ShouldRefreshOnItsNextHeading()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(0);
        navigator.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar),
            groupId: 9);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.MovementGroupID.Should().Be(9);
        navigator.SetTestPosition(middle);

        steering.ConfigurePathRechecks(2, 1);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void IdlePhaseAndActiveDisable_ShouldStartWithoutDebtThenUseOrdinaryCountdown()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(2);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(2, 0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.SetTestPosition(middle);
        steering.ConfigurePathRechecks(1);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void PausedOverdueSession_ShouldRefreshOnceOnResumeAndRealign()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(2, 0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);
        steering.CanMove = false;

        TestWorld.Context.Simulate();
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        TestWorld.Context.Simulate();
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(4);

        steering.CanMove = true;
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();

        navigator.SetTestPosition(start);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();

        TestWorld.Context.Simulate();
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(6);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void ConfigurePathRechecks_ShouldRejectInvalidEnabledPairsTransactionally()
    {
        var steering = new NavSteering(TestWorld.Context);
        steering.ConfigurePathRechecks(7, 3);

        Action invalidInterval = () => steering.ConfigurePathRechecks(0, 0);
        Action negativePhase = () => steering.ConfigurePathRechecks(7, -1);
        Action phaseAtInterval = () => steering.ConfigurePathRechecks(7, 7);

        invalidInterval.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("intervalFrames");
        negativePhase.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("phase");
        phaseAtInterval.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("phase");
        steering.PathRecheckCooldownFrames.Should().Be(7);
        steering.PathRecheckPhase.Should().Be(3);

        steering.ConfigurePathRechecks(int.MinValue);
        steering.PathRecheckCooldownFrames.Should().Be(int.MinValue);
        steering.PathRecheckPhase.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void EnabledRoundTrip_ShouldRestoreConfigurationAndRequireFreshAdmission(
        bool useMemoryPack)
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(start, profile);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.ConfigurePathRechecks(16, 0);
        source.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        sourceSteering.GetHeading(source, out _).Should().Be(Vector3d.Right);
        sourceSteering.HasLineOfSightPath.Should().BeTrue();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        source.Reset();
        TestNavigator target = CreateNavigator(start, profile);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.PathRecheckCooldownFrames.Should().Be(16);
        targetSteering.PathRecheckPhase.Should().Be(0);
        targetSteering.HasLineOfSightPath.Should().BeFalse();
        targetSteering.HasNavigationGuidance.Should().BeFalse();
        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        targetSteering.HasLineOfSightPath.Should().BeTrue();
        target.Reset();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void EnabledRoundTripIntoNewContext_ShouldRequireFreshAdmission(
        bool useMemoryPack)
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(start, profile);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.ConfigurePathRechecks(16, 0);
        source.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        sourceSteering.GetHeading(source, out _).Should().Be(Vector3d.Right);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        source.Reset();

        using var restoredWorld = new GridWorld();
        restoredWorld.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext restoredContext =
            TrailblazerWorldContext.Attach(restoredWorld);
        PublishLine(restoredContext, Fixed64.Zero);
        TestNavigator target = CreateNavigator(restoredContext, start, profile);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.PathRecheckCooldownFrames.Should().Be(16);
        targetSteering.PathRecheckPhase.Should().Be(0);
        targetSteering.HasLineOfSightPath.Should().BeFalse();
        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        targetSteering.HasLineOfSightPath.Should().BeTrue();
        target.Reset();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void InvalidSerializedPhase_ShouldPreserveLiveConfigurationAndGuidance(
        bool useMemoryPack)
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(start, profile);
        TestRequire.NotNull(source.Steering).ConfigurePathRechecks(4, 2);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        int? invalidPhase = 4;
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            invalidPhase,
            "Steering",
            "PathRecheckPhase");
        source.Reset();
        TestNavigator target = CreateNavigator(start, profile);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.ConfigurePathRechecks(2, 0);
        PathQuery query = CreateQuery(start, end, profile, PathAlgorithm.AStar);
        target.ApplyGuidedTrekRequest(query);
        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        targetSteering.HasNavigationGuidance.Should().BeTrue();
        target.SetTestPosition(middle);
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(2);

        Action populate = () => SerializationUtility.PopulateRecord(
            target,
            payload,
            useMemoryPack);

        populate.Should().Throw<ArgumentOutOfRangeException>();
        targetSteering.PathRecheckCooldownFrames.Should().Be(2);
        targetSteering.PathRecheckPhase.Should().Be(0);
        targetSteering.CurrentQuery.Should().Be(query);
        targetSteering.HasNavigationGuidance.Should().BeTrue();
        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        targetSteering.HasLineOfSightPath.Should().BeTrue();
        targetSteering.HasNavigationGuidance.Should().BeFalse();
        target.Reset();
    }

    [Fact]
    public void EnablingDueWorkAtLogicalFrameZero_ShouldUseDueNowWithoutUnderflow()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);
        object clock = ReflectionUtility.GetPrivateField<object>(TestWorld.Context, "_clock");
        clock.GetType().GetMethod("Reset")!.Invoke(clock, null);
        TestWorld.Context.FrameCount.Should().Be(0);

        steering.ConfigurePathRechecks(2, 1);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void ContextResetReturningToSameFrame_ShouldDiscardDirectTravelTrust()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 1);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();

        TestWorld.Context.Reset();
        (Vector3d restoredStart, _, Vector3d restoredEnd) = PublishLine(Fixed64.One);
        restoredStart.Should().Be(start);
        restoredEnd.Should().Be(end);
        TestWorld.Context.FrameCount.Should().Be(1);
        steering.ConfigurePathRechecks(16, 2);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void StopAndSteeringReset_ShouldClearDebtAndRetainConfiguration()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 15);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.StopMove();
        steering.PathRecheckCooldownFrames.Should().Be(16);
        steering.PathRecheckPhase.Should().Be(15);

        steering.Reset();
        steering.PathRecheckCooldownFrames.Should().Be(16);
        steering.PathRecheckPhase.Should().Be(15);
        navigator.Reset();
    }

    [Fact]
    public void Rebind_ShouldDiscardOldClockDirectTravelTrustAndRetainConfiguration()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 15);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();

        using var reboundWorld = new GridWorld();
        reboundWorld.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext reboundContext =
            TrailblazerWorldContext.Attach(reboundWorld);
        PublishLine(reboundContext, Fixed64.One);

        steering.BindContext(reboundContext);
        steering.ConfigurePathRechecks(16, 14);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.PathRecheckCooldownFrames.Should().Be(16);
        steering.PathRecheckPhase.Should().Be(14);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void WarmedPhasedHeadings_ShouldNotAllocate()
    {
        const int warmupFrames = 32;
        const int measuredFrames = 64;
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);

        for (int frame = 0; frame < warmupFrames; frame++)
        {
            TestWorld.Context.Simulate();
            steering.GetHeading(navigator, out _);
        }

        int rightHeadings = 0;
        Vector3d heading = default;
        Action measureHeading = () => heading = steering.GetHeading(navigator, out _);
        long allocated = 0;
        for (int frame = 0; frame < measuredFrames; frame++)
        {
            TestWorld.Context.Simulate();
            allocated += AllocationTestUtility.MeasureAllocatedBytes(measureHeading);
            if (heading == Vector3d.Right)
                rightHeadings++;
        }

        rightHeadings.Should().Be(measuredFrames);
        steering.HasLineOfSightPath.Should().BeTrue();
        allocated.Should().Be(0, "warmed phased headings must not allocate on due or off-phase frames");
        navigator.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ExplicitPhases_ShouldReplayActualMovementAcrossReversedConstruction(
        PathAlgorithm algorithm)
    {
        var normal = RunMovementReplay(algorithm, reverseConstruction: false);
        var reversed = RunMovementReplay(algorithm, reverseConstruction: true);

        reversed.FirstFrames.Should().Equal(normal.FirstFrames);
        reversed.SecondFrames.Should().Equal(normal.SecondFrames);
        normal.FirstFrames[^1].Position.Should().NotBe(normal.FirstFrames[0].Position);
        normal.SecondFrames[^1].Position.Should().NotBe(normal.SecondFrames[0].Position);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
#endif
    public void ExplicitPhases_ShouldReplayEquivalentRestoredMovementAcrossConstructionOrders(
        PathAlgorithm algorithm,
        bool useMemoryPack)
    {
        (object First, object Second) savedIntent =
            CreateMovementReplayPayloads(algorithm, useMemoryPack);
        var normal = RunRestoredMovementReplay(
            algorithm,
            useMemoryPack,
            savedIntent,
            reverseConstruction: false);
        var reversed = RunRestoredMovementReplay(
            algorithm,
            useMemoryPack,
            savedIntent,
            reverseConstruction: true);

        reversed.FirstFrames.Should().Equal(normal.FirstFrames);
        reversed.SecondFrames.Should().Equal(normal.SecondFrames);
        normal.FirstFrames[0].Frame.Should().Be(2);
        normal.FirstFrames[^1].Frame.Should().Be(13);
        normal.FirstFrames[^1].Position.Should().NotBe(normal.FirstFrames[0].Position);
        normal.SecondFrames[^1].Position.Should().NotBe(normal.SecondFrames[0].Position);
    }

    private static TestNavigator CreateNavigator(
        Vector3d position,
        NavigationAgentProfile profile) =>
        CreateNavigator(TestWorld.Context, position, profile);

    private static TestNavigator CreateNavigator(
        TrailblazerWorldContext context,
        Vector3d position,
        NavigationAgentProfile profile,
        Guid? globalId = null)
    {
        var navigator = new TestNavigator(context);
        navigator.Setup(position, profile, globalId: globalId);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static NavigationAgentProfile CreateProfile() => new(
        new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);

    private static (
        (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[] FirstFrames,
        (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[] SecondFrames)
        RunMovementReplay(PathAlgorithm algorithm, bool reverseConstruction)
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);
        (Vector3d start, _, Vector3d end) = PublishLine(context, Fixed64.One);
        NavigationAgentProfile profile = CreateMovementReplayProfile();
        CreateMovementReplayNavigators(
            context,
            start,
            profile,
            reverseConstruction,
            out TestNavigator first,
            out TestNavigator second);
        NavSteering firstSteering = TestRequire.NotNull(first.Steering);
        NavSteering secondSteering = TestRequire.NotNull(second.Steering);
        firstSteering.ConfigurePathRechecks(4, 0);
        secondSteering.ConfigurePathRechecks(4, 2);
        first.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, algorithm),
            rate: TrekRate.Slow);
        second.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, algorithm),
            rate: TrekRate.Slow);

        return CaptureMovementReplay(context, first, second);
    }

    private static (object First, object Second) CreateMovementReplayPayloads(
        PathAlgorithm algorithm,
        bool useMemoryPack)
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);
        (Vector3d start, _, Vector3d end) = PublishLine(context, Fixed64.One);
        NavigationAgentProfile profile = CreateMovementReplayProfile();
        CreateMovementReplayNavigators(
            context,
            start,
            profile,
            reverseConstruction: false,
            out TestNavigator first,
            out TestNavigator second);
        TestRequire.NotNull(first.Steering).ConfigurePathRechecks(4, 0);
        TestRequire.NotNull(second.Steering).ConfigurePathRechecks(4, 2);
        first.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, algorithm),
            rate: TrekRate.Slow);
        second.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, algorithm),
            rate: TrekRate.Slow);

        object firstPayload = SerializationUtility.SerializeRecord(first, useMemoryPack);
        object secondPayload = SerializationUtility.SerializeRecord(second, useMemoryPack);

        first.Reset();
        second.Reset();
        return (firstPayload, secondPayload);
    }

    private static (
        (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[] FirstFrames,
        (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[] SecondFrames)
        RunRestoredMovementReplay(
            PathAlgorithm algorithm,
            bool useMemoryPack,
            (object First, object Second) savedIntent,
            bool reverseConstruction)
    {
        using var world = new GridWorld();
        world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);
        (Vector3d start, _, _) = PublishLine(context, Fixed64.One);
        NavigationAgentProfile profile = CreateMovementReplayProfile();
        CreateMovementReplayNavigators(
            context,
            start,
            profile,
            reverseConstruction,
            out TestNavigator first,
            out TestNavigator second);

        SerializationUtility.PopulateRecord(first, savedIntent.First, useMemoryPack);
        SerializationUtility.PopulateRecord(second, savedIntent.Second, useMemoryPack);

        first.GlobalId.Should().Be(FirstReplayId);
        second.GlobalId.Should().Be(SecondReplayId);
        NavSteering firstSteering = TestRequire.NotNull(first.Steering);
        NavSteering secondSteering = TestRequire.NotNull(second.Steering);
        firstSteering.CurrentQuery.Should().NotBeNull();
        firstSteering.CurrentQuery!.Value.Algorithm.Should().Be(algorithm);
        secondSteering.CurrentQuery.Should().NotBeNull();
        secondSteering.CurrentQuery!.Value.Algorithm.Should().Be(algorithm);
        firstSteering.PathRecheckPhase.Should().Be(0);
        secondSteering.PathRecheckPhase.Should().Be(2);

        return CaptureMovementReplay(context, first, second);
    }

    private static (
        (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[] FirstFrames,
        (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[] SecondFrames)
        CaptureMovementReplay(
            TrailblazerWorldContext context,
            TestNavigator first,
            TestNavigator second)
    {
        const int frames = 12;
        NavSteering firstSteering = TestRequire.NotNull(first.Steering);
        NavSteering secondSteering = TestRequire.NotNull(second.Steering);
        var firstFrames =
            new (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[frames];
        var secondFrames =
            new (long Frame, Vector3d Position, bool HasLineOfSight, bool HasGuidance)[frames];

        for (int frame = 0; frame < frames; frame++)
        {
            context.Simulate();
            first.Simulate();
            second.Simulate();
            first.ResolveAcceptedFramePosition(first.RequestedFramePosition);
            second.ResolveAcceptedFramePosition(second.RequestedFramePosition);
            first.CommitFrameMotion();
            second.CommitFrameMotion();
            firstFrames[frame] = (
                context.FrameCount,
                first.Position,
                firstSteering.HasLineOfSightPath,
                firstSteering.HasNavigationGuidance);
            secondFrames[frame] = (
                context.FrameCount,
                second.Position,
                secondSteering.HasLineOfSightPath,
                secondSteering.HasNavigationGuidance);
        }

        first.Reset();
        second.Reset();
        return (firstFrames, secondFrames);
    }

    private static NavigationAgentProfile CreateMovementReplayProfile() => new(
        new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);

    private static void CreateMovementReplayNavigators(
        TrailblazerWorldContext context,
        Vector3d start,
        NavigationAgentProfile profile,
        bool reverseConstruction,
        out TestNavigator first,
        out TestNavigator second)
    {
        if (reverseConstruction)
        {
            second = CreateMovementReplayNavigator(context, start, profile, SecondReplayId);
            first = CreateMovementReplayNavigator(context, start, profile, FirstReplayId);
        }
        else
        {
            first = CreateMovementReplayNavigator(context, start, profile, FirstReplayId);
            second = CreateMovementReplayNavigator(context, start, profile, SecondReplayId);
        }

        first.GlobalId.Should().Be(FirstReplayId);
        second.GlobalId.Should().Be(SecondReplayId);
    }

    private static TestNavigator CreateMovementReplayNavigator(
        TrailblazerWorldContext context,
        Vector3d start,
        NavigationAgentProfile profile,
        Guid globalId)
    {
        TestNavigator navigator = CreateNavigator(context, start, profile, globalId);
        navigator.ConstrainTestMotionToSurface(start.Y);
        navigator.SetGroundContact(start.Y, Vector3d.Up, updateMotorState: true);
        return navigator;
    }

    private static PathQuery CreateQuery(
        Vector3d start,
        Vector3d end,
        NavigationAgentProfile profile,
        PathAlgorithm algorithm) => new(
            new NavigationEndpoint(start, MapId),
            new NavigationEndpoint(end, MapId),
            profile,
            new NavigationAreaPolicyKey("steering-los-phase", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            algorithm,
            new NavigationWorkBudget(
                4096,
                4096,
                4096,
                4096,
                4096,
                0,
                0,
                0,
                4096,
                4096,
                0),
            allowTransitions: false);

    private static (Vector3d Start, Vector3d Middle, Vector3d End) PublishLine(
        Fixed64 middleEnterCost) => PublishLine(TestWorld.Context, middleEnterCost);

    private static (Vector3d Start, Vector3d Middle, Vector3d End) PublishLine(
        TrailblazerWorldContext context,
        Fixed64 middleEnterCost)
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            radiusClearance: (Fixed64)4,
            heightClearance: (Fixed64)4);
        var startIndex = new VoxelIndex(4, 4, 4);
        var middleIndex = new VoxelIndex(5, 4, 4);
        var endIndex = new VoxelIndex(6, 4, 4);
        NavigationMap map = new NavigationMapBuilder(MapId, binding)
            .AddCell(startIndex, cell)
            .AddCell(middleIndex, new NavigationCell(
                cell.Media,
                cell.RequiredCapabilities,
                cell.Area,
                middleEnterCost,
                cell.RadiusClearance,
                cell.HeightClearance,
                cell.Flags))
            .AddCell(endIndex, cell)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("steering-los-phase", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            policy,
            policySequence: 2);

        return (
            GuidedPathTestScene.Anchor(binding, startIndex),
            GuidedPathTestScene.Anchor(binding, middleIndex),
            GuidedPathTestScene.Anchor(binding, endIndex));
    }
}
