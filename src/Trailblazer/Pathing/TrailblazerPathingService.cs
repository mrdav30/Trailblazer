//=======================================================================
// TrailblazerPathingService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned admission, query, maintenance, and diagnostic API for unified navigation maps.
/// </summary>
public sealed class TrailblazerPathingService
{
    private readonly TrailblazerWorldContext _context;

    private bool _disposed;

    private readonly NavigationGraphRuntime _navigationGraph;
    private readonly NavigationImmediateRayWorkspace _immediateRayWorkspace;
    private readonly NavigationAStarAdmissionGate _navigationAStarAdmissionGate;
    private readonly NavigationFlowAdmissionGate _navigationFlowAdmissionGate;

    internal TrailblazerPathingService(TrailblazerWorldContext context)
    {
        _context = context;
        _navigationGraph = new NavigationGraphRuntime(context.World, context.Settings);
        NavigationQueryLimits queryLimits = context.Settings.QueryLimits;
        _immediateRayWorkspace = new NavigationImmediateRayWorkspace(
            Math.Max(
                queryLimits.AStarWorkspaceMapCapacity,
                queryLimits.FlowWorkspaceMapCapacity),
            Math.Max(
                queryLimits.AStarWorkspaceEndpointPageCapacity,
                queryLimits.FlowWorkspaceEndpointPageCapacity),
            Math.Max(
                queryLimits.AStarWorkspaceComponentCapacity,
                queryLimits.FlowWorkspaceComponentCapacity),
            queryLimits.RayWorkspaceCoveredAddressCapacity,
            queryLimits.RayWorkspaceTraceIntervalCapacity);
        var admissionCoordinator = new NavigationQueryAdmissionCoordinator(
            queryLimits.MaxConcurrentNavigationQueries);
        _navigationAStarAdmissionGate = new NavigationAStarAdmissionGate(
            context.World,
            _navigationGraph.Store,
            context.Settings.QueryLimits,
            admissionCoordinator);
        _navigationFlowAdmissionGate = new NavigationFlowAdmissionGate(
            context.World,
            _navigationGraph.Store,
            context.Settings.QueryLimits,
            admissionCoordinator,
            _immediateRayWorkspace);
        context.World.OnChangeCommitted += HandleCommittedChange;
    }

    internal int RetainedBaselineCaptureCount =>
        _navigationGraph.RetainedBaselineCaptureCount;

    internal NavigationWorldGraphStore NavigationGraphStore =>
        _navigationGraph.Store;

    internal NavigationAStarAdmissionGate NavigationAStarAdmissionGate =>
        _navigationAStarAdmissionGate;

    internal NavigationFlowAdmissionGate NavigationFlowAdmissionGate =>
        _navigationFlowAdmissionGate;

    internal NavigationImmediateRayWorkspace ImmediateRayWorkspace =>
        _immediateRayWorkspace;

    internal int RetainedCompositionWorkCount =>
        _navigationGraph.RetainedCompositionWorkCount;

    internal int RetainedOperationWorkCount =>
        _navigationGraph.RetainedOperationWorkCount;

    internal MaintenanceWorkMeter NavigationMaintenanceMeter =>
        _navigationGraph.MaintenanceMeter;

    internal int LastAffectedMapCollectionCount =>
        _navigationGraph.LastAffectedMapCollectionCount;

    /// <summary>Admits one prepared map commit for deterministic fixed-step publication.</summary>
    public bool Admit(NavigationMapCommitOperation operation)
    {
        EnsureUsable();
        return _navigationGraph.Admit(operation);
    }

    /// <summary>Admits one map removal for deterministic fixed-step publication.</summary>
    public bool Admit(NavigationMapRemoveOperation operation)
    {
        EnsureUsable();
        return _navigationGraph.Admit(operation);
    }

    /// <summary>Admits one atomic semantic overlay transaction for deterministic fixed-step publication.</summary>
    public bool Admit(NavigationOverlayCommitOperation operation)
    {
        EnsureUsable();
        return _navigationGraph.Admit(operation);
    }

    /// <summary>Admits one immutable navigation-area policy revision for fixed-step publication.</summary>
    public bool Admit(NavigationAreaPolicyCommitOperation operation)
    {
        EnsureUsable();
        return _navigationGraph.Admit(operation);
    }

    internal void MaintainNavigationGraph(int frame)
    {
        EnsureUsable();
        _navigationGraph.Maintain(frame);
    }

    internal NavigationWorldGraphLease? TryAcquireNavigationGraph() =>
        _navigationGraph.TryAcquire();

    internal bool TryGetNavigationGraphCellState(
        string mapId,
        VoxelIndex index,
        out NavigationGraphCellState state)
    {
        EnsureUsable();
        return _navigationGraph.TryGetCellState(mapId, index, out state);
    }

    internal NavigationCommittedCellResolveStatus TryResolveCommittedCell(
        GridForge.Configuration.GridConfigurationKey gridKey,
        WorldVoxelIndex worldIndex,
        out NavigationCellAddress address,
        out NavigationAreaId area,
        out long graphVersion)
    {
        EnsureUsable();
        NavigationWorldGraph graph = _navigationGraph.Current;
        graphVersion = graph.GraphVersion;
        if (!graph.TryGetMapId(gridKey, out string mapId))
        {
            address = default;
            area = default;
            return NavigationCommittedCellResolveStatus.NoCell;
        }
        graph.TryGetMap(mapId, out NavigationMapInstance? instance);
        System.Diagnostics.Debug.Assert(instance != null);
        if (!instance.GridIdentity.Matches(
                worldIndex.WorldSpawnToken,
                worldIndex.GridIndex,
                worldIndex.GridSpawnToken))
        {
            address = default;
            area = default;
            return NavigationCommittedCellResolveStatus.Unavailable;
        }
        NavigationCell cell;
        if (!instance.TryGetSlot(worldIndex.VoxelIndex, out int slot))
        {
            address = default;
            area = default;
            return NavigationCommittedCellResolveStatus.NoCell;
        }
        bool foundPhysicalState = instance.TryGetPhysicalState(slot, out bool isPresent, out _);
        System.Diagnostics.Debug.Assert(foundPhysicalState);
        if (!isPresent
            || !instance.TryGetEffectiveCell(slot, out cell))
        {
            address = default;
            area = default;
            return NavigationCommittedCellResolveStatus.NoCell;
        }

        address = new NavigationCellAddress(mapId, worldIndex.VoxelIndex);
        area = cell.Area;
        return NavigationCommittedCellResolveStatus.Resolved;
    }

    internal bool TryResolveNavigationAreaPolicy(
        NavigationAreaPolicyKey key,
        out NavigationAreaPolicy? policy)
    {
        EnsureUsable();
        return _navigationGraph.TryResolveAreaPolicy(key, out policy);
    }

    /// <summary>Copies a bounded immutable diagnostic view of the context navigation graph.</summary>
    public NavigationGraphDiagnosticsSnapshot GetNavigationGraphDiagnostics()
    {
        EnsureUsable();
        return _navigationGraph.GetDiagnostics(
            _context.Settings.MaintenanceBudget.MaxBaselineAddresses);
    }

    /// <summary>
    /// Cancels active graph queries and clears this context's unified navigation graph.
    /// </summary>
    public void Reset()
    {
        EnsureUsable();
        _navigationAStarAdmissionGate.CancelActive();
        _navigationFlowAdmissionGate.CancelActive();
        _navigationGraph.Reset();
    }

    internal void Dispose()
    {
        _context.World.OnChangeCommitted -= HandleCommittedChange;
        _navigationAStarAdmissionGate.Dispose();
        _navigationFlowAdmissionGate.Dispose();
        _navigationGraph.Dispose();
        _disposed = true;
    }

    private void HandleCommittedChange(GridEventInfo eventInfo) =>
        _navigationGraph.EnqueueCommittedChange(eventInfo);

    private void EnsureUsable()
    {
        SwiftThrowHelper.ThrowIfDisposed(
            _disposed,
            nameof(TrailblazerWorldContext));
        SwiftThrowHelper.ThrowIfTrue(
            !_context.World.IsActive,
            nameof(TrailblazerPathingService),
            "TrailblazerPathingService is bound to an inactive GridWorld.");
    }
}

internal enum NavigationCommittedCellResolveStatus : byte
{
    NoCell,
    Resolved,
    Unavailable
}
