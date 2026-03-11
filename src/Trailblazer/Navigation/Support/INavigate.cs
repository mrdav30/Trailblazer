using Trailblazer.Navigation.Turning;

namespace Trailblazer.Navigation
{
    /// <summary>
    /// Defines the core interface for a navigator entity, providing position, rotation, traversal state, and event handling.
    /// </summary>
    public interface INavigate : ISteer, IMotor, ITurn
    {

    }
}
