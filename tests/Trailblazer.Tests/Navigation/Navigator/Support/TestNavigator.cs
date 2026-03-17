using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation;

internal sealed class TestNavigator : Navigator
{
    public TrekRequest FrameRequest => _frameRequest;

    public TrekCondition FrameCondition => _frameCondition;

    public override void CheckTrekCondition()
    {
    }
}
