using System;
using Chronicler.Timing;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests;

public sealed class TrailblazerTimingBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue - 1)]
    public void PendingPublicationContinuesAcrossTheFrameBoundary(int initialFrame)
    {
        using var context = TrailblazerWorldContext.CreateOwned(settings: PublicationSettings());
        TimingTestUtility.SetTime(context, initialFrame);
        var first = Operation("first", 1, initialFrame + 1);
        var second = Operation("second", 2, initialFrame + 1);
        Assert.True(context.Pathing.Admit(first));
        Assert.True(context.Pathing.Admit(second));

        context.Simulate();
        Assert.Equal(NavigationOperationStatus.Applied, first.Receipt.Status);
        Assert.Equal(initialFrame + 1, first.Receipt.PublishedFrame);
        Assert.Equal(NavigationOperationStatus.Pending, second.Receipt.Status);

        context.Simulate();
        Assert.Equal(NavigationOperationStatus.Applied, second.Receipt.Status);
        Assert.Equal((long)initialFrame + 2, second.Receipt.PublishedFrame);
    }

    [Fact]
    public void ElapsedTimeContinuesAfterNarrowRange()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        TimingTestUtility.SetTime(context, 1, new ChronicleTimestamp(int.MaxValue, uint.MaxValue));
        var before = context.ElapsedTime;
        context.Simulate();
        Assert.True(context.ElapsedTime > before);
        Assert.Equal(new ChronicleDuration(0, 0x08000000), context.ElapsedTime - before);
    }

    [Fact]
    public void DurationConversionCanExceedIntFrameRange()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        Assert.Equal(2147483648L, context.GetFrameCountForDuration((Fixed64)67108864));
    }

    [Fact]
    public void PendingMotorTraversalCannotCommitInResetLifetimeAtSameFrame()
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        var condition = new TrekCondition { Medium = TraversalMedium.Gas };
        var motor = new NavMotor(context, condition);
        Assert.True(motor.TryTraversal(new TrekRequest(), out _, out _, out _));
        context.Reset();

        Assert.Throws<InvalidOperationException>(() => motor.FinalizeTraversal(
            Vector3d.Up, Vector3d.Zero, FixedQuaternion.Identity, condition));
        Assert.True(motor.TraversalInProgress);
        Assert.Equal(Vector3d.Zero, motor.Handler.Move.FrameVelocity);
        motor.AbortTraversalFrame();
    }

    internal static TrailblazerWorldContextSettings PublicationSettings()
    {
        var d = TrailblazerWorldContextSettings.Default;
        var m = d.MaintenanceBudget;
        return new(d.OperationLimits,
            new MaintenanceWorkBudget(m.MaxConsumedEnvelopes, m.MaxBaselineAddresses, m.MaxOverlaySlots,
                m.MaxComponentNodes, m.MaxSeamCandidateProbes, m.MaxExplicitEdges, 5),
            d.GuideSampleBudget, d.MovementGroupPadding, d.MaxIngressEntries, d.MaxIngressBytes,
            d.MaxActiveSnapshots, d.MaxActiveSnapshotBytes, d.MaxRetiredSnapshots, d.MaxRetiredSnapshotBytes,
            d.MaxPersistentGraphPages, d.MaxDynamicCellSlotsPerMap, d.MaxDynamicCellSlots,
            1, 2, 1, 2, d.MaxConcurrentSnapshotLeases, d.QueryLimits);
    }

    internal static NavigationAreaPolicyCommitOperation Operation(string id, long sequence, long frame) =>
        new(new NavigationAreaPolicy(new NavigationAreaPolicyKey(id, 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) }), sequence, frame);
}
