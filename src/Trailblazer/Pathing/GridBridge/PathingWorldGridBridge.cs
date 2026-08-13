//=======================================================================
// PathingWorldGridBridge.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>
/// Subscribes one pathing state owner to one <see cref="GridWorld"/>'s grid lifecycle events.
/// </summary>
internal sealed class PathingWorldGridBridge : IDisposable
{
    private readonly PathingWorldState _state;

    private bool _disposed;

    internal PathingWorldGridBridge(PathingWorldState state)
    {
        _state = state;
    }

    internal void HandleCommittedChange(GridEventInfo eventInfo)
    {
        if (_disposed)
            return;
        switch (eventInfo.ChangeKind)
        {
            case GridEventKind.GridAdded:
                HandleGridAdded(eventInfo);
                break;
            case GridEventKind.GridRemoved:
                HandleGridRemoved(eventInfo);
                break;
            case GridEventKind.WorldReset:
                HandleGridReset();
                break;
            default:
                HandleGridChanged(eventInfo);
                break;
        }
    }

    internal ExternalGridBridgeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        using (PathManager.EnterState(_state))
            return PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
    }

    internal void ResetDiagnostics()
    {
        using (PathManager.EnterState(_state))
            PathManagerExternalGridBridge.ResetDiagnostics();
    }

    internal void FlushPendingGridChanges()
    {
        using (PathManager.EnterState(_state))
            PathManagerExternalGridBridge.FlushPendingGridChanges();
    }

    private void HandleGridAdded(GridEventInfo eventInfo)
    {
        using (PathManager.EnterState(_state))
            PathManagerExternalGridBridge.HandleGridAdded(eventInfo);
    }

    private void HandleGridRemoved(GridEventInfo eventInfo)
    {
        using (PathManager.EnterState(_state))
            PathManagerExternalGridBridge.HandleGridRemoved(eventInfo);
    }

    private void HandleGridChanged(GridEventInfo eventInfo)
    {
        using (PathManager.EnterState(_state))
            PathManagerExternalGridBridge.HandleGridChanged(eventInfo);
    }

    private void HandleGridReset()
    {
        PathManager.ResetPathingState(_state, resetScopedRegistries: true, flushGuideCache: true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
