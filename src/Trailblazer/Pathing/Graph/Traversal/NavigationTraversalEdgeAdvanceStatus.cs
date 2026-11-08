//=======================================================================
// NavigationTraversalEdgeAdvanceStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports bounded progress through canonical outgoing traversal edges.</summary>
internal enum NavigationTraversalEdgeAdvanceStatus : byte
{
    Pending = 0,
    Edge = 1,
    Complete = 2,
    Blocked = 3,
    BudgetExceeded = 4,
    CapacityExceeded = 5,
    CostOverflow = 6,
    Stale = 7
}
