//=======================================================================
// NavigationTraversalEvaluationStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports traversal passability or the condition that prevented a result.</summary>
internal enum NavigationTraversalEvaluationStatus : byte
{
    Passable = 0,
    Impassable = 1,
    BudgetExceeded = 2,
    CostOverflow = 3,
    CapacityExceeded = 4,
    Stale = 5
}
