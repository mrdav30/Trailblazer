using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Chronicler.Serialization;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

public class NavSteeringConnectorTests
{
    [Fact]
    public void ImmediateGuide_DisposedInsideTargetHookMustDeferWithoutIssuingDefaultStepMovement()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = new ConnectorHookSteering(context);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        steering.ApplyPathQuery(query);
        steering.BeforeTarget = () => GetTestGuide(steering)!.Value.Dispose();
        try
        {
            steering.GetHeading(host, out var action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.BeforeTarget.Should().BeNull();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.CurrentQuery.Should().Be(query);
            steering.ShouldMove.Should().BeTrue();
            steering.StuckChecks.Should().Be(0);
            for (int repeat = 0; repeat < 3; repeat++)
                steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            invalid.Should().Be(0);
            arrived.Should().Be(0);
            context.Simulate();
            steering.GetHeading(host, out _).Should().NotBe(Vector3d.Zero);
        }
        finally { steering.StopMove(); }
    }

    [Fact]
    public void Connector_DisposedAfterAnchorAdvancementMustRefuseTheNextStepWithoutArrival()
    {
        using var context = Trailblazer.Tests.Pathing.Graph.NavigationAStarConnectorTests.CreateLine(
            TraversalMedium.Solid, out var query);
        var steering = new ConnectorHookSteering(context);
        AdoptConnector(context, query, new Vector3d(Fixed64.Zero, query.Start.Position.Y, query.Start.Position.Z), steering);
        var guide = GetTestGuide(steering)!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        object sync = typeof(NavigationAStarPayloadCache).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cache)!;
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        Exception? failure = null;
        Vector3d? heading = null;
        NavigationTransitionInstruction? action = null;
        int invalid = 0, arrived = 0;
        int stuckChecks = steering.StuckChecks;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        steering.BeforeTarget = () =>
        {
            entered.Set();
            proceed.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
                .Should().BeTrue("the test must release the target hook after acquiring the cache lock");
        };
        var worker = new Thread(() =>
        {
            try { heading = steering.GetHeading(host, out action); }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        context.Simulate();
        try
        {
            worker.Start();
            entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
                .Should().BeTrue("the charged connector proof must complete before blocking its release");
            lock (sync)
            {
                proceed.Set();
                SpinWait.SpinUntil(() => guide.CurrentStepIndex == 1 || TestContext.Current.CancellationToken.IsCancellationRequested, 10_000)
                    .Should().BeTrue("the valid anchor must advance before its proof release waits on the cache lock");
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                guide.CurrentStepIndex.Should().Be(1);
                guide.Dispose();
            }
            worker.Join(10_000).Should().BeTrue("disposing the guide must not deadlock proof release");
            failure.Should().BeNull();
            heading.Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.CurrentQuery.Should().Be(query);
            steering.ShouldMove.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.StuckChecks.Should().Be(stuckChecks);
            invalid.Should().Be(0);
            arrived.Should().Be(0);
            cache.LeasedBytes.Should().Be(0);
            context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        }
        finally
        {
            proceed.Set();
            worker.Join(10_000).Should().BeTrue();
            steering.StopMove();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Connector_PublicationInsideTargetHookMustDeferWithoutReprovingOrLosingRoute(bool recurring, bool replaceRefusedOutput)
    {
        using var context = Trailblazer.Tests.Pathing.Graph.NavigationAStarConnectorTests.CreateLine(
            TraversalMedium.Solid, out var query);
        var steering = new ConnectorHookSteering(context) { ReplaceRefusedOutput = replaceRefusedOutput };
        var foot = new Vector3d(Fixed64.Zero, query.Start.Position.Y, query.Start.Position.Z);
        var host = new MockSteerAgent(foot) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        NavigationGuideLease guide = default;
        int stuckChecks = 0;
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.ConfigurePathRechecks(10000);
        steering.ApplyPathQuery(query);
        steering.GetHeading(new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent }, out _);
        if (recurring)
        {
            for (int i = 0; i < 100 && !steering.HasNavigationGuidance; i++)
            {
                context.Simulate();
                steering.GetHeading(host, out _);
            }
            steering.HasNavigationGuidance.Should().BeTrue();
        }
        steering.BeforeTarget = () =>
        {
            guide = GetTestGuide(steering)!.Value;
            guide.HasConnector.Should().BeTrue();
            Trailblazer.Tests.Pathing.Graph.NavigationAStarConnectorTests.PublishCost(context, 0, 3);
            guide.Status.Should().Be(NavigationGuideStatus.Success, "only the connector page changed");
            context.Pathing.ImmediateRayWorkspace.WorkMeter.Reset(query.Budget);
            stuckChecks = steering.StuckChecks;
        };
        try
        {
            Vector3d heading = Vector3d.Zero;
            NavigationTransitionInstruction? action = null;
            for (int i = 0; i < 100 && steering.BeforeTarget != null; i++)
            {
                context.Simulate();
                heading = steering.GetHeading(host, out action);
            }
            steering.BeforeTarget.Should().BeNull();
            heading.Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.LastBaseHeading.Should().Be(Vector3d.Zero);
            guide.Status.Should().Be(NavigationGuideStatus.Success);
            guide.CurrentStepIndex.Should().Be(0);
            steering.CurrentQuery.Should().Be(query);
            steering.ShouldMove.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.StuckChecks.Should().Be(stuckChecks);
            for (int repeat = 0; repeat < 3; repeat++)
                steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            context.Pathing.ImmediateRayWorkspace.WorkMeter.LookupProbes.Should().Be(0);
            context.Pathing.ImmediateRayWorkspace.WorkMeter.CoveredVoxelIntervals.Should().Be(0);
            action.Should().BeNull();
            invalid.Should().Be(0);
            arrived.Should().Be(0);
            context.Simulate();
            steering.GetHeading(host, out _).Should().Be(Vector3d.Zero, "the next charged turn retires the stale local proof");
            context.Simulate();
            steering.GetHeading(host, out _).X.Should().BeGreaterThan(Fixed64.Zero);
            guide.CurrentStepIndex.Should().Be(0);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
        }
        finally { steering.StopMove(); }
    }

    [Theory]
    [InlineData("RedirectOrigin")]
    [InlineData("RawWorldMutation")]
    [InlineData("PathingReset")]
    public void Connector_TargetHookBoundaryChangesMustNotUseAnUnprovedOrDisposedStep(string change)
    {
        using var context = Trailblazer.Tests.Pathing.Graph.NavigationAStarConnectorTests.CreateLine(
            TraversalMedium.Solid, out var query);
        var steering = new ConnectorHookSteering(context);
        var foot = new Vector3d(Fixed64.Zero, query.Start.Position.Y, query.Start.Position.Z);
        AdoptConnector(context, query, foot, steering);
        var guide = GetTestGuide(steering)!.Value;
        var host = new MockSteerAgent(foot) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        int stuckChecks = steering.StuckChecks;
        steering.BeforeTarget = () =>
        {
            if (change == "RedirectOrigin") steering.BaseOrigin = foot + Vector3d.Right;
            else if (change == "RawWorldMutation") context.World.ActiveGrids[0].TryRemoveVoxel(default).Should().BeTrue();
            else context.Pathing.Reset();
            context.Pathing.ImmediateRayWorkspace.WorkMeter.Reset(query.Budget);
        };
        try
        {
            context.Simulate();
            steering.GetHeading(host, out var action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.CurrentQuery.Should().Be(query);
            steering.ShouldMove.Should().BeTrue();
            steering.IsAwaitingGuidance.Should().BeTrue();
            steering.IsAtDestination.Should().BeFalse();
            steering.StuckChecks.Should().Be(stuckChecks);
            for (int repeat = 0; repeat < 3; repeat++)
                steering.GetHeading(host, out action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            context.Pathing.ImmediateRayWorkspace.WorkMeter.LookupProbes.Should().Be(0);
            context.Pathing.ImmediateRayWorkspace.WorkMeter.CoveredVoxelIntervals.Should().Be(0);
            invalid.Should().Be(0);
            arrived.Should().Be(0);
            if (change == "RedirectOrigin")
            {
                guide.Status.Should().Be(NavigationGuideStatus.Success);
                guide.CurrentStepIndex.Should().Be(0);
                context.Simulate();
                steering.GetHeading(host, out _).X.Should().BeGreaterThan(Fixed64.Zero);
                context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
            }
            else if (change == "PathingReset")
                guide.Status.Should().Be(NavigationGuideStatus.Stale);
            else
            {
                guide.Status.Should().Be(NavigationGuideStatus.Success, "the removed cell belongs only to the connector");
                guide.CurrentStepIndex.Should().Be(0);
            }
        }
        finally { steering.StopMove(); }
    }

    private sealed class ConnectorHookSteering : NavSteering
    {
        internal ConnectorHookSteering(TrailblazerWorldContext context) : base(context) { }
        internal Action? BeforeTarget { get; set; }
        internal bool ReplaceRefusedOutput { get; set; }
        internal Vector3d? BaseOrigin { get; set; }
        internal Vector3d LastBaseHeading { get; private set; }
        internal int StuckChecks { get; private set; }
        protected override Vector3d FindTargetDirection(Vector3d position, out NavigationTransitionInstruction? pending)
        {
            Action? hook = BeforeTarget;
            BeforeTarget = null;
            hook?.Invoke();
            LastBaseHeading = base.FindTargetDirection(BaseOrigin ?? position, out pending);
            BaseOrigin = null;
            if (hook != null && ReplaceRefusedOutput)
            {
                pending = default(NavigationTransitionInstruction);
                return Vector3d.Right;
            }
            return LastBaseHeading;
        }
        protected override bool CheckStuckStatus(Vector3d position, Fixed64 speed, Fixed64 stuckThreshold)
        {
            StuckChecks++;
            return base.CheckStuckStatus(position, speed, stuckThreshold);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidationRegistration_SchedulerClosingAtLockBoundaryMustNotIssueHeadingOrLeakOwnership(bool recurring)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = recurring ? AdoptConnector(context, query) : new NavSteering(context);
        var host = new MockSteerAgent(recurring ? query.Start.Position + Vector3d.Right : query.Start.Position)
        { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        if (!recurring)
        {
            steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
            steering.ApplyPathQuery(query);
            steering.GetHeading(host, out _);
            var ticket = NavSteeringAcquisitionTests.GetTicket(steering)!;
            for (int i = 0; i < 128 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
            ticket.Status.Should().Be(NavigationGuideStatus.Success);
        }
        context.Simulate();
        var scheduler = context.Guides.Scheduler;
        object sync = typeof(NavigationGuideScheduler).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scheduler)!;
        using var started = new ManualResetEventSlim();
        Exception? failure = null;
        Vector3d? heading = null;
        NavigationTransitionInstruction? action = null;
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        var worker = new Thread(() =>
        {
            started.Set();
            try { heading = steering.GetHeading(host, out action); }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        try
        {
            lock (sync)
            {
                worker.Start();
                started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).Should().BeTrue();
                SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, 10_000)
                    .Should().BeTrue("the only conflicting monitor is registration after the owner/connector eligibility checks");
                scheduler.Dispose();
            }
            worker.Join(10_000).Should().BeTrue();
            failure.Should().BeNull();
            heading.Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            steering.ShouldMove.Should().BeTrue();
            steering.CurrentQuery.Should().Be(query);
            invalid.Should().Be(0);
            arrived.Should().Be(0);
            scheduler.RetainedRequestCount.Should().Be(0);
            scheduler.RetainedValidationInterestCount.Should().Be(0);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ReservedPayloadBytes.Should().Be(0);
        }
        finally
        {
            worker.Join(10_000).Should().BeTrue();
            steering.StopMove();
        }
    }

    [Fact]
    public void Connector_ChargedSnapshotCapacityFailureMustKeepRouteAndRetryNextTurn()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = AdoptConnector(context, query);
        var guide = GetTestGuide(steering)!.Value;
        context.Simulate();
        var held = new List<NavigationWorldGraphLease>();
        try
        {
            for (int i = 0; i < context.Settings.MaxConcurrentSnapshotLeases; i++)
            {
                var snapshot = context.Pathing.NavigationGraphStore.TryAcquire();
                if (snapshot == null) break;
                held.Add(snapshot);
            }
            context.Pathing.NavigationGraphStore.TryAcquire().Should().BeNull();
            steering.GetHeading(new MockSteerAgent(query.Start.Position + Vector3d.Right)
            { NavigationProfile = query.Agent, Speed = (Fixed64)20 }, out _).Should().Be(Vector3d.Zero);
            steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.CapacityExceeded);
            steering.ShouldMove.Should().BeTrue();
            guide.HasConnector.Should().BeTrue();
            guide.CurrentStepIndex.Should().Be(0);
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        }
        finally { foreach (var snapshot in held) snapshot.Dispose(); }
        context.Simulate();
        steering.GetHeading(new MockSteerAgent(query.Start.Position + Vector3d.Right)
        { NavigationProfile = query.Agent, Speed = (Fixed64)20 }, out _).X.Should().BeLessThan(Fixed64.Zero);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
        steering.StopMove();
    }

    [Fact]
    public void ImmediateConnector_ActualMediumValidationMustRespectItsDurableTraversalContract()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = AdoptConnector(context, query);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        int invalid = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.ValidateActualMedium(TraversalMedium.Solid);
        steering.ShouldMove.Should().BeTrue();
        invalid.Should().Be(0);
        steering.ValidateActualMedium(TraversalMedium.Gas);
        steering.ShouldMove.Should().BeFalse();
        steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Unsupported);
        invalid.Should().Be(1);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Immediate)]
    [InlineData(NavigationAcquisitionMode.Scheduled)]
    public void AStarAdoptionFinalization_ClosedSchedulerMustReleaseTransferredProofBeforeRetry(NavigationAcquisitionMode mode)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = new NavSteering(context);
        steering.ConfigureAcquisitionMode(mode);
        steering.ApplyPathQuery(query);
        context.Guides.Schedule(query, out var acquired).Should().Be(NavigationGuideStatus.Pending);
        using var ticket = acquired!;
        for (int i = 0; i < 128 && ticket.Status == NavigationGuideStatus.Pending; i++) context.Simulate();
        ticket.Status.Should().Be(NavigationGuideStatus.Success);
        ticket.IsDirect.Should().BeFalse();
        var scheduler = context.Guides.Scheduler;
        var interest = new NavigationValidationInterest();
        scheduler.RegisterValidation(interest).Should().BeTrue();
        context.Simulate();
        scheduler.TryConsumeValidationTurn(interest).Should().BeTrue();
        ticket.TryTakeGuide(out var transferred).Should().BeTrue();
        var guide = transferred!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        try
        {
            context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Right, guide)
                .Should().Be(NavigationGuideStatus.Success);
            guide.HasConnector.Should().BeTrue();
            cache.LeasedBytes.Should().BeGreaterThan(cache.CachedBytes);
            scheduler.Dispose();
            guide.HasConnector.Should().BeTrue("the transferred local guide has not yet registered its teardown alias");
            steering.CompleteAStarAdoption(scheduler, guide).Should().BeTrue();
            steering.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Cancelled);
            steering.ShouldMove.Should().BeTrue();
            steering.CurrentQuery.Should().Be(query);
            steering.HasNavigationGuidance.Should().BeFalse();
            cache.LeasedBytes.Should().Be(0);
            cache.ReservedPayloadBytes.Should().Be(0);
            cache.ActiveLeaseCount.Should().Be(0);
            scheduler.RetainedRequestCount.Should().Be(0);
            scheduler.RetainedValidationInterestCount.Should().Be(0);
            steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
            var host = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent };
            steering.GetHeading(host, out var action).Should().Be(Vector3d.Zero);
            action.Should().BeNull();
            invalid.Should().Be(0);
            arrived.Should().Be(0);
        }
        finally
        {
            guide.Dispose();
            scheduler.ReleaseValidation(interest);
            steering.StopMove();
        }
    }

    [Fact]
    public void Connector_WaitingModeChangeMustKeepProofAndRequireNextFramePermit()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = AdoptConnector(context, query);
        var guide = GetTestGuide(steering)!.Value;
        var host = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
        steering.IsAwaitingGuidance.Should().BeTrue();
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long bytes = cache.LeasedBytes;
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        guide.HasConnector.Should().BeTrue();
        cache.LeasedBytes.Should().Be(bytes);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(1);
        steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
        context.Simulate();
        steering.GetHeading(host, out _).X.Should().BeLessThan(Fixed64.Zero);
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
        steering.StopMove();
        cache.LeasedBytes.Should().Be(0);
    }

    [Fact]
    public void ProtectedValidation_ModeChangeDeferralMustExpireOnlyOnNextFrame()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = new BoundarySteering(context);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.ApplyPathQuery(query);
        steering.ValidateForHost(query.Start.Position).Should().BeTrue();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        steering.ValidateForHost(query.Start.Position).Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeFalse();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(0);
        context.Simulate();
        steering.ValidateForHost(query.Start.Position).Should().BeTrue();
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.StopMove();
    }

    [Fact]
    public void RestoredScheduledPolicy_ProtectedValidationBeforeFirstHeadingMustAcquireFreshGuidance()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var source = new NavSteering(context);
        source.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        source.ApplyPathQuery(query);
        object record = SerializationUtility.SerializeRecord(source, false);
        var target = new BoundarySteering(context);
        SerializationUtility.PopulateRecord(target, record, false);
        target.ApplyPathQuery(query);

        target.ValidateForHost(query.Start.Position).Should().BeTrue();
        target.IsAwaitingGuidance.Should().BeTrue();
        target.HasNavigationGuidance.Should().BeFalse();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(0);
        for (int frame = 0; frame < 128 && !target.HasNavigationGuidance; frame++)
        {
            context.Simulate();
            target.ValidateForHost(query.Start.Position).Should().BeTrue();
        }

        target.HasNavigationGuidance.Should().BeTrue();
        target.LastAcquisitionStatus.Should().Be(NavigationGuideStatus.Success);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        target.GetHeading(host, out var action).X.Should().BeGreaterThan(Fixed64.Zero);
        action.Should().BeNull();
        source.StopMove();
        target.StopMove();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void ReentrantAdoptionHook_MustNotIssueTwoConnectorHeadingsForOnePermit()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var host = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        var steering = new BoundarySteering(context) { ReentrantHost = host };
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.ConfigurePathRechecks(10000);
        steering.ApplyPathQuery(query);
        steering.GetHeading(new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent }, out _);
        for (int i = 0; i < 100 && !steering.Reentered; i++)
        {
            context.Simulate();
            steering.GetHeading(host, out _).Should().Be(Vector3d.Zero);
        }
        steering.Reentered.Should().BeTrue();
        steering.InnerHeading.X.Should().BeLessThan(Fixed64.Zero);
        var guide = GetTestGuide(steering)!.Value;
        guide.CurrentStepIndex.Should().Be(0);
        guide.HasConnector.Should().BeTrue();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
        context.Simulate();
        steering.GetHeading(host, out _).X.Should().BeLessThan(Fixed64.Zero);
        steering.StopMove();
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
    }

    private sealed class BoundarySteering : NavSteering
    {
        internal BoundarySteering(TrailblazerWorldContext context) : base(context) { }
        internal MockSteerAgent? ReentrantHost { get; set; }
        internal bool Reentered { get; private set; }
        internal Vector3d InnerHeading { get; private set; }
        internal object? PendingComponentRecord { get; set; }
        internal int TargetSamples { get; private set; }
        internal bool ValidateForHost(Vector3d origin) => base.ValidateMovementPath(origin);
        protected override bool ValidateMovementPath(Vector3d origin)
        {
            if (PendingComponentRecord != null)
            {
                object record = PendingComponentRecord;
                PendingComponentRecord = null;
                SerializationUtility.PopulateRecord(this, record, false);
                return true;
            }
            bool result = base.ValidateMovementPath(origin);
            if (ReentrantHost != null && !Reentered && HasNavigationGuidance)
            {
                Reentered = true;
                InnerHeading = base.GetHeading(ReentrantHost, out _);
            }
            return result;
        }
        protected override Vector3d FindTargetDirection(Vector3d position, out NavigationTransitionInstruction? pending)
        {
            TargetSamples++;
            return base.FindTargetDirection(position, out pending);
        }
    }

    [Fact]
    public void ComponentPopulationFromValidationHook_MustNotSampleRetiredQuery()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var source = new NavSteering(context);
        source.ApplyPathQuery(query);
        object record = SerializationUtility.SerializeRecord(source, false);
        var target = new BoundarySteering(context) { PendingComponentRecord = record };
        target.ApplyPathQuery(query);
        var host = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        target.GetHeading(host, out var action).Should().Be(Vector3d.Zero);
        action.Should().BeNull();
        target.ShouldMove.Should().BeTrue("component records restore movement flags; the enclosing Navigator restores the query separately");
        target.CurrentQuery.Should().BeNull();
        target.TargetSamples.Should().Be(0);
        target.HasNavigationGuidance.Should().BeFalse();
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        target.GetHeading(host, out _).Should().Be(Vector3d.Zero);
        target.ShouldMove.Should().BeFalse();
        source.StopMove();
        target.StopMove();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Connector_LocalProofFailureMustPreserveRouteOrStopAtOriginalBudget(bool exhaustTraceBudget)
    {
        using var context = Trailblazer.Tests.Pathing.Graph.NavigationAStarConnectorTests.CreateLine(
            TraversalMedium.Solid, out var original);
        var query = exhaustTraceBudget ? new PathQuery(original.Start, original.End, original.Agent,
            original.AreaPolicy, original.Traversal, original.Algorithm,
            new NavigationWorkBudget(65536, 65536, 65536, 65536, 65536, 0, 0, 0, 4, 65536, 0), false) : original;
        var foot = new Vector3d((Fixed64)(exhaustTraceBudget ? 95 : 0), query.Start.Position.Y, query.Start.Position.Z);
        var steering = AdoptConnector(context, query, foot);
        var guide = GetTestGuide(steering)!.Value;
        guide.HasConnector.Should().BeTrue();
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long builds = cache.AcquisitionDiagnostics.BuildStarts;
        long baseline = cache.CachedBytes;
        int invalid = 0, arrived = 0;
        steering.Events.OnInvalidPath += () => invalid++;
        steering.Events.OnArrive += () => arrived++;
        if (!exhaustTraceBudget)
            Trailblazer.Tests.Pathing.Graph.NavigationAStarConnectorTests.PublishCost(context, 0, 3);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        var moved = new MockSteerAgent(new Vector3d(Fixed64.Zero, query.Start.Position.Y, query.Start.Position.Z))
        { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(moved, out var action).Should().Be(Vector3d.Zero);
        action.Should().BeNull();
        steering.LastAcquisitionStatus.Should().Be(exhaustTraceBudget
            ? NavigationGuideStatus.BudgetExceeded : NavigationGuideStatus.Stale);
        invalid.Should().Be(exhaustTraceBudget ? 1 : 0);
        arrived.Should().Be(exhaustTraceBudget ? 1 : 0,
            "terminal invalid paths retain the existing HandleInvalidPath/Arrive event contract");
        steering.ShouldMove.Should().Be(!exhaustTraceBudget);
        cache.ReservedPayloadBytes.Should().Be(0);
        cache.LeasedBytes.Should().Be(exhaustTraceBudget ? 0 : baseline);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        for (int i = 0; i < 3; i++) steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
        if (!exhaustTraceBudget)
        {
            context.Simulate();
            steering.GetHeading(moved, out _).X.Should().BeGreaterThan(Fixed64.Zero);
            guide.CurrentStepIndex.Should().Be(0);
            guide.HasConnector.Should().BeTrue();
        }
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(builds);
        steering.StopMove();
        cache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    private readonly ITestOutputHelper _output;
    public NavSteeringConnectorTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Connector_MovementPublicationModeAndPermitHistoryMustReplayExactly()
    {
        var first = RunConnectorHistory();
        var replay = RunConnectorHistory();
        replay.Should().Equal(first, "fixed-point headings, cursor/ownership/status and permits must replay, not just final arrival");
    }

    private static List<(Vector3d Heading, NavigationGuideStatus? Status, int Cursor, bool Connector,
        bool Waiting, bool Arrived, long Bytes, long Builds, int Operations)> RunConnectorHistory()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        var steering = AdoptConnector(context, query);
        context.Guides.Schedule(query, out var request);
        using var other = request!;
        for (int frame = 0; frame < 256 && other.Status == NavigationGuideStatus.Pending; frame++) context.Simulate();
        other.Status.Should().Be(NavigationGuideStatus.Success);
        var competitor = new NavigationValidationInterest();
        context.Guides.Scheduler.RegisterValidation(competitor).Should().BeTrue();
        var history = new List<(Vector3d, NavigationGuideStatus?, int, bool, bool, bool, long, long, int)>();
        int connectorRays = 0, connectorWaits = 0, advanced = 0, staleFrames = 0;
        try
        {
            for (int frame = 0; frame < 60; frame++)
            {
                if (frame == 2 || frame == 14) steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
                if (frame == 4 || frame == 18) steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
                if (frame == 20) PublishRouteCost(context, 3);
                context.Simulate();
                context.Guides.Scheduler.TryConsumeValidationTurn(competitor);
                Fixed64 x = frame >= 55 ? (Fixed64)3 : frame >= 50 ? (Fixed64)2 : frame >= 40
                    ? Fixed64.One : (frame % 2 == 0 ? (Fixed64)1.25m : (Fixed64)0.75m);
                var host = new MockSteerAgent(new Vector3d(x, query.Start.Position.Y, query.Start.Position.Z))
                { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
                var heading = steering.GetHeading(host, out var action);
                action.Should().BeNull();
                var guide = GetTestGuide(steering);
                bool connector = guide?.HasConnector == true;
                int cursor = guide?.CurrentStepIndex ?? -1;
                if (connector)
                {
                    if (heading == Vector3d.Zero) connectorWaits++;
                    else connectorRays++;
                    steering.GetHeading(host, out _).Should().Be(Vector3d.Zero, "same-frame replay must not get a second connector turn");
                }
                if (cursor > 0) advanced++;
                if (steering.LastAcquisitionStatus == NavigationGuideStatus.Stale) staleFrames++;
                var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
                history.Add((heading, steering.LastAcquisitionStatus, cursor, connector,
                    steering.IsAwaitingGuidance, steering.IsAtDestination, cache.LeasedBytes,
                    cache.AcquisitionDiagnostics.BuildStarts, context.Guides.Scheduler.LastFrameOperationCount));
            }
            connectorRays.Should().BeGreaterThan(4);
            connectorWaits.Should().BeGreaterThan(4);
            staleFrames.Should().BeGreaterThan(0, "the actual shared-route publication must affect the replay");
            advanced.Should().BeGreaterThan(0, "the history must eventually consume the proved ordinary anchor");
            steering.IsAtDestination.Should().BeTrue();
        }
        finally
        {
            context.Guides.Scheduler.ReleaseValidation(competitor);
            steering.StopMove();
        }
        other.Dispose();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        return history;
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Immediate, false)]
    [InlineData(NavigationAcquisitionMode.Scheduled, false)]
    [InlineData(NavigationAcquisitionMode.Immediate, true)]
    [InlineData(NavigationAcquisitionMode.Scheduled, true)]
    public void Connector_SharedRouteFailureMustWaitForPermitAndDeferRecovery(
        NavigationAcquisitionMode mode, bool exhaustSnapshots)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        var steering = AdoptConnector(context, query);
        var guide = GetTestGuide(steering)!.Value;
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Right)
        { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        // Exercise both policy directions on an already adopted connector.
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        steering.ConfigureAcquisitionMode(mode);
        context.Guides.Schedule(query, out var request);
        using var other = request!;
        for (int frame = 0; frame < 256 && other.Status == NavigationGuideStatus.Pending; frame++) context.Simulate();
        other.Status.Should().Be(NavigationGuideStatus.Success);
        var competitor = new NavigationValidationInterest();
        var scheduler = context.Guides.Scheduler;
        scheduler.RegisterValidation(competitor).Should().BeTrue();
        var held = new List<NavigationWorldGraphLease>();
        try
        {
            if (!exhaustSnapshots) PublishRouteCost(context, 3);
            bool consumed = false;
            for (int frame = 0; frame < 4 && !consumed; frame++)
            {
                context.Simulate();
                consumed = scheduler.TryConsumeValidationTurn(competitor);
            }
            consumed.Should().BeTrue("the completed competing ticket takes this frame's only permit");
            var store = context.Pathing.NavigationGraphStore;
            if (exhaustSnapshots)
            {
                for (int i = 0; i < context.Settings.MaxConcurrentSnapshotLeases; i++)
                {
                    var snapshot = store.TryAcquire();
                    if (snapshot == null) break;
                    held.Add(snapshot);
                }
                store.TryAcquire().Should().BeNull();
            }
            guide.Status.Should().Be(exhaustSnapshots ? NavigationGuideStatus.CapacityExceeded : NavigationGuideStatus.Stale);
            var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
            long builds = cache.AcquisitionDiagnostics.BuildStarts;
            long bytes = cache.LeasedBytes;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                steering.GetHeading(moved, out var action).Should().Be(Vector3d.Zero);
                action.Should().BeNull();
                steering.ShouldMove.Should().BeTrue();
                steering.IsAtDestination.Should().BeFalse();
                steering.IsStuck.Should().BeFalse();
                steering.HasLineOfSightPath.Should().BeFalse();
                guide.CurrentStepIndex.Should().Be(0, "ungranted validation cannot consume or discard the owner");
                cache.LeasedBytes.Should().Be(bytes);
                cache.AcquisitionDiagnostics.BuildStarts.Should().Be(builds);
                scheduler.LastFrameOperationCount.Should().Be(1);
            }
            scheduler.ReleaseValidation(competitor);
            context.Simulate();
            // The next granted frame observes failure, never an immediate acquisition fallback.
            steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
            foreach (var snapshot in held) snapshot.Dispose();
            held.Clear();
            for (int repeat = 0; repeat < 3; repeat++)
                steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
            cache.AcquisitionDiagnostics.BuildStarts.Should().Be(builds);
            Vector3d recovered = Vector3d.Zero;
            for (int frame = 0; frame < 256 && recovered == Vector3d.Zero; frame++)
            {
                context.Simulate();
                recovered = steering.GetHeading(moved, out _);
            }
            recovered.X.Should().NotBe(Fixed64.Zero);
            steering.ShouldMove.Should().BeTrue();
        }
        finally
        {
            foreach (var snapshot in held) snapshot.Dispose();
            scheduler.ReleaseValidation(competitor);
            steering.StopMove();
        }
    }

    private static NavigationGuideLease? GetTestGuide(NavSteering steering) =>
        (NavigationGuideLease?)typeof(NavSteering).GetField("_navigationGuideLease",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(steering);

    private static void PublishRouteCost(TrailblazerWorldContext context, long sequence)
    {
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            (Fixed64)2, Fixed64.One, Fixed64.One);
        var delta = new NavigationMapOverlayDelta("acquisition", cells: new[]
            { NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), cell) });
        var operation = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(new[] { delta })), sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void Connector_FailedTransactionsPreserveProofWhileSuccessfulRestoreReleasesIt(bool memoryPack)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var agent = CreateConnectorAgent(context, query);
        var steering = agent.Steering!;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long bytes = cache.LeasedBytes;
        object saved = SerializationUtility.SerializeRecord(agent, memoryPack);
        object invalid = SerializationUtility.SetPayloadValue(saved, memoryPack, 99, "Steering", "AcquisitionMode");
        System.Action populate = () => SerializationUtility.PopulateRecord(agent, invalid, memoryPack);
        populate.Should().Throw<System.ArgumentOutOfRangeException>();
        agent.TryReplaceNavigationProfile(query.Agent).Should().Be(NavigationProfileReplacementStatus.Unchanged);
        agent.AcceptPhysicalReplacement = false;
        var profile = new NavigationAgentProfile(query.Agent.Shape, query.Agent.MaxStepUp,
            query.Agent.MaxDropDown, Fixed64.Quarter, query.Agent.AllowedMedia, query.Agent.Capabilities);
        agent.TryReplaceNavigationProfile(profile).Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        cache.LeasedBytes.Should().Be(bytes);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(1);
        context.Simulate();
        steering.GetHeading(agent, out _).X.Should().BeLessThan(Fixed64.Zero);
        cache.LeasedBytes.Should().Be(bytes);
        SerializationUtility.PopulateRecord(agent, saved, memoryPack);
        steering.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Scheduled);
        steering.HasNavigationGuidance.Should().BeFalse();
        cache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        agent.Reset();
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Immediate)]
    [InlineData(NavigationAcquisitionMode.Scheduled)]
    public void Connector_RebindMustReleaseOldContextOwnershipRegardlessOfAcquisitionMode(NavigationAcquisitionMode mode)
    {
        using var first = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        using var second = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out _);
        var steering = AdoptConnector(first, query);
        steering.ConfigureAcquisitionMode(mode);
        steering.BindContext(second);
        first.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        first.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.ShouldMove.Should().BeTrue();
        steering.StopMove();
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Immediate)]
    [InlineData(NavigationAcquisitionMode.Scheduled)]
    public void Connector_PathingOnlyResetMustReleaseProofEvenBeforeNextHeading(NavigationAcquisitionMode mode)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = AdoptConnector(context, query);
        steering.ConfigureAcquisitionMode(mode);
        object lifetime = context.ClockLifetime;
        context.Pathing.Reset();
        context.ClockLifetime.Should().BeSameAs(lifetime);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        steering.StopMove();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Connector_ShouldReleaseProofOnExactAndToleranceAdvance(bool exact)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = AdoptConnector(context, query);
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.LeasedBytes.Should().BeGreaterThan(cache.CachedBytes);
        var foot = query.Start.Position + (exact ? Vector3d.Zero : Vector3d.Right * (Fixed64)0.1m);
        var reached = new MockSteerAgent(foot) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        context.Simulate();
        steering.GetHeading(reached, out _);
        cache.LeasedBytes.Should().Be(cache.CachedBytes);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        steering.ShouldMove.Should().BeTrue();
        steering.StopMove();
        cache.LeasedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void AdoptedConnector_ShouldReleaseEverySessionBoundary(int boundary)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var agent = CreateConnectorAgent(context, query);
        var steering = agent.Steering!;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.LeasedBytes.Should().BeGreaterThan(cache.CachedBytes);
        switch (boundary)
        {
            case 0: steering.StopMove(); break;
            case 1: steering.Arrive(); break;
            case 2: agent.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Slow); break;
            case 3: agent.Reset(); break;
            case 4: agent.ApplyGuidedTrekRequest(query.WithStartState(agent.FootPosition, TraversalMedium.Solid)); break;
            case 5:
                var profile = new NavigationAgentProfile(query.Agent.Shape, query.Agent.MaxStepUp,
                    query.Agent.MaxDropDown, Fixed64.Quarter, query.Agent.AllowedMedia, query.Agent.Capabilities);
                agent.TryReplaceNavigationProfile(profile).Should().Be(NavigationProfileReplacementStatus.Applied);
                break;
            case 6: JsonRecordSerializer.Populate(agent, JsonRecordSerializer.Serialize(agent)); break;
            case 7: context.Reset(); steering.GetHeading(agent, out _); break;
            case 8: context.Dispose(); break;
        }
        cache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
        if (agent.IsActive && !context.IsDisposed) agent.Reset();
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Scheduled)]
    [InlineData(NavigationAcquisitionMode.Immediate)]
    public void AdoptedConnector_ModeChangesMustNotBypassPermitOrSurviveReset(NavigationAcquisitionMode mode)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        var steering = AdoptConnector(context, query);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Immediate);
        steering.ConfigureAcquisitionMode(mode);
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
        steering.HasNavigationGuidance.Should().BeTrue();
        context.Reset();
        steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero);
        steering.HasNavigationGuidance.Should().BeFalse();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        steering.StopMove();
    }

    private static NavSteering AdoptConnector(TrailblazerWorldContext context, PathQuery query, Vector3d? movedFoot = null,
        NavSteering? steering = null)
    {
        steering ??= new NavSteering(context);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.ConfigurePathRechecks(10000);
        steering.ApplyPathQuery(query);
        steering.GetHeading(new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 }, out _);
        var moved = new MockSteerAgent(movedFoot ?? query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        for (int i = 0; i < 100 && !steering.HasNavigationGuidance; i++)
        {
            context.Simulate();
            steering.GetHeading(moved, out _);
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        return steering;
    }

    private static TestNavigator CreateConnectorAgent(TrailblazerWorldContext context, PathQuery query)
    {
        var agent = new TestNavigator(context);
        agent.Setup(query.Start.Position, query.Agent);
        agent.Initialize(new TrekCondition { Medium = TraversalMedium.Solid, SurfaceLevel = query.Start.Position.Y });
        var steering = agent.Steering!;
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        agent.ApplyGuidedTrekRequest(query);
        steering.GetHeading(agent, out _);
        agent.SetTestPosition(query.Start.Position + Vector3d.Right);
        for (int i = 0; i < 100 && !steering.HasNavigationGuidance; i++)
        {
            context.Simulate();
            steering.GetHeading(agent, out _);
        }
        steering.HasNavigationGuidance.Should().BeTrue();
        return agent;
    }

    [Theory]
    [InlineData(453, NavigationGuideStatus.CapacityExceeded)]
    [InlineData(454, NavigationGuideStatus.Success)]
    public void ConnectorProof_ShouldReserveAllRetainedBytesBeforeAllocation(int available, NavigationGuideStatus expected)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long payloadBytes = cache.LeasedBytes;
        long held = context.Settings.QueryLimits.MaxAStarActivePayloadBytes - payloadBytes - available;
        cache.TryReserveEndpointProof(held).Should().BeTrue();
        try
        {
            context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Right, guide).Should().Be(expected);
            cache.LeasedBytes.Should().Be(payloadBytes + (expected == NavigationGuideStatus.Success ? 454 : 0));
            cache.ReservedPayloadBytes.Should().Be(held);
            if (expected == NavigationGuideStatus.Success)
            {
                cache.TryReserveEndpointProof(1).Should().BeFalse();
                cache.TryReservePayload(1, out _).Should().BeFalse("ordinary payload reservation includes connector charges");
                context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Right, guide)
                    .Should().Be(NavigationGuideStatus.Success, "replacement drops the old proof before reserving its replacement");
                cache.LeasedBytes.Should().Be(payloadBytes + 454);
                guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
                cache.LeasedBytes.Should().Be(payloadBytes);
            }
        }
        finally { cache.ReleaseEndpointProof(ref held); }
    }

    [Theory]
    [InlineData(NavigationAcquisitionMode.Scheduled)]
    [InlineData(NavigationAcquisitionMode.Immediate)]
    public void MovedFoot_ShouldRejoinOriginalAnchorAndReproveEachHeadingWithoutRebuilding(
        NavigationAcquisitionMode nextMode)
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query);
        var steering = new NavSteering(context);
        steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        steering.ConfigurePathRechecks(10000);
        var initial = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        steering.ApplyPathQuery(query);
        steering.GetHeading(initial, out _);
        Vector3d heading = Vector3d.Zero;
        for (int frame = 0; frame < 100 && heading == Vector3d.Zero; frame++)
        {
            context.Simulate();
            heading = steering.GetHeading(moved, out _);
        }
        heading.X.Should().BeLessThan(Fixed64.Zero, "the original ordinary anchor is behind the moved foot");
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
        cache.LeasedBytes.Should().BeGreaterThan(cache.CachedBytes, "the independently owned connector has a real charge");
        steering.ConfigureAcquisitionMode(nextMode);
        steering.GetHeading(moved, out _).Should().Be(Vector3d.Zero, "same-frame calls cannot repeat connector work");
        context.Simulate();
        steering.GetHeading(moved, out _).X.Should().BeLessThan(Fixed64.Zero);
        var offMap = new MockSteerAgent(query.Start.Position + Vector3d.Forward * 3) { NavigationProfile = query.Agent, Speed = Fixed64.One };
        context.Simulate();
        steering.GetHeading(offMap, out _).Should().Be(Vector3d.Zero, "unchanged dependencies do not prove a newly moved segment");
        for (int repeat = 0; repeat < 3; repeat++) steering.GetHeading(offMap, out _).Should().Be(Vector3d.Zero);
        steering.ShouldMove.Should().BeTrue("connector recovery cannot synchronously retry in the same frame after a mode change");
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
        steering.StopMove();
        cache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }

    [Fact]
    public void MultipleConnectorOwners_ShouldAllProgressAlongsideALaterAdoptionUnderOneOperation()
    {
        using var context = NavSteeringAcquisitionTests.CreateContext(PathAlgorithm.AStar, out var query,
            scheduling: new NavigationSchedulingSettings(operationStepLimit: 1));
        var owners = new NavSteering[3];
        var initial = new MockSteerAgent(query.Start.Position) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        var moved = new MockSteerAgent(query.Start.Position + Vector3d.Right) { NavigationProfile = query.Agent, Speed = (Fixed64)20 };
        int[] progresses = new int[3];
        for (int i = 0; i < owners.Length; i++)
        {
            owners[i] = new NavSteering(context);
            owners[i].ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
            owners[i].ConfigurePathRechecks(10000);
            owners[i].ApplyPathQuery(query);
            owners[i].GetHeading(initial, out _);
            for (int frame = 0; frame < 512 && !owners[i].HasNavigationGuidance; frame++)
            {
                context.Simulate();
                for (int j = 0; j <= i; j++)
                {
                    var h = owners[j].GetHeading(moved, out _);
                    if (h.X < Fixed64.Zero) progresses[j]++;
                    owners[j].CurrentQuery?.Start.Position.Should().Be(query.Start.Position);
                }
                context.Guides.Scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
            }
            owners[i].HasNavigationGuidance.Should().BeTrue();
        }
        for (int i = 0; i < progresses.Length; i++) progresses[i] = 0;
        int lookupWork = 0;
        int coveredWork = 0;
        for (int frame = 0; frame < 24; frame++)
        {
            context.Simulate();
            for (int i = 0; i < owners.Length; i++)
                if (owners[i].GetHeading(moved, out _).X < Fixed64.Zero)
                {
                    progresses[i]++;
                    lookupWork += context.Pathing.ImmediateRayWorkspace.WorkMeter.LookupProbes;
                    coveredWork += context.Pathing.ImmediateRayWorkspace.WorkMeter.CoveredVoxelIntervals;
                }
            context.Guides.Scheduler.LastFrameOperationCount.Should().BeLessThanOrEqualTo(1);
        }
        for (int i = 0; i < progresses.Length; i++)
            progresses[i].Should().BeInRange(6, 8, $"owner {i} must receive deterministic bounded turns despite earlier host-order callers");
        _output.WriteLine($"24 frames, three fixed-order owners: successful rays=[{string.Join(",", progresses)}], "
            + $"waiting heading calls={72 - progresses[0] - progresses[1] - progresses[2]}, lookupWork={lookupWork}, coveredWork={coveredWork}.");
        foreach (var owner in owners) owner.StopMove();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(0);
        context.Guides.Scheduler.RetainedValidationInterestCount.Should().Be(0);
    }
}
