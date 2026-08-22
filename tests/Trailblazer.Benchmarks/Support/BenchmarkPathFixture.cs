using GridForge.Configuration;
using GridForge.Grids;

namespace Trailblazer.Benchmarks;

/// <summary>
/// Manages one explicit Trailblazer world context for benchmark classes.
/// Call <see cref="Setup"/> once in GlobalSetup and <see cref="Teardown"/> once in GlobalCleanup.
/// </summary>
internal sealed class BenchmarkPathFixture
{
    private GridWorld _world;
    private TrailblazerWorldContext _context;

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
    /// <param name="settings">Optional context capacity settings.</param>
    public void Setup(
        GridConfiguration? config = null,
        TrailblazerWorldContextSettings settings = null)
    {
        _world = BenchmarkEnvironment.PrepareWorld();
        _context = TrailblazerWorldContext.Attach(_world, settings: settings);

        if (config.HasValue)
            _world.TryAddGrid(config.Value, out _);
    }

    /// <summary>
    /// Disposes the context and world. Safe to call even when Setup was not called.
    /// </summary>
    public void Teardown()
    {
        _context?.Dispose();
        BenchmarkEnvironment.ResetWorld();
        _context = null;
        _world = null;
    }

}
