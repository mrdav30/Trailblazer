//=======================================================================
// NavigationFlowQueryStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the complete internal lifecycle of one bounded flow query.</summary>
internal enum NavigationFlowQueryStatus : byte
{
    Pending = 0,
    Success = 1,
    Unsupported = 2,
    NoMap = 3,
    InvalidProfile = 4,
    InvalidStart = 5,
    InvalidEnd = 6,
    NoPath = 7,
    BudgetExceeded = 8,
    CostOverflow = 9,
    CapacityExceeded = 10,
    Stale = 11
}
