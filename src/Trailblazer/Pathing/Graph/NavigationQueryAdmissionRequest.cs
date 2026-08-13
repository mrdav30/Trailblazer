//=======================================================================
// NavigationQueryAdmissionRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Describes one bounded query-resource reservation in deterministic batch order.</summary>
internal readonly struct NavigationQueryAdmissionRequest
{
    internal NavigationQueryAdmissionRequest(
        long operationOrdinal,
        int minimumNodeCapacity,
        long maximumResultBytes)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(operationOrdinal < 0, null, nameof(operationOrdinal));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(minimumNodeCapacity <= 0, minimumNodeCapacity, nameof(minimumNodeCapacity));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(maximumResultBytes < 0, null, nameof(maximumResultBytes));
        OperationOrdinal = operationOrdinal;
        MinimumNodeCapacity = minimumNodeCapacity;
        MaximumResultBytes = maximumResultBytes;
    }

    internal long OperationOrdinal { get; }

    internal int MinimumNodeCapacity { get; }

    internal long MaximumResultBytes { get; }
}
