//=======================================================================
// NavigationSurfaceEdgeRouteStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports resumable semantic and swept-geometry edge progress.</summary>
internal enum NavigationSurfaceEdgeRouteStatus : byte
{
    Pending = 0,
    Point = 1,
    Passable = 2,
    Impassable = 3,
    BudgetExceeded = 4,
    CostOverflow = 5
}
