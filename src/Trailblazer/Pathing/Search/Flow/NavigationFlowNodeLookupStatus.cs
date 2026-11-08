//=======================================================================
// NavigationFlowNodeLookupStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Samples only the canonical selected edge stored by one flow node.</summary>
internal enum NavigationFlowNodeLookupStatus : byte
{
    Success,
    NotFound,
    BudgetExceeded
}
