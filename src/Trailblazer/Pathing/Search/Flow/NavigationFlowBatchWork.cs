//=======================================================================
// NavigationFlowBatchWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Coordinates one deterministically admitted flow query batch.</summary>
internal readonly struct NavigationFlowBatchWork : IDisposable
{
    private readonly NavigationFlowAdmissionGate? _owner;

    internal NavigationFlowBatchWork(
        NavigationFlowAdmissionGate owner,
        ulong generation)
    {
        _owner = owner;
        Generation = generation;
    }

    internal ulong Generation { get; }

    internal int AdmittedCount => Owner.GetAdmittedCount(this);

    internal bool IsAdmissionComplete => Owner.IsAdmissionComplete(this);

    internal NavigationFlowQueryStatus GetStatus(int inputIndex) =>
        Owner.GetStatus(this, inputIndex);

    internal bool IsReadyToPublish(int inputIndex) =>
        Owner.IsReadyToPublish(this, inputIndex);

    internal void AdvanceAdmission(int lookupStepLimit, int endpointCandidateStepLimit) =>
        Owner.AdvanceAdmission(this, lookupStepLimit, endpointCandidateStepLimit);

    internal NavigationFlowQueryStatus AdvanceSearch(
        int inputIndex,
        int lookupStepLimit,
        int nodeStepLimit,
        int edgeStepLimit,
        int connectionStepLimit) => Owner.AdvanceSearch(
            this,
            inputIndex,
            lookupStepLimit,
            nodeStepLimit,
            edgeStepLimit,
            connectionStepLimit);

    internal int PublishReadyPrefix(int maximumCount) =>
        Owner.PublishReadyPrefix(this, maximumCount);

    internal NavigationFlowQueryResult TakeResult(int inputIndex) =>
        Owner.TakeResult(this, inputIndex);

    public void Dispose() => _owner?.Release(this);

    internal bool IsOwnedBy(NavigationFlowAdmissionGate owner) =>
        ReferenceEquals(_owner, owner);

    private NavigationFlowAdmissionGate Owner =>
        _owner ?? throw new ObjectDisposedException(nameof(NavigationFlowBatchWork));
}
