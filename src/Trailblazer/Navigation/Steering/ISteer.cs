//=======================================================================
// ISteer.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Defines the contract for an agent that can move and steer within a voxel-based world, providing access
/// to its velocity, speed, acceleration, and spatial characteristics.
/// </summary>
public interface ISteer : IVoxelOccupant
{
    /// <summary>
    /// The current simulation velocity of the object in world units per second.
    /// </summary>
    Vector3d Velocity { get; }

    /// <summary>
    /// The current movement speed in world units per second, derived from the magnitude of the velocity.
    /// </summary>
    Fixed64 Speed { get; }

    /// <summary>
    /// The current simulation acceleration in world units per second squared, based on fixed-step velocity change.
    /// </summary>
    Vector3d Acceleration { get; }

    /// <summary>
    /// Minimum speed the agent must maintain to avoid being considered stuck.
    /// </summary>
    Fixed64 StuckThresholdSpeed { get; }

    /// <summary>
    /// Gets the exact immutable navigation profile that owns the agent body shape.
    /// </summary>
    NavigationAgentProfile NavigationProfile { get; }

    /// <summary>
    /// Gets the authoritative body shape from <see cref="NavigationProfile"/>.
    /// </summary>
    KinematicBodyShape BodyShape { get; }

    /// <summary>
    /// Body radius used for spatial checks.
    /// </summary>
    Fixed64 Radius { get; }
}
