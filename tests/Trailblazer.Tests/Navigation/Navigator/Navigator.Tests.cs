using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation.Steering;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorTests : IDisposable
{
    public NavigatorTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Setup_ShouldUseTheExactNavigationProfileAsBodyAuthority()
    {
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                radius: Fixed64.Half,
                height: (Fixed64)2,
                rootToFootOffsetY: Fixed64.Quarter),
            maxStepUp: Fixed64.Half,
            maxDropDown: Fixed64.One,
            arrivalRadius: Fixed64.Quarter,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.Climb);
        var navigator = new TestNavigator(TestWorld.Context);
        Vector3d rootPosition = new(2, 3, 4);

        navigator.Setup(rootPosition, profile);

        navigator.NavigationProfile.Should().Be(profile);
        navigator.BodyShape.Should().Be(profile.Shape);
        navigator.Radius.Should().Be(profile.Shape.Radius);
        navigator.FootPosition.Should().Be(
            rootPosition + Vector3d.Down * profile.Shape.RootToFootOffsetY);
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldRejectAQueryWithADifferentAgentProfile()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavigationAgentProfile ownerProfile = navigator.NavigationProfile;
        var mismatchedProfile = new NavigationAgentProfile(
            ownerProfile.Shape,
            ownerProfile.MaxStepUp,
            ownerProfile.MaxDropDown,
            ownerProfile.ArrivalRadius + Fixed64.One,
            ownerProfile.AllowedMedia,
            ownerProfile.Capabilities);
        PathQuery query = CreateSurfaceQuery(
            navigator.FootPosition,
            Vector3d.Right,
            mismatchedProfile);

        Action apply = () => navigator.ApplyGuidedTrekRequest(query);

        apply.Should().Throw<ArgumentException>()
            .WithParameterName("query");
    }

    [Theory]
    [InlineData("start")]
    [InlineData("volume")]
    public void ApplyGuidedTrekRequest_ShouldRejectInvalidFlowFieldShape(string mismatch)
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        PathQuery valid = CreateSurfaceQuery(
            navigator.FootPosition,
            Vector3d.Right,
            navigator.NavigationProfile,
            algorithm: PathAlgorithm.FlowField);
        PathQuery query = mismatch switch
        {
            "start" => valid.WithStartState(
                Vector3d.Left,
                valid.Traversal.StartMedium),
            "volume" => new PathQuery(
                valid.Start,
                valid.End,
                valid.Agent,
                valid.AreaPolicy,
                new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
                valid.Algorithm,
                valid.Budget,
                allowTransitions: false,
                valid.FlowField),
            _ => throw new InvalidOperationException()
        };

        Action apply = () => navigator.ApplyGuidedTrekRequest(query);

        apply.Should().Throw<ArgumentException>()
            .WithParameterName("query");
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.CurrentQuery.Should().BeNull();
        steering.ShouldMove.Should().BeFalse();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldStoreTheExactSurfaceAStarQuery()
    {
        var navigator = CreateNavigator(new Vector3d(2, 3, 4));
        PathQuery query = CreateSurfaceQuery(
            navigator.FootPosition,
            new Vector3d(7, 1, 2),
            navigator.NavigationProfile);

        navigator.ApplyGuidedTrekRequest(query, rate: TrekRate.Moderate, groupId: 8);

        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        navigator.IsGuideded.Should().BeTrue();
        steering.CurrentQuery.Should().Be(query);
        steering.MovementGroupID.Should().Be(8);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
    }

    [Fact]
    public void NavSteering_ShouldNotExposeDirectPublicPathQueryEntryPoint()
    {
        typeof(NavSteering).GetMethod(
                "ApplyPathQuery",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Should().BeNull();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldStoreTheExactSurfaceFlowFieldQuery()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        PathQuery query = CreateSurfaceQuery(
            navigator.FootPosition,
            Vector3d.Right,
            navigator.NavigationProfile,
            algorithm: PathAlgorithm.FlowField);

        navigator.ApplyGuidedTrekRequest(query, groupId: 12);

        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.CurrentQuery.Should().Be(query);
        steering.MovementGroupID.Should().Be(12);
    }

    [Fact]
    public void Simulate_ShouldHoldAndCompleteTheExactAuthoredTransition()
    {
        (Vector3d start, Vector3d end, _) = PublishTransitionGraph();
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        Vector3d startRoot = start + Vector3d.Up * profile.Shape.RootToFootOffsetY;
        var navigator = CreateNavigator(startRoot, profile: profile);
        var query = new PathQuery(
            new NavigationEndpoint(start, "navigator-transition"),
            new NavigationEndpoint(end, "navigator-transition"),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                8192,
                32,
                128,
                1024,
                1024,
                1024,
                1024,
                0,
                0,
                1024,
                32),
            allowTransitions: true);
        navigator.ApplyGuidedTrekRequest(query, rate: TrekRate.Moderate);

        navigator.Simulate();

        navigator.PendingTransition.Should().NotBeNull();
        NavigationTransitionInstruction pending = navigator.PendingTransition!.Value;
        pending.IdentityKind.Should().Be(NavigationTransitionIdentityKind.Definition);
        pending.Id.Should().Be("climb-out");
        pending.SourceMedium.Should().Be(TraversalMedium.Solid);
        pending.DestinationMedium.Should().Be(TraversalMedium.Gas);
        pending.LocomotionHints.Should().Be(
            TraversalTransitionLocomotionHints.RequestClimb
            | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion);
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();

        navigator.CompletePendingTransition(default)
            .Should().Be(NavigationGuideStatus.Stale);
        navigator.PendingTransition.Should().Be(pending);

        navigator.CompletePendingTransition(pending)
            .Should().Be(NavigationGuideStatus.Success);
        navigator.PendingTransition.Should().BeNull();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue(
            "the authored preserve hint survives exact completion");
        TestRequire.NotNull(navigator.Steering).CurrentQuery!.Value
            .Traversal.StartMedium.Should().Be(TraversalMedium.Gas);

        navigator.ApplyGuidedTrekRequest(query, rate: TrekRate.Moderate);
        navigator.Simulate();
        navigator.PendingTransition.Should().NotBeNull();

        navigator.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Moderate);

        navigator.PendingTransition.Should().BeNull();
        TestRequire.NotNull(navigator.Steering).CurrentQuery.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetHeading_ShouldReachTheExactAuthoredSourceActionBeforeHoldingAStarTransition(
        bool includeOrdinaryApproachEdge)
    {
        (Vector3d start, Vector3d end, Vector3d sourceAction) = PublishTransitionGraph(
            includeOrdinaryApproachEdge,
            sourceActionOffset: Fixed64.FromFraction(1, 4));
        NavigationAgentProfile profile = new(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            maxStepUp: Fixed64.Zero,
            maxDropDown: Fixed64.Zero,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas,
            capabilities: TraversalCapability.None);
        Vector3d rootOffset = Vector3d.Up * profile.Shape.RootToFootOffsetY;
        var navigator = CreateNavigator(start + rootOffset, profile: profile);
        var query = new PathQuery(
            new NavigationEndpoint(start, "navigator-transition"),
            new NavigationEndpoint(end, "navigator-transition"),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8192, 32, 128, 1024, 1024, 1024, 1024, 0, 0, 1024, 32),
            allowTransitions: true);
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        Vector3d firstHeading = steering.GetHeading(
            navigator,
            out NavigationTransitionInstruction? pending);
        firstHeading.Should().Be(Vector3d.Right);
        pending.Should().BeNull();

        if (includeOrdinaryApproachEdge)
        {
            Vector3d sourceFoot = sourceAction - Vector3d.Right * Fixed64.FromFraction(1, 4);
            navigator.SetTestPosition(sourceFoot + rootOffset);
            steering.GetHeading(navigator, out pending).Should().Be(Vector3d.Right);
            pending.Should().BeNull();
        }

        navigator.SetTestPosition(sourceAction + rootOffset);
        steering.GetHeading(navigator, out pending).Should().Be(Vector3d.Zero);
        pending.Should().NotBeNull();
        pending!.Value.SourcePosition.Should().Be(sourceAction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSteeringCancellation_ShouldClearTheNavigatorPendingAction(bool arrive)
    {
        (Vector3d start, Vector3d end, _) = PublishTransitionGraph();
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        var query = new PathQuery(
            new NavigationEndpoint(start, "navigator-transition"),
            new NavigationEndpoint(end, "navigator-transition"),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8192, 32, 128, 1024, 1024, 1024, 1024, 0, 0, 1024, 32),
            allowTransitions: true);
        navigator.ApplyGuidedTrekRequest(query);
        navigator.Simulate();
        NavigationTransitionInstruction pending = navigator.PendingTransition!.Value;
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        if (arrive)
            steering.Arrive();
        else
            steering.StopMove();

        navigator.PendingTransition.Should().BeNull();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.CompletePendingTransition(pending).Should().Be(NavigationGuideStatus.Stale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleSteeringCancellation_ShouldNotClearAReinitializedNavigatorPendingAction(bool arrive)
    {
        (Vector3d start, Vector3d end, _) = PublishTransitionGraph();
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        Vector3d rootPosition = start + Vector3d.Up * profile.Shape.RootToFootOffsetY;
        var navigator = CreateNavigator(rootPosition, profile: profile);
        NavSteering staleSteering = TestRequire.NotNull(navigator.Steering);

        navigator.Reset();
        navigator.Setup(TestWorld.Context, rootPosition, profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        var query = new PathQuery(
            new NavigationEndpoint(start, "navigator-transition"),
            new NavigationEndpoint(end, "navigator-transition"),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8192, 32, 128, 1024, 1024, 1024, 1024, 0, 0, 1024, 32),
            allowTransitions: true);
        navigator.ApplyGuidedTrekRequest(query);
        navigator.Simulate();
        NavigationTransitionInstruction pending = navigator.PendingTransition!.Value;

        if (arrive)
            staleSteering.Arrive();
        else
            staleSteering.StopMove();

        navigator.PendingTransition.Should().Be(pending);
        TestRequire.NotNull(navigator.Steering).CurrentQuery.Should().Be(query);
    }

    [Theory]
    [InlineData(false, "Steering")]
    [InlineData(false, "Motor")]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, "Steering")]
    [InlineData(true, "Motor")]
#endif
    public void RoundTrip_ShouldRejectMalformedLateNestedStateWithoutMutatingActiveShell(
        bool useMemoryPack,
        string malformedBranch)
    {
        (Vector3d start, Vector3d end, _) = PublishTransitionGraph();
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var target = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        var query = new PathQuery(
            new NavigationEndpoint(start, "navigator-transition"),
            new NavigationEndpoint(end, "navigator-transition"),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(8192, 32, 128, 1024, 1024, 1024, 1024, 0, 0, 1024, 32),
            allowTransitions: true);
        target.ApplyGuidedTrekRequest(query);
        target.Simulate();
        NavigationTransitionInstruction pending = target.PendingTransition!.Value;
        NavSteering steering = TestRequire.NotNull(target.Steering);
        NavMotor motor = TestRequire.NotNull(target.Motor);
        steering.HasNavigationGuidance.Should().BeTrue();

        var source = CreateNavigator(new Vector3d(2, 2, 2), profile: profile);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = malformedBranch == "Steering"
            ? SerializationUtility.SetPayloadValue(
                payload,
                useMemoryPack,
                -Fixed64.One,
                "Steering",
                "BrakingPower")
            : SerializationUtility.SetPayloadValue(
                payload,
                useMemoryPack,
                -Fixed64.One,
                "Motor",
                "Handler",
                "Move",
                "MaxFastSpeed");
        Vector3d shellPosition = target.Position;
        Fixed64 shellStopMultiplier = steering.StopMultiplier;
        Fixed64 shellMaxFastSpeed = motor.Handler.Move.MaxFastSpeed;

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        target.Position.Should().Be(shellPosition);
        target.Steering.Should().BeSameAs(steering);
        target.Motor.Should().BeSameAs(motor);
        steering.CurrentQuery.Should().Be(query);
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.StopMultiplier.Should().Be(shellStopMultiplier);
        motor.Handler.Move.MaxFastSpeed.Should().Be(shellMaxFastSpeed);
        target.PendingTransition.Should().Be(pending);
    }

    [Fact]
    public void GetHeading_ShouldRepathAStaleLeaseByChangingOnlyTheQueryStart()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        Vector3d startRoot = start + Vector3d.Up * profile.Shape.RootToFootOffsetY;
        var navigator = CreateNavigator(startRoot);
        PathQuery query = CreateSurfaceQuery(start, end, profile, mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        Vector3d firstHeading = steering.GetHeading(navigator, out _);
        steering.ShouldMove.Should().BeTrue();
        firstHeading.Should().NotBe(Vector3d.Zero);

        Vector3d middleRoot = middle + Vector3d.Up * profile.Shape.RootToFootOffsetY;
        navigator.SetTestPosition(middleRoot);
        PublishGraphLine(bakeVersion: 2, publicationSequence: 3);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        steering.CurrentQuery.Should().Be(query);

        Vector3d secondHeading = steering.GetHeading(navigator, out _);
        secondHeading.Should().NotBe(Vector3d.Zero);

        PathQuery expected = new(
            new NavigationEndpoint(
                middle,
                query.Start.MapId,
                query.Start.Resolution,
                query.Start.MaxResolutionDistance),
            query.End,
            query.Agent,
            query.AreaPolicy,
            query.Traversal,
            query.Algorithm,
            query.Budget,
            query.AllowTransitions,
            query.FlowField);
        steering.CurrentQuery.Should().Be(expected);
    }

    [Fact]
    public void GetHeading_ShouldArriveAndReleaseIntentAtTheFinalGraphWaypoint()
    {
        (Vector3d start, _, _) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(start, start, profile, mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.ShouldMove.Should().BeFalse();
        steering.IsAtDestination.Should().BeTrue();
        steering.CurrentQuery.Should().BeNull();
    }

    [Fact]
    public void GetHeading_ShouldAdvanceAcrossGraphWaypointsBeforeArrivingAndReleasingTheLease()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            maxStepUp: Fixed64.Zero,
            maxDropDown: Fixed64.Zero,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile);
        navigator.ApplyGuidedTrekRequest(
            CreateSurfaceQuery(start, end, profile, mapId: "navigator-graph"));
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationAStarPayloadCache cache = TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);

        navigator.SetTestPosition(middle);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.HasNavigationGuidance.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);

        navigator.SetTestPosition(end);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.ShouldMove.Should().BeFalse();
        steering.IsAtDestination.Should().BeTrue();
        steering.CurrentQuery.Should().BeNull();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void GetHeading_ClearGraphRay_ShouldSteerDirectlyWithoutAcquiringGuidance(
        PathAlgorithm algorithm)
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile);
        PathQuery query = WithRayBudget(CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm,
            mapId: "navigator-graph"));
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
    }

    [Fact]
    public void GetHeading_WeightedDirectRay_ShouldRetainGuideUntilLaterClearCooldownRay()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishGraphLine(
            bakeVersion: 1,
            middleEnterCost: Fixed64.One);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile);
        PathQuery query = WithRayBudget(CreateSurfaceQuery(
            start,
            end,
            profile,
            mapId: "navigator-graph"));
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.PathRecheckCooldownFrames = 0;
        NavigationAStarPayloadCache cache =
            TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);

        navigator.SetTestPosition(middle);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);

        steering.HasLineOfSightPath.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_StaleDirectRay_ShouldRetryWithoutHeadingOrGuidance()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        PathQuery source = WithRayBudget(CreateSurfaceQuery(
            start,
            end,
            profile,
            mapId: "navigator-graph"));
        var query = new PathQuery(
            source.Start,
            source.End,
            source.Agent,
            new NavigationAreaPolicyKey("navigator-test", revision: 2),
            source.Traversal,
            source.Algorithm,
            source.Budget,
            source.AllowTransitions,
            source.FlowField);
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.CurrentQuery.Should().Be(query);
        steering.ShouldMove.Should().BeTrue();
        steering.IsAtDestination.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldSampleGraphFlowFromCurrentFootAndReleaseAtDestination()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile);
        PathQuery query = CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowFieldPayloadCache cache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        int arriveCount = 0;
        steering.Events.OnArrive += () => arriveCount++;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        steering.CurrentQuery.Should().Be(query);
        steering.HasNavigationGuidance.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);

        navigator.SetTestPosition(end);
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.ShouldMove.Should().BeFalse();
        steering.IsAtDestination.Should().BeTrue();
        steering.CurrentQuery.Should().BeNull();
        steering.HasNavigationGuidance.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(0);
        arriveCount.Should().Be(1);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        arriveCount.Should().Be(1);
    }

    [Fact]
    public void GetHeading_ShouldArriveOnZeroFlowSampleBeforeNearbySteeringCanMoveIt()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile);
        navigator.ApplyGuidedTrekRequest(CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph"));
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        navigator.SetTestPosition(end, syncLastPosition: false);
        navigator.CommitFrameMotion();
        navigator.SetTestMotion(Vector3d.Right);
        var neighbor = new MockSteerAgent(end + Vector3d.Right * Fixed64.Quarter)
        {
            Velocity = Vector3d.Right,
            Speed = Fixed64.One,
            Size = Fixed64.One
        };
        TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? foundGrid)
            .Should().BeTrue();
        VoxelGrid grid = TestRequire.NotNull(foundGrid);
        grid.TryAddVoxelOccupant(neighbor).Should().BeTrue();
        steering.ComputeCombinedSteering(
                navigator.Position,
                navigator.Velocity,
                navigator.Speed,
                navigator.Radius,
                navigator.GlobalId)
            .Should().NotBe(Vector3d.Zero, "the nearby moving occupant exercises the overwrite regression");
        int arriveCount = 0;
        steering.Events.OnArrive += () => arriveCount++;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        arriveCount.Should().Be(1);
        steering.IsAtDestination.Should().BeTrue();
        steering.ShouldMove.Should().BeFalse();
        steering.CurrentQuery.Should().BeNull();
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        arriveCount.Should().Be(1);
        grid.TryRemoveVoxelOccupant(neighbor).Should().BeTrue();
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldCancelAndReleaseActiveGraphFlowGuidance()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile);
        navigator.ApplyGuidedTrekRequest(CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph"));
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowFieldPayloadCache cache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Right);
        cache.ActiveLeaseCount.Should().Be(1);
        navigator.IsActive.Should().BeTrue();

        navigator.ApplyInputTrekRequest(direction: Vector3d.Forward, rate: TrekRate.Moderate);

        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
        navigator.IsGuideded.Should().BeFalse();
        steering.ShouldMove.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldKeepFlowLeaseAndIntentWhenSampleBudgetIsExceeded()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(
            new GuideSampleWorkBudget(0, 0, 0, 0, 0, 0, 0));
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        context.World.TryAddGrid(
                new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)),
                out _)
            .Should().BeTrue();
        (Vector3d start, _, Vector3d end) = PublishGraphLine(
            bakeVersion: 1,
            context: context);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var navigator = CreateNavigator(start, profile: profile, context: context);
        PathQuery query = CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowFieldPayloadCache cache =
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        NavigationFlowFieldLease lease = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease"));

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease")
            .Should().Be(lease);
        steering.CurrentQuery.Should().Be(query);
        steering.ShouldMove.Should().BeTrue();
        steering.IsAtDestination.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(1);

        steering.StopMove();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldRetryFlowAcquisitionAfterCapacityBecomesAvailable()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query, groupId: 41);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowAdmissionGate gate = TestWorld.Context.Pathing.NavigationFlowAdmissionGate;
        gate.Begin(query, out NavigationFlowBatchWork blockingWork)
            .Should().Be(NavigationFlowQueryStatus.Pending);

        using (blockingWork)
        {
            steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
            steering.CurrentQuery.Should().Be(query);
            steering.MovementGroupID.Should().Be(41);
            steering.ShouldMove.Should().BeTrue();
            gate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        }

        Vector3d firstHeading = steering.GetHeading(navigator, out _);
        firstHeading.Should().NotBe(Vector3d.Zero, "the initial Flow lease must be sampleable before invalidation");
        steering.CurrentQuery.Should().Be(query);
        steering.MovementGroupID.Should().Be(41);
        steering.ShouldMove.Should().BeTrue();
        gate.PayloadCache.ActiveLeaseCount.Should().Be(1);
        steering.StopMove();
        gate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldReleaseAndReacquireAStaleFlowLease()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowFieldPayloadCache cache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;

        steering.GetHeading(navigator, out _);
        NavigationFlowFieldLease firstLease = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease"));
        firstLease.TrySampleHeading(
                start,
                TestWorld.Context.Settings.GuideSampleBudget,
                out Vector3d sampledHeading)
            .Should().Be(NavigationGuideStatus.Success);
        sampledHeading.Should().Be(Vector3d.Right);
        cache.ActiveLeaseCount.Should().Be(1);

        PublishGraphLine(bakeVersion: 2, publicationSequence: 3);
        firstLease.Status.Should().Be(NavigationGuideStatus.Stale);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease")
            .Should().BeNull();
        steering.CurrentQuery.Should().Be(query);
        steering.ShouldMove.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(0);

        steering.GetHeading(navigator, out _);
        NavigationFlowFieldLease replacementLease = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease"));
        replacementLease.Should().NotBe(firstLease);
        replacementLease.Status.Should().Be(NavigationGuideStatus.Success);
        steering.CurrentQuery.Should().Be(query);
        cache.ActiveLeaseCount.Should().Be(1);
        steering.StopMove();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldRaiseTerminalFlowEventsExactlyOnce()
    {
        (Vector3d start, _, _) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(
            start,
            new Vector3d(100, 0, 0),
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        TestWorld.Context.Guides.RequestFlowField(
                query,
                out NavigationFlowFieldLease? directLease)
            .Should().Be(NavigationGuideStatus.InvalidEnd);
        directLease.Should().BeNull();
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        int invalidCount = 0;
        int arriveCount = 0;
        steering.Events.OnInvalidPath += () => invalidCount++;
        steering.Events.OnArrive += () => arriveCount++;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        invalidCount.Should().Be(1, "the first terminal Flow status raises invalid-path");
        arriveCount.Should().Be(1, "the same terminal frame arrives once");

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        invalidCount.Should().Be(1);
        arriveCount.Should().Be(1);
        steering.ShouldMove.Should().BeFalse();
        steering.IsAtDestination.Should().BeTrue();
        steering.CurrentQuery.Should().BeNull();
    }

    [Fact]
    public void ApplyGuidedTrekRequest_ShouldReleasePreviousFlowLeaseBeforeNewRequest()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery first = CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        PathQuery second = CreateSurfaceQuery(
            start,
            middle,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowFieldPayloadCache cache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        navigator.ApplyGuidedTrekRequest(first);
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        cache.ActiveLeaseCount.Should().Be(1);

        navigator.ApplyGuidedTrekRequest(second);

        cache.ActiveLeaseCount.Should().Be(0);
        steering.CurrentQuery.Should().Be(second);
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        cache.ActiveLeaseCount.Should().Be(1);
        steering.StopMove();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GraphFlowMovementGroup_ShouldShareDestinationFieldAcrossDifferentStarts()
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var first = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        var second = CreateNavigator(
            middle + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery firstQuery = CreateSurfaceQuery(
            start,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        PathQuery secondQuery = CreateSurfaceQuery(
            middle,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        first.ApplyGuidedTrekRequest(firstQuery, groupId: 17);
        second.ApplyGuidedTrekRequest(secondQuery, groupId: 17);
        NavSteering firstSteering = TestRequire.NotNull(first.Steering);
        NavSteering secondSteering = TestRequire.NotNull(second.Steering);
        NavigationFlowFieldPayloadCache cache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;

        firstSteering.GetHeading(first, out _).Should().NotBe(Vector3d.Zero);
        secondSteering.GetHeading(second, out _).Should().NotBe(Vector3d.Zero);

        firstSteering.CurrentQuery!.Value.End.Should().Be(firstQuery.End);
        secondSteering.CurrentQuery!.Value.End.Should().Be(firstQuery.End);
        firstSteering.MovementGroupID.Should().Be(17);
        secondSteering.MovementGroupID.Should().Be(17);
        cache.Count.Should().Be(1);
        cache.ActiveLeaseCount.Should().Be(2);

        firstSteering.StopMove();
        secondSteering.StopMove();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldRetainFlowAndAvoidAStarWhenLocalRejoinCannotComplete()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        Vector3d displacedStart = start + Vector3d.Forward * Fixed64.Quarter;
        var navigator = CreateNavigator(
            displacedStart + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery flowQuery = CreateSurfaceQuery(
            displacedStart,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(flowQuery);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationFlowFieldPayloadCache flowCache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        NavigationAStarPayloadCache aStarCache =
            TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.CurrentQuery.Should().Be(flowQuery);
        flowCache.ActiveLeaseCount.Should().Be(1);
        aStarCache.ActiveLeaseCount.Should().Be(0);
        NavigationFlowFieldLease flowLease = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease"));

        navigator.SetTestPosition(start + Vector3d.Up * profile.Shape.RootToFootOffsetY);
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);

        steering.CurrentQuery.Should().Be(flowQuery);
        ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease")
            .Should().Be(flowLease);
        flowCache.ActiveLeaseCount.Should().Be(1);
        aStarCache.ActiveLeaseCount.Should().Be(0);

        steering.Arrive();

        flowCache.ActiveLeaseCount.Should().Be(0);
        aStarCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void GetHeading_ShouldKeepLocalRecoveryAtZeroDespiteNearbySteering()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        Vector3d displacedStart = start + Vector3d.Forward * Fixed64.Quarter;
        var navigator = CreateNavigator(
            displacedStart + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(
            displacedStart,
            end,
            profile,
            algorithm: PathAlgorithm.FlowField,
            mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query, groupId: 53);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
        NavigationFlowFieldLease flowLease = TestRequire.NotNull(
            ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease"));

        navigator.CommitFrameMotion();
        navigator.SetTestMotion(Vector3d.Right);
        var neighbor = new MockSteerAgent(navigator.Position + Vector3d.Right * Fixed64.Quarter)
        {
            Velocity = Vector3d.Right,
            Speed = Fixed64.One,
            Size = Fixed64.One
        };
        TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? foundGrid)
            .Should().BeTrue();
        VoxelGrid grid = TestRequire.NotNull(foundGrid);
        grid.TryAddVoxelOccupant(neighbor).Should().BeTrue();
        steering.ComputeCombinedSteering(
                navigator.Position,
                navigator.Velocity,
                navigator.Speed,
                navigator.Radius,
                navigator.GlobalId)
            .Should().NotBe(Vector3d.Zero, "nearby steering must be available to test suppression");
        int invalidCount = 0;
        int arriveCount = 0;
        steering.Events.OnInvalidPath += () => invalidCount++;
        steering.Events.OnArrive += () => arriveCount++;

        steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);

        steering.CurrentQuery.Should().Be(query);
        steering.MovementGroupID.Should().Be(53);
        ReflectionUtility.GetPrivateField<NavigationFlowFieldLease?>(
                steering,
                "_navigationFlowFieldLease")
            .Should().Be(flowLease);
        steering.ShouldMove.Should().BeTrue();
        steering.IsAtDestination.Should().BeFalse();
        invalidCount.Should().Be(0);
        arriveCount.Should().Be(0);

        grid.TryRemoveVoxelOccupant(neighbor).Should().BeTrue();
        steering.StopMove();
    }

    [Fact]
    public void GetHeading_ShouldRetryAfterGraphGuideAcquisitionCapacityIsAvailable()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(start, end, profile, mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationAStarAdmissionGate gate = TestWorld.Context.Pathing.NavigationAStarAdmissionGate;
        gate.Begin(query, out NavigationAStarBatchWork blockingWork)
            .Should().Be(NavigationAStarQueryStatus.Pending);

        using (blockingWork)
        {
            steering.GetHeading(navigator, out _).Should().Be(Vector3d.Zero);
            steering.CurrentQuery.Should().Be(query);
            steering.ShouldMove.Should().BeTrue();
            steering.IsAtDestination.Should().BeFalse();
        }

        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        steering.CurrentQuery.Should().Be(query);
        steering.ShouldMove.Should().BeTrue();
    }

    [Fact]
    public async Task GetHeading_ShouldRetryAfterGraphGuideAcquisitionBecomesStale()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(start, end, profile, mapId: "navigator-graph");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavigationAStarAdmissionGate gate = TestWorld.Context.Pathing.NavigationAStarAdmissionGate;
        NavigationWorldGraphStore store = TestWorld.Context.Pathing.NavigationGraphStore;
        NavigationSurfaceComponentIndex currentComponents = store.Current.SurfaceComponents;
        FieldInfo cacheSyncField = TestRequire.NotNull(
            typeof(NavigationAStarPayloadCache).GetField(
                "_sync",
                BindingFlags.Instance | BindingFlags.NonPublic));
        object cacheSync = TestRequire.NotNull(cacheSyncField.GetValue(gate.PayloadCache));
        Task<Vector3d> acquisition;

        Monitor.Enter(cacheSync);
        try
        {
            acquisition = Task.Run(() => steering.GetHeading(navigator, out _));
            SpinWait.SpinUntil(
                    () => store.ActiveLeaseCount > 0,
                    TimeSpan.FromSeconds(5))
                .Should().BeTrue();
            NavigationWorldGraph current = store.Current;
            NavigationWorldGraph changed = current.WithSurfaceComponents(
                NavigationSurfaceComponentIndex.Empty).WithGraphVersion(
                    current.GraphVersion + 1);
            store.TryPublish(changed).Should().Be(NavigationCandidatePublication.Published);
        }
        finally
        {
            Monitor.Exit(cacheSync);
        }

        (await acquisition).Should().Be(Vector3d.Zero);
        steering.CurrentQuery.Should().Be(query);
        steering.ShouldMove.Should().BeTrue();
        steering.IsAtDestination.Should().BeFalse();

        NavigationWorldGraph invalidated = store.Current;
        store.TryPublish(invalidated.WithSurfaceComponents(currentComponents).WithGraphVersion(
                invalidated.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        steering.CurrentQuery.Should().Be(query);
        steering.ShouldMove.Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreExactDurableGraphQueryWithoutReacquiringGuide()
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var source = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        PathQuery query = CreateSurfaceQuery(start, end, profile, mapId: "navigator-graph");
        source.ApplyGuidedTrekRequest(query);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.GetHeading(source, out _).Should().NotBe(Vector3d.Zero);

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);
        NavigationAStarPayloadCache cache =
            TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(1);
        source.Reset();
        cache.ActiveLeaseCount.Should().Be(0);
        var target = CreateNavigator(new Vector3d(-2, 0, -2), profile: profile);
        JsonRecordSerializer.Populate(target, json);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.NavigationProfile.Should().Be(profile);
        targetSteering.CurrentQuery.Should().Be(query);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreExactFlowQueryAndRebaseFromLoadedFoot(bool useMemoryPack)
    {
        (Vector3d start, Vector3d middle, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var budget = new NavigationWorkBudget(
            101, 102, 103, 104, 105, 6, 7, 8, 9, 10, 11);
        var query = new PathQuery(
            new NavigationEndpoint(start, "navigator-graph"),
            new NavigationEndpoint(end, "navigator-graph"),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.FlowField,
            budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.FromFraction(3, 2)));
        var source = CreateNavigator(start, profile: profile);
        source.ApplyGuidedTrekRequest(query, rate: TrekRate.Moderate, groupId: 27);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.GetHeading(source, out _).Should().Be(Vector3d.Right);
        source.SetTestPosition(middle);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        NavigationFlowFieldPayloadCache cache =
            TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        source.Reset();
        cache.ActiveLeaseCount.Should().Be(0);
        var target = CreateNavigator(new Vector3d(-2, 0, -2), profile: profile);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        PathQuery expected = query.WithStartState(
            middle,
            query.Traversal.StartMedium);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        targetSteering.CurrentQuery.Should().Be(expected);
        targetSteering.ShouldMove.Should().BeTrue();
        targetSteering.MovementGroupID.Should().Be(27);
        target.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
        cache.ActiveLeaseCount.Should().Be(0);

        targetSteering.Arrive();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRejectInvalidRestoredTargetMediaWithoutAcquiringLease(bool useMemoryPack)
    {
        (Vector3d start, _, Vector3d end) = PublishGraphLine(bakeVersion: 1);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var source = CreateNavigator(
            start + Vector3d.Up * profile.Shape.RootToFootOffsetY,
            profile: profile);
        source.ApplyGuidedTrekRequest(CreateSurfaceQuery(start, end, profile, mapId: "navigator-graph"));
        TestRequire.NotNull(source.Steering).GetHeading(source, out _).Should().NotBe(Vector3d.Zero);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            TraversalMedia.None,
            "PathSession",
            "TargetMedia");
        source.Reset();

        NavigationAStarPayloadCache cache = TestWorld.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(0);
        var target = CreateNavigator(new Vector3d(-2, 0, -2), profile: profile);

        Action populate = () => SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        populate.Should().Throw<InvalidOperationException>();
        TestRequire.NotNull(target.Steering).CurrentQuery.Should().BeNull();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Setup_ShouldHonorExplicitGlobalId()
    {
        Guid explicitId = new("11111111-2222-3333-4444-555555555555");
        var navigator = new TestNavigator(TestWorld.Context);

        navigator.Setup(Vector3d.Zero, PathTestFactory.DefaultNavigationProfile, globalId: explicitId);

        navigator.GlobalId.Should().Be(explicitId);
    }

    [Fact]
    public void Setup_ShouldAssignDeterministicGlobalIds_AndReplayAfterReset()
    {
        var first = new TestNavigator(TestWorld.Context);
        var second = new TestNavigator(TestWorld.Context);

        first.Setup(Vector3d.Zero, PathTestFactory.DefaultNavigationProfile);
        second.Setup(Vector3d.Right, PathTestFactory.DefaultNavigationProfile);

        Guid firstId = first.GlobalId;
        Guid secondId = second.GlobalId;

        secondId.Should().NotBe(firstId);
        firstId.Should().NotBe(Guid.Empty);

        TestWorld.Context.Reset();

        var replayFirst = new TestNavigator(TestWorld.Context);
        var replaySecond = new TestNavigator(TestWorld.Context);

        replayFirst.Setup(Vector3d.Zero, PathTestFactory.DefaultNavigationProfile);
        replaySecond.Setup(Vector3d.Right, PathTestFactory.DefaultNavigationProfile);

        replayFirst.GlobalId.Should().Be(firstId);
        replaySecond.GlobalId.Should().Be(secondId);
    }

    [Fact]
    public void Setup_ShouldRejectEmptyExplicitGlobalId()
    {
        var navigator = new TestNavigator(TestWorld.Context);

        Action act = () => navigator.Setup(
            Vector3d.Zero,
            PathTestFactory.DefaultNavigationProfile,
            globalId: Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("globalId");
    }

    [Fact]
    public void Setup_ShouldRejectRepeatedLifecycleEntryUntilReset()
    {
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Setup(Vector3d.Zero, profile);
        Guid initialId = navigator.GlobalId;

        Action repeatedSetup = () => navigator.Setup(Vector3d.Right, profile);
        Action activateAfterSetup = () => navigator.Activate(new TrekCondition(), Vector3d.Forward, profile);

        repeatedSetup.Should().Throw<InvalidOperationException>();
        activateAfterSetup.Should().Throw<InvalidOperationException>();
        navigator.Position.Should().Be(Vector3d.Zero);
        navigator.GlobalId.Should().Be(initialId);

        navigator.Reset();
        navigator.Setup(TestWorld.Context, Vector3d.Right, profile);
        navigator.Context.Should().BeSameAs(TestWorld.Context);
        navigator.Position.Should().Be(Vector3d.Right);
    }

    [Fact]
    public void Activate_ShouldRejectRepeatedLifecycleEntryUntilReset()
    {
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Activate(new TrekCondition(), Vector3d.Zero, profile);
        NavSteering initialSteering = TestRequire.NotNull(navigator.Steering);

        Action repeatedActivation = () => navigator.Activate(new TrekCondition(), Vector3d.Right, profile);
        Action setupAfterActivation = () => navigator.Setup(Vector3d.Forward, profile);

        repeatedActivation.Should().Throw<InvalidOperationException>();
        setupAfterActivation.Should().Throw<InvalidOperationException>();
        navigator.Position.Should().Be(Vector3d.Zero);
        navigator.Steering.Should().BeSameAs(initialSteering);

        navigator.Reset();
        navigator.Activate(TestWorld.Context, new TrekCondition(), Vector3d.Right, profile);
        navigator.Context.Should().BeSameAs(TestWorld.Context);
        navigator.Position.Should().Be(Vector3d.Right);
        navigator.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldCaptureFacingDirection()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Backward,
            TrekRate.Moderate,
            facingDirection: Vector3d.Forward);

        navigator.FrameRequest.Direction.Should().Be(Vector3d.Backward);
        navigator.FrameRequest.FacingDirection.Should().Be(Vector3d.Forward);
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldUseDefaults_WhenArgumentsAreOmitted()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest();

        navigator.IsGuideded.Should().BeFalse();
        navigator.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Stationary);
        navigator.FrameRequest.IsRequestingJump.Should().BeFalse();
        navigator.FrameRequest.CanAffordJump.Should().BeTrue();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingSwim.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        navigator.FrameRequest.FacingDirection.Should().BeNull();
    }

    [Fact]
    public void ApplyInputTrekRequest_ShouldCaptureExplicitSwimIntent()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Forward,
            TrekRate.Moderate,
            isRequestingSwim: true);

        navigator.FrameRequest.IsRequestingSwim.Should().BeTrue();
    }

    [Fact]
    public void Simulate_ShouldUseFacingDirectionForTurnSelection()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavTurning turning = TestRequire.NotNull(navigator.Turning);

        navigator.ApplyInputTrekRequest(
            Vector3d.Backward,
            TrekRate.Fast,
            facingDirection: Vector3d.Right);

        TestWorld.Context.Simulate();
        navigator.Simulate();

        turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
    }

    [Fact]
    public void Simulate_ShouldNotAutoTurnToMovement_WhenLockedOnAndNotSprinting()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavTurning turning = TestRequire.NotNull(navigator.Turning);
        navigator.IsLockedOn = true;

        navigator.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Moderate);

        TestWorld.Context.Simulate();
        navigator.Simulate();

        navigator.Rotation.Should().Be(FixedQuaternion.Identity);
        turning.TargetReached.Should().BeTrue();
    }

    [Fact]
    public void Simulate_ShouldAutoTurnToMovement_WhenLockedOnAndSprinting()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavTurning turning = TestRequire.NotNull(navigator.Turning);
        navigator.IsLockedOn = true;

        navigator.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Fast);

        TestWorld.Context.Simulate();
        navigator.Simulate();

        turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
    }

    [Fact]
    public void Simulate_ShouldAllowBackpedalWithoutChangingFacing_WhenFacingDirectionMatchesForward()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.ApplyInputTrekRequest(
            Vector3d.Backward,
            TrekRate.Fast,
            facingDirection: Vector3d.Forward);

        TestWorld.Context.Simulate();
        navigator.Simulate();
        navigator.CommitFrameMotion();

        navigator.Rotation.Should().Be(FixedQuaternion.Identity);
        navigator.Forward.Should().Be(Vector3d.Forward);
        navigator.Position.Z.Should().BeLessThan(Fixed64.Zero);
        navigator.Velocity.Z.Should().BeLessThan(Fixed64.Zero);
    }

    [Fact]
    public void NotifyCollision_ShouldForwardToTurningController()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavTurning turning = TestRequire.NotNull(navigator.Turning);
        navigator.SetTestPosition(new Vector3d(1, 0, 0), syncLastPosition: false);

        navigator.NotifyCollision();

        TestWorld.Context.Simulate();
        navigator.Simulate();
        turning.TargetReached.Should().BeTrue();
        navigator.CommitFrameMotion();

        TestWorld.Context.Simulate();
        navigator.Simulate();

        turning.TargetReached.Should().BeFalse();
        turning.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
        navigator.CommitFrameMotion();
    }

    [Fact]
    public void SetGroundContact_ShouldPopulateGroundStateAndUpdateMotorWhenRequested()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        var snapshot = new PlatformSnapshot(
            7,
            Fixed4x4.CreateTransform(new Vector3d(3, 1, 2), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: (Fixed64)3,
            platform: snapshot,
            surfaceFriction: (Fixed64)0.2f,
            motionTransfer: MotionTransfer.PermaLocked,
            updateMotorState: true);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Solid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        Assert.NotNull(navigator.FrameCondition.GroundState);
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);
        navigator.FrameCondition.GroundState.Value.Platform.Transform.Translation.Should().Be(snapshot.Transform.Translation);
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.2f);
        navigator.FrameCondition.GroundState.Value.MotionTransferState.Should().Be(MotionTransfer.PermaLocked);

        TrekCondition motorCondition = motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Solid);
        motorCondition.SurfaceLevel.Should().Be((Fixed64)3);
        Assert.NotNull(motorCondition.GroundState);
        motorCondition.GroundState.Value.Platform.Should().Be(snapshot);
        motorCondition.GroundState.Value.Platform.Transform.Translation.Should().Be(snapshot.Transform.Translation);
    }

    [Fact]
    public void SetAirborne_ShouldPreserveGroundStateByDefault()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        var snapshot = new PlatformSnapshot(
            5,
            Fixed4x4.CreateTransform(new Vector3d(1, 0, 1), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: snapshot,
            motionTransfer: MotionTransfer.InitTransfer,
            updateMotorState: true);

        navigator.SetAirborne(surfaceLevel: (Fixed64)4, updateMotorState: true);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)4);
        Assert.NotNull(navigator.FrameCondition.GroundState);
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);

        TrekCondition motorCondition = motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Gas);
        Assert.NotNull(motorCondition.GroundState);
        motorCondition.GroundState.Value.Platform.Should().Be(snapshot);
    }

    [Fact]
    public void SetWaterContact_ShouldClearGroundState()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        var snapshot = new PlatformSnapshot(
            5,
            Fixed4x4.CreateTransform(new Vector3d(1, 0, 1), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: snapshot,
            motionTransfer: MotionTransfer.InitTransfer,
            updateMotorState: true);

        navigator.SetWaterContact(surfaceLevel: (Fixed64)2, updateMotorState: true);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Liquid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)2);
        navigator.FrameCondition.GroundState.Should().BeNull();

        TrekCondition motorCondition = motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Liquid);
        motorCondition.GroundState.Should().BeNull();
    }

    [Fact]
    public void SyncCurrentTrekConditionToMotor_ShouldPushCurrentFrameConditionImmediately()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        TrekCondition replacement = new()
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = (Fixed64)2,
            GroundState = null,
            CeilingLevel = (Fixed64)5
        };

        navigator.ReplaceTrekCondition(replacement, updateMotorState: false);

        motor.CurrentState.Medium.Should().Be(TraversalMedium.Solid);

        navigator.SyncCurrentTrekConditionToMotor();

        TrekCondition motorCondition = motor.CurrentState.ToTrekCondition();
        motorCondition.Medium.Should().Be(TraversalMedium.Liquid);
        motorCondition.SurfaceLevel.Should().Be((Fixed64)2);
        motorCondition.GroundState.Should().BeNull();
        motorCondition.CeilingLevel.Should().Be((Fixed64)5);
    }

    [Fact]
    public void InactiveNavigator_ShouldThrowForPrewarmSimulateAndCommit()
    {
        var navigator = new TestNavigator(TestWorld.Context);

        navigator.Invoking(n => n.PrewarmMovementGroup())
            .Should().Throw<InvalidOperationException>();
        navigator.Invoking(n => n.Simulate())
            .Should().Throw<InvalidOperationException>();
        navigator.Invoking(n => n.CommitFrameMotion())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GuidedRequestSetters_ShouldUpdateFrameRequestState()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.SetFrameJumpAffordability(false);
        navigator.ToggleGuidedJump(true);
        navigator.ToggleGuidedFlight(true);
        navigator.ToggleGuidedSwim(true);
        navigator.ToggleGuidedClimb(true);
        navigator.SetGuidedTrekRate(TrekRate.Moderate);

        navigator.FrameRequest.CanAffordJump.Should().BeFalse();
        navigator.FrameRequest.IsRequestingJump.Should().BeTrue();
        navigator.FrameRequest.IsRequestingFlight.Should().BeTrue();
        navigator.FrameRequest.IsRequestingSwim.Should().BeTrue();
        navigator.FrameRequest.IsRequestingClimb.Should().BeTrue();
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
    }

    [Fact]
    public void ReplaceAndSetTrekCondition_ShouldCloneState_AndOnlyUpdateMotorWhenRequested()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        TrekCondition replacement = new()
        {
            Medium = TraversalMedium.Gas,
            SurfaceLevel = (Fixed64)4,
            CeilingLevel = (Fixed64)8
        };

        navigator.ReplaceTrekCondition(replacement, updateMotorState: false);
        replacement.Medium = TraversalMedium.Liquid;
        replacement.SurfaceLevel = (Fixed64)9;

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)4);
        motor.CurrentState.Medium.Should().Be(TraversalMedium.Solid);

        navigator.ReplaceTrekCondition(new TrekCondition
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = (Fixed64)2,
            GroundState = null,
            CeilingLevel = (Fixed64)5
        }, updateMotorState: true);

        motor.CurrentState.Medium.Should().Be(TraversalMedium.Liquid);
        motor.CurrentState.SurfaceLevel.Should().Be((Fixed64)2);

        GroundCondition updatedGround = new()
        {
            SurfaceFriction = (Fixed64)0.25f
        };
        navigator.SetTrekCondition(
            surfaceLevel: (Fixed64)6,
            surfaceCondition: updatedGround,
            ceilingLevel: (Fixed64)7,
            updateMotorState: true);

        updatedGround.SurfaceFriction = Fixed64.Zero;

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Liquid);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)6);
        Assert.NotNull(navigator.FrameCondition.GroundState);
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.25f);
        navigator.FrameCondition.CeilingLevel.Should().Be((Fixed64)7);
        motor.CurrentState.SurfaceLevel.Should().Be((Fixed64)6);
        Assert.NotNull(motor.CurrentState.GroundState);
        motor.CurrentState.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.25f);
    }

    [Fact]
    public void SetTrekCondition_ShouldPreserveExistingSurfaceState_WhenOptionalArgumentsAreOmitted()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        var snapshot = new PlatformSnapshot(
            12,
            Fixed4x4.CreateTransform(new Vector3d(2, 0, 2), FixedQuaternion.Identity, Vector3d.One));

        navigator.SetGroundContact(
            surfaceLevel: (Fixed64)3,
            platform: snapshot,
            surfaceFriction: (Fixed64)0.4f,
            motionTransfer: MotionTransfer.PermaLocked,
            ceilingLevel: (Fixed64)8,
            updateMotorState: false);

        navigator.SetTrekCondition(medium: TraversalMedium.Gas, replaceGroundContact: false, updateMotorState: false);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        navigator.FrameCondition.SurfaceLevel.Should().Be((Fixed64)3);
        navigator.FrameCondition.CeilingLevel.Should().Be((Fixed64)8);
        Assert.NotNull(navigator.FrameCondition.GroundState);
        navigator.FrameCondition.GroundState.Value.Platform.Should().Be(snapshot);
        navigator.FrameCondition.GroundState.Value.SurfaceFriction.Should().Be((Fixed64)0.4f);
        navigator.FrameCondition.GroundState.Value.MotionTransferState.Should().Be(MotionTransfer.PermaLocked);
    }

    [Fact]
    public void DeltaHelpers_ShouldIgnoreZeroInputs_AndApplyQueuedMotionOnCommit()
    {
        var navigator = CreateNavigator(Vector3d.Zero);
        FixedQuaternion quarterTurn = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);

        navigator.AddPositionDelta(Vector3d.Zero);
        navigator.ApplyRotationDelta(FixedQuaternion.Identity);
        navigator.AddVelocityDelta(Vector3d.Zero);
        navigator.AddPositionDelta(Vector3d.Right);
        navigator.ApplyRotationDelta(quarterTurn);
        navigator.AddVelocityDelta(Vector3d.Forward);

        navigator.CommitFrameMotion();

        navigator.Position.Should().Be(new Vector3d(1, 0, 1));
        navigator.Rotation.Should().Be(quarterTurn);
        navigator.Forward.Should().Be(quarterTurn.Rotate(Vector3d.Forward));
        navigator.Speed.Should().BeGreaterThan(Fixed64.Zero);
        navigator.Acceleration.Should().NotBe(Vector3d.Zero);
    }

    [Fact]
    public void CommitFrameMotion_ShouldReportZeroSpeed_WhenNoMovementOccurred()
    {
        var navigator = CreateNavigator(Vector3d.Zero);

        navigator.CommitFrameMotion();

        navigator.Speed.Should().Be(Fixed64.Zero);
        navigator.StuckThresholdSpeed.Should().Be(Fixed64.Zero);
        navigator.Acceleration.Should().Be(Vector3d.Zero);
    }

    private static TestNavigator CreateNavigator(
        Vector3d position,
        FixedQuaternion? rotation = null,
        NavigationAgentProfile? profile = null,
        TrailblazerWorldContext? context = null)
    {
        var navigator = new TestNavigator(context ?? TestWorld.Context);
        navigator.Setup(position, profile ?? PathTestFactory.DefaultNavigationProfile, rotation: rotation);
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static PathQuery CreateSurfaceQuery(
        Vector3d start,
        Vector3d end,
        NavigationAgentProfile profile,
        PathAlgorithm algorithm = PathAlgorithm.AStar,
        string? mapId = null,
        bool allowTransitions = false) => new(
            new NavigationEndpoint(start, mapId),
            new NavigationEndpoint(end, mapId),
            profile,
            new NavigationAreaPolicyKey("navigator-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            algorithm,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0),
            allowTransitions);

    private static PathQuery WithRayBudget(PathQuery query) => new(
        query.Start,
        query.End,
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        query.Algorithm,
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
        query.AllowTransitions,
        query.FlowField);

    private static (Vector3d Start, Vector3d Middle, Vector3d End) PublishGraphLine(
        int bakeVersion,
        long publicationSequence = 1,
        TrailblazerWorldContext? context = null,
        Fixed64 middleEnterCost = default)
    {
        context ??= TestWorld.Context;
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var startIndex = new VoxelIndex(4, 4, 4);
        var middleIndex = new VoxelIndex(5, 4, 4);
        var endIndex = new VoxelIndex(6, 4, 4);
        NavigationMap map = new NavigationMapBuilder("navigator-graph", binding)
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
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion),
            OverlayReplacementPolicy.Clear,
            publicationSequence,
            context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();

        if (bakeVersion == 1)
        {
            var policy = new NavigationAreaPolicy(
                new NavigationAreaPolicyKey("navigator-test", 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) });
            var policyOperation = new NavigationAreaPolicyCommitOperation(
                policy,
                publicationSequence: publicationSequence + 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(policyOperation).Should().BeTrue();
        }

        context.Simulate();
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        return (
            GetGraphFoot(binding, startIndex),
            GetGraphFoot(binding, middleIndex),
            GetGraphFoot(binding, endIndex));
    }

    private static (Vector3d Start, Vector3d End, Vector3d SourceAction) PublishTransitionGraph(
        bool includeOrdinaryApproachEdge = false,
        Fixed64 sourceActionOffset = default)
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var sourceIndex = new VoxelIndex(4, 4, 4);
        var destinationIndex = new VoxelIndex(5, 4, 4);
        var approachIndex = new VoxelIndex(3, 4, 4);
        var sourceCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var destinationCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        Vector3d sourceFoot = GetGraphFoot(binding, sourceIndex);
        Vector3d sourceAction = sourceFoot + Vector3d.Right * sourceActionOffset;
        var transition = new TraversalTransitionDefinition(
            "climb-out",
            TraversalTransitionType.Climb,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("navigator-transition", destinationIndex),
            TraversalMedium.Gas,
            actionCost: Fixed64.One,
            locomotionHints: TraversalTransitionLocomotionHints.RequestClimb
                | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion,
            sourcePointOverride: sourceAction,
            hasSourcePointOverride: sourceActionOffset != Fixed64.Zero);
        var builder = new NavigationMapBuilder("navigator-transition", binding);
        if (includeOrdinaryApproachEdge)
            builder.AddCell(approachIndex, sourceCell);
        NavigationMap map = builder.AddCell(sourceIndex, sourceCell)
            .AddCell(destinationIndex, destinationCell)
            .AddTransition(transition)
            .Build();
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            1,
            TestWorld.Context.FrameCount + 1);
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("navigator-test", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            policy,
            publicationSequence: 2,
            effectiveFrame: TestWorld.Context.FrameCount + 1);
        TestWorld.Context.Pathing.Admit(mapOperation).Should().BeTrue();
        TestWorld.Context.Pathing.Admit(policyOperation).Should().BeTrue();
        TestWorld.Context.Simulate();
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        return (
            GetGraphFoot(binding, includeOrdinaryApproachEdge ? approachIndex : sourceIndex),
            GetGraphFoot(binding, destinationIndex),
            sourceAction);
    }

    private static TrailblazerWorldContextSettings CreateSettings(
        GuideSampleWorkBudget guideSampleBudget)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            guideSampleBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }

    private static Vector3d GetGraphFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

}
