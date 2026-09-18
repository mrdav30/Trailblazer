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
        int connectionStepLimit = 64,
        int operationStepLimit = 64)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(maxRetainedRequests <= 0, maxRetainedRequests, nameof(maxRetainedRequests));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(maxRetainedDescriptorBytes <= 0, null, nameof(maxRetainedDescriptorBytes));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(lookupStepLimit <= 0, lookupStepLimit, nameof(lookupStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(endpointCandidateStepLimit <= 0, endpointCandidateStepLimit, nameof(endpointCandidateStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(nodeStepLimit <= 0, nodeStepLimit, nameof(nodeStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(edgeStepLimit <= 0, edgeStepLimit, nameof(edgeStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(connectionStepLimit <= 0, connectionStepLimit, nameof(connectionStepLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(operationStepLimit <= 0, operationStepLimit, nameof(operationStepLimit));
        MaxRetainedRequests = maxRetainedRequests;
        MaxRetainedDescriptorBytes = maxRetainedDescriptorBytes;
        LookupStepLimit = lookupStepLimit;
        EndpointCandidateStepLimit = endpointCandidateStepLimit;
        NodeStepLimit = nodeStepLimit;
        EdgeStepLimit = edgeStepLimit;
        ConnectionStepLimit = connectionStepLimit;
        OperationStepLimit = operationStepLimit;
    }

    /// <summary>Gets the maximum queued, active and completed-but-unclaimed requests combined.</summary>
    public int MaxRetainedRequests { get; }
    /// <summary>Gets the logical query descriptor-byte ceiling across all retained requests.</summary>
    public long MaxRetainedDescriptorBytes { get; }
    /// <summary>Gets the shared lookup allowance per context frame; one atomic evaluation may overrun it.</summary>
    public int LookupStepLimit { get; }
    /// <summary>Gets the shared endpoint-candidate allowance per context frame.</summary>
    public int EndpointCandidateStepLimit { get; }
    /// <summary>Gets the shared node allowance, including clearing, reconstruction and payload copying, per context frame.</summary>
    public int NodeStepLimit { get; }
    /// <summary>Gets the shared evaluated-edge allowance per context frame.</summary>
    public int EdgeStepLimit { get; }
    /// <summary>Gets the shared connection-leg allowance per context frame.</summary>
    public int ConnectionStepLimit { get; }
    /// <summary>Gets the shared frame ceiling on queue operations, mandatory direct checks and granted Navigator validation turns.</summary>
    /// <remarks>A validation turn includes constant-time owner selection and one atomic validation; unused turns expire without refill.</remarks>
    public int OperationStepLimit { get; }
}
