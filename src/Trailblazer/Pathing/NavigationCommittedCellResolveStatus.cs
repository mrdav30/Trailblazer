//=======================================================================
// NavigationCommittedCellResolveStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports whether an addressed cell resolved against the published navigation graph.</summary>
internal enum NavigationCommittedCellResolveStatus : byte
{
    NoCell,
    Resolved,
    Unavailable
}
