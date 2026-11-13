using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DSupportTests
{
    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void TopDownPopulationReplaysTheAcceptedTurnAndSilentlyRebuildsOccupancy(bool memoryPack)
    {
        using var sourceContext = Scene(NavigationMode2D.TopDown);
        using var targetContext = Scene(NavigationMode2D.TopDown);
        using var incompatibleContext = Scene();
        var profile = new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.Quarter,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            TraversalMedia.Solid, TraversalCapability.None);
        var motor = new NavigationMotorSettings2D(Fixed64.One, Fixed64.Two, Fixed64.Zero, Fixed64.Zero);
        using var source = new Host(sourceContext, profile, motor);
        using var target = new Host(targetContext, profile, motor);
        using var incompatible = new Host(incompatibleContext);
        source.OccupantGroupId = 9;
        Turn(sourceContext, source);
        targetContext.Simulate();
        targetContext.LateSimulate();
        object payload = SerializationUtility.SerializeRecord(source, memoryPack);
        int notifications = 0;
        target.CommittedCellChanged += _ => notifications++;
        SerializationUtility.PopulateRecord(target, payload, memoryPack);
        target.Pose.RotationRadians.Should().Be(Fixed64.FromFraction(1, 8));
        target.OccupantGroupId.Should().Be(9);
        target.LastCommittedCell.Should().Be(source.LastCommittedCell);
        GridOccupantManager.GetOccupiedIndices(targetContext.World, target).Should().ContainSingle();
        notifications.Should().Be(0);
        Assert.Throws<InvalidOperationException>(() => SerializationUtility.PopulateRecord(incompatible, payload, memoryPack));
        for (int i = 0; i < 5; i++)
        {
            Turn(sourceContext, source);
            Turn(targetContext, target);
            target.Pose.Should().Be(source.Pose);
            target.Facing.Should().Be(source.Facing);
        }
        notifications.Should().Be(0);

        static void Turn(TrailblazerWorldContext context, Host host)
        {
            context.Simulate();
            host.PrepareFrameMotion(new NavigationInput2D(null, false, Vector2d.Forward));
            host.Result = new NavigationMotionResult2D(host.Request.Target, NavigationSupportStatus2D.NotApplicable);
            host.CommitFrameMotion();
            context.LateSimulate();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, false)]
    [InlineData(true, true)]
#endif
    public void SideViewPopulationReacquiresFromTheRefreshedSupportOwner(bool memoryPack, bool scheduled)
    {
        using var sourceContext = GuidedStepScene(flat: true);
        using var source = new Host(sourceContext);
        source.Sample(FlatSupport(source.Pose.Center));
        source.RefreshSupport();
        var mode = scheduled ? NavigationAcquisitionMode.Scheduled : NavigationAcquisitionMode.Immediate;
        source.ConfigureAcquisitionMode(mode);
        source.ApplySideViewGuidedTrekRequest(GuidedAddress(0), GuidedAddress(1), SideViewPolicy,
            PathAlgorithm.FlowField, SideViewBudget).Should().Be(scheduled ? NavigationGuideStatus.Pending : NavigationGuideStatus.Success);
        var checkpoint = Vector2d.Right * Fixed64.FromFraction(3, 4);
        for (int i = 0; i < 128 && source.Pose.Center != checkpoint; i++)
            Follow(sourceContext, source);
        source.Pose.Center.Should().Be(checkpoint);
        source.HasArrived.Should().BeFalse();
        object payload = SerializationUtility.SerializeRecord(source, memoryPack);

        Replay().Should().Equal(Replay());

        List<(NavigationPose2D Pose, Vector2d Velocity, NavigationGuideStatus? Status, NavigationCellAddress? Changed)> Replay()
        {
            using var context = GuidedStepScene(flat: true);
            using var host = new Host(context);
            while (context.FrameCount < sourceContext.FrameCount)
            {
                context.Simulate();
                context.LateSimulate();
            }
            NavigationCellAddress? changed = null;
            host.CommittedCellChanged += cell => changed = cell?.Address;
            SerializationUtility.PopulateRecord(host, payload, memoryPack);
            host.Pose.Center.Should().Be(checkpoint);
            host.AcquisitionMode.Should().Be(mode);
            host.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
            host.Support.Should().BeNull();
            changed.Should().BeNull();
            context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
            if (scheduled)
            {
                context.Simulate();
                host.PrepareFrameMotion();
                host.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
                context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
                host.Accept(checkpoint, null);
                host.CommitFrameMotion();
                context.LateSimulate();
            }
            host.Sample(FlatSupport(checkpoint));
            host.RefreshSupport();
            var replay = new List<(NavigationPose2D, Vector2d, NavigationGuideStatus?, NavigationCellAddress?)>();
            for (int i = 0; i < 128 && !host.HasArrived; i++)
            {
                Follow(context, host);
                replay.Add((host.Pose, host.Velocity, host.GuidanceStatus, changed));
                changed = null;
            }
            host.HasArrived.Should().BeTrue("the reacquired route ended at {0} with {1}", host.Pose.Center,
                host.GuidanceStatus);
            host.Pose.Center.Should().Be(Vector2d.Right);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
            return replay;
        }

        static NavigationSupport2D FlatSupport(Vector2d center) =>
            new(new Vector2d(center.X, -Fixed64.Half), Vector2d.Forward, Fixed64.One);

        static void Follow(TrailblazerWorldContext context, Host host)
        {
            context.Simulate();
            host.PrepareFrameMotion();
            host.Accept(host.Request.Target.Center, FlatSupport(host.Request.Target.Center));
            host.CommitFrameMotion();
            context.LateSimulate();
        }
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void LateMalformedTrekPreservesPreparedMotionAndExistingGuide(bool memoryPack)
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        ApplySideView(host).Should().Be(NavigationGuideStatus.Success);
        Step(context, host, default);
        object payload = SerializationUtility.SerializeRecord(host, memoryPack);
        object priorSchema = SerializationUtility.SetPayloadValue(payload, memoryPack, 1, "SchemaVersion");
        priorSchema = SerializationUtility.SetPayloadValue(priorSchema, memoryPack, Fixed64.Zero, "ArrivalRadius");
        object[] invalidRecords =
        [
            priorSchema,
            SerializationUtility.SetPayloadValue(payload, memoryPack, 0, "SchemaVersion"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, "Trailblazer.Navigator", "Kind"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, 0, "Layer"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, Fixed64.One, "Rotation"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, -1, "CarrierId"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, (NavigationAcquisitionMode)127, "AcquisitionMode"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, "missing", "AcceptedMap"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, 0, "Trek", "SchemaVersion"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, 1, "Trek", "SchemaVersion"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, -Fixed64.One, "Trek", "ArrivalRadius"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, -1, "Trek", "MaxLookupProbes"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, 99L, "Trek", "PolicyRevision"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, -2, "Trek", "MovementGroupId"),
            SerializationUtility.SetPayloadValue(payload, memoryPack, "missing", "Trek", "DestinationMap")
        ];
        context.Simulate();
        host.PrepareFrameMotion();
        var before = host.Pose;
        var priorCell = host.LastCommittedCell;
        var priorCost = host.GuideCost;
        foreach (object invalid in invalidRecords)
        {
            Assert.Throws<InvalidOperationException>(() => SerializationUtility.PopulateRecord(host, invalid, memoryPack));
            host.Pose.Should().Be(before);
            host.LastCommittedCell.Should().Be(priorCell);
            host.GuideCost.Should().Be(priorCost);
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);
        }
        context.Pathing.NavigationGraphStore.MarkSafetyPending();
        try
        {
            Assert.Throws<InvalidOperationException>(() => SerializationUtility.PopulateRecord(host, payload, memoryPack));
        }
        finally
        {
            context.Pathing.NavigationGraphStore.ClearSafetyPending();
        }
        host.Accept(host.Request.Target.Center, Ground);
        host.CommitFrameMotion();
        context.LateSimulate();
        host.Pose.Center.Should().Be(host.Request.Target.Center);

        context.Simulate();
        host.PrepareFrameMotion();
        SerializationUtility.PopulateRecord(host, payload, memoryPack);
        host.Pose.Should().Be(before);
        host.Support.Should().BeNull();
        host.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        Assert.Throws<InvalidOperationException>(host.CommitFrameMotion);
        context.LateSimulate();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void ChangedPostureAndConsumedJumpRoundTripIntoTheMatchingShell(bool memoryPack)
    {
        using var sourceContext = GuidedStepScene(flat: true);
        using var restoredContext = GuidedStepScene(flat: true);
        using var wrongPostureContext = GuidedStepScene(flat: true);
        var identity = new Guid("ed1398cd-eed6-4dd7-bde4-d18f60625177");
        using var source = new PostureHost(sourceContext, identity: identity);
        var compact = Posture(Fixed64.Zero);
        source.RefreshSupport();
        source.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.Applied);
        source.RefreshSupport();
        TryJump(sourceContext, source);
        source.Velocity.Y.Should().Be((Fixed64)3);
        object payload = SerializationUtility.SerializeRecord(source, memoryPack);
        using var restored = new PostureHost(restoredContext, center: source.Pose.Center, initial: compact, identity: identity);
        using var wrongPosture = new PostureHost(wrongPostureContext, identity: identity);
        restoredContext.Simulate();
        restoredContext.LateSimulate();
        Assert.Throws<InvalidOperationException>(() => SerializationUtility.PopulateRecord(wrongPosture, payload, memoryPack));
        wrongPosture.NavigationProfile.Should().Be(Profile);
        SerializationUtility.PopulateRecord(restored, payload, memoryPack);
        restored.NavigationProfile.Should().Be(compact);
        restored.HostProfile.Should().Be(compact);
        restored.Pose.Center.Should().Be(restored.HostCenter);
        restored.HostCalls.Should().Be(0);
        restored.RefreshSupport();

        for (int i = 0; i < 2; i++)
        {
            TryJump(sourceContext, source);
            TryJump(restoredContext, restored);
            restored.Pose.Should().Be(source.Pose);
            restored.Velocity.Should().Be(source.Velocity);
            restored.Facing.Should().Be(-Vector2d.Right);
            restored.Velocity.Y.Should().Be((Fixed64)(2 - i));
        }

        static void TryJump(TrailblazerWorldContext context, PostureHost host)
        {
            context.Simulate();
            host.PrepareFrameMotion(new NavigationInput2D(null, true, -Vector2d.Right));
            host.CommitFrameMotion();
            context.LateSimulate();
        }
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void CarrierContinuationRebindsByStableIdAndPoseWithTheRecordedAnchor(bool memoryPack)
    {
        using var context = Scene();
        using var host = new Host(context);
        var prior = new NavigationPose2D(Vector2d.Zero, Fixed64.Pi / 3);
        var anchor = new Vector2d(3, 2);
        host.Sample(new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 12, prior, anchor)));
        host.RefreshSupport();
        var payload = SerializationUtility.SerializeRecord(host, memoryPack);
        SerializationUtility.PopulateRecord(host, payload, memoryPack);
        host.Support.Should().BeNull();
        host.Sample(new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, prior, Vector2d.Zero)));
        host.RefreshSupport();
        host.Support!.Value.Carrier!.Value.LocalAnchor.Should().Be(anchor);
        host.Support.Value.Carrier.Value.Lifetime.Should().Be(11);
        var next = new NavigationPose2D(Vector2d.Right, Fixed64.Pi / 2);
        host.CarrierPose = next;
        Vector2d.TryGetRelativeOffset(next.Center, anchor, next.RotationRadians,
            prior.Center, anchor, prior.RotationRadians, out var expected).Should().BeTrue();
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Pose.Center.Should().Be(expected);

        SerializationUtility.PopulateRecord(host, payload, memoryPack);
        host.Sample(new NavigationSupport2D(Vector2d.Zero, Vector2d.Forward, Fixed64.One,
            new NavigationCarrier2D(7, 11, next, Vector2d.Zero)));
        host.RefreshSupport();
        host.Support!.Value.Carrier!.Value.LocalAnchor.Should().Be(Vector2d.Zero);
        Step(context, host, new NavigationInput2D(Vector2d.Zero, false));
        host.Pose.Center.Should().Be(Vector2d.Zero);

        SerializationUtility.PopulateRecord(host, payload, memoryPack);
        host.Sample(Ground);
        host.RefreshSupport();
        host.Support!.Value.Carrier.Should().BeNull();
    }

    [Fact]
    public void RejectedIncompleteIntentDoesNotProduceAnUnloadableRecord()
    {
        using var context = GuidedStepScene(flat: true);
        using var host = new Host(context);
        host.Sample(Ground);
        host.RefreshSupport();
        host.ApplySideViewGuidedTrekRequest(GuidedAddress(0), default, SideViewPolicy,
            PathAlgorithm.AStar, SideViewBudget).Should().Be(NavigationGuideStatus.InvalidEnd);
        var payload = SerializationUtility.SerializeRecord(host, false);
        SerializationUtility.PopulateRecord(host, payload, false);
        host.GuidanceStatus.Should().BeNull();
        host.HasArrived.Should().BeFalse();
    }

    [Fact]
    public void PopulationRejectsAnOccupiedDestinationBeforeRetiringTheOldTicket()
    {
        using var sourceContext = GuidedStepScene(flat: true);
        using var targetContext = GuidedStepScene(flat: true);
        using var source = new Host(sourceContext, pose: new NavigationPose2D(Vector2d.Right, Fixed64.Zero));
        using var target = new Host(targetContext);
        target.Sample(Ground);
        target.RefreshSupport();
        Step(targetContext, target, new NavigationInput2D(Vector2d.Zero, false));
        var previous = GridOccupantManager.GetOccupiedIndices(targetContext.World, target);
        previous.Should().ContainSingle();
        var destination = new Vector3d(Fixed64.One, Fixed64.One, Fixed64.Zero);
        targetContext.World.TryGetGrid(destination, out var grid).Should().BeTrue();
        grid!.TryGetVoxel(GuidedAddress(1).Index, out var voxel).Should().BeTrue();
        for (int i = 1; i <= GridOccupantManager.MaxOccupantCount; i++)
            grid.TryAddVoxelOccupant(voxel!, new OccupancyFixture(new Guid(i, 0, 0, new byte[8]), destination)).Should().BeTrue();
        voxel!.HasVacancy.Should().BeFalse();

        object payload = SerializationUtility.SerializeRecord(source, false);
        Assert.Throws<InvalidOperationException>(() => SerializationUtility.PopulateRecord(target, payload, false));
        target.Pose.Center.Should().Be(Vector2d.Zero);
        target.LastCommittedCell!.Value.Address.Should().Be(GuidedAddress(0));
        GridOccupantManager.GetOccupiedIndices(targetContext.World, target).Should().Equal(previous);
    }

    [Fact]
    public void PopulationPreservesDetachedPoseWithoutRetainingItsOldCell()
    {
        using var sourceContext = Scene();
        using var targetContext = Scene();
        var outside = new Vector2d(12, 12);
        using var source = new Host(sourceContext, pose: new NavigationPose2D(outside, Fixed64.Zero));
        using var target = new Host(targetContext);
        target.Sample(Ground);
        target.RefreshSupport();
        Step(targetContext, target, new NavigationInput2D(Vector2d.Zero, false));
        GridOccupantManager.GetOccupiedIndices(targetContext.World, target).Should().ContainSingle();
        var payload = SerializationUtility.SerializeRecord(source, false);
        SerializationUtility.PopulateRecord(target, payload, false);
        target.Pose.Center.Should().Be(outside);
        target.LastCommittedCell.Should().BeNull();
        GridOccupantManager.GetOccupiedIndices(targetContext.World, target).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReacquisitionAcceptsOnlyTheOpenRoundingStripOfTheWitnessSupport(bool steep)
    {
        using var context = TallSlopeScene(steep, ceiling: false, out var profile);
        var pathing = context.Pathing;
        pathing.TryGetPlanarBinding("side", out var plane).Should().BeTrue();
        var source = TallAddress(0);
        pathing.ResolveSideViewAnchor(source, profile, SideViewPolicy, SideViewBudget, out var anchor)
            .Should().Be(NavigationGuideStatus.Success);
        var witness = new Vector2d(Fixed64.Zero, steep ? -Fixed64.Half : -Fixed64.FromFraction(3, 8));
        var outward = steep ? -Vector2d.Right : Vector2d.Forward;
        var rounded = anchor + outward * Fixed64.MinIncrement;
        Resolve(rounded).Should().Be(NavigationGuideStatus.Success);
        pathing.ValidateSideViewControllerConnector(source, "side", plane, "side", rounded, rounded, anchor, null,
            profile, SideViewPolicy, SideViewBudget, allowRoundedOrigin: true).Should().Be(NavigationGuideStatus.Success);
        Resolve(anchor - outward * Fixed64.MinIncrement).Should().Be(NavigationGuideStatus.InvalidStart);
        Resolve(anchor + outward * Fixed64.FromRaw(3)).Should().Be(NavigationGuideStatus.InvalidStart);

        NavigationGuideStatus Resolve(Vector2d center) => pathing.ResolveSideViewResumeSource("side", plane, "side",
            witness, center, profile, SideViewPolicy, out _, out _);
    }

    [Fact]
    public void ResumeSupportDiscoveryHonorsPublicationAndLookupCapacity()
    {
        using var context = GuidedStepScene(flat: true);
        var pathing = context.Pathing;
        pathing.TryGetPlanarBinding("side", out var plane).Should().BeTrue();
        var witness = new Vector2d(Fixed64.Quarter, -Fixed64.Half);
        var center = Vector2d.Right * Fixed64.Quarter;
        pathing.ResolveSideViewResumeSource("missing", plane, "missing", witness, center,
            Profile, SideViewPolicy, out _, out _).Should().Be(NavigationGuideStatus.NoMap);
        pathing.ResolveSideViewResumeSource("side", plane, "side", witness, center,
            Profile, new NavigationAreaPolicyKey("unpublished", 1), out _, out _).Should().Be(NavigationGuideStatus.Stale);
        var store = pathing.NavigationGraphStore;
        store.MarkSafetyPending();
        try
        {
            pathing.ResolveSideViewResumeSource("side", plane, "side", witness, center,
                Profile, SideViewPolicy, out _, out _).Should().Be(NavigationGuideStatus.CapacityExceeded);
        }
        finally
        {
            store.ClearSafetyPending();
        }

        using var snapshot = TestRequire.NotNull(pathing.TryAcquireNavigationGraph());
        var graph = snapshot.Graph;
        graph.AreaCatalog.TryGet(SideViewPolicy, out var policy).Should().BeTrue();
        var meter = new NavigationWorkMeter(default);
        Resolve(64, 16).Should().Be(NavigationGuideStatus.Success);
        int needed = meter.LookupProbes;
        Resolve(needed - 1, 16).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Resolve(1, 16).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Resolve(0, 16).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Resolve(64, 0).Should().Be(NavigationGuideStatus.BudgetExceeded);
        Resolve(64, 16, "missing").Should().Be(NavigationGuideStatus.Success);

        NavigationGuideStatus Resolve(int lookups, int candidates, string preferred = "side")
        {
            meter.Reset(new NavigationWorkBudget(lookups, candidates, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            return pathing.ImmediateRayWorkspace.RayWork.Workspace.Planar.ResolveSideViewResumeSource(context.World,
                graph, plane, witness, center, Profile, policy!, meter, preferred, out _, out _);
        }
    }
}
