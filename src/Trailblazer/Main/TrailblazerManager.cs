using FixedMathSharp;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;
using Trailblazer.Support;

namespace Trailblazer;

/// <summary>
/// Provides global simulation parameters and timing management for the Trailblazer system.
/// </summary>
/// <remarks>
/// This static class handles fixed-time updates, frame progression, and ordered internal lifecycle hooks.
/// Subsystems should register maintenance work through those hooks instead of being hard-wired into the manager.
/// </remarks>
public static class TrailblazerManager
{
    internal static readonly LifecycleHookHandler HookHandler = new();

    private static readonly object _initializationLock = new();

    private static readonly SwiftList<OrderedLifecycleHook> _simulateHooks = new();

    private static readonly SwiftList<OrderedLifecycleHook> _lateSimulateHooks = new();

    private static readonly SwiftList<OrderedLifecycleHook> _visualizeHooks = new();

    private static readonly SwiftList<OrderedLifecycleHook> _resetHooks = new();

    private static readonly SwiftList<OrderedLifecycleHook> _frameRateChangedHooks = new();

    private static volatile bool _isInitialized;

    /// <summary>
    /// The fixed simulation frame rate.
    /// </summary>
    /// <remarks>
    /// This determines how frequently physics and movement updates occur.
    /// </remarks>
    public static int FrameRate { get; private set; } = 32;

    /// <summary>
    /// The fixed time step for each simulation frame.
    /// </summary>
    /// <remarks>
    /// This value is derived from <see cref="FrameRate"/> to ensure a consistent time step across updates.
    /// </remarks>
    public static Fixed64 DeltaTime { get; private set; } = Fixed64.One / (Fixed64)FrameRate;

    public static Fixed64 InvDeltaTime => Fixed64.One / DeltaTime;

    /// <summary>
    /// The number of frames elapsed since the simulation started.
    /// </summary>
    public static int FrameCount { get; private set; }

    /// <summary>
    /// The total simulation time since start (in seconds).
    /// </summary>
    public static Fixed64 TotalTime { get; private set; }

    /// <summary>
    /// The total simulation time since the last late simulate frame (in seconds).
    /// </summary>
    public static Fixed64 AccumulatedTime { get; private set; }

    public static bool ResetAccumulation { get; private set; }

    public static Fixed64 ExpectedAccumulation { get; private set; }

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
    /// Updates the simulation frame rate, recalculates the delta time, and notifies ordered frame-rate hooks.
    /// </summary>
    /// <param name="frameRate">The new frame rate value.</param>
    public static void SetFrameRate(int frameRate)
    {
        EnsureInitialized();
        FrameRate = frameRate;
        DeltaTime = Fixed64.One / (Fixed64)FrameRate;
        HookHandler.InvokeHooks(_frameRateChangedHooks);
    }

    /// <summary>
    /// Advances the simulation by incrementing the frame count and running ordered simulate hooks.
    /// </summary>
    public static void Simulate()
    {
        EnsureInitialized();
        FrameCount++;
        TotalTime += DeltaTime;
        HookHandler.InvokeHooks(_simulateHooks);
    }

    public static void LateSimulate()
    {
        EnsureInitialized();
        ResetAccumulation = true;
        HookHandler.InvokeHooks(_lateSimulateHooks);
    }

    public static void Visualize()
    {
        EnsureInitialized();
        if (ResetAccumulation)
        {
            AccumulatedTime = Fixed64.Zero;
            ResetAccumulation = false;
        }

        AccumulatedTime += DeltaTime;
        ExpectedAccumulation = AccumulatedTime / DeltaTime;
        HookHandler.InvokeHooks(_visualizeHooks);
    }

    /// <summary>
    /// Resets the simulation clock state and runs ordered reset hooks.
    /// </summary>
    public static void Reset()
    {
        EnsureInitialized();
        FrameCount = 0;
        TotalTime = Fixed64.Zero;
        AccumulatedTime = Fixed64.Zero;
        ExpectedAccumulation = Fixed64.Zero;
        ResetAccumulation = false;
        HookHandler.InvokeHooks(_resetHooks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetFrameFromTime(Fixed64 timestamp)
    {
        return (timestamp * InvDeltaTime).FloorToInt();
    }

    internal static void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        Initialize();
    }

    internal static IDisposable RegisterOnSimulate(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnSimulateCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnSimulateCore(string owner, int order, Action callback) =>
        HookHandler.RegisterHook(_simulateHooks, owner, order, callback);

    internal static IDisposable RegisterOnLateSimulate(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnLateSimulateCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnLateSimulateCore(string owner, int order, Action callback) =>
        HookHandler.RegisterHook(_lateSimulateHooks, owner, order, callback);

    internal static IDisposable RegisterOnVisualize(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnVisualizeCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnVisualizeCore(string owner, int order, Action callback) =>
        HookHandler.RegisterHook(_visualizeHooks, owner, order, callback);

    internal static IDisposable RegisterOnReset(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnResetCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnResetCore(string owner, int order, Action callback) =>
        HookHandler.RegisterHook(_resetHooks, owner, order, callback);

    internal static IDisposable RegisterOnFrameRateChanged(string owner, int order, Action callback)
    {
        EnsureInitialized();
        return RegisterOnFrameRateChangedCore(owner, order, callback);
    }

    internal static IDisposable RegisterOnFrameRateChangedCore(string owner, int order, Action callback) =>
        HookHandler.RegisterHook(_frameRateChangedHooks, owner, order, callback);
}
