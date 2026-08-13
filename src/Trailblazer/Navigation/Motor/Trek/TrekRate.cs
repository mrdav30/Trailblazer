//=======================================================================
// TrekRate.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>
/// Defines movement speed categories for traversal.
/// </summary>
public enum TrekRate
{
    /// <summary>
    /// No movement (idle state).
    /// </summary>
    Stationary = 0,

    /// <summary>
    /// Slow movement, typically equivalent to walking.
    /// </summary>
    Slow = 1,

    /// <summary>
    /// Moderate movement, commonly used for jogging.
    /// </summary>
    Moderate = 2,

    /// <summary>
    /// Fast movement, typically equivalent to sprinting.
    /// </summary>
    Fast = 3,
}
