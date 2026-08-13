//=======================================================================
// PathQueryWorkspacePool.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Bounds concurrent query scratch and deterministic retained-pool bytes.</summary>
internal sealed class PathQueryWorkspacePool : IDisposable
{
    private const int BytesPerNode = 24;
    private const int BaseBytes = 64;

    private readonly object _sync = new();
    private readonly SwiftList<PathQueryWorkspaceLease> _retained = new();
    private readonly int _maxConcurrent;
    private readonly long _maxActiveBytes;
    private readonly long _maxRetainedBytes;
    private int _activeCount;
    private long _activeBytes;
    private long _retainedBytes;
    private int _nextStableSlot;
    private bool _disposed;

    internal PathQueryWorkspacePool(TrailblazerWorldContextSettings settings)
    {
        _maxConcurrent = settings.MaxConcurrentPathQueries;
        _maxActiveBytes = settings.MaxActiveWorkspaceBytes;
        _maxRetainedBytes = settings.MaxRetainedWorkspaceBytes;
    }

    internal int ActiveCount
    {
        get { lock (_sync) return _activeCount; }
    }

    internal long ActiveBytes
    {
        get { lock (_sync) return _activeBytes; }
    }

    internal long RetainedBytes
    {
        get { lock (_sync) return _retainedBytes; }
    }

    internal bool TryCheckout(int minimumNodeCapacity, out PathQueryWorkspaceLease? lease)
    {
        SwiftThrowHelper.ThrowIfArgument(minimumNodeCapacity <= 0, nameof(minimumNodeCapacity));
        long requestedBytes = checked(BaseBytes + ((long)minimumNodeCapacity * BytesPerNode));
        lock (_sync)
        {
            if (_disposed
                || _activeCount >= _maxConcurrent
                || requestedBytes > _maxActiveBytes - _activeBytes)
            {
                lease = null;
                return false;
            }

            int best = -1;
            int bestCapacity = int.MaxValue;
            for (int i = 0; i < _retained.Count; i++)
            {
                int capacity = _retained[i].Workspace.NodeCapacity;
                if (capacity >= minimumNodeCapacity
                    && _retained[i].Workspace.RetainedBytes <= _maxActiveBytes - _activeBytes
                    && (capacity < bestCapacity
                        || (capacity == bestCapacity
                            && (best < 0 || _retained[i].StableSlot < _retained[best].StableSlot))))
                {
                    best = i;
                    bestCapacity = capacity;
                }
            }

            PathQueryWorkspaceLease workspaceLease;
            if (best >= 0)
            {
                workspaceLease = _retained[best];
                _retained.RemoveAt(best);
                _retainedBytes -= workspaceLease.Workspace.RetainedBytes;
                workspaceLease.Reinitialize(this);
            }
            else
            {
                workspaceLease = new PathQueryWorkspaceLease(
                    this,
                    new PathQueryWorkspace(minimumNodeCapacity),
                    _nextStableSlot++);
            }

            PathQueryWorkspace workspace = workspaceLease.Workspace;
            _activeCount++;
            _activeBytes += workspace.RetainedBytes;
            lease = workspaceLease;
            return true;
        }
    }

    internal void Return(PathQueryWorkspaceLease lease)
    {
        PathQueryWorkspace workspace = lease.Workspace;
        lock (_sync)
        {
            _activeCount--;
            _activeBytes -= workspace.RetainedBytes;
            if (_disposed)
                return;
            _retained.Add(lease);
            _retainedBytes += workspace.RetainedBytes;
            TrimRetained();
        }
    }

    internal void ClearRetained()
    {
        lock (_sync)
        {
            _retained.Clear();
            _retainedBytes = 0;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _retained.Clear();
            _retainedBytes = 0;
        }
    }

    private void TrimRetained()
    {
        while (_retainedBytes > _maxRetainedBytes)
        {
            int victim = 0;
            for (int i = 1; i < _retained.Count; i++)
            {
                long candidateBytes = _retained[i].Workspace.RetainedBytes;
                long victimBytes = _retained[victim].Workspace.RetainedBytes;
                if (candidateBytes > victimBytes
                    || (candidateBytes == victimBytes
                        && _retained[i].StableSlot < _retained[victim].StableSlot))
                {
                    victim = i;
                }
            }
            _retainedBytes -= _retained[victim].Workspace.RetainedBytes;
            _retained.RemoveAt(victim);
        }
    }
}
