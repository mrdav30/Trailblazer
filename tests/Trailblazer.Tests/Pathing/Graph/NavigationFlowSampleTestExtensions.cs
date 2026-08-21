//=======================================================================
// NavigationFlowSampleTestExtensions.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

internal static class NavigationFlowSampleTestExtensions
{
    internal static NavigationGuideStatus TrySampleHeading(
        this NavigationFlowFieldLease lease,
        Vector3d actualFootPosition,
        GuideSampleWorkBudget budget,
        out Vector3d heading)
    {
        NavigationGuideStatus status = lease.TrySample(actualFootPosition, budget, out NavigationFlowSample sample);
        heading = sample.Heading;
        return status;
    }
}
