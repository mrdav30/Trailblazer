//=======================================================================
// NavigationGuideStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the terminal state of one public navigation guide request or lease operation.</summary>
public enum NavigationGuideStatus : byte
{
    /// <summary>The request or lease operation succeeded.</summary>
    Success,
    /// <summary>The requested query family is not available at this service.</summary>
    Unsupported,
    /// <summary>No eligible navigation map was available.</summary>
    NoMap,
    /// <summary>The query's navigation profile was invalid.</summary>
    InvalidProfile,
    /// <summary>The start endpoint could not be resolved.</summary>
    InvalidStart,
    /// <summary>The end endpoint could not be resolved.</summary>
    InvalidEnd,
    /// <summary>No route exists between the resolved endpoints.</summary>
    NoPath,
    /// <summary>The finite query work budget was exhausted.</summary>
    BudgetExceeded,
    /// <summary>Fixed-point route cost could not be represented.</summary>
    CostOverflow,
    /// <summary>Finite query, cache, or lease capacity was exhausted.</summary>
    CapacityExceeded,
    /// <summary>A required graph dependency changed during the operation.</summary>
    Stale
}
