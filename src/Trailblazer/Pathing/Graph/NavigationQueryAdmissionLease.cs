//=======================================================================
// NavigationQueryAdmissionLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Owns one graph lease, exclusive workspace, and result-byte reservation.</summary>
internal sealed class NavigationQueryAdmissionLease : IDisposable
{
    private NavigationQueryAdmissionGate? _owner;
    private NavigationWorldGraphLease? _graphLease;
    private PathQueryWorkspaceLease? _workspaceLease;

    internal NavigationQueryAdmissionLease(
        NavigationQueryAdmissionGate owner,
        NavigationWorldGraphLease graphLease,
        PathQueryWorkspaceLease workspaceLease,
        long resultBytes,
        long safetyEpoch)
    {
        Reinitialize(owner, graphLease, workspaceLease, resultBytes, safetyEpoch);
    }

    internal NavigationWorldGraph Graph => Volatile.Read(ref _graphLease)!.Graph;

    internal PathQueryWorkspace Workspace => Volatile.Read(ref _workspaceLease)!.Workspace;

    internal long ResultBytes { get; private set; }

    internal long SafetyEpoch { get; private set; }

    internal void Reinitialize(
        NavigationQueryAdmissionGate owner,
        NavigationWorldGraphLease graphLease,
        PathQueryWorkspaceLease workspaceLease,
        long resultBytes,
        long safetyEpoch)
    {
        _graphLease = graphLease;
        _workspaceLease = workspaceLease;
        ResultBytes = resultBytes;
        SafetyEpoch = safetyEpoch;
        Volatile.Write(ref _owner, owner);
    }

    internal void Detach(
        out NavigationWorldGraphLease graphLease,
        out PathQueryWorkspaceLease workspaceLease,
        out long resultBytes)
    {
        graphLease = Interlocked.Exchange(ref _graphLease, null)!;
        workspaceLease = Interlocked.Exchange(ref _workspaceLease, null)!;
        resultBytes = ResultBytes;
        ResultBytes = 0;
        SafetyEpoch = 0;
    }

    internal void TransferReservationToPayload(long resultBytes) =>
        ResultBytes -= resultBytes;

    public void Dispose()
    {
        NavigationQueryAdmissionGate? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Return(this);
    }
}
