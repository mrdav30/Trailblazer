using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Tests.Navigation;

internal sealed class TestNavigator : Navigator
{
    public TrekRequest FrameRequest => _frameRequest;

    public override void CheckTrekCondition()
    {
    }
}
