//=======================================================================
// NavigationSchedulingSettings.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Finite retention and counted work slices for context-owned guide acquisition.</summary>
/// <remarks>These limits do not promise an elapsed-time deadline. Query total budgets remain independent.</remarks>
public sealed class NavigationSchedulingSettings
{
    /// <summary>Gets the default bounded scheduler settings.</summary>
    public static NavigationSchedulingSettings Default { get; } = new();

    /// <summary>Creates positive retention ceilings and per-frame continuation limits.</summary>
    public NavigationSchedulingSettings(
        int maxRetainedRequests = 8,
        long maxRetainedDescriptorBytes = 65_536,
        int lookupStepLimit = 8192,
        int endpointCandidateStepLimit = 1,
        int nodeStepLimit = 256,
        int edgeStepLimit = 256,
        int connectionStepLimit = 64)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(maxRetainedRequests <= 0, maxRetainedRequests, nameof(maxRetainedRequests));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(maxRetainedDescriptorBytes <= 0, null, nameof(maxRetainedDescriptorBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(lookupStepLimit <= 0, lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(endpointCandidateStepLimit <= 0, endpointCandidateStepLimit, nameof(endpointCandidateStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(nodeStepLimit <= 0, nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(edgeStepLimit <= 0, edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(connectionStepLimit <= 0, connectionStepLimit, nameof(connectionStepLimit));
        MaxRetainedRequests = maxRetainedRequests;
        MaxRetainedDescriptorBytes = maxRetainedDescriptorBytes;
        LookupStepLimit = lookupStepLimit;
        EndpointCandidateStepLimit = endpointCandidateStepLimit;
        NodeStepLimit = nodeStepLimit;
        EdgeStepLimit = edgeStepLimit;
        ConnectionStepLimit = connectionStepLimit;
    }

    /// <summary>Gets the maximum queued, active and completed-but-unclaimed requests combined.</summary>
    public int MaxRetainedRequests { get; }
    /// <summary>Gets the logical query descriptor-byte ceiling across all retained requests.</summary>
    public long MaxRetainedDescriptorBytes { get; }
    /// <summary>Gets the lookup steps supplied to one continuation slice.</summary>
    public int LookupStepLimit { get; }
    /// <summary>Gets the endpoint candidates supplied to one admission slice.</summary>
    public int EndpointCandidateStepLimit { get; }
    /// <summary>Gets the nodes supplied to one search slice.</summary>
    public int NodeStepLimit { get; }
    /// <summary>Gets the edges supplied to one search slice.</summary>
    public int EdgeStepLimit { get; }
    /// <summary>Gets the connection legs supplied to one search slice.</summary>
    public int ConnectionStepLimit { get; }
}
