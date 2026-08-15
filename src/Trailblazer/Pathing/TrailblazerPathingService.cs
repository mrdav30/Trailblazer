//=======================================================================
// TrailblazerPathingService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned pathing API for chart registration, live chart state, and local pathing queries.
/// </summary>
public sealed class TrailblazerPathingService
{
    private readonly TrailblazerWorldContext _context;

    private bool _disposed;

    private readonly NavigationGraphRuntime _navigationGraph;
    private readonly NavigationAStarAdmissionGate _navigationAStarAdmissionGate;

    internal TrailblazerPathingService(TrailblazerWorldContext context)
    {
        _context = context;
        State = new PathingWorldState(context);
        _navigationGraph = new NavigationGraphRuntime(context.World, context.Settings);
        _navigationAStarAdmissionGate = new NavigationAStarAdmissionGate(
            context.World,
            _navigationGraph.Store,
            context.Settings.QueryLimits);
        context.World.OnChangeCommitted += HandleCommittedChange;
    }

    internal PathingWorldState State { get; }

    internal int RetainedBaselineCaptureCount =>
        _navigationGraph.RetainedBaselineCaptureCount;

    internal NavigationWorldGraphStore NavigationGraphStore =>
        _navigationGraph.Store;

    internal NavigationAStarAdmissionGate NavigationAStarAdmissionGate =>
        _navigationAStarAdmissionGate;

    internal int RetainedCompositionWorkCount =>
        _navigationGraph.RetainedCompositionWorkCount;

    internal long RetainedCompositionWorkBytes =>
        _navigationGraph.RetainedCompositionWorkBytes;

    internal long RetainedOperationWorkBytes =>
        _navigationGraph.RetainedOperationWorkBytes;

    internal int RetainedOperationWorkCount =>
        _navigationGraph.RetainedOperationWorkCount;

    internal int RetainedCompositionWorkPageCount =>
        _navigationGraph.RetainedCompositionWorkPageCount;

    internal int RetainedOperationWorkPageCount =>
        _navigationGraph.RetainedOperationWorkPageCount;

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

    /// <summary>
    /// Gets a snapshot of all charts registered to this context.
    /// </summary>
    public IEnumerable<NavigationChart> AllCharts
    {
        get
        {
            using (EnterUsableState())
                return PathManager.AllCharts;
        }
    }

    /// <inheritdoc cref="PathManager.Register(NavigationChart,bool)"/>
    public bool Register(NavigationChart chart, bool initializeChart = true)
    {
        using (EnterUsableState())
            return PathManager.Register(_context.World, chart, initializeChart);
    }

    /// <inheritdoc cref="PathManager.Register(TraversalBuildResult,bool)"/>
    public bool Register(TraversalBuildResult buildResult, bool initializeChart = true)
    {
        using (EnterUsableState())
            return PathManager.Register(_context.World, buildResult, initializeChart);
    }

    /// <inheritdoc cref="PathManager.IsChartRegistered(string)"/>
    public bool IsChartRegistered(string name)
    {
        using (EnterUsableState())
            return PathManager.IsChartRegistered(name);
    }

    /// <inheritdoc cref="PathManager.TryGetNavigationChart(string,out NavigationChart)"/>
    public bool TryGetNavigationChart(string name, out NavigationChart chart)
    {
        using (EnterUsableState())
            return PathManager.TryGetNavigationChart(name, out chart);
    }

    /// <inheritdoc cref="PathManager.TryGetNavigationChartRegistration(string,out NavigationChartRegistration)"/>
    public bool TryGetNavigationChartRegistration(string name, out NavigationChartRegistration registration)
    {
        using (EnterUsableState())
            return PathManager.TryGetNavigationChartRegistration(name, out registration);
    }

    /// <inheritdoc cref="PathManager.IsChartInitialized(string)"/>
    public bool IsChartInitialized(string name)
    {
        using (EnterUsableState())
            return PathManager.IsChartInitialized(name);
    }

    /// <inheritdoc cref="PathManager.IsChartInitialized(NavigationChart)"/>
    public bool IsChartInitialized(NavigationChart chart)
    {
        using (EnterUsableState())
            return PathManager.IsChartInitialized(chart);
    }

    /// <inheritdoc cref="PathManager.InitializeAllCharts()"/>
    public void InitializeAllCharts()
    {
        using (EnterUsableState())
            PathManager.InitializeAllCharts(_context.World);
    }

    /// <inheritdoc cref="PathManager.InitializeChart(string)"/>
    public void InitializeChart(string chartKey)
    {
        using (EnterUsableState())
            PathManager.InitializeChart(_context.World, chartKey);
    }

    /// <inheritdoc cref="PathManager.UnloadChart(string)"/>
    public void UnloadChart(string chartKey)
    {
        using (EnterUsableState())
            PathManager.UnloadChart(_context.World, chartKey);
    }

    /// <inheritdoc cref="PathManager.UnloadChart(NavigationChart)"/>
    public void UnloadChart(NavigationChart chart)
    {
        using (EnterUsableState())
            PathManager.UnloadChart(_context.World, chart);
    }

    /// <inheritdoc cref="PathManager.TryGetEffectiveCell(GridWorld,Vector3d,out NavigationChartCell)"/>
    public bool TryGetEffectiveCell(Vector3d worldPosition, out NavigationChartCell cell)
    {
        using (EnterUsableState())
            return PathManager.TryGetEffectiveCell(_context.World, worldPosition, out cell);
    }

    /// <inheritdoc cref="PathManager.TryGetEffectiveCell(WorldVoxelIndex,out NavigationChartCell)"/>
    public bool TryGetEffectiveCell(WorldVoxelIndex voxelIndex, out NavigationChartCell cell)
    {
        using (EnterUsableState())
            return PathManager.TryGetEffectiveCell(voxelIndex, out cell);
    }

    /// <inheritdoc cref="PathManager.TryGetEffectiveChartOwner(GridWorld,Vector3d,out string?)"/>
    public bool TryGetEffectiveChartOwner(Vector3d worldPosition, out string? chartName)
    {
        using (EnterUsableState())
            return PathManager.TryGetEffectiveChartOwner(_context.World, worldPosition, out chartName);
    }

    /// <inheritdoc cref="PathManager.TryGetEffectiveChartOwner(WorldVoxelIndex,out string?)"/>
    public bool TryGetEffectiveChartOwner(WorldVoxelIndex voxelIndex, out string? chartName)
    {
        using (EnterUsableState())
            return PathManager.TryGetEffectiveChartOwner(voxelIndex, out chartName);
    }

    /// <inheritdoc cref="PathManager.TryUpdateChartCell(string,int,int,int,NavigationChartCell)"/>
    public bool TryUpdateChartCell(string chartName, int x, int y, int z, NavigationChartCell cell)
    {
        using (EnterUsableState())
            return PathManager.TryUpdateChartCell(_context.World, chartName, x, y, z, cell);
    }

    /// <inheritdoc cref="PathManager.TryUpdateChartCell(string,Vector3d,NavigationChartCell)"/>
    public bool TryUpdateChartCell(string chartName, Vector3d worldPosition, NavigationChartCell cell)
    {
        using (EnterUsableState())
            return PathManager.TryUpdateChartCell(_context.World, chartName, worldPosition, cell);
    }

    /// <inheritdoc cref="PathManager.ApplyChartUpdates(string,IReadOnlyList{NavigationChartCellUpdate})"/>
    public int ApplyChartUpdates(string chartName, IReadOnlyList<NavigationChartCellUpdate> updates)
    {
        using (EnterUsableState())
            return PathManager.ApplyChartUpdates(_context.World, chartName, updates);
    }

    /// <summary>
    /// Flushes pending grid event rebuild work for this context.
    /// </summary>
    public void FlushPendingGridChanges()
    {
        EnsureUsable();
        State.ExternalGridBridge.FlushPendingGridChanges();
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
    /// Gets diagnostics for this context's external-grid bridge.
    /// </summary>
    internal ExternalGridBridgeDiagnosticsSnapshot GetExternalGridBridgeDiagnosticsSnapshot()
    {
        EnsureUsable();
        return State.ExternalGridBridge.GetDiagnosticsSnapshot();
    }

    internal bool TryGetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)
    {
        EnsureUsable();
        GridWorld world = _context.World;
        if (!world.TryGetGrid(start.WorldIndex, out VoxelGrid? startGrid)
            || !world.TryGetGrid(end.WorldIndex, out VoxelGrid? endGrid))
        {
            maxSearchSize = 0;
            return false;
        }

        maxSearchSize = startGrid == endGrid
            ? startGrid!.ConfiguredVoxelCount
            : startGrid!.ConfiguredVoxelCount + endGrid!.ConfiguredVoxelCount;
        return true;
    }

    internal bool NeedsPath(
        Vector3d startPosition,
        Vector3d endPosition,
        Fixed64 unitSize,
        bool includeEnd = false)
    {
        using (EnterUsableState())
            return PathManager.NeedsPath(_context.World, startPosition, endPosition, unitSize, includeEnd);
    }

    internal void HandleGridChanged(GridEventInfo eventInfo)
    {
        using (EnterUsableState())
            PathManagerExternalGridBridge.HandleGridChanged(eventInfo);
    }

    /// <summary>
    /// Clears this context's registered charts, live partitions, transition registry, volume rules, and guide caches.
    /// </summary>
    public void Reset()
    {
        EnsureUsable();
        _navigationAStarAdmissionGate.CancelActive();
        _navigationGraph.Reset();
        PathManager.ResetPathingState(State, resetScopedRegistries: true, flushGuideCache: true);
    }

    internal void ResetNavigationGraph()
    {
        EnsureUsable();
        _navigationAStarAdmissionGate.CancelActive();
        _navigationGraph.Reset();
    }

    internal void Dispose()
    {
        if (_disposed)
            return;

        _context.World.OnChangeCommitted -= HandleCommittedChange;
        _navigationAStarAdmissionGate.Dispose();
        State.ExternalGridBridge.Dispose();
        _navigationGraph.Dispose();
        PathManager.ResetPathingState(State, resetScopedRegistries: true, flushGuideCache: true);
        State.Dispose();
        _disposed = true;
    }

    private void HandleCommittedChange(GridEventInfo eventInfo)
    {
        if (_disposed || eventInfo.WorldSpawnToken != _context.World.SpawnToken)
            return;
        _navigationGraph.EnqueueCommittedChange(eventInfo);
        State.ExternalGridBridge.HandleCommittedChange(eventInfo);
    }

    private IDisposable EnterUsableState()
    {
        EnsureUsable();
        return PathManager.EnterState(State);
    }

    private void EnsureUsable()
    {
        SwiftThrowHelper.ThrowIfDisposed(
            _disposed || _context.IsDisposed,
            nameof(TrailblazerWorldContext));
        SwiftThrowHelper.ThrowIfTrue(
            !_context.World.IsActive,
            nameof(TrailblazerPathingService),
            "TrailblazerPathingService is bound to an inactive GridWorld.");
    }
}
