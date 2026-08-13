//=======================================================================
// ClimbAffordanceKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Identifies the broad type of climb affordance provided by the host.
/// </summary>
public enum ClimbAffordanceKind
{
    /// <summary>
    /// No climb affordance is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// A ladder-like climb affordance with an explicit up direction.
    /// </summary>
    Ladder = 1,

    /// <summary>
    /// A ledge or edge grab affordance.
    /// </summary>
    Ledge = 2,

    /// <summary>
    /// A free-climb surface affordance.
    /// </summary>
    Surface = 3
}
