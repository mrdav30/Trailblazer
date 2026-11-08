//=======================================================================
// NavigationVolumeAnchorStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the outcome of resolving a volumetric endpoint anchor.</summary>
internal enum NavigationVolumeAnchorStatus : byte
{
    Success,
    Unavailable,
    BudgetExceeded,
    CostOverflow,
    CapacityExceeded,
    Stale
}
