using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>
/// Manages one explicit Trailblazer world context for benchmark classes.
/// Call <see cref="Setup"/> once in GlobalSetup and <see cref="Teardown"/> once in GlobalCleanup.
/// </summary>
internal sealed class BenchmarkPathFixture
{
    private GridWorld _world;
    private TrailblazerWorldContext _context;
    private IDisposable _pathingScope;

    /// <summary>
    /// The active GridWorld for this fixture session.
    /// </summary>
    public GridWorld World => _world;

    /// <summary>
    /// The active Trailblazer context for this fixture session.
    /// </summary>
    public TrailblazerWorldContext Context => _context;

    /// <summary>
    /// Prepares logging suppression, creates the GridWorld, and creates a Trailblazer context.
    /// Optionally adds a single grid using the provided configuration.
    /// </summary>
    /// <param name="config">Optional grid configuration to add to the world.</param>
    /// <param name="voxelSize">Optional voxel size override for the world.</param>
    public void Setup(GridConfiguration? config = null, Fixed64? voxelSize = null)
    {
        _world = BenchmarkEnvironment.PrepareWorld(voxelSize: voxelSize);
        _context = TrailblazerWorldContext.Attach(_world);
        _pathingScope = PathManager.EnterState(_context.Pathing.State);

        if (config.HasValue)
            _world.TryAddGrid(config.Value, out _);
    }

    /// <summary>
    /// Disposes the context and world. Safe to call even when Setup was not called.
    /// </summary>
    public void Teardown()
    {
        _pathingScope?.Dispose();
        _context?.Dispose();
        BenchmarkEnvironment.ResetWorld();
        _pathingScope = null;
        _context = null;
        _world = null;
    }

    /// <summary>
    /// Flushes all guide caches without tearing down the world or chart state.
    /// Useful between IterationSetup calls to ensure a cold-cache benchmark body.
    /// </summary>
    public void FlushGuideCache()
    {
        _context.Guides.FlushCache(force: true);
    }
}
