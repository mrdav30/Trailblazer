//=======================================================================
// TrailblazerWorldContext.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Heightmaps;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer;

/// <summary>
/// Owns Trailblazer runtime state for one explicit <see cref="GridWorld"/>.
/// </summary>
/// <remarks>
/// This is the context-first host API for multi-world Trailblazer usage. It owns world lifetime,
/// deterministic clock state, pathing state, reachability snapshots, and guide caches.
/// </remarks>
public sealed class TrailblazerWorldContext : IDisposable
{
    private static readonly object _worldOwnershipLock = new();

    private static readonly SwiftHashSet<GridWorld> _ownedWorlds = new();

    private readonly TrailblazerClock _clock = new();

    private readonly TrailblazerLifecycleHooks _hooks = new();

    private readonly bool _ownsWorld;

    private bool _disposed;

    private TrailblazerWorldContext(
        GridWorld world,
        bool ownsWorld,
        TrailblazerWorldContextSettings settings)
    {
        World = world;
        Settings = settings;
        _ownsWorld = ownsWorld;
        Pathing = new TrailblazerPathingService(this);
        Guides = new TrailblazerGuideService(this);
        Navigation = new TrailblazerNavigationService(this);
        Heightmaps = new TrailblazerHeightmapService(this);
    }

    /// <summary>
    /// Gets the explicit GridForge world owned or referenced by this context.
    /// </summary>
    public GridWorld World { get; }

    /// <summary>Gets the immutable finite runtime ceilings for this context.</summary>
    public TrailblazerWorldContextSettings Settings { get; }

    /// <summary>
    /// Gets this context's world-local pathing service.
    /// </summary>
    public TrailblazerPathingService Pathing { get; }

    /// <summary>
    /// Gets this context's world-local path guide service.
    /// </summary>
    public TrailblazerGuideService Guides { get; }

    internal TrailblazerNavigationService Navigation { get; }

    /// <summary>
    /// Gets this context's world-local heightmap registry and sampling service.
    /// </summary>
    public TrailblazerHeightmapService Heightmaps { get; }

    /// <summary>
    /// Gets whether this context has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

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
    /// <param name="settings">Optional immutable context ceilings; defaults to the recommended finite settings.</param>
    /// <returns>A context bound to <paramref name="world"/>.</returns>
    public static TrailblazerWorldContext Attach(
        GridWorld world,
        bool takeOwnership = false,
        TrailblazerWorldContextSettings? settings = null)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfTrue(
            !world.IsActive,
            nameof(TrailblazerWorldContext),
            "TrailblazerWorldContext requires an active GridWorld.");

        return CreateRegistered(world, takeOwnership, settings ?? TrailblazerWorldContextSettings.Default);
    }

    /// <summary>
    /// Creates a context with an owned <see cref="GridWorld"/>.
    /// </summary>
    /// <param name="spatialGridCellSize">Spatial hash cell size for the created world.</param>
    /// <param name="settings">Optional immutable context ceilings; defaults to the recommended finite settings.</param>
    /// <returns>A context that owns its created world.</returns>
    public static TrailblazerWorldContext CreateOwned(
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize,
        TrailblazerWorldContextSettings? settings = null)
    {
        return CreateRegistered(
            new GridWorld(spatialGridCellSize),
            ownsWorld: true,
            settings ?? TrailblazerWorldContextSettings.Default);
    }

    /// <summary>
    /// Advances this context's deterministic simulation clock and ordered simulate hooks.
    /// </summary>
    public void Simulate()
    {
        ThrowIfDisposed();
        _clock.Simulate();
        Pathing.MaintainNavigationGraph(_clock.FrameCount);
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
        Heightmaps.Reset();
        Pathing.Reset();
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
        lock (_worldOwnershipLock)
        {
            if (_disposed)
                return;

            Pathing.Dispose();
            Heightmaps.Dispose();
            _disposed = true;
            ReleaseWorldOwnership(this);

            if (_ownsWorld && World.IsActive)
                World.Dispose();
        }
    }

    private static TrailblazerWorldContext CreateRegistered(
        GridWorld world,
        bool ownsWorld,
        TrailblazerWorldContextSettings settings)
    {
        lock (_worldOwnershipLock)
        {
            ThrowIfWorldOwned(world);
            TrailblazerWorldContext context = new(world, ownsWorld, settings);
            _ownedWorlds.Add(world);
            return context;
        }
    }

    private static void ThrowIfWorldOwned(GridWorld world)
    {
        if (!_ownedWorlds.Contains(world))
            return;
        throw new InvalidOperationException("GridWorld is already attached to an active TrailblazerWorldContext.");
    }

    private static void ReleaseWorldOwnership(TrailblazerWorldContext context) =>
        _ownedWorlds.Remove(context.World);

    internal static void ThrowIfUnusable(TrailblazerWorldContext? context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context), "An explicit TrailblazerWorldContext is required.");
        if (context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!context.World.IsActive)
            throw new InvalidOperationException("An active TrailblazerWorldContext is required.");
    }

    private void ThrowIfDisposed()
    {
        SwiftThrowHelper.ThrowIfDisposed(_disposed, nameof(TrailblazerWorldContext));
        if (!World.IsActive)
            throw new InvalidOperationException("TrailblazerWorldContext is bound to an inactive GridWorld.");
    }
}
