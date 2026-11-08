using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DVerticalTraversalTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator3DVerticalTraversalTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void AuthoredStairs_ShouldReachTheUpperLandingWithAcceptedMotion(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        PathQuery query = fixture.CreateQuery(algorithm);
        actor.Navigator.ApplyGuidedTrekRequest(query, TrekRate.Fast);
        int invalidPathCount = 0;
        int arriveCount = 0;
        actor.Navigator.Steering!.Events.OnInvalidPath += () => invalidPathCount++;
        actor.Navigator.Steering.Events.OnArrive += () => arriveCount++;

        Vector3d expectedFoot = fixture.EndFoot;
        var expectedAddress = new NavigationCellAddress(
            "gravitas-stair-3",
            default);
        int? arrivalFrame = null;
        int? firstBlockedFrame = null;
        Vector3d firstRequested = Vector3d.Zero;
        Vector3d firstAccepted = Vector3d.Zero;
        FixedQuaternion firstRequestedRotation = FixedQuaternion.Identity;
        FixedQuaternion firstAcceptedRotation = FixedQuaternion.Identity;
        Fixed64 previousAcceptedSurface = fixture.StartFoot.Y;
        int acceptedStepCount = 0;
        var observationTail = new string[8];
        _output.WriteLine("Stairs motion trace (Fixed64 raw X,Y,Z): frame|navigator-request|host-request|accepted-position|accepted-velocity");

        for (int frame = 1; frame <= 512; frame++)
        {
            fixture.Prepare(actor);
            fixture.Resolve(actor);
            GravitasMotionTrace.WriteFrame(_output, frame,
                actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
                actor.Navigator.Position, actor.Navigator.Velocity);
            if (!firstBlockedFrame.HasValue
                && actor.HostRequestedPosition != actor.Body.Position3d)
            {
                firstBlockedFrame = frame;
                firstRequested = actor.HostRequestedPosition;
                firstAccepted = actor.Body.Position3d;
                firstRequestedRotation = actor.Body.PositionTransform.WorldRotation;
                firstAcceptedRotation = actor.Body.Rotation;
            }
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            Fixed64 acceptedSurface = actor.Navigator.FootPosition.Y;
            if (acceptedSurface > previousAcceptedSurface)
                acceptedStepCount++;
            previousAcceptedSurface = acceptedSurface;
            observationTail[(frame - 1) % observationTail.Length] =
                $"f{frame}:navigatorRequest={actor.NavigatorRequestedPosition},hostRequest={actor.HostRequestedPosition},requestedRotation={actor.Body.PositionTransform.WorldRotation},accepted={actor.Body.Position3d},acceptedRotation={actor.Body.Rotation},foot={actor.Navigator.FootPosition},surface={actor.Navigator.Motor!.CurrentState.SurfaceLevel},heading={actor.Navigator.Steering!.TargetDirection},velocity={actor.Navigator.Velocity},guided={actor.Navigator.IsGuided},stuck={actor.Navigator.Steering.IsStuck}";

            if ((actor.Navigator.FootPosition - expectedFoot).Magnitude
                    <= actor.Navigator.NavigationProfile.ArrivalRadius
                && actor.Navigator.LastCommittedCell?.Address == expectedAddress
                && !actor.Navigator.IsGuided)
            {
                arrivalFrame = frame;
                break;
            }
        }

        arrivalFrame.Should().HaveValue(
            "the upper landing at {0} must be reached within 512 frames; final foot was {1}, heading was {2}, velocity was {3}, committed address was {4}, guided was {5}, invalid paths were {6}, arrivals were {7}, first requested-versus-accepted divergence was frame {8}, requested {9}/{10}, accepted {11}/{12}; observation tail: {13}",
            expectedFoot,
            actor.Navigator.FootPosition,
            actor.Navigator.Steering!.TargetDirection,
            actor.Navigator.Velocity,
            actor.Navigator.LastCommittedCell?.Address,
            actor.Navigator.IsGuided,
            invalidPathCount,
            arriveCount,
            firstBlockedFrame,
            firstRequested,
            firstRequestedRotation,
            firstAccepted,
            firstAcceptedRotation,
            string.Join("; ", observationTail));
        arrivalFrame!.Value.Should().BeLessThanOrEqualTo(512);
        (actor.Navigator.FootPosition - expectedFoot).Magnitude.Should()
            .BeLessThanOrEqualTo(actor.Navigator.NavigationProfile.ArrivalRadius);
        actor.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            expectedAddress);
        actor.Navigator.IsGuided.Should().BeFalse();
        invalidPathCount.Should().Be(0);
        arriveCount.Should().Be(1);
        acceptedStepCount.Should().Be(3,
            "Gravitas must accept each authored rise before the actor reaches the landing");
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StackedFloors_ShouldRemainOnTheExplicitUpperMap(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStackedFloors();
        using GravitasNavigatorActor actor = fixture.CreateActor();
        PathQuery query = fixture.CreateQuery(algorithm);
        int invalidPathCount = 0;
        int arriveCount = 0;
        actor.Navigator.Steering!.Events.OnInvalidPath += () => invalidPathCount++;
        actor.Navigator.Steering.Events.OnArrive += () => arriveCount++;
        actor.Navigator.ApplyGuidedTrekRequest(query, TrekRate.Fast);

        var committedAddresses = new List<NavigationCellAddress>();
        if (actor.Navigator.LastCommittedCell is { } initialCell)
            committedAddresses.Add(initialCell.Address);
        actor.Navigator.CommittedCellChanged += state =>
        {
            if (state is { } cell)
                committedAddresses.Add(cell.Address);
        };

        var expectedAddress = new NavigationCellAddress(
            "gravitas-upper",
            new VoxelIndex(2, 0, 0));
        int? arrivalFrame = null;
        _output.WriteLine("Stacked-floor motion trace (Fixed64 raw X,Y,Z): frame|navigator-request|host-request|accepted-position|accepted-velocity");
        for (int frame = 1; frame <= 256; frame++)
        {
            fixture.Step(actor);
            GravitasMotionTrace.WriteFrame(_output, frame,
                actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
                actor.Navigator.Position, actor.Navigator.Velocity);
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            if ((actor.Navigator.FootPosition - fixture.EndFoot).Magnitude
                    <= actor.Navigator.NavigationProfile.ArrivalRadius
                && actor.Navigator.LastCommittedCell?.Address == expectedAddress
                && !actor.Navigator.IsGuided)
            {
                arrivalFrame = frame;
                break;
            }
        }

        arrivalFrame.Should().HaveValue(
            "the upper floor route must complete within 256 frames; final foot was {0}, committed address was {1}, guided was {2}, and heading was {3}",
            actor.Navigator.FootPosition,
            actor.Navigator.LastCommittedCell?.Address,
            actor.Navigator.IsGuided,
            actor.Navigator.Steering!.TargetDirection);
        arrivalFrame!.Value.Should().BeLessThanOrEqualTo(256);
        (actor.Navigator.FootPosition - fixture.EndFoot).Magnitude.Should()
            .BeLessThanOrEqualTo(actor.Navigator.NavigationProfile.ArrivalRadius);
        actor.Navigator.LastCommittedCell!.Value.Address.Should().Be(expectedAddress);
        actor.Navigator.IsGuided.Should().BeFalse();
        invalidPathCount.Should().Be(0);
        arriveCount.Should().Be(1);
        committedAddresses.Should().OnlyContain(
            address => address.MapId == "gravitas-upper",
            "the explicit query endpoints must keep occupancy on the upper map even when vertically aligned maps overlap");
        committedAddresses.Should().Contain(
            new NavigationCellAddress("gravitas-upper", new VoxelIndex(0, 0, 0)));
        committedAddresses.Should().Contain(
            new NavigationCellAddress("gravitas-upper", new VoxelIndex(1, 0, 0)));
        committedAddresses.Should().Contain(expectedAddress);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StairRiseOneRawUnitBeyondStepLimit_ShouldHaveNoPath(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();
        NavigationAgentProfile baseline = fixture.Profile;
        var profile = new NavigationAgentProfile(
            baseline.Shape,
            Fixed64.FromFraction(3, 4) - Fixed64.MinIncrement,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RequestStatus(fixture, fixture.CreateQueryForProfile(algorithm, profile))
            .Should().Be(NavigationGuideStatus.NoPath);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StairDropAtInclusiveLimit_ShouldHavePath(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();

        RequestStatus(
                fixture,
                fixture.CreateQueryForProfile(
                    algorithm,
                    fixture.Profile,
                    reverse: true))
            .Should().Be(NavigationGuideStatus.Success);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StairDropOneRawUnitBeyondDropLimit_ShouldHaveNoPath(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();
        NavigationAgentProfile baseline = fixture.Profile;
        var profile = new NavigationAgentProfile(
            baseline.Shape,
            baseline.MaxStepUp,
            Fixed64.FromFraction(3, 4) - Fixed64.MinIncrement,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RequestStatus(
                fixture,
                fixture.CreateQueryForProfile(
                    algorithm,
                    profile,
                    reverse: true))
            .Should().Be(NavigationGuideStatus.NoPath);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StairClearanceAtInclusiveLimits_ShouldHavePath(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();
        NavigationAgentProfile baseline = fixture.Profile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter,
                Fixed64.FromFraction(3, 4),
                baseline.Shape.RootToFootOffsetY),
            Fixed64.FromFraction(3, 4),
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RequestStatus(fixture, fixture.CreateQueryForProfile(algorithm, profile))
            .Should().Be(NavigationGuideStatus.Success);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StairPortalOneRawUnitShorterThanBody_ShouldHaveNoPath(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();
        NavigationAgentProfile baseline = fixture.Profile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                baseline.Shape.Radius,
                Fixed64.FromFraction(3, 4) + Fixed64.MinIncrement,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RequestStatus(fixture, fixture.CreateQueryForProfile(algorithm, profile))
            .Should().Be(NavigationGuideStatus.NoPath);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void StairPortalOneRawUnitNarrowerThanBody_ShouldHaveNoPath(
        PathAlgorithm algorithm)
    {
        using var fixture = GravitasNavigatorScenarioFixture.CreateStairs();
        NavigationAgentProfile baseline = fixture.Profile;
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.Quarter + Fixed64.MinIncrement,
                baseline.Shape.Height,
                baseline.Shape.RootToFootOffsetY),
            baseline.MaxStepUp,
            baseline.MaxDropDown,
            baseline.ArrivalRadius,
            baseline.AllowedMedia,
            baseline.Capabilities);

        RequestStatus(fixture, fixture.CreateQueryForProfile(algorithm, profile))
            .Should().Be(NavigationGuideStatus.NoPath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void OffAxisFlowCorridor_ShouldProduceBoundedAcceptedMotionForSingleActor(
        int radiusEighths) =>
        AssertOffAxisFlowCorridor(radiusEighths, useGroup: false);

    [Fact]
    public void OffAxisFlowCorridor_ShouldProduceBoundedAcceptedMotionForGroup() =>
        AssertOffAxisFlowCorridor(radiusEighths: 1, useGroup: true);

    private void AssertOffAxisFlowCorridor(
        int radiusEighths,
        bool useGroup)
    {
        Fixed64 radius = Fixed64.FromFraction(radiusEighths, 8);
        // A shared group target needs room for physical members to finish around
        // the point instead of requiring their body centers to overlap.
        Fixed64 arrivalRadius = useGroup
            ? radius * 2
            : Fixed64.FromFraction(1, 16);
        using var fixture = GravitasNavigatorScenarioFixture.CreateOffAxisCorridor(
            radius,
            arrivalRadius);
        Fixed64 lateralOffset = useGroup ? Fixed64.Quarter : Fixed64.Zero;
        Vector3d firstStartFoot = fixture.StartFoot + Vector3d.Forward * lateralOffset;
        Vector3d secondStartFoot = fixture.StartFoot - Vector3d.Forward * lateralOffset;
        using GravitasNavigatorActor first = fixture.CreateActor(
            firstStartFoot + Vector3d.Up * fixture.Profile.Shape.RootToFootOffsetY);
        using GravitasNavigatorActor? second = useGroup
            ? fixture.CreateActor(
                secondStartFoot + Vector3d.Up * fixture.Profile.Shape.RootToFootOffsetY)
            : null;
        PathQuery firstQuery = fixture.CreateQuery(
            PathAlgorithm.FlowField,
            firstStartFoot,
            fixture.EndFoot);
        PathQuery? secondQuery = second == null
            ? null
            : fixture.CreateQuery(
                PathAlgorithm.FlowField,
                secondStartFoot,
                fixture.EndFoot);
        fixture.Trailblazer.Guides.RequestFlowField(
                firstQuery,
                out NavigationFlowFieldLease? sampledGuide)
            .Should().Be(NavigationGuideStatus.Success);
        using NavigationFlowFieldLease diagnosticGuide = sampledGuide!.Value;
        diagnosticGuide.OriginIntegrationCost.Should().BeLessThan(
            (Fixed64)6,
            "the explicit off-axis corridor must beat the expensive native middle cell");
        diagnosticGuide.TrySample(
                firstStartFoot,
                fixture.Trailblazer.Settings.GuideSampleBudget,
                out NavigationFlowSample initialSample)
            .Should().Be(NavigationGuideStatus.Success);
        initialSample.HasTransition.Should().BeFalse();
        initialSample.Target.Should().Be(fixture.EndFoot,
            "the sample should retain the selected edge destination while its heading follows the explicit portal legs");
        initialSample.Heading.Should().NotBe(Vector3d.Zero);
        initialSample.Heading.Should().NotBe(
            (fixture.EndFoot - firstStartFoot).Normalized,
            "the cheaper explicit connection must first steer through its off-axis entry");

        int groupId = useGroup ? 71 : -1;
        first.Navigator.ApplyGuidedTrekRequest(
            firstQuery,
            TrekRate.Fast,
            groupId: groupId);
        if (second != null)
        {
            second.Navigator.ApplyGuidedTrekRequest(
                secondQuery!.Value,
                TrekRate.Fast,
                groupId: groupId);
            first.Navigator.PrewarmMovementGroup();
            second.Navigator.PrewarmMovementGroup();
        }

        int invalidPathCount = 0;
        int stuckCount = 0;
        int arriveCount = 0;
        first.Navigator.Steering!.Events.OnInvalidPath += () => invalidPathCount++;
        first.Navigator.Steering.Events.OnIsStuck += () => stuckCount++;
        first.Navigator.Steering.Events.OnArrive += () => arriveCount++;
        if (second != null)
        {
            second.Navigator.Steering!.Events.OnInvalidPath += () => invalidPathCount++;
            second.Navigator.Steering.Events.OnIsStuck += () => stuckCount++;
            second.Navigator.Steering.Events.OnArrive += () => arriveCount++;
        }

        GravitasNavigatorActor[] actors = second == null
            ? new[] { first }
            : new[] { first, second };
        var priorPositions = new Vector3d[actors.Length];
        for (int i = 0; i < actors.Length; i++)
            priorPositions[i] = actors[i].Body.Position3d;

        int? completionFrame = null;
        bool observedGroupSteeringInfluence = false;
        var observationTail = new string[8];
        _output.WriteLine("Off-axis motion trace (Fixed64 raw X,Y,Z): frame|first.navigator-request|first.host-request|first.accepted-position|first.accepted-velocity"
            + (second == null ? string.Empty : "|second.navigator-request|second.host-request|second.accepted-position|second.accepted-velocity"));
        const int frameBudget = 384;
        for (int frame = 1; frame <= frameBudget; frame++)
        {
            Vector3d sampledPathHeading = Vector3d.Zero;
            bool hasLocalSteering = false;
            if (useGroup)
            {
                diagnosticGuide.TrySample(
                        first.Navigator.FootPosition,
                        fixture.Trailblazer.Settings.GuideSampleBudget,
                        out NavigationFlowSample pathSample)
                    .Should().Be(NavigationGuideStatus.Success);
                Vector3d localSteering = first.Navigator.Steering!
                    .ComputeCombinedSteering(
                        first.Navigator.Position,
                        first.Navigator.Velocity,
                        first.Navigator.Speed,
                        first.Navigator.Radius,
                        first.Navigator.GlobalId);
                sampledPathHeading = pathSample.Heading;
                hasLocalSteering = localSteering != Vector3d.Zero;
            }

            fixture.Step(actors);
            if (second == null)
            {
                GravitasMotionTrace.WriteFrame(_output, frame,
                    first.NavigatorRequestedPosition, first.HostRequestedPosition,
                    first.Navigator.Position, first.Navigator.Velocity);
            }
            else
            {
                GravitasMotionTrace.WriteFrame(_output, frame,
                    first.NavigatorRequestedPosition, first.HostRequestedPosition,
                    first.Navigator.Position, first.Navigator.Velocity,
                    second.NavigatorRequestedPosition, second.HostRequestedPosition,
                    second.Navigator.Position, second.Navigator.Velocity);
            }
            if (hasLocalSteering
                && sampledPathHeading != Vector3d.Zero
                && first.Navigator.Steering!.TargetDirection != Vector3d.Zero)
            {
                observedGroupSteeringInfluence |=
                    (first.Navigator.Steering.TargetDirection.Normalized
                        - sampledPathHeading).Magnitude > Fixed64.FromRaw(16);
            }
            NavigationGuideStatus diagnosticStatus = diagnosticGuide.TrySample(
                first.Navigator.FootPosition,
                fixture.Trailblazer.Settings.GuideSampleBudget,
                out NavigationFlowSample diagnosticSample);
            observationTail[(frame - 1) % observationTail.Length] =
                $"f{frame}:foot={first.Navigator.FootPosition},requested={first.NavigatorRequestedPosition},host={first.HostRequestedPosition},target={diagnosticSample.Target},heading={diagnosticSample.Heading},status={diagnosticStatus},steering={first.Navigator.Steering!.TargetDirection},velocity={first.Navigator.Velocity},guided={first.Navigator.IsGuided}";
            bool allComplete = true;
            for (int i = 0; i < actors.Length; i++)
            {
                GravitasNavigatorActor actor = actors[i];
                Vector3d displacement = actor.Body.Position3d - priorPositions[i];
                Fixed64 maximumFrameDisplacement = actor.Navigator.Motor!
                    .Handler.Move.MaxFastSpeed
                    * fixture.Trailblazer.DeltaTime
                    + Fixed64.FromRaw(16);
                displacement.Magnitude.Should().BeLessThanOrEqualTo(
                    maximumFrameDisplacement,
                    "accepted motion must remain within the configured fast speed at frame {0}",
                    frame);
                priorPositions[i] = actor.Body.Position3d;
                actor.Navigator.Position.Should().Be(actor.Body.Position3d);
                actor.Navigator.Steering!.IsStuck.Should().BeFalse();
                allComplete &= !actor.Navigator.IsGuided;
            }

            if (allComplete)
            {
                completionFrame = frame;
                break;
            }
        }

        completionFrame.Should().HaveValue(
            "every actor should finish; first={0}/{1}/{2}/{3}, second={4}/{5}/{6}/{7}, invalid={8}, stuck={9}, arrivals={10}; observation tail: {11}",
            first.Navigator.FootPosition,
            first.Navigator.Steering!.TargetDirection,
            first.Navigator.LastCommittedCell?.Address,
            first.Navigator.IsGuided,
            second?.Navigator.FootPosition,
            second?.Navigator.Steering!.TargetDirection,
            second?.Navigator.LastCommittedCell?.Address,
            second?.Navigator.IsGuided,
            invalidPathCount,
            stuckCount,
            arriveCount,
            string.Join("; ", observationTail));
        completionFrame!.Value.Should().BeLessThanOrEqualTo(frameBudget);
        invalidPathCount.Should().Be(0);
        stuckCount.Should().Be(0);
        arriveCount.Should().Be(actors.Length);
        if (useGroup)
        {
            observedGroupSteeringInfluence.Should().BeTrue(
                "the group case must exercise local steering rather than duplicate two independent Flow traversals");
        }
        var destination = new NavigationCellAddress(
            "gravitas-off-axis",
            new VoxelIndex(2, 0, 0));
        for (int i = 0; i < actors.Length; i++)
        {
            actors[i].Navigator.LastCommittedCell!.Value.Address.Should().Be(destination);
            (actors[i].Navigator.FootPosition - fixture.EndFoot).Magnitude.Should()
                .BeLessThanOrEqualTo(actors[i].Navigator.NavigationProfile.ArrivalRadius);
        }
    }

    private static NavigationGuideStatus RequestStatus(
        GravitasNavigatorScenarioFixture fixture,
        PathQuery query)
    {
        if (query.Algorithm == PathAlgorithm.AStar)
        {
            NavigationGuideStatus status = fixture.Trailblazer.Guides.RequestGuide(
                query,
                out NavigationGuideLease? guide);
            guide?.Dispose();
            return status;
        }

        NavigationGuideStatus flowStatus = fixture.Trailblazer.Guides.RequestFlowField(
            query,
            out NavigationFlowFieldLease? flow);
        flow?.Dispose();
        return flowStatus;
    }

}
