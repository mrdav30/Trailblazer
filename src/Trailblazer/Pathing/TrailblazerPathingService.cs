//=======================================================================
// TrailblazerPathingService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Collections.Generic;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned pathing API for chart registration, live chart state, and local pathing queries.
/// </summary>
public sealed class TrailblazerPathingService
{
    private readonly TrailblazerWorldContext _context;

    private bool _disposed;

    internal TrailblazerPathingService(TrailblazerWorldContext context)
    {
        _context = context;
        State = new PathingWorldState(context);
    }

    internal PathingWorldState State { get; }

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
        PathManager.ResetPathingState(State, resetScopedRegistries: true, flushGuideCache: true);
    }

    internal void Dispose()
    {
        if (_disposed)
            return;

        State.ExternalGridBridge.Dispose();
        PathManager.ResetPathingState(State, resetScopedRegistries: true, flushGuideCache: true);
        State.Dispose();
        _disposed = true;
    }

    private IDisposable EnterUsableState()
    {
        EnsureUsable();
        return PathManager.EnterState(State);
    }

    private void EnsureUsable()
    {
        if (_disposed || _context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerPathingService is bound to an inactive GridWorld.");
    }
}
