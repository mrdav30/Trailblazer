//=======================================================================
// ProfileReplacementQueryResult.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Gravitas;
using Gravitas.Queries;

namespace Trailblazer.Navigation;

internal static class ProfileReplacementQueryResult
{
    internal static NavigationProfileReplacementStatus Resolve(PhysicsQueryExecutionStatus status,
        ColliderReconfigurationStatus? result) => status != PhysicsQueryExecutionStatus.Completed
        ? NavigationProfileReplacementStatus.PhysicalFitUnavailable
        : result == ColliderReconfigurationStatus.Blocked
            ? NavigationProfileReplacementStatus.PhysicalFitRejected
            : NavigationProfileReplacementStatus.Applied;
}
