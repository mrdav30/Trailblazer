using System;
using Chronicler;
using Chronicler.Serialization;
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

public class NavSteeringAcquisitionTests
{
    [Fact]
    public void ExhaustedClockLeavesScheduledAcquisitionPendingUntilExplicitReset()
    {
        using var context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        TimingTestUtility.SetTime(context, long.MaxValue);
        context.Guides.Schedule(query, out var ticket).Should().Be(NavigationGuideStatus.Pending);
        using var owned = ticket!;
        Assert.Throws<InvalidOperationException>(context.Simulate);
        owned.Status.Should().Be(NavigationGuideStatus.Pending);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        context.FrameCount.Should().Be(long.MaxValue);
        owned.TryTakeGuide(out _).Should().BeFalse();
        context.Reset();
        owned.Status.Should().Be(NavigationGuideStatus.Cancelled);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
    }

    [Fact]
    public void ValidationShell_ModePolicyMayBePreparedBeforeBindingWithoutCreatingRuntimeWork()
    {
        var steering = NavSteering.CreateSerializationValidationShell();
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Scheduled);
        steering.CurrentQuery.Should().BeNull();
        steering.ShouldMove.Should().BeFalse();
        using var context = CreateContext(PathAlgorithm.AStar, out var query);
        steering.BindContext(context);
        steering.ApplyPathQuery(query);
        steering.GetHeading(new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent }, out _)
            .Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        steering.StopMove();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedDirectTicket_AdoptionMustRetryCapacityOrUseGraphOnlyAfterBlockingPublication(bool blocked)
    {
        using var context = CreateContext(PathAlgorithm.AStar, out var query, neutral: true);
        var steering = new NavSteering(context) { CanPathfind = blocked };
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(host, out _);
        var ticket = GetTicket(steering)!;
        for (int i = 0; i < 32 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        ticket.IsDirect.Should().BeTrue();
        steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
        context.Simulate();
        var held = new System.Collections.Generic.List<NavigationWorldGraphLease>();
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        try
        {
            if (blocked)
            {
                var changed = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
                    default, Fixed64.One, Fixed64.One, Fixed64.One);
                var delta = new NavigationMapOverlayDelta("acquisition", cells: new[]
                    { NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), changed) });
                var publication = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(
                    new NavigationOverlayTransaction(new[] { delta })), 3, context.FrameCount + 1);
                context.Pathing.Admit(publication).Should().BeTrue();
                GuidedPathTestScene.AdvanceUntilApplied(context, publication.Receipt);
            }
            else
            {
                for (int i = 0; i < context.Settings.MaxConcurrentSnapshotLeases; i++)
                {
                    var snapshot = context.Pathing.NavigationGraphStore.TryAcquire();
                    if (snapshot == null) break;
                    held.Add(snapshot);
                }
                context.Pathing.NavigationGraphStore.TryAcquire().Should().BeNull();
            }
            steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
            steering.LastAcquisitionStatus.Should().Be(blocked
                ? NavigationGuideStatus.LocalRecoveryRequired : NavigationGuideStatus.CapacityExceeded);
            steering.ShouldMove.Should().BeTrue();
            steering.HasLineOfSightPath.Should().BeFalse();
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
            steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        }
        finally { foreach (var snapshot in held) snapshot.Dispose(); }
        for (int i = 0; i < 128 && !steering.HasNavigationGuidance && !steering.HasLineOfSightPath; i++)
        {
            context.Simulate();
            steering.GetHeading(host, out _);
        }
        steering.HasNavigationGuidance.Should().Be(blocked);
        steering.HasLineOfSightPath.Should().Be(!blocked);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(blocked ? 1 : 0);
        invalid.Should().Be(0);
        arrived.Should().Be(0);
        steering.StopMove();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Fact]
    public void ScheduledRestoreIntoFreshShell_MustBindNewLifetimeAndAcquireWithoutInheritedOwnership()
    {
        using var context = CreateContext(PathAlgorithm.AStar, out var query);
        var source = CreateAgent(context, query);
        source.Steering!.GetHeading(source, out _);
        object record = SerializationUtility.SerializeRecord(source, false);
        var target = new TestNavigator(context);
        target.Setup(query.Start.Position, query.Agent);
        target.Initialize(new TrekCondition { Medium = TraversalMedium.Solid, SurfaceLevel = query.Start.Position.Y });
        target.Steering!.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Immediate);
        SerializationUtility.PopulateRecord(target, record, false);
        target.Steering.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Scheduled);
        target.Steering.HasNavigationGuidance.Should().BeFalse();
        GetTicket(target.Steering).Should().BeNull();
        target.Steering.GetHeading(target, out _).Should().Be(Vector3d.Zero);
        GetTicket(target.Steering).Should().NotBeSameAs(GetTicket(source.Steering));
        GetTicket(target.Steering).Should().NotBeNull();
        for (int i = 0; i < 128 && !target.Steering.HasNavigationGuidance; i++)
        {
            context.Simulate();
            target.Steering.GetHeading(target, out _);
        }
        target.Steering.HasNavigationGuidance.Should().BeTrue();
        source.Reset();
        target.Reset();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledRestore_ImmediateModeBeforeFirstHeadingMustDeferThenAcquire(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out var query);
        var source = CreateAgent(context, query);
        object record = SerializationUtility.SerializeRecord(source, false);
        var target = new TestNavigator(context);
        target.Setup(query.Start.Position, query.Agent);
        target.Initialize(new TrekCondition { Medium = TraversalMedium.Solid, SurfaceLevel = query.Start.Position.Y });
        SerializationUtility.PopulateRecord(target, record, false);

        target.Steering!.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        target.Steering.GetHeading(target, out _).Should().Be(Vector3d.Zero);
        target.Steering.ShouldMove.Should().BeTrue();
        target.Steering.IsAwaitingGuidance.Should().BeTrue();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);

        context.Simulate();
        target.Steering.GetHeading(target, out _).X.Should().BeGreaterThan(Fixed64.Zero);
        target.Steering.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Immediate);
        target.Steering.IsAwaitingGuidance.Should().BeFalse();
        target.Steering.HasNavigationGuidance.Should().BeTrue();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        source.Reset();
        target.Reset();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ScheduledIdleOwner_RebindMustPreservePolicyWithoutCreatingIntentOrWork()
    {
        using var first = CreateContext(PathAlgorithm.AStar, out var query);
        using var second = CreateContext(PathAlgorithm.AStar, out _);
        var steering = new NavSteering(first);
        ConfigureScheduled(steering);
        steering.BindContext(second);
        steering.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Scheduled);
        steering.CurrentQuery.Should().BeNull();
        steering.ShouldMove.Should().BeFalse();
        int invalid = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.ValidateActualMedium(TraversalMedium.Gas);
        steering.LastAcquisitionStatus.Should().BeNull();
        invalid.Should().Be(0, "an idle scheduled owner has no traversal contract to violate");
        steering.GetHeading(new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent }, out _)
            .Should().Be(Vector3d.Zero);
        first.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        second.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        steering.StopMove();
    }

    [Fact]
    public void ScheduledDirectOnly_TotalTraceBudgetFailureMustStopWithoutGraphFallback()
    {
        using var context = CreateContext(PathAlgorithm.AStar, out var original, neutral: true);
        var query = new PathQuery(original.Start, original.End, original.Agent, original.AreaPolicy,
            original.Traversal, original.Algorithm,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 4096, 0), false);
        var steering = new NavSteering(context) { CanPathfind = false };
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = Fixed64.One };
        int invalid = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        for (int i = 0; i < 16 && steering.ShouldMove; i++)
        {
            steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
            context.Simulate();
        }
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.BudgetExceeded);
        steering.ShouldMove.Should().BeFalse();
        invalid.Should().Be(1);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(0);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }
    [Fact]
    public void CompletedGraphTicket_MovedOutsideMapMustReleaseTransferredRouteAndRetryLater()
    {
        using var context = CreateContext(PathAlgorithm.AStar, out var query);
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var original = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(original, out _);
        var ticket = GetTicket(steering)!;
        for (int i = 0; i < 64 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        ticket.IsDirect.Should().BeFalse();
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Forward * 3)
            { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
        context.Simulate();
        steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.LocalRecoveryRequired);
        steering.ShouldMove.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        for (int i = 0; i < 3; i++) steering.GetHeading(original, out _).Should().Be(Vector3d.Zero);
        for (int i = 0; i < 64 && !steering.HasNavigationGuidance; i++)
        {
            context.Simulate();
            steering.GetHeading(original, out _);
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.StopMove();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void ScheduledOrdinaryRoute_StaleOrSnapshotPressureMustQueueRecovery(PathAlgorithm algorithm, bool exhaustSnapshots)
    {
        using var context = CreateContext(algorithm, out var query);
        var agent = CreateAgent(context, query);
        var steering = agent.Steering!;
        steering.ConfigurePathRechecks(10000);
        for (int i = 0; i < 64 && !steering.HasNavigationGuidance; i++)
        {
            steering.GetHeading(agent, out _);
            context.Simulate();
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        var held = new System.Collections.Generic.List<NavigationWorldGraphLease>();
        try
        {
            steering.CanMove = false;
            if (exhaustSnapshots)
            {
                var store = context.Pathing.NavigationGraphStore;
                for (int i = 0; i < context.Settings.MaxConcurrentSnapshotLeases; i++)
                {
                    var snapshot = store.TryAcquire();
                    if (snapshot == null) break;
                    held.Add(snapshot);
                }
                store.TryAcquire().Should().BeNull();
            }
            else
            {
                var changed = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
                    default, (Fixed64)2, Fixed64.One, Fixed64.One);
                var delta = new NavigationMapOverlayDelta("acquisition", cells: new[]
                    { NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), changed) });
                var publication = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(
                    new NavigationOverlayTransaction(new[] { delta })), 3, context.FrameCount + 1);
                context.Pathing.Admit(publication).Should().BeTrue();
                GuidedPathTestScene.AdvanceUntilApplied(context, publication.Receipt);
            }
            steering.CanMove = true;
            steering.GetHeading(agent, out var action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.ShouldMove.Should().BeTrue();
            steering.HasNavigationGuidance.Should().BeFalse();
            steering.IsAwaitingGuidance.Should().BeTrue();
        }
        finally { foreach (var snapshot in held) snapshot.Dispose(); }
        for (int i = 0; i < 128 && !steering.HasNavigationGuidance; i++)
        {
            context.Simulate();
            steering.GetHeading(agent, out _);
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        agent.Reset();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void CompletedGraphTicket_PublicationBeforeAdoptionMustDeferAndRecover(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out var query);
        var agent = CreateAgent(context, query);
        var steering = agent.Steering!;
        steering.GetHeading(agent, out _);
        var ticket = GetTicket(steering)!;
        for (int frame = 0; frame < 64 && ticket.Status == NavigationGuideStatus.Pending; frame++) context.Simulate();
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        ticket.IsDirect.Should().BeFalse();
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(1);
        var changed = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, (Fixed64)2, Fixed64.One, Fixed64.One);
        var delta = new NavigationMapOverlayDelta("acquisition", cells: new[]
            { NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), changed) });
        var publication = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(new[] { delta })), 3, context.FrameCount + 1);
        context.Pathing.Admit(publication).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, publication.Receipt);
        ticket.Status.Should().Be(NavigationGuideStatus.Success, "the stored outcome is not refreshed until transfer");
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Stale);
        steering.ShouldMove.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.IsAwaitingGuidance.Should().BeTrue();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        for (int repeat = 0; repeat < 3; repeat++) steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        for (int frame = 0; frame < 64 && !steering.HasNavigationGuidance; frame++)
        {
            context.Simulate();
            steering.GetHeading(agent, out _);
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Success);
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void DirectOnlyCompletedTicket_MovedOutsideMapMustStopWithoutGraphFallback(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out var query, neutral: true);
        var steering = new NavSteering(context) { CanPathfind = false };
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var original = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(original, out _).Should().Be(Vector3d.Zero);
        var ticket = GetTicket(steering)!;
        for (int frame = 0; frame < 32 && ticket.Status == NavigationGuideStatus.Pending; frame++) context.Simulate();
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        ticket.IsDirect.Should().BeTrue();
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Forward * 3)
            { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        int invalid = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        for (int frame = 0; frame < 4; frame++)
        {
            steering.GetHeading(moved, out var action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            context.Simulate();
        }
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.NoPath);
        steering.ShouldMove.Should().BeFalse();
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        invalid.Should().Be(1);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(0);
        steering.StopMove();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledWaiting_ShouldContinueActualGravity(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out PathQuery groundQuery);
        Vector3d start = groundQuery.Start.Position + Vector3d.Up * 2;
        var profile = new NavigationAgentProfile(groundQuery.Agent.Shape, Fixed64.Zero, Fixed64.Zero,
            Fixed64.Zero, TraversalMedia.Gas, TraversalCapability.None);
        var query = new PathQuery(new NavigationEndpoint(start, "acquisition"),
            new NavigationEndpoint(start + Vector3d.Right, "acquisition"), profile, groundQuery.AreaPolicy,
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas), algorithm, groundQuery.Budget, false);
        var agent = new TestNavigator(context);
        agent.Setup(start, profile);
        agent.Initialize(new TrekCondition { Medium = TraversalMedium.Gas, SurfaceLevel = groundQuery.Start.Position.Y });
        agent.Steering!.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        agent.ApplyGuidedTrekRequest(query);
        agent.Simulate();
        agent.Steering.IsAwaitingGuidance.Should().BeTrue();
        agent.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        agent.CommitFrameMotion();
        agent.Position.Y.Should().BeLessThan(start.Y);
        agent.Velocity.Y.Should().BeLessThan(Fixed64.Zero);
        agent.Steering.ShouldMove.Should().BeTrue();
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ScheduledWaiting_ShouldContinueActualPlatformTransfer(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out PathQuery query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        var agent = CreateAgent(context, query);
        agent.SetGroundContact(query.Start.Position.Y, Vector3d.Up,
            platform: new PlatformSnapshot(7, Fixed4x4.Identity), motionTransfer: MotionTransfer.InitTransfer,
            updateMotorState: true);
        agent.Simulate();
        agent.ResolveAcceptedFramePosition(query.Start.Position);
        agent.CommitFrameMotion();
        var module = agent.Motor!.Handler.Platform;
        var platform = module.ActivePlatform!.Value;
        platform.Transform.SetTranslation(Vector3d.Right);
        module.ActivePlatform = platform;
        context.Simulate();
        agent.Simulate();
        agent.Steering!.IsAwaitingGuidance.Should().BeTrue();
        agent.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        agent.RequestedFramePosition.X.Should().Be(Fixed64.One);
        agent.ResolveAcceptedFramePosition(new Vector3d(agent.RequestedFramePosition.X,
            query.Start.Position.Y, agent.RequestedFramePosition.Z));
        agent.CommitFrameMotion();
        agent.Position.X.Should().Be(Fixed64.One);
        agent.LastCommittedCell!.Value.Address.Index.Should().Be(new VoxelIndex(1, 0, 0));
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
#endif
    public void SuccessfulRestoreAfterBlockedDirectAdoption_ShouldStartDirectFirst(PathAlgorithm algorithm, bool memoryPack)
    {
        using var context = CreateContext(algorithm, out PathQuery query, neutral: true);
        var agent = CreateAgent(context, query);
        object saved = SerializationUtility.SerializeRecord(agent, memoryPack);
        agent.Steering!.GetHeading(agent, out _);
        NavigationGuideRequest ticket = GetTicket(agent.Steering)!;
        for (int i = 0; i < 64 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        ticket.IsDirect.Should().BeTrue();
        agent.SetTestPosition(query.Start.Position + Vector3d.Forward * 3);
        agent.Steering.GetHeading(agent, out _);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        agent.Steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.LocalRecoveryRequired);
        SerializationUtility.PopulateRecord(agent, saved, memoryPack);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _);
        NavigationGuideRequest restored = GetTicket(agent.Steering)!;
        for (int i = 0; i < 64 && restored.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        restored.Status.Should().Be(NavigationGuideStatus.Success);
        restored.IsDirect.Should().BeTrue("a new durable session must not inherit the old graph-only retry flavor");
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false, false)]
    [InlineData(PathAlgorithm.AStar, false, true)]
    [InlineData(PathAlgorithm.FlowField, false, false)]
    [InlineData(PathAlgorithm.FlowField, false, true)]
    [InlineData(PathAlgorithm.AStar, true, false)]
    [InlineData(PathAlgorithm.AStar, true, true)]
    [InlineData(PathAlgorithm.FlowField, true, false)]
    [InlineData(PathAlgorithm.FlowField, true, true)]
    public void PathingResetOfCompletedTicket_ShouldRetryWithoutRevivingResultOrLosingIntent(
        PathAlgorithm algorithm, bool direct, bool registered)
    {
        using var context = CreateContext(algorithm, out PathQuery query, neutral: direct);
        var agent = CreateAgent(context, query);
        var steering = agent.Steering!;
        steering.AddToMovementGroup(42);
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        steering.GetHeading(agent, out _);
        var original = GetTicket(steering)!;
        for (int frame = 0; frame < 64 && original.Status == NavigationGuideStatus.Pending; frame++)
            context.Simulate();
        original.Status.Should().Be(NavigationGuideStatus.Success);
        original.IsDirect.Should().Be(direct);
        if (registered)
        {
            steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(1);
        }
        object lifetime = context.ClockLifetime;
        context.Pathing.Reset();
        context.ClockLifetime.Should().BeSameAs(lifetime);
        original.Status.Should().Be(NavigationGuideStatus.Success, "completed public outcomes survive disposal");
        original.Owner.Should().BeNull();
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        PublishAcquisitionMap(context, direct);

        using (new FluentAssertions.Execution.AssertionScope())
        {
            steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
            steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Cancelled);
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.HasNavigationGuidance.Should().BeFalse();
            steering.HasLineOfSightPath.Should().BeFalse();
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
            for (int repeat = 0; repeat < 3; repeat++) steering.GetHeading(agent, out _);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
            context.Simulate();
            steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
            steering.ShouldMove.Should().BeTrue();
            steering.CurrentQuery.Should().Be(query);
            steering.MovementGroupID.Should().Be(42);
            invalid.Should().Be(0);
            arrived.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        }
        var replacement = GetTicket(steering)!;
        replacement.Should().NotBeSameAs(original);
        for (int repeat = 0; repeat < 3; repeat++) steering.GetHeading(agent, out _);
        GetTicket(steering).Should().BeSameAs(replacement);
        for (int frame = 0; frame < 64 && steering.IsAwaitingGuidance; frame++)
        {
            context.Simulate();
            steering.GetHeading(agent, out _);
        }
        steering.IsAwaitingGuidance.Should().BeFalse();
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Success);
        steering.HasNavigationGuidance.Should().Be(!direct);
        steering.HasLineOfSightPath.Should().Be(direct);
        steering.ShouldMove.Should().BeTrue();
        invalid.Should().Be(0);
        arrived.Should().Be(0);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        original.TryTakeGuide(out _).Should().BeFalse();
        original.TryTakeFlowField(out _).Should().BeFalse();
        agent.Reset();
    }

    [Fact]
    public void SwitchingAdoptedDirectGuidanceToScheduled_ShouldRememberLifetimeBeforeFirstHeading()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query, neutral: true);
        var steering = new NavSteering(context);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = Fixed64.One };
        steering.ApplyPathQuery(query);
        steering.GetHeading(agent, out _).X.Should().BeGreaterThan(Fixed64.Zero);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.HasLineOfSightPath.Should().BeTrue();
        context.Reset();
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.IsAwaitingGuidance.Should().BeTrue();
        steering.StopMove();
    }

    [Fact]
    public void ValidationTurns_ShouldAdmitLaterCompletedOwnerAlongsideRecurringGuidesBeyondTicketCapacity()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query,
            scheduling: new NavigationSchedulingSettings(maxRetainedRequests: 1, operationStepLimit: 1));
        context.Guides.RequestFlowField(query, out var firstGuide).Should().Be(NavigationGuideStatus.Success);
        context.Guides.RequestFlowField(query, out var secondGuide).Should().Be(NavigationGuideStatus.Success);
        using var firstLease = firstGuide!.Value;
        using var secondLease = secondGuide!.Value;
        var scheduler = context.Guides.Scheduler;
        var first = new NavigationValidationInterest();
        var second = new NavigationValidationInterest();
        scheduler.RegisterValidation(first).Should().BeTrue();
        scheduler.RegisterValidation(second).Should().BeTrue();
        context.Guides.Schedule(query, out var laterTicket).Should().Be(NavigationGuideStatus.Pending);
        using var ticket = laterTicket!;
        int recurringTurns = 0;
        for (int frame = 0; frame < 256 && ticket.Status == NavigationGuideStatus.Pending; frame++)
        {
            context.Simulate();
            scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
            if (scheduler.TryConsumeValidationTurn(second)) recurringTurns++;
            // The first recurring owner is paused and never consumes its turns.
        }
        recurringTurns.Should().BeGreaterThan(0);
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        var later = new NavigationValidationInterest();
        scheduler.RegisterValidation(later).Should().BeTrue();
        scheduler.RegisterValidation(later).Should().BeTrue();
        scheduler.RetainedValidationInterestCount.Should().Be(3);
        int lateTurns = 0;
        for (int frame = 0; frame < 12; frame++)
        {
            context.Simulate();
            scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
            if (scheduler.TryConsumeValidationTurn(later)) lateTurns++;
        }
        lateTurns.Should().BeGreaterThan(0);
        scheduler.ReleaseValidation(second);
        scheduler.ReleaseValidation(first);
        // Drain bounded cohort cleanup, then completed retention alone must not reserve queue turns.
        for (int frame = 0; frame < 12; frame++) context.Simulate();
        for (int frame = 0; frame < 3; frame++)
        {
            context.Simulate();
            scheduler.LastFrameOperationCount.Should().Be(1);
            scheduler.TryConsumeValidationTurn(later).Should().BeTrue();
        }
        scheduler.ReleaseValidation(later);
    }

    [Theory]
    [InlineData(64L)]
    [InlineData(2147483646L)]
    public void FlowAdoption_LocalRecovery_ShouldReleaseAndRetryCurrentFootOnLaterFrame(long initialFrame)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        TimingTestUtility.SetTime(context, initialFrame);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        var ticket = GetTicket(agent.Steering)!;
        for (int i = 0; i < 40 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        agent.SetTestPosition(query.End.Position + Vector3d.Right);
        agent.Steering.GetHeading(agent, out _);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        agent.Steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.LocalRecoveryRequired);
        agent.Steering.ShouldMove.Should().BeTrue();
        agent.Steering.HasNavigationGuidance.Should().BeFalse();
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        agent.Steering.GetHeading(agent, out _);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _);
        agent.Steering.CurrentQuery!.Value.Start.Position.Should().Be(agent.FootPosition);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        agent.Reset();
    }

    [Fact]
    public void FlowAdoption_TransientGraphCapacity_ShouldReleaseAndRetryNotAdvertiseReady()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        var ticket = GetTicket(agent.Steering)!;
        for (int i = 0; i < 40 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        agent.Steering.GetHeading(agent, out _);
        context.Simulate();
        var held = new System.Collections.Generic.List<NavigationWorldGraphLease>();
        while (context.Pathing.NavigationGraphStore.TryAcquire() is NavigationWorldGraphLease lease) held.Add(lease);
        try
        {
            agent.Steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
            agent.Steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.CapacityExceeded);
            agent.Steering.HasNavigationGuidance.Should().BeFalse();
            agent.Steering.ShouldMove.Should().BeTrue();
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        }
        finally { foreach (var lease in held) lease.Dispose(); }
        agent.Reset();
    }

    [Fact]
    public void NewDestinationAfterSubmission_ShouldWaitUntilNextFrameBeforeNewTicket()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        NavigationGuideRequest original = GetTicket(agent.Steering)!;
        agent.ApplyGuidedTrekRequest(query);
        agent.Steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        original.Status.Should().Be(NavigationGuideStatus.Cancelled);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void WaitingMotorAndHostDisplacement_ShouldCommitVelocityAndCurrentCell(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.ConstrainTestMotionToSurface(query.Start.Position.Y);
        agent.Simulate();
        agent.Steering!.IsAwaitingGuidance.Should().BeTrue();
        agent.FrameRequest.Direction.Should().Be(Vector3d.Zero);
        agent.Motor!.TraversalInProgress.Should().BeTrue();
        agent.AddLocomotionDisplacement(Vector3d.Right);
        agent.CommitFrameMotion();
        agent.Velocity.X.Should().BeGreaterThan(Fixed64.Zero);
        agent.Motor.TraversalInProgress.Should().BeFalse();
        agent.LastCommittedCell!.Value.Address.Index.Should().Be(new VoxelIndex(1, 0, 0));
        agent.LastCommittedCell.Value.SimulationFrame.Should().Be(context.FrameCount);
        agent.Steering.ShouldMove.Should().BeTrue();
        agent.Reset();
    }
    [Fact]
    public void CancelDuringDirectPhase_ShouldNotRunClearedLeaderIntentForSurvivingFollower()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        context.Guides.Scheduler.Schedule(query, out var leader, NavigationGuideRequestFlavor.NavigatorDirectFirst);
        context.Guides.Scheduler.Schedule(query, out var follower, NavigationGuideRequestFlavor.NavigatorDirectFirst);
        for (int i = 0; i < 30 && leader!.Phase != NavigationGuideRequestPhase.Direct; i++) context.Simulate();
        leader!.Phase.Should().Be(NavigationGuideRequestPhase.Direct);
        leader.Dispose();
        for (int i = 0; i < 100 && follower!.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        follower!.Status.Should().Be(NavigationGuideStatus.Success);
        follower.Dispose();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
    }

    [Fact]
    public void DirectOnlyModeChangeToImmediate_ShouldResumeAfterOneFrame()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query, neutral: true);
        var steering = new NavSteering(context) { CanPathfind = false };
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent };
        steering.ApplyPathQuery(query);
        steering.GetHeading(agent, out _);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Simulate();
        steering.GetHeading(agent, out _).X.Should().BeGreaterThan(Fixed64.Zero);
        steering.StopMove();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlowAdoption_ShouldUseItsFirstSampleOnceAndClearItAcrossVirtualOverrides(bool bypassFirst)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var steering = new FirstSampleSteering(context, bypassFirst);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.ApplyPathQuery(query);
        Vector3d heading = Vector3d.Zero;
        for (int i = 0; i < 40 && heading == Vector3d.Zero; i++)
        {
            context.Simulate();
            heading = steering.GetHeading(agent, out _);
        }
        heading.Normalized.Should().Be(bypassFirst ? Vector3d.Forward : Vector3d.Right);
        steering.FirstSampleWasVisited.Should().BeTrue();
        var arrivedAgent = new MockSteerAgent(query.End.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(arrivedAgent, out _).Should().Be(Vector3d.Zero);
        steering.IsAtDestination.Should().BeTrue();
        steering.StopMove();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FlowAdoption_TargetHookChangesMustDeferThenValidateRetainedGuidance(bool rawWorldChange, bool resetGuideCache)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var steering = new MutatingSampleSteering(context, rawWorldChange, resetGuideCache);
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        try
        {
            Vector3d heading = Vector3d.Zero;
            NavigationTransitionInstruction? action = null;
            for (int frame = 0; frame < 128 && !steering.Mutated; frame++)
            {
                context.Simulate();
                heading = steering.GetHeading(host, out action);
            }

            steering.Mutated.Should().BeTrue();
            heading.Should().Be(Vector3d.Zero, "adoption already consumed this heading's sample");
            action.Should().BeNull();
            steering.ShouldMove.Should().BeTrue();
            steering.CurrentQuery.Should().Be(query);
            steering.HasNavigationGuidance.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.StuckChecks.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);

            steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.StuckChecks.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0,
                "a deferred sample must not start replacement work in the same frame");

            context.Simulate();
            steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.HasNavigationGuidance.Should().BeFalse();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.StuckChecks.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(1,
                "ordinary next-frame dependency validation retires the stale guide and schedules its replacement");
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        }
        finally
        {
            steering.StopMove();
        }
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FlowAdoption_UnrelatedPublicationMustWaitWithoutReplacingCurrentGuidance(
        bool switchAcquisitionMode, bool replaceDeferredOutput)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var unrelatedPolicy = new NavigationAreaPolicy(new NavigationAreaPolicyKey("unrelated-acquisition", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        var steering = new MutatingSampleSteering(context, false, false, unrelatedPolicy, replaceDeferredOutput);
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        try
        {
            Vector3d heading = Vector3d.Zero;
            NavigationTransitionInstruction? action = null;
            for (int frame = 0; frame < 128 && !steering.Mutated; frame++)
            {
                context.Simulate();
                heading = steering.GetHeading(host, out action);
            }
            steering.Mutated.Should().BeTrue();
            heading.Should().Be(Vector3d.Zero);
            action.Should().BeNull("a hook cannot expose an action after deferring its sample");
            steering.TargetDirection.Should().Be(Vector3d.Zero);
            steering.HasNavigationGuidance.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.StuckChecks.Should().Be(0);
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);

            if (switchAcquisitionMode)
            {
                steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
                steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
                action.Should().BeNull();
                steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
            }
            steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.StuckChecks.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);

            context.Simulate();
            steering.GetHeading(host, out action).X.Should().BeGreaterThan(Fixed64.Zero,
                "an unrelated publication leaves the adopted lease usable on its next normal sample");
            action.Should().BeNull();
            steering.HasNavigationGuidance.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeFalse();
            steering.StuckChecks.Should().Be(1);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);
        }
        finally
        {
            steering.StopMove();
        }
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    private sealed class MutatingSampleSteering : NavSteering
    {
        private readonly TrailblazerWorldContext _testContext;
        private readonly bool _rawWorldChange;
        private readonly bool _resetGuideCache;
        private readonly NavigationAreaPolicy? _unrelatedPolicy;
        private readonly bool _replaceDeferredOutput;
        internal bool Mutated { get; private set; }
        internal int StuckChecks { get; private set; }

        internal MutatingSampleSteering(TrailblazerWorldContext context, bool rawWorldChange, bool resetGuideCache,
            NavigationAreaPolicy? unrelatedPolicy = null, bool replaceDeferredOutput = false) : base(context)
        {
            _testContext = context;
            _rawWorldChange = rawWorldChange;
            _resetGuideCache = resetGuideCache;
            _unrelatedPolicy = unrelatedPolicy;
            _replaceDeferredOutput = replaceDeferredOutput;
        }

        protected override Vector3d FindTargetDirection(Vector3d position, out NavigationTransitionInstruction? pending)
        {
            if (!Mutated)
            {
                if (_rawWorldChange)
                {
                    var graph = _testContext.Pathing.NavigationGraphStore.Current;
                    ulong sequence = _testContext.World.ChangeSequence;
                    VoxelGrid grid = _testContext.World.ActiveGrids[0];
                    grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? voxel).Should().BeTrue();
                    grid.TryAddObstacle(voxel!, _testContext.World.AllocateObstacleToken()).Should().BeTrue();
                    _testContext.World.ChangeSequence.Should().BeGreaterThan(sequence);
                    _testContext.Pathing.NavigationGraphStore.Current.Should().BeSameAs(graph,
                        "the raw mutation has not been composed into a published graph");
                }
                else if (_resetGuideCache)
                {
                    var graph = _testContext.Pathing.NavigationGraphStore.Current;
                    ulong sequence = _testContext.World.ChangeSequence;
                    _testContext.Pathing.NavigationFlowAdmissionGate.PayloadCache.Reset();
                    _testContext.World.ChangeSequence.Should().Be(sequence);
                    _testContext.Pathing.NavigationGraphStore.Current.Should().BeSameAs(graph);
                }
                else if (_unrelatedPolicy != null)
                {
                    long graphVersion = _testContext.Pathing.NavigationGraphStore.Current.GraphVersion;
                    var publication = new NavigationAreaPolicyCommitOperation(_unrelatedPolicy, 3, _testContext.FrameCount + 1);
                    _testContext.Pathing.Admit(publication).Should().BeTrue();
                    GuidedPathTestScene.AdvanceUntilApplied(_testContext, publication.Receipt);
                    _testContext.Pathing.NavigationGraphStore.Current.GraphVersion.Should().BeGreaterThan(graphVersion);
                }
                else
                {
                    var delta = new NavigationMapOverlayDelta("acquisition", cells: new[]
                        { NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0)) });
                    var publication = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(
                        new NavigationOverlayTransaction(new[] { delta })), 3, _testContext.FrameCount + 1);
                    _testContext.Pathing.Admit(publication).Should().BeTrue();
                    GuidedPathTestScene.AdvanceUntilApplied(_testContext, publication.Receipt);
                }
                Mutated = true;
                Vector3d heading = base.FindTargetDirection(position, out pending);
                if (!_replaceDeferredOutput) return heading;
                pending = default(NavigationTransitionInstruction);
                return Vector3d.Right;
            }
            return base.FindTargetDirection(position, out pending);
        }

        protected override bool CheckStuckStatus(Vector3d position, Fixed64 speed, Fixed64 stuckThreshold)
        {
            StuckChecks++;
            return base.CheckStuckStatus(position, speed, stuckThreshold);
        }
    }

    [Fact]
    public void FlowAdoption_ChangedSamplingOriginMustDeferSecondSampleAndRecoverAtCurrentFoot()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var steering = new RedirectedSampleSteering(context, query.End.Position);
        ConfigureScheduled(steering);
        steering.ApplyPathQuery(query);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        int arrivals = 0;
        steering.Events.OnArrive += () => arrivals++;
        try
        {
            Vector3d heading = Vector3d.Zero;
            NavigationTransitionInstruction? action = null;
            for (int frame = 0; frame < 128 && !steering.Redirected; frame++)
            {
                context.Simulate();
                heading = steering.GetHeading(host, out action);
            }

            steering.Redirected.Should().BeTrue();
            heading.Should().Be(Vector3d.Zero, "the adoption sample cannot certify a different foot");
            action.Should().BeNull();
            steering.IsAtDestination.Should().BeFalse();
            steering.ShouldMove.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.HasNavigationGuidance.Should().BeTrue();
            steering.StuckChecks.Should().Be(0);
            arrivals.Should().Be(0, "a second sample at the destination would incorrectly arrive in this heading");
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);

            steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.StuckChecks.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0,
                "the consumed adoption turn must not start replacement work in the same frame");

            var movedHost = new MockSteerAgent(query.End.Position)
                { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
            for (int frame = 0; frame < 128 && !steering.IsAtDestination; frame++)
            {
                context.Simulate();
                steering.GetHeading(movedHost, out action).Should().Be(Vector3d.Zero);
                action.Should().BeNull();
            }
            steering.IsAtDestination.Should().BeTrue();
            steering.ShouldMove.Should().BeFalse();
            arrivals.Should().Be(1);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        }
        finally
        {
            steering.StopMove();
        }
    }

    private sealed class RedirectedSampleSteering : NavSteering
    {
        private readonly Vector3d _samplePosition;
        internal bool Redirected { get; private set; }
        internal int StuckChecks { get; private set; }

        internal RedirectedSampleSteering(TrailblazerWorldContext context, Vector3d samplePosition) : base(context)
            => _samplePosition = samplePosition;

        protected override Vector3d FindTargetDirection(Vector3d position, out NavigationTransitionInstruction? pending)
        {
            if (Redirected) return base.FindTargetDirection(position, out pending);
            Redirected = true;
            return base.FindTargetDirection(_samplePosition, out pending);
        }

        protected override bool CheckStuckStatus(Vector3d position, Fixed64 speed, Fixed64 stuckThreshold)
        {
            StuckChecks++;
            return base.CheckStuckStatus(position, speed, stuckThreshold);
        }
    }

    private sealed class FirstSampleSteering : NavSteering
    {
        private readonly TrailblazerWorldContext _testContext;
        private readonly bool _bypassFirst;
        internal bool FirstSampleWasVisited { get; private set; }
        internal FirstSampleSteering(TrailblazerWorldContext context, bool bypassFirst) : base(context)
        {
            _testContext = context;
            _bypassFirst = bypassFirst;
        }
        protected override Vector3d FindTargetDirection(Vector3d position, out NavigationTransitionInstruction? pending)
        {
            if (FirstSampleWasVisited) return base.FindTargetDirection(position, out pending);
            FirstSampleWasVisited = true;
            if (_bypassFirst)
            {
                pending = null;
                return Vector3d.Forward;
            }
            var held = new System.Collections.Generic.List<NavigationWorldGraphLease>();
            while (_testContext.Pathing.NavigationGraphStore.TryAcquire() is NavigationWorldGraphLease lease) held.Add(lease);
            try
            {
                // A duplicate sample needs another graph lease and would fail CapacityExceeded.
                return base.FindTargetDirection(position, out pending);
            }
            finally { foreach (var lease in held) lease.Dispose(); }
        }
    }
    [Theory]
    [InlineData(PathAlgorithm.AStar, 0)]
    [InlineData(PathAlgorithm.AStar, 1)]
    [InlineData(PathAlgorithm.AStar, 2)]
    [InlineData(PathAlgorithm.AStar, 3)]
    [InlineData(PathAlgorithm.AStar, 4)]
    [InlineData(PathAlgorithm.AStar, 5)]
    [InlineData(PathAlgorithm.AStar, 6)]
    [InlineData(PathAlgorithm.FlowField, 0)]
    [InlineData(PathAlgorithm.FlowField, 1)]
    [InlineData(PathAlgorithm.FlowField, 2)]
    [InlineData(PathAlgorithm.FlowField, 3)]
    [InlineData(PathAlgorithm.FlowField, 4)]
    [InlineData(PathAlgorithm.FlowField, 5)]
    [InlineData(PathAlgorithm.FlowField, 6)]
    public void OwnedAcquisition_ShouldReleaseOnEverySessionBoundary(PathAlgorithm algorithm, int boundary)
    {
        using var context = CreateContext(algorithm, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        switch (boundary)
        {
            case 0: agent.Steering.StopMove(); break;
            case 1: agent.Steering.Arrive(); break;
            case 2: agent.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Slow); break;
            case 3: agent.Reset(); break;
            case 4: agent.ApplyGuidedTrekRequest(query); break;
            case 5:
                agent.TryReplaceNavigationProfile(WithArrivalRadius(query.Agent))
                    .Should().Be(NavigationProfileReplacementStatus.Applied);
                break;
            case 6: JsonRecordSerializer.Populate(agent, JsonRecordSerializer.Serialize(agent)); break;
        }
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        for (int i = 0; i < 8; i++) context.Simulate();
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        if (agent.IsActive) agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
#endif
    public void FailedNestedLoadAndRejectedProfile_ShouldPreserveLiveTicketAndSameFrameAttempt(PathAlgorithm algorithm, bool memoryPack)
    {
        using var context = CreateContext(algorithm, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        var originalTicket = GetTicket(agent.Steering);
        object record = SerializationUtility.SerializeRecord(agent, memoryPack);
        object invalid = SerializationUtility.SetPayloadValue(record, memoryPack, 99, "Steering", "AcquisitionMode");
        Action populate = () => SerializationUtility.PopulateRecord(agent, invalid, memoryPack);
        populate.Should().Throw<ArgumentOutOfRangeException>();
        agent.TryReplaceNavigationProfile(query.Agent).Should().Be(NavigationProfileReplacementStatus.Unchanged);
        agent.AcceptPhysicalReplacement = false;
        agent.TryReplaceNavigationProfile(WithArrivalRadius(query.Agent)).Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        GetTicket(agent.Steering).Should().BeSameAs(originalTicket);
        agent.Steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void UnplannedActualMediumChange_ShouldCancelAcquisitionAndRequireExplicitReintent(PathAlgorithm algorithm, bool replace)
    {
        using var context = CreateContext(algorithm, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        int invalid = 0;
        agent.Steering.Events.OnInvalidPath += () => invalid++;
        if (replace) agent.ReplaceTrekCondition(new TrekCondition { Medium = TraversalMedium.Gas }, false);
        else agent.SetTrekCondition(medium: TraversalMedium.Gas);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        agent.Steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Unsupported);
        agent.Steering.ShouldMove.Should().BeFalse();
        invalid.Should().Be(1);
        agent.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void ClockReset_ShouldCancelOldLifetimeEvenWhilePausedWithoutDiscardingIntent(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out PathQuery query);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        NavigationGuideRequest ticket = GetTicket(agent.Steering)!;
        agent.Steering.CanMove = false;
        context.Reset();
        agent.Steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        ticket.Status.Should().Be(NavigationGuideStatus.Cancelled);
        GetTicket(agent.Steering).Should().BeNull();
        agent.Steering.CurrentQuery.Should().Be(query);
        agent.Steering.ShouldMove.Should().BeTrue();
        agent.Steering.CanMove = true;
        agent.Steering.GetHeading(agent, out _);
        GetTicket(agent.Steering).Should().NotBeSameAs(ticket);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        agent.Reset();
    }

    [Theory]
    [InlineData(64L)]
    [InlineData(2147483646L)]
    public void AStarMovedFoot_ShouldAdoptBoundedConnectorToOriginalRoute(long initialFrame)
    {
        using var context = CreateContext(PathAlgorithm.AStar, out PathQuery query);
        TimingTestUtility.SetTime(context, initialFrame);
        var agent = CreateAgent(context, query);
        agent.Steering!.GetHeading(agent, out _);
        NavigationGuideRequest original = GetTicket(agent.Steering)!;
        for (int i = 0; i < 40 && original.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        original.Status.Should().Be(NavigationGuideStatus.Success);
        agent.SetTestPosition(query.Start.Position + Vector3d.Right);
        agent.Steering.GetHeading(agent, out _);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _).X.Should().BeLessThan(Fixed64.Zero);
        agent.Steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Success);
        agent.Steering.HasNavigationGuidance.Should().BeTrue();
        agent.Steering.GetHeading(agent, out _);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Simulate();
        agent.Steering.GetHeading(agent, out _);
        agent.Steering.CurrentQuery!.Value.Start.Position.Should().Be(query.Start.Position);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        agent.Reset();
    }

    [Fact]
    public void WideValidationGrantsExpireAndResetCannotReuseTheSameNumericFrame()
    {
        using var context = CreateContext(PathAlgorithm.AStar, out PathQuery query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        context.Guides.Schedule(query, out var ticket);
        using var owned = ticket!;
        for (int i = 0; i < 256 && owned.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        owned.Status.Should().Be(NavigationGuideStatus.Success);
        TimingTestUtility.SetTime(context, int.MaxValue - 1L);
        var scheduler = context.Guides.Scheduler;
        var first = new NavigationValidationInterest();
        var second = new NavigationValidationInterest();
        scheduler.RegisterValidation(first).Should().BeTrue();
        scheduler.RegisterValidation(second).Should().BeTrue();
        context.Simulate();
        first.GrantedFrame.Should().Be(int.MaxValue);
        context.Simulate();
        scheduler.TryConsumeValidationTurn(first).Should().BeFalse();
        // A just-completed cohort may still own a charged cleanup turn. The
        // same one-operation allowance must rotate to the second owner fairly.
        for (int i = 0; i < 8 && second.GrantedFrame != context.FrameCount; i++)
        {
            context.Simulate();
            scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
        }
        second.GrantedFrame.Should().Be(context.FrameCount);
        second.GrantedFrame.Should().BeGreaterThan(int.MaxValue);
        scheduler.TryConsumeValidationTurn(second).Should().BeTrue();
        scheduler.TryConsumeValidationTurn(second).Should().BeFalse();
        context.Simulate();
        long previousFrame = first.GrantedFrame;
        context.Reset();
        TimingTestUtility.SetTime(context, previousFrame);
        scheduler.TryConsumeValidationTurn(first).Should().BeFalse();
        scheduler.RetainedValidationInterestCount.Should().Be(0);
        owned.TryTakeGuide(out _).Should().BeFalse();
    }

    internal static NavigationGuideRequest? GetTicket(NavSteering steering)
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;
        object? state = typeof(NavSteering).GetField("_acquisitionState", fields)!.GetValue(steering);
        return state == null ? null
            : (NavigationGuideRequest?)state.GetType().GetField("Request", fields)!.GetValue(state);
    }

    private static NavigationAgentProfile WithArrivalRadius(NavigationAgentProfile profile) => new(
        profile.Shape, profile.MaxStepUp, profile.MaxDropDown, Fixed64.Quarter, profile.AllowedMedia, profile.Capabilities);

    private static TestNavigator CreateAgent(TrailblazerWorldContext context, PathQuery query)
    {
        var agent = new TestNavigator(context);
        agent.Setup(query.Start.Position, query.Agent);
        agent.Initialize(new TrekCondition { Medium = TraversalMedium.Solid,
            SurfaceLevel = query.Start.Position.Y, GroundState = new GroundCondition { SurfaceNormal = Vector3d.Up } });
        agent.Steering!.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        agent.ApplyGuidedTrekRequest(query);
        return agent;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidationTurns_AtLimitOne_ShouldRotatePastPausedOwnersAndProgressQueue(bool queuedWork)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        context.Guides.Schedule(query, out var firstTicket);
        context.Guides.Schedule(query, out var secondTicket);
        for (int i = 0; i < 256 && secondTicket!.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        firstTicket!.Status.Should().Be(NavigationGuideStatus.Success);
        secondTicket!.Status.Should().Be(NavigationGuideStatus.Success);
        var first = new NavigationValidationInterest();
        var second = new NavigationValidationInterest();
        var scheduler = context.Guides.Scheduler;
        scheduler.RegisterValidation(first).Should().BeTrue();
        scheduler.RegisterValidation(second).Should().BeTrue();
        NavigationGuideRequest? queued = null;
        if (queuedWork) context.Guides.Schedule(query, out queued);
        int firstTurns = 0, secondTurns = 0;
        for (int i = 0; i < 100; i++)
        {
            context.Simulate();
            scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
            // Deliberately leave every first-owner grant unused, as a paused host can do.
            if (first.GrantedFrame == context.FrameCount
                && ReferenceEquals(first.GrantedLifetime, context.ClockLifetime)) firstTurns++;
            if (scheduler.TryConsumeValidationTurn(second)) secondTurns++;
            scheduler.TryConsumeValidationTurn(second).Should().BeFalse();
        }
        firstTurns.Should().BeGreaterThan(0);
        secondTurns.Should().BeGreaterThan(0);
        if (queuedWork) queued!.Status.Should().Be(NavigationGuideStatus.Success);
        scheduler.ReleaseValidation(first);
        context.Simulate();
        scheduler.TryConsumeValidationTurn(first).Should().BeFalse();
        scheduler.Reset();
        scheduler.RetainedValidationInterestCount.Should().Be(0);
        scheduler.TryConsumeValidationTurn(second).Should().BeFalse();
        firstTicket.Dispose();
        secondTicket.Dispose();
        queued?.Dispose();
    }

    [Fact]
    public void FlowAdoption_FullSampleBudgetFailure_ShouldStopAndNotifyOnce()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query, sampleBudget: default(GuideSampleWorkBudget));
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent };
        int invalid = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.ApplyPathQuery(query);
        for (int i = 0; i < 50; i++)
        {
            steering.GetHeading(agent, out _);
            context.Simulate();
        }
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.BudgetExceeded);
        steering.ShouldMove.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        invalid.Should().Be(1);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void PausedAcquisition_ShouldRetainIntentAndAdoptOnlyAfterResume(PathAlgorithm algorithm)
    {
        using var context = CreateContext(algorithm, out PathQuery query);
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.ApplyPathQuery(query);
        steering.GetHeading(agent, out _);
        steering.CanMove = false;
        for (int i = 0; i < 32; i++)
        {
            context.Simulate();
            steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        }
        steering.IsAwaitingGuidance.Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        steering.CanMove = true;
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Simulate();
        steering.GetHeading(agent, out _).X.Should().BeGreaterThan(Fixed64.Zero);
        steering.StopMove();
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void CommitAfterHostGroundCorrection_ShouldMoveOccupancyWhileWaitingOrIdle(bool scheduled, bool move)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var agent = new TestNavigator(context);
        agent.Setup(query.Start.Position, query.Agent);
        agent.Initialize(new TrekCondition { Medium = TraversalMedium.Solid });
        agent.ConstrainTestMotionToSurface(query.Start.Position.Y);
        if (scheduled)
        {
            agent.Steering!.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
            agent.ApplyGuidedTrekRequest(query);
            agent.Simulate();
            agent.Steering.IsAwaitingGuidance.Should().BeTrue();
        }
        agent.ResolveAcceptedFramePosition(query.Start.Position + (move ? Vector3d.Right : Vector3d.Zero)
            + Vector3d.Down * Fixed64.FromFraction(1, 100));
        agent.CommitFrameMotion();
        var expected = new VoxelIndex(move ? 1 : 0, 0, 0);
        agent.LastCommittedCell!.Value.Address.Index.Should().Be(expected);
        int occupants = 0;
        foreach (WorldVoxelIndex cell in GridOccupantManager.GetOccupiedIndices(context.World, agent))
        {
            cell.VoxelIndex.Should().Be(expected);
            GridOccupantManager.TryGetOccupancyTicket(context.World, agent, cell, out _).Should().BeTrue();
            occupants++;
        }
        occupants.Should().Be(1);
        agent.Reset();
    }
    [Fact]
    public void ScheduledPhases_ShouldNotCancelUnarmedInitialTicketEveryFrame()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        steering.ConfigurePathRechecks(16, 3);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.ApplyPathQuery(query);
        for (int i = 0; i < 30 && !steering.HasNavigationGuidance; i++)
        {
            steering.GetHeading(agent, out _);
            context.Simulate();
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.StopMove();
    }
    [Fact]
    public void ModeChange_ShouldValidatePreserveNoOpAndDeferInFlightRestart()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query);
        var steering = new NavSteering(context);
        steering.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Immediate);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent };
        steering.ApplyPathQuery(query);
        steering.GetHeading(agent, out _);
        Action invalid = () => steering.ConfigureAcquisitionMode((NavigationAcquisitionMode)99);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        steering.ShouldMove.Should().BeTrue();
        context.Simulate();
        steering.GetHeading(agent, out _).X.Should().BeGreaterThan(Fixed64.Zero);
        steering.StopMove();
    }

    [Fact]
    public void QueueFull_ShouldAttemptOnlyOncePerFrameAndPreserveIntent()
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query,
            scheduling: new NavigationSchedulingSettings(maxRetainedRequests: 1));
        context.Guides.Schedule(query, out var blocker);
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent };
        steering.ApplyPathQuery(query);
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.CapacityExceeded);
        blocker!.Dispose();
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Simulate();
        steering.GetHeading(agent, out _);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        steering.StopMove();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectOnly_ShouldNeverAcquireGraphWork(bool neutral)
    {
        using var context = CreateContext(PathAlgorithm.FlowField, out PathQuery query, neutral);
        var steering = new NavSteering(context) { CanPathfind = false };
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.ApplyPathQuery(query);
        for (int i = 0; i < 12; i++)
        {
            steering.GetHeading(agent, out _);
            context.Simulate();
        }
        steering.HasLineOfSightPath.Should().Be(neutral);
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.ShouldMove.Should().Be(neutral);
        steering.LastAcquisitionStatus.Should().Be(neutral ? NavigationGuideStatus.Success : NavigationGuideStatus.NoPath);
        steering.StopMove();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Rebind_ShouldCancelUnphasedTicketBeforeNewContextCanAcquire(PathAlgorithm algorithm)
    {
        using var first = CreateContext(algorithm, out PathQuery query);
        using var second = CreateContext(algorithm, out _);
        var steering = new NavSteering(first);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent };
        steering.ApplyPathQuery(query);
        steering.GetHeading(agent, out _);
        steering.BindContext(second);
        first.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        steering.ShouldMove.Should().BeTrue();
        steering.GetHeading(agent, out _);
        second.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        steering.StopMove();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    [InlineData(PathAlgorithm.FlowField, false)]
    public void MovedFoot_ShouldAdoptCurrentDirectOrFlowProof(PathAlgorithm algorithm, bool neutral)
    {
        using var context = CreateContext(algorithm, out PathQuery query, neutral);
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        var initial = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent };
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = Fixed64.One };
        steering.ApplyPathQuery(query);
        steering.GetHeading(initial, out _);
        Vector3d heading = Vector3d.Zero;
        for (int i = 0; i < 100 && heading == Vector3d.Zero; i++)
        {
            context.Simulate();
            heading = steering.GetHeading(moved, out _);
        }
        heading.X.Should().BeGreaterThan(Fixed64.Zero);
        steering.IsAwaitingGuidance.Should().BeFalse();
        steering.StopMove();
    }
    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void Scheduled_ShouldWaitThenAdoptWithoutArriving(PathAlgorithm algorithm, bool neutral)
    {
        using TrailblazerWorldContext context = CreateContext(algorithm, out PathQuery query, neutral);
        var steering = new NavSteering(context);
        ConfigureScheduled(steering);
        var agent = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = Fixed64.One };
        int arrivals = 0;
        steering.Events.OnArrive += () => arrivals++;
        steering.ApplyPathQuery(query, 42);
        steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.HasLineOfSightPath.Should().BeFalse();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        for (int repeat = 0; repeat < 3; repeat++)
            steering.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        Vector3d heading = Vector3d.Zero;
        for (int frame = 0; frame < 256 && heading == Vector3d.Zero; frame++)
        {
            context.Simulate();
            heading = steering.GetHeading(agent, out _);
        }
        heading.X.Should().BeGreaterThan(Fixed64.Zero);
        steering.HasLineOfSightPath.Should().Be(neutral);
        steering.HasNavigationGuidance.Should().Be(!neutral);
        steering.ShouldMove.Should().BeTrue();
        steering.MovementGroupID.Should().Be(42);
        arrivals.Should().Be(0);
        steering.StopMove();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
    }

    private static void ConfigureScheduled(NavSteering steering)
    {
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
    }

    internal static TrailblazerWorldContext CreateContext(PathAlgorithm algorithm, out PathQuery query,
        bool neutral = false, NavigationSchedulingSettings? scheduling = null, GuideSampleWorkBudget? sampleBudget = null)
    {
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(4, 1, 1));
        var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        TrailblazerWorldContextSettings d = TrailblazerWorldContextSettings.Default;
        var settings = new TrailblazerWorldContextSettings(d.OperationLimits, d.MaintenanceBudget,
            sampleBudget ?? d.GuideSampleBudget, d.MovementGroupPadding, d.MaxIngressEntries,
            d.MaxIngressBytes, d.MaxActiveSnapshots, d.MaxActiveSnapshotBytes, d.MaxRetiredSnapshots,
            d.MaxRetiredSnapshotBytes, d.MaxPersistentGraphPages, d.MaxDynamicCellSlotsPerMap,
            d.MaxDynamicCellSlots, d.NavigationAreaCount, d.MaxAreaPolicies, d.MaxAreaRulesPerPolicy,
            d.MaxAreaRules, d.MaxConcurrentSnapshotLeases, d.QueryLimits, scheduling);
        var context = TrailblazerWorldContext.Attach(world, takeOwnership: true, settings: settings);
        NormalizedGridConfiguration binding = PublishAcquisitionMap(context, neutral);
        var key = new NavigationAreaPolicyKey("acquisition", 1);
        var profile = new NavigationAgentProfile(new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        query = new PathQuery(new NavigationEndpoint(GuidedPathTestScene.Anchor(binding, new VoxelIndex(0, 0, 0)), "acquisition"),
            new NavigationEndpoint(GuidedPathTestScene.Anchor(binding, new VoxelIndex(3, 0, 0)), "acquisition"),
            profile, key, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), algorithm,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 4096, 4096, 0), false);
        return context;
    }

    private static NormalizedGridConfiguration PublishAcquisitionMap(TrailblazerWorldContext context, bool neutral)
    {
        new GridConfiguration(Vector3d.Zero, new Vector3d(4, 1, 1))
            .TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("acquisition", binding);
        for (int i = 0; i < 4; i++)
            builder.AddCell(new VoxelIndex(i, 0, 0), new NavigationCell(TraversalMedia.Solid,
                TraversalCapability.None, default, neutral ? Fixed64.Zero : Fixed64.One,
                Fixed64.One, Fixed64.One));
        var key = new NavigationAreaPolicyKey("acquisition", 1);
        GuidedPathTestScene.PublishMapAndPolicy(context, builder.Build(), 1, OverlayReplacementPolicy.Clear,
            1, new NavigationAreaPolicy(key, new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 2);
        return binding;
    }
}
