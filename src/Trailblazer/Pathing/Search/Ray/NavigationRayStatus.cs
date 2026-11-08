//=======================================================================
// NavigationRayStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the progress or result of an internal navigation ray.</summary>
internal enum NavigationRayStatus : byte
{
    Pending,
    Success,
    Blocked,
    BudgetExceeded,
    CostOverflow,
    CapacityExceeded,
    Stale,
    CertifiedObstruction
}
