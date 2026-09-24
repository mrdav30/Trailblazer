using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

public sealed partial class NavSteeringLineOfSightPhaseTests
{
    [Fact]
    public void ResetThenDisablePhasing_ShouldDiscardOldDirectTravelTrust()
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
        PublishLine(Fixed64.One);
        steering.ConfigurePathRechecks(16);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void ResetThenTwoEnabledReconfigurations_ShouldDiscardOldDirectTravelTrust()
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
        PublishLine(Fixed64.One);
        steering.ConfigurePathRechecks(16, 2);
        steering.ConfigurePathRechecks(16, 3);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void EnabledNewDirectOnlyRequest_ShouldPerformItsInitialDirectCheck(
        PathAlgorithm algorithm)
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 0);
        steering.CanPathfind = false;
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, algorithm));

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void ReconfiguredNewDirectOnlyRequest_ShouldCheckImmediatelyThenUseTheFuturePhase()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(middle, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 0);
        steering.CanPathfind = false;
        navigator.ApplyGuidedTrekRequest(CreateQuery(middle, end, profile, PathAlgorithm.AStar));

        steering.ConfigurePathRechecks(16, 1);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        navigator.SetTestPosition(start);
        AssertDirectOnlyRechecksAtFrame17(TestWorld.Context, navigator, steering);
        navigator.Reset();
    }

    [Fact]
    public void TwiceReconfiguredResetDirectOnlySession_ShouldCheckImmediatelyThenUseTheFuturePhase()
    {
        (_, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(middle, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 0);
        steering.CanPathfind = false;
        navigator.ApplyGuidedTrekRequest(CreateQuery(middle, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        TestWorld.Context.Reset();
        (Vector3d restoredStart, _, _) = PublishLine(Fixed64.One);
        steering.ConfigurePathRechecks(16, 2);
        steering.ConfigurePathRechecks(16, 1);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        navigator.SetTestPosition(restoredStart);
        AssertDirectOnlyRechecksAtFrame17(TestWorld.Context, navigator, steering);
        navigator.Reset();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void EnabledDirectOnlyRestore_ShouldPerformAFreshDirectCheck(
        bool useMemoryPack)
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(start, profile);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.ConfigurePathRechecks(16, 0);
        source.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        sourceSteering.GetHeading(source, out _).Should().Be(Vector3d.Right);
        sourceSteering.CanPathfind = false;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        source.Reset();
        TestNavigator target = CreateNavigator(start, profile);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.CanPathfind.Should().BeFalse();
        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        targetSteering.HasLineOfSightPath.Should().BeTrue();
        targetSteering.HasNavigationGuidance.Should().BeFalse();
        target.Reset();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void EnabledDirectOnlyRestoreThenReconfigure_ShouldCheckImmediatelyThenUseTheFuturePhase(
        bool useMemoryPack)
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(middle, profile);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.ConfigurePathRechecks(16, 0);
        sourceSteering.CanPathfind = false;
        source.ApplyGuidedTrekRequest(CreateQuery(middle, end, profile, PathAlgorithm.AStar));
        sourceSteering.GetHeading(source, out _).Should().Be(Vector3d.Right);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        source.Reset();
        TestNavigator target = CreateNavigator(middle, profile);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.ConfigurePathRechecks(16, 1);

        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        targetSteering.HasLineOfSightPath.Should().BeTrue();
        target.SetTestPosition(start);
        AssertDirectOnlyRechecksAtFrame17(TestWorld.Context, target, targetSteering);
        target.Reset();
    }

    private static void AssertDirectOnlyRechecksAtFrame17(
        TrailblazerWorldContext context,
        TestNavigator navigator,
        NavSteering steering)
    {
        while (context.FrameCount < 17)
        {
            context.Simulate();
            Vector3d heading = steering.GetHeading(navigator, out _);
            if (context.FrameCount < 17)
            {
                heading.Should().Be(Vector3d.Right);
                steering.HasLineOfSightPath.Should().BeTrue();
            }
            else
            {
                heading.Should().Be(Vector3d.Zero);
                steering.HasLineOfSightPath.Should().BeFalse();
            }
        }
    }

    [Fact]
    public void EnabledDirectOnlySessionAfterContextReset_ShouldPerformAFreshDirectCheck()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.CanPathfind = false;

        TestWorld.Context.Reset();
        PublishLine(Fixed64.Zero);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void DisabledReplacementRequestInitialStale_ShouldPreserveTheExistingCountdown()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        PathQuery initial = CreateQuery(start, end, profile, PathAlgorithm.AStar);
        navigator.ApplyGuidedTrekRequest(initial);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        var revisionTwo = new NavigationAreaPolicyKey("steering-los-phase", 2);
        var replacement = new PathQuery(
            initial.Start,
            initial.End,
            initial.Agent,
            revisionTwo,
            initial.Traversal,
            initial.Algorithm,
            initial.Budget,
            initial.AllowTransitions,
            initial.FlowField);

        navigator.ApplyGuidedTrekRequest(replacement);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        steering.CanPathfind = false;
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                revisionTwo,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 3,
            effectiveFrame: TestWorld.Context.FrameCount + 1);
        TestWorld.Context.Pathing.Admit(policyOperation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(TestWorld.Context, policyOperation.Receipt);

        for (int completedHeading = 0; completedHeading < 14; completedHeading++)
        {
            steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
            steering.HasLineOfSightPath.Should().BeFalse();
        }

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void OverdueStopAndReset_ShouldClearDebtBeforeTheNextRequest()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        PathQuery query = CreateQuery(start, end, profile, PathAlgorithm.AStar);
        steering.ConfigurePathRechecks(2, 0);
        navigator.ApplyGuidedTrekRequest(query);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(2);

        steering.StopMove();
        navigator.ApplyGuidedTrekRequest(query);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        TestWorld.Context.Simulate();
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(4);

        steering.Reset();
        navigator.ApplyGuidedTrekRequest(query);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.PathRecheckCooldownFrames.Should().Be(2);
        steering.PathRecheckPhase.Should().Be(0);
        navigator.Reset();
    }

    [Fact]
    public void InactiveEnabledRebind_ShouldAdmitTheNextRequestInTheNewContext()
    {
        NavigationAgentProfile profile = CreateProfile();
        var navigator = CreateNavigator(Vector3d.Zero, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 15);
        using var reboundWorld = new GridWorld();
        reboundWorld.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext reboundContext =
            TrailblazerWorldContext.Attach(reboundWorld);
        (Vector3d start, _, Vector3d end) = PublishLine(reboundContext, Fixed64.One);
        navigator.SetTestPosition(start);

        steering.BindContext(reboundContext);
        navigator.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.PathRecheckCooldownFrames.Should().Be(16);
        steering.PathRecheckPhase.Should().Be(15);
        navigator.Reset();
    }

    [Fact]
    public void ActiveGuideRebind_ShouldReleaseTheOldLeaseAndAdmitInTheNewContext()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 15);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(1);
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

        steering.HasNavigationGuidance.Should().BeFalse();
        TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void ActiveDirectOnlyRebindThenReconfigure_ShouldCheckImmediatelyThenUseTheFuturePhase()
    {
        (_, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(middle, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 0);
        steering.CanPathfind = false;
        navigator.ApplyGuidedTrekRequest(CreateQuery(middle, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        using var reboundWorld = new GridWorld();
        reboundWorld.TryAddGrid(
            new GridConfiguration(
                new Vector3d(-4, -4, -4),
                new Vector3d(8, 8, 8)),
            out _).Should().BeTrue();
        using TrailblazerWorldContext reboundContext =
            TrailblazerWorldContext.Attach(reboundWorld);
        (Vector3d reboundStart, _, _) = PublishLine(reboundContext, Fixed64.One);

        steering.BindContext(reboundContext);
        steering.ConfigurePathRechecks(16, 1);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        navigator.SetTestPosition(reboundStart);
        AssertDirectOnlyRechecksAtFrame17(reboundContext, navigator, steering);
        navigator.Reset();
    }

    [Theory]
    [InlineData(2147483646L, 2147483648L)]
    [InlineData(4294967294L, 4294967296L)]
    public void PhasedHeading_ShouldBecomeDueAcrossOldFrameBoundaries(
        long startingFrame,
        long expectedDueFrameCount)
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        TimingTestUtility.SetTime(TestWorld.Context, startingFrame);
        TestWorld.Context.Simulate();
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(2, 0);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.SetTestPosition(middle);

        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(expectedDueFrameCount);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void PhasedHeading_ShouldBecomeDueAtTheLastLogicalFrameBeforeExhaustion()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        TimingTestUtility.SetTime(TestWorld.Context, long.MaxValue - 2);
        TestWorld.Context.Simulate();
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(2, 1);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.SetTestPosition(middle);

        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(long.MaxValue);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        TestWorld.Context.Invoking(static value => value.Simulate())
            .Should().Throw<InvalidOperationException>();
        navigator.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void PolicyInvalidationOffPhase_ShouldReleaseStaleGuidanceImmediately(
        PathAlgorithm algorithm)
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 15);
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile, algorithm));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        var replacementPolicy = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("steering-los-phase", 2),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 3,
            effectiveFrame: TestWorld.Context.FrameCount + 1);
        TestWorld.Context.Pathing.Admit(replacementPolicy).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(TestWorld.Context, replacementPolicy.Receipt);
        TestWorld.Context.FrameCount.Should().Be(2);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.ShouldMove.Should().BeTrue();
        steering.PathRecheckCooldownFrames.Should().Be(16);
        steering.PathRecheckPhase.Should().Be(15);
        navigator.Reset();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void LateNestedLoadFailure_ShouldPreserveAlreadyDuePhasedWork(
        bool useMemoryPack)
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(start, profile);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            -Fixed64.One,
            "Motor",
            "Handler",
            "Move",
            "MaxFastSpeed");
        source.Reset();
        TestNavigator target = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(target.Steering);
        steering.ConfigurePathRechecks(2, 0);
        PathQuery query = CreateQuery(start, end, profile, PathAlgorithm.AStar);
        target.ApplyGuidedTrekRequest(query);
        steering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        target.SetTestPosition(middle);
        TestWorld.Context.Simulate();
        TestWorld.Context.FrameCount.Should().Be(2);

        Action populate = () => SerializationUtility.PopulateRecord(
            target,
            payload,
            useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        steering.CurrentQuery.Should().Be(query);
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.PathRecheckCooldownFrames.Should().Be(2);
        steering.PathRecheckPhase.Should().Be(0);
        steering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        target.Reset();
    }
}
