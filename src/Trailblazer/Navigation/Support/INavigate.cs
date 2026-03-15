using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <summary>
/// Defines the core interface for a navigator entity, providing position, rotation, traversal state, and event handling.
/// </summary>
public interface INavigate : ISteer
{
    /// <summary>
    /// The last world position of the navigator.
    /// </summary>
    Vector3d LastPosition { get; }

    /// <summary>
    /// The navigator's visual rotation in world space.
    /// </summary>
    FixedQuaternion Rotation { get; }

    /// <summary>
    /// The direction the navigator is currently facing.
    /// </summary>
    Vector3d Forward { get; }
}
