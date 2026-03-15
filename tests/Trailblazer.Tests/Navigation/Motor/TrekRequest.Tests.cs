using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

public class TrekRequestTests
{
    [Fact]
    public void Clone_ShouldCopyFootAndTargetState()
    {
        var request = new TrekRequest
        {
            Origin = new Vector3d(1, 2, 3),
            Rotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)),
            Direction = Vector3d.Forward,
            Rate = TrekRate.Fast,
            IsRequestingJump = true
        };

        TrekRequest clone = request.Clone();

        clone.Should().NotBeSameAs(request);
        clone.Origin.Should().Be(request.Origin);
        clone.FootPosition.Should().Be(request.FootPosition);
        clone.Rotation.Should().Be(request.Rotation);
        clone.TargetPosition.Should().Be(request.TargetPosition);
        clone.Direction.Should().Be(request.Direction);
        clone.Rate.Should().Be(request.Rate);
        clone.IsRequestingJump.Should().Be(request.IsRequestingJump);
    }

    [Fact]
    public void Reset_ShouldClearFrameLocalState_ButPreserveGuidedTarget()
    {
        var request = new TrekRequest
        {
            Origin = new Vector3d(1, 2, 3),
            Rotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)),
            Direction = Vector3d.Right,
            Rate = TrekRate.Moderate,
            IsRequestingJump = true,
            FootPosition = new Vector3d(1, 1, 3),
            TargetPosition = new Vector3d(9, 0, 9)
        };

        request.Reset();

        request.Origin.Should().Be(Vector3d.Zero);
        request.FootPosition.Should().BeNull();
        request.Rotation.Should().Be(FixedQuaternion.Identity);
        request.TargetPosition.Should().Be(new Vector3d(9, 0, 9));
        request.Direction.Should().Be(Vector3d.Zero);
        request.Rate.Should().Be(TrekRate.Stationary);
        request.IsRequestingJump.Should().BeFalse();
    }
}
