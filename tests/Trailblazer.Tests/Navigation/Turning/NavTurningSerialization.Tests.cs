using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Turning;
using Xunit;

namespace Trailblazer.Tests.Navigation.Turning;

public class NavTurningSerializationTests
{
    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldResumeBufferedTurnAndReplaceExistingTurnState(bool useMemoryPack)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var source = new NavTurning(context, Fixed64.One);
        source.RequestTurnDirection(Vector3d.Forward, Vector3d.Right, interpolation: Fixed64.One);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        var target = new NavTurning(context, Fixed64.One);
        target.RequestTurnDirection(Vector3d.Forward, Vector3d.Left, interpolation: Fixed64.Half);
        target.TrySimulateTurn(Vector3d.Zero, Vector3d.Zero, Vector3d.Forward,
            FixedQuaternion.Identity, out _).Should().BeTrue();
        target.TargetReached.Should().BeFalse();
        target.RequestTurnDirection(Vector3d.Forward, Vector3d.Backward);
        target.NotifyCollision();

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.TargetReached.Should().BeTrue();
        target.TargetRotation.Should().Be(FixedQuaternion.Identity);
        target.TrySimulateTurn(Vector3d.Zero, Vector3d.Zero, Vector3d.Forward,
            FixedQuaternion.Identity, out FixedQuaternion applied).Should().BeTrue();
        applied.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
        target.TargetReached.Should().BeTrue("the buffered interpolation of one must also be restored");
        for (int frame = 0; frame < 2; frame++)
        {
            target.TrySimulateTurn(Vector3d.Left, Vector3d.Zero, Vector3d.Right,
                applied, out _).Should().BeFalse("neither the old queued target nor collision signal may survive population");
        }
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldCompleteActiveTurnBeforeItsBufferedSuccessor(bool useMemoryPack)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var source = new NavTurning(context, Fixed64.One);
        source.RequestTurnDirection(Vector3d.Forward, Vector3d.Right, interpolation: Fixed64.Half);
        source.TrySimulateTurn(Vector3d.Zero, Vector3d.Zero, Vector3d.Forward,
            FixedQuaternion.Identity, out FixedQuaternion rotation).Should().BeTrue();
        source.TargetReached.Should().BeFalse();
        source.RequestTurnDirection(rotation.Rotate(Vector3d.Forward), Vector3d.Left,
            interpolation: Fixed64.One);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = new NavTurning(context, Fixed64.One);

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.TargetReached.Should().BeFalse();
        target.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
        foreach (Vector3d direction in new[] { Vector3d.Right, Vector3d.Left })
        {
            Vector3d forward = rotation.Rotate(Vector3d.Forward);
            source.TrySimulateTurn(Vector3d.Zero, Vector3d.Zero, forward,
                rotation, out FixedQuaternion expected).Should().BeTrue();
            target.TrySimulateTurn(Vector3d.Zero, Vector3d.Zero, forward,
                rotation, out FixedQuaternion actual).Should().BeTrue();
            expected.Should().Be(FixedQuaternion.FromDirection(direction));
            actual.Should().Be(expected);
            target.TargetReached.Should().BeTrue();
            rotation = actual;
        }
        target.TrySimulateTurn(Vector3d.Zero, Vector3d.Zero, Vector3d.Left,
            rotation, out _).Should().BeFalse("the buffered successor is consumed exactly once");
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreCollisionSignalAndRetainShellCollisionPredicate(bool useMemoryPack)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var source = new NavTurning(context, Fixed64.One);
        source.NotifyCollision();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = new NavTurning(context, Fixed64.One);
        bool allowTurn = false;
        int predicateCalls = 0;
        Func<bool> predicate = () =>
        {
            predicateCalls++;
            return allowTurn;
        };
        target.CanTurnOnCollision = predicate;

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.CanTurnOnCollision.Should().BeSameAs(predicate);
        target.TrySimulateTurn(Vector3d.Right, Vector3d.Zero, Vector3d.Forward,
            FixedQuaternion.Identity, out _).Should().BeFalse();
        target.TargetReached.Should().BeTrue();
        predicateCalls.Should().Be(1);
        allowTurn = true;
        target.TrySimulateTurn(Vector3d.Right, Vector3d.Zero, Vector3d.Forward,
            FixedQuaternion.Identity, out _).Should().BeFalse("the admitted collision turn is buffered first");
        predicateCalls.Should().Be(2);
        target.TrySimulateTurn(Vector3d.Right, Vector3d.Zero, Vector3d.Forward,
            FixedQuaternion.Identity, out FixedQuaternion applied).Should().BeTrue();
        applied.Should().NotBe(FixedQuaternion.Identity);
        target.TargetRotation.Should().Be(FixedQuaternion.FromDirection(Vector3d.Right));
        predicateCalls.Should().Be(2, "consuming a buffered turn does not consult the collision predicate again");
    }
}
