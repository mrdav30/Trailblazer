//=======================================================================
// NavigationEndpointResolutionStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports bounded endpoint-resolution progress.</summary>
internal enum NavigationEndpointResolutionStatus : byte
{
    Pending = 0,
    Success = 1,
    NoMap = 2,
    InvalidEndpoint = 3,
    BudgetExceeded = 4,
    CostOverflow = 5,
    CapacityExceeded = 6,
    Stale = 7
}
