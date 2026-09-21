using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Motor;

public sealed class TransitSlopeTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void RisingPlane_ShouldClassifyHorizontalTravelByItsActualHeightChange(int direction, int expectedSign)
    {
        // Plane y = 3x/4 has outward normal (-3/5, 4/5, 0).
        var state = new TransitState(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            GroundState = new GroundCondition
            {
                SurfaceNormal = new Vector3d(Fixed64.FromFraction(-3, 5), Fixed64.FromFraction(4, 5), Fixed64.Zero)
            }
        });

        Fixed64 angle = state.GetSignedSlopeAngle(Vector3d.Right * (Fixed64)direction);

        Fixed64.Sign(angle).Should().Be(expectedSign);
        angle.Abs().Should().Be(state.SlopeAngle);
        state.GetSignedSlopeAngle(Vector3d.Forward).Should().Be(state.SlopeAngle,
            "the support retains its inclination even along the contour");
        state.GetSignedSlopeAngle(Vector3d.Zero).Should().Be(-state.SlopeAngle,
            "idle slope handling is explicitly gravity-biased");
    }
}
