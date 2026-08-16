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
    Success = 0,
    /// <summary>The requested query family is not available at this service.</summary>
    Unsupported = 1,
    /// <summary>No eligible navigation map was available.</summary>
    NoMap = 2,
    /// <summary>The query's navigation profile was invalid.</summary>
    InvalidProfile = 3,
    /// <summary>The start endpoint could not be resolved.</summary>
    InvalidStart = 4,
    /// <summary>The end endpoint could not be resolved.</summary>
    InvalidEnd = 5,
    /// <summary>No route exists between the resolved endpoints.</summary>
    NoPath = 6,
    /// <summary>The finite query work budget was exhausted.</summary>
    BudgetExceeded = 7,
    /// <summary>Fixed-point route cost could not be represented.</summary>
    CostOverflow = 8,
    /// <summary>Finite query, cache, or lease capacity was exhausted.</summary>
    CapacityExceeded = 9,
    /// <summary>A required graph dependency changed during the operation.</summary>
    Stale = 10,
    /// <summary>The actual foot position requires bounded local field recovery.</summary>
    LocalRecoveryRequired = 11
}
