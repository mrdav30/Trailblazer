using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

public sealed partial class NavSteeringLineOfSightPhaseTests
{
    [Fact]
    public void DefaultCooldown_ShouldKeepGuideForFifteenLaterHeadingsAndRecheckOnTheSixteenth()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationAStarPayloadCache cache =
            TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        navigator.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar));

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);

        for (int completedHeading = 1; completedHeading < 16; completedHeading++)
        {
            steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
            steering.HasLineOfSightPath.Should().BeFalse();
            cache.ActiveLeaseCount.Should().Be(1);
        }

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(0);
        navigator.Reset();
    }

    [Fact]
    public void NegativeCooldown_ShouldRecheckOnEveryCompletedHeading()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(-3);
        navigator.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar));

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();

        navigator.SetTestPosition(middle);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        navigator.Reset();
    }

    [Fact]
    public void ContextOnlyFrames_ShouldNotConsumeTheCompletedHeadingCooldown()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        navigator.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);

        for (int frame = 0; frame < 32; frame++)
            TestWorld.Context.Simulate();

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

    [Fact]
    public void MovementDisabledEarlyReturns_ShouldNotConsumeTheCompletedHeadingCooldown()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        navigator.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar));
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        navigator.SetTestPosition(middle);
        steering.CanMove = false;

        for (int skippedHeading = 0; skippedHeading < 32; skippedHeading++)
            steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.CanMove = true;
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
    }

#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreDirectTravelWithoutAcquiringGuidance()
    {
        (Vector3d start, _, Vector3d end) = PublishLine(Fixed64.Zero);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator source = CreateNavigator(start, profile);
        source.ApplyGuidedTrekRequest(
            CreateQuery(start, end, profile, PathAlgorithm.AStar));
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.GetHeading(source, out _).Should().Be(Vector3d.Right);
        sourceSteering.HasLineOfSightPath.Should().BeTrue();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack: true);
        source.Reset();
        TestNavigator target = CreateNavigator(start, profile);
        NavigationAStarPayloadCache cache =
            TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack: true);

        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.HasLineOfSightPath.Should().BeTrue();
        targetSteering.HasNavigationGuidance.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(0);
        targetSteering.GetHeading(target, out _).Should().Be(Vector3d.Right);
        cache.ActiveLeaseCount.Should().Be(0);
        target.Reset();
    }
#endif
}
