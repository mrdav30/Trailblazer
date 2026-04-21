using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;

namespace Trailblazer.Tests.Navigation;

internal sealed class TestNavigator : Navigator
{
    public TrekRequest FrameRequest => _frameRequest;

    public TrekCondition FrameCondition => _frameCondition;

    public void SetTestSteering(NavSteering steering) => Steering = steering;

    public void SetTestPosition(Vector3d position, bool syncLastPosition = true)
    {
        Position = position;
        if (syncLastPosition)
            LastPosition = position;
    }

    public override void CheckTrekCondition()
    {
    }
}
