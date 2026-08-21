//=======================================================================
// TraversalTransitionLocomotionHints.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores the compact built-in locomotion intent carried by a transition action.</summary>
[Flags]
public enum TraversalTransitionLocomotionHints
{
    /// <summary>No built-in locomotion intent is requested.</summary>
    None = 0,

    /// <summary>Request climb locomotion while the transition is pending.</summary>
    RequestClimb = 1 << 0,

    /// <summary>Preserve climb locomotion after exact transition completion.</summary>
    PreserveClimbAfterCompletion = 1 << 1
}
