using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;
using Trailblazer.Support;

namespace Trailblazer.Benchmarks;

/// <summary>
/// Manages the Trailblazer world and PathManager lifecycle for benchmark classes.
/// Call <see cref="Setup"/> once in GlobalSetup and <see cref="Teardown"/> once in GlobalCleanup.
/// </summary>
internal sealed class BenchmarkPathFixture
{
    private GridWorld _world;

    /// <summary>
    /// The active GridWorld for this fixture session.
    /// </summary>
    public GridWorld World => _world;

    /// <summary>
    /// Prepares logging suppression, creates the GridWorld, and attaches Trailblazer.
    /// Optionally adds a single grid using the provided configuration.
    /// </summary>
    /// <param name="config">Optional grid configuration to add to the world.</param>
    /// <param name="voxelSize">Optional voxel size override for the world.</param>
    public void Setup(GridConfiguration? config = null, Fixed64? voxelSize = null)
    {
        _world = BenchmarkEnvironment.PrepareWorld(voxelSize: voxelSize);
        TrailblazerManager.Initialize(_world);

        if (config.HasValue)
            _world.TryAddGrid(config.Value, out _);
    }

    /// <summary>
    /// Resets PathManager, TrailblazerWorldManager, and TrailblazerManager, then disposes
    /// the world. Safe to call even when Setup was not called.
    /// </summary>
    public void Teardown()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        BenchmarkEnvironment.ResetWorld();
        _world = null;
    }

    /// <summary>
    /// Flushes all guide caches without tearing down the world or chart state.
    /// Useful between IterationSetup calls to ensure a cold-cache benchmark body.
    /// </summary>
    public static void FlushGuideCache()
    {
        PathGuideFactory.FlushCache(force: true);
    }
}
