using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation;

internal sealed class TestNavigator : Navigator
{
    public TrekRequest FrameRequest => _frameRequest;

    public TrekCondition FrameCondition => _frameCondition;

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
