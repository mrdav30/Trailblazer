//=======================================================================
// NavigationGuideStatusMapper.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Maps internal query progress and failures to public guide statuses.</summary>
internal static class NavigationGuideStatusMapper
{
    internal static NavigationGuideStatus ToPublic(NavigationFlowFieldStatus status) => status switch
    {
        NavigationFlowFieldStatus.Success => NavigationGuideStatus.Success,
        NavigationFlowFieldStatus.Pending => NavigationGuideStatus.Pending,
        NavigationFlowFieldStatus.NoPath => NavigationGuideStatus.NoPath,
        NavigationFlowFieldStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
        NavigationFlowFieldStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
        NavigationFlowFieldStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
        _ => NavigationGuideStatus.Stale
    };

    internal static NavigationGuideStatus ToPublic(NavigationAStarQueryStatus status) => status switch
    {
        NavigationAStarQueryStatus.Success => NavigationGuideStatus.Success,
        NavigationAStarQueryStatus.Unsupported => NavigationGuideStatus.Unsupported,
        NavigationAStarQueryStatus.NoMap => NavigationGuideStatus.NoMap,
        NavigationAStarQueryStatus.InvalidProfile => NavigationGuideStatus.InvalidProfile,
        NavigationAStarQueryStatus.InvalidStart => NavigationGuideStatus.InvalidStart,
        NavigationAStarQueryStatus.InvalidEnd => NavigationGuideStatus.InvalidEnd,
        NavigationAStarQueryStatus.NoPath => NavigationGuideStatus.NoPath,
        NavigationAStarQueryStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
        NavigationAStarQueryStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
        NavigationAStarQueryStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
        NavigationAStarQueryStatus.Stale => NavigationGuideStatus.Stale,
        _ => NavigationGuideStatus.Stale
    };

    internal static NavigationGuideStatus ToPublic(NavigationFlowQueryStatus status) => status switch
    {
        NavigationFlowQueryStatus.Success => NavigationGuideStatus.Success,
        NavigationFlowQueryStatus.Unsupported => NavigationGuideStatus.Unsupported,
        NavigationFlowQueryStatus.NoMap => NavigationGuideStatus.NoMap,
        NavigationFlowQueryStatus.InvalidProfile => NavigationGuideStatus.InvalidProfile,
        NavigationFlowQueryStatus.InvalidStart => NavigationGuideStatus.InvalidStart,
        NavigationFlowQueryStatus.InvalidEnd => NavigationGuideStatus.InvalidEnd,
        NavigationFlowQueryStatus.NoPath => NavigationGuideStatus.NoPath,
        NavigationFlowQueryStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
        NavigationFlowQueryStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
        NavigationFlowQueryStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
        NavigationFlowQueryStatus.Stale => NavigationGuideStatus.Stale,
        _ => NavigationGuideStatus.Stale
    };

    internal static NavigationGuideStatus ToPublic(NavigationRayStatus status)
    {
        System.Diagnostics.Debug.Assert(status is
            NavigationRayStatus.Blocked or
            NavigationRayStatus.BudgetExceeded or
            NavigationRayStatus.CostOverflow or
            NavigationRayStatus.CapacityExceeded or
            NavigationRayStatus.Stale,
            "Ray recovery maps only terminal statuses not handled by its caller.");
        return status switch
        {
            NavigationRayStatus.Blocked => NavigationGuideStatus.LocalRecoveryRequired,
            NavigationRayStatus.BudgetExceeded => NavigationGuideStatus.BudgetExceeded,
            NavigationRayStatus.CostOverflow => NavigationGuideStatus.CostOverflow,
            NavigationRayStatus.CapacityExceeded => NavigationGuideStatus.CapacityExceeded,
            _ => NavigationGuideStatus.Stale
        };
    }

    internal static NavigationFlowFieldStatus ToFlowField(
        NavigationTraversalEdgeAdvanceStatus status)
    {
        System.Diagnostics.Debug.Assert(status is
            NavigationTraversalEdgeAdvanceStatus.Complete or
            NavigationTraversalEdgeAdvanceStatus.BudgetExceeded or
            NavigationTraversalEdgeAdvanceStatus.CostOverflow or
            NavigationTraversalEdgeAdvanceStatus.CapacityExceeded or
            NavigationTraversalEdgeAdvanceStatus.Stale,
            "Flow traversal maps only terminal statuses not handled by its caller.");
        return status switch
        {
            NavigationTraversalEdgeAdvanceStatus.BudgetExceeded =>
                NavigationFlowFieldStatus.BudgetExceeded,
            NavigationTraversalEdgeAdvanceStatus.CostOverflow =>
                NavigationFlowFieldStatus.CostOverflow,
            NavigationTraversalEdgeAdvanceStatus.CapacityExceeded =>
                NavigationFlowFieldStatus.CapacityExceeded,
            _ => NavigationFlowFieldStatus.Stale
        };
    }
}
