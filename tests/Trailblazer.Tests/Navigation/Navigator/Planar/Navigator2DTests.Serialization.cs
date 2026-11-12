using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed partial class Navigator2DTests
{
    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void PopulationKeepsTheCurrentFrameConsumed(bool memoryPack)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        context.LateSimulate();

        object payload = SerializationUtility.SerializeRecord(navigator, memoryPack);
        SerializationUtility.PopulateRecord(navigator, payload, memoryPack);
        Assert.Throws<InvalidOperationException>(() => navigator.PrepareFrameMotion());

        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        context.LateSimulate();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RejectedOffGridTrekDoesNotProduceAnUnloadableRecord(bool memoryPack)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ApplyGuidedTrekRequest(Cell(1000, 0), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.InvalidEnd);

        object payload = SerializationUtility.SerializeRecord(navigator, memoryPack);
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.InvalidEnd);
        SerializationUtility.PopulateRecord(navigator, payload, memoryPack);
        navigator.GuidanceStatus.Should().BeNull();
    }

    [Fact]
    public void PublishedTrekRemainsDurableDuringSnapshotPressure()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var store = context.Pathing.NavigationGraphStore;
        object payload;
        store.MarkSafetyPending();
        try
        {
            navigator.ApplyGuidedTrekRequest(Cell(1, 0), Policy.Key, PathAlgorithm.AStar, Budget)
                .Should().Be(NavigationGuideStatus.CapacityExceeded);
            payload = SerializationUtility.SerializeRecord(navigator, false);
        }
        finally
        {
            store.ClearSafetyPending();
        }

        SerializationUtility.PopulateRecord(navigator, payload, false);
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.Pending);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void TopDownTrekAndArrivalRoundTripWithoutKeepingScheduledOwnership(bool memoryPack)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled);
        navigator.ApplyGuidedTrekRequest(Cell(1, 0), Policy.Key, PathAlgorithm.AStar, Budget, groupId: 7)
            .Should().Be(NavigationGuideStatus.Pending);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(1);
        object pending = SerializationUtility.SerializeRecord(navigator, memoryPack);
        SerializationUtility.PopulateRecord(navigator, pending, memoryPack);
        navigator.MovementGroupId.Should().Be(7);
        navigator.AcquisitionMode.Should().Be(NavigationAcquisitionMode.Scheduled);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);
        for (int frame = 0; frame < 128 && !navigator.HasArrived; frame++)
            Step();
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.Should().Be(Vector2d.Right);
        object arrived = SerializationUtility.SerializeRecord(navigator, memoryPack);
        SerializationUtility.PopulateRecord(navigator, arrived, memoryPack);
        navigator.HasArrived.Should().BeTrue();
        navigator.MovementGroupId.Should().Be(-1);
        Step();
        navigator.Pose.Center.Should().Be(Vector2d.Right);
        context.Guides.Scheduler.RetainedRequestCount.Should().Be(0);

        void Step()
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            navigator.Accept(navigator.Request.Target.Center);
            navigator.CommitFrameMotion();
            context.LateSimulate();
        }
    }
}
