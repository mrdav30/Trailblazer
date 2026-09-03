//=======================================================================
// INavigate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation;

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
    /// The object's authoritative simulation rotation in world space, before host presentation smoothing.
    /// </summary>
    FixedQuaternion Rotation { get; }

    /// <summary>
    /// The direction the object is currently facing.
    /// </summary>
    Vector3d Forward { get; }
}
