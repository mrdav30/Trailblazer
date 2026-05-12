using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer;

/// <summary>
/// Owns Trailblazer runtime state for one explicit <see cref="GridWorld"/>.
/// </summary>
/// <remarks>
/// This is the context-first host API for multi-world Trailblazer usage. It owns world lifetime,
/// deterministic clock state, pathing state, transition state, volume rules, reachability snapshots,
/// and guide caches. Later phases move navigation coordination state behind this context.
/// </remarks>
public sealed class TrailblazerWorldContext : IDisposable
{
    private readonly TrailblazerClock _clock = new();

    private readonly TrailblazerLifecycleHooks _hooks = new();

    private readonly bool _ownsWorld;

    private bool _disposed;

    private TrailblazerWorldContext(GridWorld world, bool ownsWorld)
    {
        World = world;
        _ownsWorld = ownsWorld;
        Pathing = new TrailblazerPathingService(this);
        Transitions = new TrailblazerTransitionService(this, Pathing.State);
        VolumeRules = new TrailblazerVolumeRulesService(this, Pathing.State);
        Guides = new TrailblazerGuideService(this, Pathing.State);
        Navigation = new TrailblazerNavigationService(this);
    }

    /// <summary>
    /// Gets the explicit GridForge world owned or referenced by this context.
    /// </summary>
    public GridWorld World { get; }

    /// <summary>
    /// Gets this context's world-local pathing service.
    /// </summary>
    public TrailblazerPathingService Pathing { get; }

    /// <summary>
    /// Gets this context's world-local traversal transition service.
    /// </summary>
    public TrailblazerTransitionService Transitions { get; }

    /// <summary>
    /// Gets this context's world-local raw-volume medium rule service.
    /// </summary>
    public TrailblazerVolumeRulesService VolumeRules { get; }

    /// <summary>
    /// Gets this context's world-local path guide service.
    /// </summary>
    public TrailblazerGuideService Guides { get; }

    /// <summary>
    /// Gets this context's world-local navigation coordination service.
    /// </summary>
    public TrailblazerNavigationService Navigation { get; }

    /// <summary>
    /// Gets whether this context has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets the voxel size of this context's world.
    /// </summary>
    public Fixed64 VoxelSize
    {
        get
        {
            ThrowIfDisposed();
            return World.VoxelSize;
        }
    }

    /// <summary>
    /// Gets this context's fixed simulation frame rate.
    /// </summary>
    public int FrameRate
    {
        get
        {
            ThrowIfDisposed();
            return _clock.FrameRate;
        }
    }

    /// <summary>
    /// Gets this context's fixed simulation time step.
    /// </summary>
    public Fixed64 DeltaTime
    {
        get
        {
            ThrowIfDisposed();
            return _clock.DeltaTime;
        }
    }

    /// <summary>
    /// Gets the reciprocal of this context's fixed simulation time step.
    /// </summary>
    public Fixed64 InvDeltaTime
    {
        get
        {
            ThrowIfDisposed();
            return _clock.InvDeltaTime;
        }
    }

    /// <summary>
    /// Gets this context's simulated frame count.
    /// </summary>
    public int FrameCount
    {
        get
        {
            ThrowIfDisposed();
            return _clock.FrameCount;
        }
    }

    /// <summary>
    /// Gets this context's total simulated time.
    /// </summary>
    public Fixed64 TotalTime
    {
        get
        {
            ThrowIfDisposed();
            return _clock.TotalTime;
        }
    }

    /// <summary>
    /// Gets this context's accumulated visualization time.
    /// </summary>
    public Fixed64 AccumulatedTime
    {
        get
        {
            ThrowIfDisposed();
            return _clock.AccumulatedTime;
        }
    }

    /// <summary>
    /// Gets whether this context's visualization accumulation will reset on the next visualize call.
    /// </summary>
    public bool ResetAccumulation
    {
        get
        {
            ThrowIfDisposed();
            return _clock.ResetAccumulation;
        }
    }

    /// <summary>
    /// Gets this context's visualization accumulation expressed in simulation frames.
    /// </summary>
    public Fixed64 ExpectedAccumulation
    {
        get
        {
            ThrowIfDisposed();
            return _clock.ExpectedAccumulation;
        }
    }

    /// <summary>
    /// Attaches a context to a host-owned <see cref="GridWorld"/>.
    /// </summary>
    /// <param name="world">The active world to bind.</param>
    /// <param name="takeOwnership">True when disposing this context should dispose the supplied world.</param>
    /// <returns>A context bound to <paramref name="world"/>.</returns>
    public static TrailblazerWorldContext Attach(GridWorld world, bool takeOwnership = false)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (!world.IsActive)
            throw new InvalidOperationException("TrailblazerWorldContext requires an active GridWorld.");

        return new TrailblazerWorldContext(world, takeOwnership);
    }

    /// <summary>
    /// Creates a context with an owned <see cref="GridWorld"/>.
    /// </summary>
    /// <param name="voxelSize">Optional voxel size for the created world.</param>
    /// <param name="spatialGridCellSize">Spatial hash cell size for the created world.</param>
    /// <returns>A context that owns its created world.</returns>
    public static TrailblazerWorldContext CreateOwned(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        return new TrailblazerWorldContext(
            new GridWorld(voxelSize, spatialGridCellSize),
            ownsWorld: true);
    }

    /// <summary>
    /// Advances this context's deterministic simulation clock and ordered simulate hooks.
    /// </summary>
    public void Simulate()
    {
        ThrowIfDisposed();
        _clock.Simulate();
        Guides.CullExpiredGuides(_clock.FrameCount);
        _hooks.InvokeSimulate();
    }

    /// <summary>
    /// Runs this context's late-simulation step.
    /// </summary>
    public void LateSimulate()
    {
        ThrowIfDisposed();
        _clock.LateSimulate();
        _hooks.InvokeLateSimulate();
    }

    /// <summary>
    /// Runs this context's visualization accumulation step.
    /// </summary>
    public void Visualize()
    {
        ThrowIfDisposed();
        _clock.Visualize();
        _hooks.InvokeVisualize();
    }

    /// <summary>
    /// Resets this context's deterministic clock and context-local lifecycle hooks.
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();
        _clock.Reset();
        Navigation.Reset();
        _hooks.InvokeReset();
    }

    /// <summary>
    /// Updates this context's fixed simulation frame rate.
    /// </summary>
    /// <param name="frameRate">The new frame rate. Must be greater than zero.</param>
    public void SetFrameRate(int frameRate)
    {
        ThrowIfDisposed();
        _clock.SetFrameRate(frameRate);
        _hooks.InvokeFrameRateChanged();
    }

    /// <summary>
    /// Calculates the frame index containing the specified fixed-point timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp to resolve.</param>
    /// <returns>The zero-based frame index for the timestamp.</returns>
    public int GetFrameFromTime(Fixed64 timestamp)
    {
        ThrowIfDisposed();
        return _clock.GetFrameFromTime(timestamp);
    }

    internal IDisposable RegisterOnSimulate(string owner, int order, Action callback)
    {
        ThrowIfDisposed();
        return _hooks.RegisterOnSimulate(owner, order, callback);
    }

    internal IDisposable RegisterOnLateSimulate(string owner, int order, Action callback)
    {
        ThrowIfDisposed();
        return _hooks.RegisterOnLateSimulate(owner, order, callback);
    }

    internal IDisposable RegisterOnVisualize(string owner, int order, Action callback)
    {
        ThrowIfDisposed();
        return _hooks.RegisterOnVisualize(owner, order, callback);
    }

    internal IDisposable RegisterOnReset(string owner, int order, Action callback)
    {
        ThrowIfDisposed();
        return _hooks.RegisterOnReset(owner, order, callback);
    }

    internal IDisposable RegisterOnFrameRateChanged(string owner, int order, Action callback)
    {
        ThrowIfDisposed();
        return _hooks.RegisterOnFrameRateChanged(owner, order, callback);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        Pathing.Dispose();
        _disposed = true;

        if (_ownsWorld && World.IsActive)
            World.Dispose();
    }

    private void ThrowIfDisposed()
    {
        SwiftThrowHelper.ThrowIfDisposed(_disposed, nameof(TrailblazerWorldContext));
        if (!World.IsActive)
            throw new InvalidOperationException("TrailblazerWorldContext is bound to an inactive GridWorld.");
    }
}
