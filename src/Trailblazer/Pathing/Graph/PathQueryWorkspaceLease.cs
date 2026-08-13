//=======================================================================
// PathQueryWorkspaceLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>Returns an exclusive query workspace exactly once.</summary>
internal sealed class PathQueryWorkspaceLease : IDisposable
{
    private PathQueryWorkspacePool? _owner;

    internal PathQueryWorkspaceLease(
        PathQueryWorkspacePool owner,
        PathQueryWorkspace workspace,
        int stableSlot)
    {
        _owner = owner;
        Workspace = workspace;
        StableSlot = stableSlot;
    }

    internal void Reinitialize(PathQueryWorkspacePool owner)
    {
        Volatile.Write(ref _owner, owner);
    }

    internal PathQueryWorkspace Workspace { get; }

    internal int StableSlot { get; }

    public void Dispose()
    {
        PathQueryWorkspacePool? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Return(this);
    }
}
