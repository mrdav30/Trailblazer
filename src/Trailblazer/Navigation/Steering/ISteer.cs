//=======================================================================
// ISteer.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Navigation;

/// <summary>
/// Defines the contract for an agent that can move and steer within a voxel-based world, providing access
/// to its velocity, speed, acceleration, and spatial characteristics.
/// </summary>
public interface ISteer : IVoxelOccupant
{
    /// <summary>
    /// The current velocity of the object in world space.
    /// </summary>
    Vector3d Velocity { get; }

    /// <summary>
    /// The current movement speed, derived from the magnitude of the velocity.
    /// </summary>
    Fixed64 Speed { get; }

    /// <summary>
    /// The current acceleration vector of the object, updated each frame based on velocity change.
    /// </summary>
    Vector3d Acceleration { get; }

    /// <summary>
    /// Minimum speed the agent must maintain to avoid being considered stuck.
    /// </summary>
    Fixed64 StuckThresholdSpeed { get; }

    /// <summary>
    /// The size of object in worldspace.
    /// </summary>
    /// <remarks>
    /// Note: Add a little padding to manevour around blockers
    /// </remarks>
    Fixed64 Size { get; }

    /// <summary>
    /// Half the unit size, used for radius-based spatial checks.
    /// </summary>
    Fixed64 Radius { get; }
}
