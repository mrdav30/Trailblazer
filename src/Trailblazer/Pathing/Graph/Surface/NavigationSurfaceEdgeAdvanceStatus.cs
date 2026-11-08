//=======================================================================
// NavigationSurfaceEdgeAdvanceStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports bounded progress through the candidate surface edges.</summary>
internal enum NavigationSurfaceEdgeAdvanceStatus : byte
{
    Pending = 0,
    Edge = 1,
    Complete = 2,
    Blocked = 3
}
