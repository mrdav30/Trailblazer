//=======================================================================
// NavigationCellFlags.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores optional authoring hints that participate in navigation-map composition.
/// </summary>
[Flags]
public enum NavigationCellFlags
{
    /// <summary>
    /// No optional authoring hints are present.
    /// </summary>
    None = 0,

    /// <summary>
    /// The cell may act as the source of a generated semantic transition.
    /// </summary>
    TransitionSourceHint = 1 << 0,

    /// <summary>
    /// The cell may act as the destination of a generated semantic transition.
    /// </summary>
    TransitionDestinationHint = 1 << 1,

    /// <summary>
    /// The cell exposes a climbable surface to transition generation.
    /// </summary>
    ClimbSurfaceHint = 1 << 2
}
