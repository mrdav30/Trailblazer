using System;
using Chronicler.Timing;
using FixedMathSharp;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests;

public sealed class TrailblazerWideClockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExhaustionLeavesPendingPublicationClockAndHooksUnchanged(bool exhaustTime)
    {
        using var context = TrailblazerWorldContext.CreateOwned(
            settings: TrailblazerTimingBoundaryTests.PublicationSettings());
        long frame = exhaustTime ? 10 : long.MaxValue;
        var elapsed = exhaustTime
            ? new ChronicleTimestamp(long.MaxValue, uint.MaxValue)
            : new ChronicleTimestamp(3155760000L, 0);
        TimingTestUtility.SetTime(context, frame, elapsed);
        var operation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(new NavigationAreaPolicyKey("pending", 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 1, long.MaxValue);
        Assert.True(context.Pathing.Admit(operation));
        int calls = 0;
        using var hook = context.RegisterOnSimulate("exhaustion", 0, () => calls++);
        object lifetime = context.ClockLifetime;

        if (exhaustTime)
            Assert.Throws<OverflowException>(context.Simulate);
        else
            Assert.Throws<InvalidOperationException>(context.Simulate);

        Assert.Equal(frame, context.FrameCount);
        Assert.Equal(elapsed, context.ElapsedTime);
        Assert.Same(lifetime, context.ClockLifetime);
        Assert.Equal(0, calls);
        Assert.Equal(NavigationOperationStatus.Pending, operation.Receipt.Status);
        Assert.Equal(-1, operation.Receipt.PublishedFrame);
        context.Reset();
        Assert.Equal(NavigationOperationStatus.Superseded, operation.Receipt.Status);
    }

    [Fact]
    public void FuturePublicationAboveIntRangeWaitsForItsExactFrameAndRejectsLateWork()
    {
        using var context = TrailblazerWorldContext.CreateOwned(
            settings: TrailblazerTimingBoundaryTests.PublicationSettings());
        TimingTestUtility.SetTime(context, int.MaxValue - 1L);
        long due = int.MaxValue + 1L;
        var operation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(new NavigationAreaPolicyKey("future", 1),
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 1, due);
        Assert.True(context.Pathing.Admit(operation));
        context.Simulate();
        Assert.Equal(NavigationOperationStatus.Pending, operation.Receipt.Status);
        context.Simulate();
        Assert.Equal(NavigationOperationStatus.Applied, operation.Receipt.Status);
        Assert.Equal(due, operation.Receipt.PublishedFrame);

        var late = new NavigationAreaPolicyCommitOperation(operation.Policy, 2, due);
        Assert.False(context.Pathing.Admit(late));
        Assert.Equal(NavigationOperationRejection.LateEffectiveFrame, late.Receipt.Rejection);
        var regressed = new NavigationAreaPolicyCommitOperation(operation.Policy, 3, due - 1);
        Assert.False(context.Pathing.Admit(regressed));
        Assert.Equal(NavigationOperationRejection.RegressingEffectiveFrame, regressed.Receipt.Rejection);
    }

    [Fact]
    public void RateChangesPreserveWideElapsedHistoryAndResetPreservesStep()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        TimingTestUtility.SetTime(context, int.MaxValue, new ChronicleTimestamp(3155760000L, 0));
        context.SetFrameRate(4);
        context.Simulate();
        context.SetFrameRate(8);
        context.Simulate();
        Assert.Equal(int.MaxValue + 2L, context.FrameCount);
        Assert.Equal(new ChronicleTimestamp(3155760000L, 0x60000000u), context.ElapsedTime);
        context.Reset();
        context.Simulate();
        Assert.Equal(1, context.FrameCount);
        Assert.Equal(new ChronicleTimestamp(0, 0x20000000u), context.ElapsedTime);
        Assert.Equal((Fixed64)8, context.InvDeltaTime);
    }

    [Fact]
    public void DurationCountsUseCompleteRepresentedStepsAndRejectNegativeIntervals()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(30);
        long step = context.DeltaTime.m_rawValue;
        Assert.Equal(0, context.GetFrameCountForDuration(Fixed64.Zero));
        Assert.Equal(0, context.GetFrameCountForDuration(Fixed64.FromRaw(step - 1)));
        Assert.Equal(1, context.GetFrameCountForDuration(Fixed64.FromRaw(step)));
        Assert.Equal(1, context.GetFrameCountForDuration(Fixed64.FromRaw(step + 1)));
        Assert.Equal(Fixed64.MaxValue.m_rawValue / step,
            context.GetFrameCountForDuration(Fixed64.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            context.GetFrameCountForDuration(Fixed64.FromRaw(-1)));
    }

    [Fact]
    public void WarmWideContainingFramesAllocateNothing()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        TimingTestUtility.SetTime(context, int.MaxValue, new ChronicleTimestamp(3155760000L, 0));
        Action step = () => { context.Simulate(); context.LateSimulate(); };
        for (int i = 0; i < 1024; i++) step();
        long before = context.FrameCount;
        Assert.Equal(0, AllocationTestUtility.MeasureAllocatedBytes(step));
        Assert.Equal(before + 1, context.FrameCount);
        Assert.Equal(new ChronicleTimestamp(3155760032L, 0x08000000u), context.ElapsedTime);
    }
}
