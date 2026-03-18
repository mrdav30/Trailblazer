using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

public class TrekRequestTests
{
    [Fact]
    public void Clone_ShouldCopyFrameState()
    {
        var request = new TrekRequest
        {
            Origin = new Vector3d(1, 2, 3),
            Rotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)),
            Direction = Vector3d.Forward,
            Rate = TrekRate.Fast,
            IsRequestingJump = true,
            IsRequestingFlight = true
        };

        TrekRequest clone = request.Clone();

        clone.Should().NotBeSameAs(request);
        clone.Origin.Should().Be(request.Origin);
        clone.FootPosition.Should().Be(request.FootPosition);
        clone.Rotation.Should().Be(request.Rotation);
        clone.Direction.Should().Be(request.Direction);
        clone.Rate.Should().Be(request.Rate);
        clone.IsRequestingJump.Should().Be(request.IsRequestingJump);
        clone.IsRequestingFlight.Should().Be(request.IsRequestingFlight);
    }

    [Fact]
    public void Reset_ShouldClearFrameLocalState()
    {
        var request = new TrekRequest
        {
            Origin = new Vector3d(1, 2, 3),
            Rotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, Fixed64.FromRaw(0x10000000L)),
            Direction = Vector3d.Right,
            Rate = TrekRate.Moderate,
            IsRequestingJump = true,
            IsRequestingFlight = true,
            FootPosition = new Vector3d(1, 1, 3)
        };

        request.Reset();

        request.Origin.Should().Be(Vector3d.Zero);
        request.FootPosition.Should().BeNull();
        request.Rotation.Should().Be(FixedQuaternion.Identity);
        request.Direction.Should().Be(Vector3d.Zero);
        request.Rate.Should().Be(TrekRate.Stationary);
        request.IsRequestingJump.Should().BeFalse();
        request.IsRequestingFlight.Should().BeFalse();
    }
}
