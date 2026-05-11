using FixedMathSharp;
using GridForge.Grids;
using System;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer;

/// <summary>
/// Provides legacy static simulation parameters and timing management for the default Trailblazer context.
/// </summary>
/// <remarks>
/// This static class remains as a compatibility facade while Trailblazer migrates to explicit
/// <see cref="TrailblazerWorldContext"/> ownership. New multi-world integrations should keep and
/// simulate their own context handles directly.
/// </remarks>
public static class TrailblazerManager
{
    #region Hook Management Fields

    private static readonly TrailblazerClock _clock = new();

    private static readonly TrailblazerLifecycleHooks _lifecycleHooks = new();

    private static readonly object _initializationLock = new();

    private static TrailblazerWorldContext? _defaultContext;

    private static volatile bool _isInitialized;

    #endregion

    #region FrameRate and Time Properties

    /// <summary>
    /// Gets the fixed simulation frame rate.
    /// </summary>
    /// <remarks>
    /// When <see cref="DefaultContext"/> is active this value comes from that context; otherwise it
    /// comes from the compatibility static clock.
    /// </remarks>
    public static int FrameRate => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.FrameRate
        : _clock.FrameRate;

    /// <summary>
    /// Gets the fixed time step for each simulation frame.
    /// </summary>
    public static Fixed64 DeltaTime => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.DeltaTime
        : _clock.DeltaTime;

    /// <summary>
    /// Gets the reciprocal of the current simulation delta time as a fixed-point value.
    /// </summary>
    public static Fixed64 InvDeltaTime => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.InvDeltaTime
        : _clock.InvDeltaTime;

    /// <summary>
    /// Gets the number of frames elapsed since the simulation started.
    /// </summary>
    public static int FrameCount => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.FrameCount
        : _clock.FrameCount;

    /// <summary>
    /// Gets the total simulation time since start, in seconds.
    /// </summary>
    public static Fixed64 TotalTime => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.TotalTime
        : _clock.TotalTime;

    /// <summary>
    /// Gets the total simulation time since the last late-simulate frame, in seconds.
    /// </summary>
    public static Fixed64 AccumulatedTime => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.AccumulatedTime
        : _clock.AccumulatedTime;

    /// <summary>
    /// Gets a value indicating whether accumulation should be reset to its initial state.
    /// </summary>
    public static bool ResetAccumulation => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.ResetAccumulation
        : _clock.ResetAccumulation;

    /// <summary>
    /// Gets the expected accumulation value used for calculations or comparisons.
    /// </summary>
    public static Fixed64 ExpectedAccumulation => TryGetActiveDefaultContext(out TrailblazerWorldContext context)
        ? context.ExpectedAccumulation
        : _clock.ExpectedAccumulation;

    /// <summary>
    /// Gets the default world context used by the legacy static facade.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the static facade has not been initialized with an active <see cref="GridWorld"/>.
    /// </exception>
    public static TrailblazerWorldContext DefaultContext
    {
        get
        {
            if (TryGetActiveDefaultContext(out TrailblazerWorldContext context))
                return context;

            throw new InvalidOperationException(
                "TrailblazerManager does not have a default context. Call Initialize(GridWorld) first.");
        }
    }

    /// <summary>
    /// Gets whether the legacy static facade has an active default context.
    /// </summary>
    public static bool HasDefaultContext => TryGetActiveDefaultContext(out _);

    #endregion

    #region Lifecycle

    /// <summary>
    /// Initializes Trailblazer's internal subsystem lifecycle hooks.
    /// </summary>
    /// <remarks>
    /// Hosts should call this once during application startup before entering the fixed-step loop.
    /// The method is idempotent and Trailblazer also invokes it lazily as a safety net when core manager APIs are used first.
    /// </remarks>
    public static void Initialize()
    {
        if (_isInitialized)
            return;

        lock (_initializationLock)
        {
            if (_isInitialized)
                return;

            NavigatorGlobalIdAllocator.RegisterTrailblazerLifecycleHooks();
            PathManager.RegisterTrailblazerLifecycleHooks();
            MovementGroupCoordinator.RegisterTrailblazerLifecycleHooks();
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Initializes Trailblazer against the supplied grid world as the default context.
    /// </summary>
    /// <param name="world">The GridForge world for the legacy default context.</param>
    public static void Initialize(GridWorld world)
    {
        AttachDefaultContext(world);
        TrailblazerWorldManager.AttachWorld(world);
        Initialize();
    }

    internal static void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        Initialize();
    }

    /// <summary>
    /// Advances the simulation by incrementing the frame count and running ordered simulate hooks.
    /// </summary>
    public static void Simulate()
    {
        EnsureInitialized();
        if (TryGetActiveDefaultContext(out TrailblazerWorldContext context))
            context.Simulate();
        else
            _clock.Simulate();

        _lifecycleHooks.InvokeSimulate();
    }

    /// <summary>
    /// Performs late simulation processing and invokes all registered late simulation hooks.
    /// </summary>
    public static void LateSimulate()
    {
        EnsureInitialized();
        if (TryGetActiveDefaultContext(out TrailblazerWorldContext context))
            context.LateSimulate();
        else
            _clock.LateSimulate();

        _lifecycleHooks.InvokeLateSimulate();
    }

    /// <summary>
    /// Performs a visualization update by accumulating time and invoking registered visualization hooks.
    /// </summary>
    public static void Visualize()
    {
        EnsureInitialized();
        if (TryGetActiveDefaultContext(out TrailblazerWorldContext context))
            context.Visualize();
        else
            _clock.Visualize();

        _lifecycleHooks.InvokeVisualize();
    }

    /// <summary>
    /// Resets the simulation clock state and runs ordered reset hooks.
    /// </summary>
    public static void Reset()
    {
        EnsureInitialized();
        _clock.Reset();

        if (TryGetActiveDefaultContext(out TrailblazerWorldContext context))
            context.Reset();

        _lifecycleHooks.InvokeReset();
    }

    #endregion

    #region FrameRate Utilities

    /// <summary>
    /// Updates the simulation frame rate, recalculates the delta time, and notifies ordered frame-rate hooks.
    /// </summary>
    /// <param name="frameRate">The new frame rate value.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="frameRate"/> is less than or equal to zero.
    /// </exception>
    public static void SetFrameRate(int frameRate)
    {
        if (frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameRate),
                frameRate,
                "Frame rate must be greater than zero.");
        }

        EnsureInitialized();
        _clock.SetFrameRate(frameRate);

        if (TryGetActiveDefaultContext(out TrailblazerWorldContext context))
            context.SetFrameRate(frameRate);

        _lifecycleHooks.InvokeFrameRateChanged();
    }

    /// <summary>
    /// Calculates the frame index corresponding to the specified timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp value, in fixed-point format, for which to determine the frame index.</param>
    /// <returns>The zero-based index of the frame that contains the specified timestamp.</returns>
    public static int GetFrameFromTime(Fixed64 timestamp)
    {
        return TryGetActiveDefaultContext(out TrailblazerWorldContext context)
            ? context.GetFrameFromTime(timestamp)
            : _clock.GetFrameFromTime(timestamp);
    }

    #endregion

    #region Hook Registration

    internal static IDisposable RegisterOnSimulate(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnSimulateCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnSimulateCore(string owner, int order, Action callback) =>
        _lifecycleHooks.RegisterOnSimulate(owner, order, callback);

    internal static IDisposable RegisterOnLateSimulate(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnLateSimulateCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnLateSimulateCore(string owner, int order, Action callback) =>
        _lifecycleHooks.RegisterOnLateSimulate(owner, order, callback);

    internal static IDisposable RegisterOnVisualize(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnVisualizeCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnVisualizeCore(string owner, int order, Action callback) =>
        _lifecycleHooks.RegisterOnVisualize(owner, order, callback);

    internal static IDisposable RegisterOnReset(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnResetCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnResetCore(string owner, int order, Action callback) =>
        _lifecycleHooks.RegisterOnReset(owner, order, callback);

    internal static IDisposable RegisterOnFrameRateChanged(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnFrameRateChangedCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnFrameRateChangedCore(string owner, int order, Action callback) =>
        _lifecycleHooks.RegisterOnFrameRateChanged(owner, order, callback);

    #endregion

    private static void AttachDefaultContext(GridWorld world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        lock (_initializationLock)
        {
            if (TryGetActiveDefaultContext(out TrailblazerWorldContext current)
                && ReferenceEquals(current.World, world))
            {
                return;
            }

            _defaultContext?.Dispose();
            TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);
            context.SetFrameRate(_clock.FrameRate);
            _defaultContext = context;
        }
    }

    private static bool TryGetActiveDefaultContext(out TrailblazerWorldContext context)
    {
        TrailblazerWorldContext? current = _defaultContext;
        if (current != null && !current.IsDisposed && current.World.IsActive)
        {
            context = current;
            return true;
        }

        context = null!;
        return false;
    }
}
