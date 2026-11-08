//=======================================================================
// NavigationOperationFrameChangeKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the candidate edit staged for an operation frame.</summary>
internal enum NavigationOperationFrameChangeKind
{
    MapCommit,
    MapRemove,
    Overlay
}
