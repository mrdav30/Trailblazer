using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Turning;

namespace Trailblazer.Navigation;

/// <summary>
/// Defines the core interface for a navigator entity, providing position, rotation, traversal state, and event handling.
/// </summary>
public interface INavigate : ISteer, ITurn
{
    /// <summary>
    /// The current traversal condition of the scout, including medium (ground, air, water) and surface level.
    /// </summary>
    TrekCondition FrameCondition { get; }

    /// <summary>
    /// The traversal request for the current frame, containing directional intent and travel mode.
    /// </summary>
    TrekRequest FrameRequest { get; }

    /// <summary>
    /// Performs a grounded surface check to determine the current traversal condition.
    /// Implementations should update the surface state based on collision or probe logic.
    /// </summary>
    void CheckTrekCondition();
}
