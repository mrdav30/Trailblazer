using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

[Collection("PathingCollection")]
public sealed class NavSteeringLineOfSightTimingTests : IDisposable
{
    private const string MapId = "steering-los-timing";

    public NavSteeringLineOfSightTimingTests()
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

    [Fact]
    public void DefaultCooldown_ShouldKeepGuideForFifteenLaterHeadingsAndRecheckOnTheSixteenth()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishLine(Fixed64.One);
        NavigationAgentProfile profile = CreateProfile();
        TestNavigator navigator = CreateNavigator(start, profile);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationAStarPayloadCache cache =
            TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile));

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
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile));

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
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile));
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
        navigator.ApplyGuidedTrekRequest(CreateQuery(start, end, profile));
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
        source.ApplyGuidedTrekRequest(CreateQuery(start, end, profile));
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

    private static TestNavigator CreateNavigator(
        Vector3d position,
        NavigationAgentProfile profile)
    {
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Setup(position, profile);
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

    private static PathQuery CreateQuery(
        Vector3d start,
        Vector3d end,
        NavigationAgentProfile profile) => new(
            new NavigationEndpoint(start, MapId),
            new NavigationEndpoint(end, MapId),
            profile,
            new NavigationAreaPolicyKey("steering-los-timing", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
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
            new NavigationAreaPolicyKey("steering-los-timing", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            TestWorld.Context,
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
