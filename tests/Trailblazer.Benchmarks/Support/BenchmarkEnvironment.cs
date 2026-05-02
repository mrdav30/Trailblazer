using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using SwiftCollections.Pool;
using System;
using System.Reflection;

namespace Trailblazer.Benchmarks;

internal static class BenchmarkEnvironment
{
    private static readonly Action _clearTrailblazerPools = CreateTrailblazerPoolClearer();
    private static bool _loggingSuppressed;
    private static GridWorld _currentWorld;

    public static GridWorld PrepareWorld(
        bool clearAllPools = false,
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        SuppressLogging();
        ResetWorld();

        if (clearAllPools)
            ClearAllPools();

        _currentWorld = new GridWorld(voxelSize, spatialGridCellSize);
        return _currentWorld;
    }

    public static void ResetWorld()
    {
        if (_currentWorld == null)
            return;

        _currentWorld.Dispose();
        _currentWorld = null;
    }

    public static void ClearAllPools()
    {
        _clearTrailblazerPools();

        SwiftHashSetPool<int>.Shared.Clear();
        SwiftHashSetPool<ushort>.Shared.Clear();
        SwiftHashSetPool<ScanCell>.Shared.Clear();
        SwiftHashSetPool<Voxel>.Shared.Clear();

        SwiftListPool<IVoxelOccupant>.Shared.Clear();
        SwiftListPool<ScanCell>.Shared.Clear();
        SwiftListPool<Voxel>.Shared.Clear();
    }

    private static void SuppressLogging()
    {
        if (_loggingSuppressed)
            return;

        GridForgeLogger.MinimumLevel = DiagnosticLevel.None;
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.None;
        TrailblazerLogger.EnableDebugLogging = false;
        _loggingSuppressed = true;
    }

    private static Action CreateTrailblazerPoolClearer()
    {
        Type poolsType = typeof(GridWorld).Assembly.GetType("GridForge.Grids.Pools") ?? throw new InvalidOperationException("Unable to locate GridForge pool manager.");
        MethodInfo clearMethod = poolsType.GetMethod(
            "ClearPools",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        return clearMethod == null
            ? throw new InvalidOperationException("Unable to locate GridForge pool reset method.")
            : (() => clearMethod.Invoke(null, null));
    }
}
