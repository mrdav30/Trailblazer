using System;

namespace Trailblazer.Pathing;

/// <summary>Remaining counted work shared by all acquisition phases in one context frame.</summary>
internal struct NavigationAcquisitionSlice
{
    internal int LookupRemaining;
    internal int EndpointCandidateRemaining;
    internal int NodeRemaining;
    internal int EdgeRemaining;
    internal int ConnectionRemaining;

    internal NavigationAcquisitionSlice(int lookupStepLimit, int endpointCandidateStepLimit = 0,
        int nodeStepLimit = 0, int edgeStepLimit = 0, int connectionStepLimit = 0)
    {
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfNegative(endpointCandidateStepLimit, nameof(endpointCandidateStepLimit));
        SwiftThrowHelper.ThrowIfNegative(nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfNegative(connectionStepLimit, nameof(connectionStepLimit));
        LookupRemaining = lookupStepLimit;
        EndpointCandidateRemaining = endpointCandidateStepLimit;
        NodeRemaining = nodeStepLimit;
        EdgeRemaining = edgeStepLimit;
        ConnectionRemaining = connectionStepLimit;
    }

    internal bool HasSameRemaining(in NavigationAcquisitionSlice other) =>
        LookupRemaining == other.LookupRemaining
        && EndpointCandidateRemaining == other.EndpointCandidateRemaining
        && NodeRemaining == other.NodeRemaining
        && EdgeRemaining == other.EdgeRemaining
        && ConnectionRemaining == other.ConnectionRemaining;

    // An indivisible traversal or ray may exceed the frame allowance, never the query budget.
    internal static void ConsumeAtomicLookups(ref int remaining, NavigationWorkMeter meter, int before) =>
        remaining = Math.Max(0, remaining - (meter.LookupProbes - before));
}
