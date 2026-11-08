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
