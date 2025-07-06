using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Navigation;
using Xunit;

namespace Trailblazer.Tests.Navigation
{

    //public class NavigatorTests
    //{
    //    [Fact]
    //    public void Setup_Should_Initialize_Position_Rotation_Velocity_And_Size()
    //    {
    //        var nav = new TestNavigator();
    //        var pos = new Vector3d(1, 2, 3);
    //        var rot = FixedQuaternion.FromEulerAngles((Fixed64)0.1f, (Fixed64)0.2f, (Fixed64)0.3f);
    //        var vel = new Vector3d(0.5, 0, 0);
    //        nav.Setup(pos, rot, vel, (Fixed64)2);

    //        nav.Position.Should().Be(pos);
    //        nav.Rotation.Should().Be(rot);
    //        nav.Velocity.Should().Be(vel);
    //        nav.Size.Should().Be((Fixed64)2);
    //    }

    //    [Fact]
    //    public void Simulate_Should_Invoke_MotorTraverse_When_ManualControl()
    //    {
    //        var nav = new TestNavigator();
    //        nav.IsManuallyControlled = true;
    //        var direction = new Vector3d(1, 0, 0);
    //        var rate = TrekRate.Walking;

    //        nav.ApplyInputTravelRequest(direction, rate, isRequestingJump: false);
    //        nav.Position = Vector3d.Zero;
    //        nav.Rotation = FixedQuaternion.Identity;
    //        nav.Simulate();

    //        nav.TraverseCalled.Should().BeTrue();
    //        nav.LastTraverseRequest.Direction.Should().Be(direction);
    //        nav.LastTraverseRequest.Rate.Should().Be(rate);
    //    }

    //    [Fact]
    //    public void Simulate_Should_Invoke_Steering_OnGuidedControl()
    //    {
    //        var nav = new TestNavigator();
    //        nav.IsManuallyControlled = false;

    //        nav.Simulate();
    //        nav.SteeringSimulated.Should().BeTrue();
    //    }

    //    [Fact]
    //    public void GetFootPosition_Should_Return_PositionPlusDownOffset()
    //    {
    //        var nav = new TestNavigator();
    //        nav.Position = new Vector3d(5, 5, 5);
    //        nav.FootPositionAdjust = (Fixed64)0.75;
    //        // Down is (0, -1, 0)
    //        var expected = new Vector3d(5, 5, 5) + Vector3d.Down * (Fixed64)0.75;

    //        nav.GetFootPosition().Should().Be(expected);
    //    }
   // }
}
