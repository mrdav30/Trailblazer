//=======================================================================
// NavigationTransitionIdentityKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Tags transition identity so explicit and procedural IDs cannot collide.</summary>
public enum NavigationTransitionIdentityKind : byte
{
    /// <summary>The action is owned by an explicit map transition definition.</summary>
    Definition = 0,

    /// <summary>The action is produced by a bounded procedural transition rule.</summary>
    Rule = 1
}
