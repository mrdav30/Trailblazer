using FixedMathSharp;
using Trailblazer.Navigation;

namespace Trailblazer;

/// <summary>
/// Defines the core interface for a object entity, providing position, rotation, traversal state, and event handling.
/// </summary>
public interface INavigate : ISteer
{
    /// <summary>
    /// The last world position of the object.
    /// </summary>
    Vector3d LastPosition { get; }

    /// <summary>
    /// The object's visual rotation in world space.
    /// </summary>
    FixedQuaternion Rotation { get; }

    /// <summary>
    /// The direction the object is currently facing.
    /// </summary>
    Vector3d Forward { get; }
}
