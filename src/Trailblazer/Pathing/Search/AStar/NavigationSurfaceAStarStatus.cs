//=======================================================================
// NavigationSurfaceAStarStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports progress and terminal outcomes for the surface A* search.</summary>
internal enum NavigationSurfaceAStarStatus : byte
{
    Pending = 0,
    Success = 1,
    NoPath = 2,
    BudgetExceeded = 3,
    CostOverflow = 4,
    CapacityExceeded = 5,
    Stale = 6
}
