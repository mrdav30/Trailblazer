using System;
using Chronicler.Timing;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

public sealed class JumpTimeRecordingTests
{
    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void WideJumpTimestampRoundTripsAndContinuesItsElapsedWindow(bool useMemoryPack)
    {
        using var context = TrailblazerWorldContext.CreateOwned();
        TimingTestUtility.SetTime(context, int.MaxValue, new ChronicleTimestamp(3155760000L, 0x80000000u));
        var source = new JumpLocomotion { MaxJumpCount = 2 };
        source.BindContext(context);
        source.RegisterJump();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = new JumpLocomotion();
        target.BindContext(context);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        context.Simulate();
        Assert.True(target.IsJumping);
        Assert.True(target.IsHoldingJump);
        Assert.Equal(1, target.JumpCount);
        Assert.Equal(new ChronicleTimestamp(3155760000L, 0x80000000u), target.JumpStartTime);
        Assert.Equal(new ChronicleDuration(0, 0x08000000u), context.ElapsedTime - target.JumpStartTime);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
#endif
    public void InvalidJumpTimingRecordPreservesExistingConfigurationAndState(bool useMemoryPack, int defect)
    {
        var source = new JumpLocomotion { MaxJumpCount = 3 };
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = defect switch
        {
            0 => SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "SchemaVersion"),
            1 => SerializationUtility.SetPayloadValue(payload, useMemoryPack, 99, "SchemaVersion"),
            2 => SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "JumpStartTime"),
            _ => SerializationUtility.SetPayloadValue(payload, useMemoryPack, -1L, "JumpStartTime", "WholeSeconds")
        };
        var target = new JumpLocomotion
        {
            MaxJumpCount = 7,
            IsJumping = true,
            IsHoldingJump = true,
            JumpStartTime = new ChronicleTimestamp(3155760000L, 17)
        };

        if (defect == 3)
            Assert.Throws<ArgumentOutOfRangeException>(() => SerializationUtility.PopulateRecord(target, payload, useMemoryPack));
        else
            Assert.Throws<InvalidOperationException>(() => SerializationUtility.PopulateRecord(target, payload, useMemoryPack));
        Assert.Equal(7, target.MaxJumpCount);
        Assert.True(target.IsJumping);
        Assert.True(target.IsHoldingJump);
        Assert.Equal(new ChronicleTimestamp(3155760000L, 17), target.JumpStartTime);
    }
}
