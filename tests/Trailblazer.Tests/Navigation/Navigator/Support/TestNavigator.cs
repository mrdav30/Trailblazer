using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;

namespace Trailblazer.Tests.Navigation;

internal sealed class TestNavigator : Navigator
{
    public TestNavigator() { }

    public TestNavigator(TrailblazerWorldContext context)
        : base(context)
    {
    }

    public TrekRequest FrameRequest => _frameRequest;

    public TrekCondition FrameCondition => _frameCondition;

    public void SetTestSteering(NavSteering steering) => _steering = steering;

    public void SetTestPosition(Vector3d position, bool syncLastPosition = true)
    {
        _position = position;
        if (syncLastPosition)
            _lastPosition = position;
    }

    public override void CheckTrekCondition()
    {
    }
}
