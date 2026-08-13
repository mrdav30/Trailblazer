//=======================================================================
// GroupBehaviorWeights.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Navigation;

/// <summary>
/// Represents the weighting factors for different group behavior components in a flocking or crowd simulation.
/// </summary>
/// <remarks>
/// Each field corresponds to a specific behavioral influence:
/// - Separation controls the tendency to avoid crowding neighbors
/// - Alignment adjusts the tendency to match the direction of nearby agents
/// - Cohesion influences the tendency to move toward the average position of the group
/// - Avoidance determines the strength of obstacle avoidance.
/// Adjust these weights to fine-tune group movement dynamics.
/// </remarks>
public struct GroupBehaviorWeights
{
    /// <summary>
    /// Controls the tendency to avoid crowding neighbors
    /// </summary>
    public Fixed64 Separation;

    /// <summary>
    /// Adjusts the tendency to match the direction of nearby agents
    /// </summary>
    public Fixed64 Alignment;

    /// <summary>
    /// Influences the tendency to move toward the average position of the group
    /// </summary>
    public Fixed64 Cohesion;

    /// <summary>
    /// Determines the strength of obstacle avoidance
    /// </summary>
    public Fixed64 Avoidance;
}
