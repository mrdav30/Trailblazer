//=======================================================================
// NavigationCellOverlayOperationKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the final-state action for a semantic cell overlay entry.</summary>
public enum NavigationCellOverlayOperationKind : byte
{
    /// <summary>Write a complete effective cell payload.</summary>
    Set = 0,

    /// <summary>Tombstone effective traversal at the address.</summary>
    Suppress = 1,

    /// <summary>Remove the override or tombstone and restore explicit, then default, authoring.</summary>
    RevertToBake = 2
}
