//=======================================================================
// TraversalTransitionOverlayOperationKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the final-state action for a source-owned semantic transition.</summary>
public enum TraversalTransitionOverlayOperationKind : byte
{
    /// <summary>Write or replace a complete transition definition.</summary>
    Upsert = 0,

    /// <summary>Tombstone the effective source-owned transition.</summary>
    Suppress = 1,

    /// <summary>Remove the override or tombstone and restore the baked definition.</summary>
    RevertToBake = 2
}
