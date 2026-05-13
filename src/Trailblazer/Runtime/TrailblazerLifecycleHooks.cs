using SwiftCollections;
using System;
using Trailblazer.Support;

namespace Trailblazer;

/// <summary>
/// Owns ordered lifecycle hook storage for one Trailblazer runtime owner.
/// </summary>
internal sealed class TrailblazerLifecycleHooks
{
    private readonly LifecycleHookHandler _hookHandler = new();

    private readonly SwiftList<OrderedLifecycleHook> _simulateHooks = new();

    private readonly SwiftList<OrderedLifecycleHook> _lateSimulateHooks = new();

    private readonly SwiftList<OrderedLifecycleHook> _visualizeHooks = new();

    private readonly SwiftList<OrderedLifecycleHook> _resetHooks = new();

    private readonly SwiftList<OrderedLifecycleHook> _frameRateChangedHooks = new();

    internal int SimulateHookCount => _simulateHooks.Count;

    internal int ResetHookCount => _resetHooks.Count;

    internal IDisposable RegisterOnSimulate(string owner, int order, Action callback) =>
        _hookHandler.RegisterHook(_simulateHooks, owner, order, callback);

    internal IDisposable RegisterOnLateSimulate(string owner, int order, Action callback) =>
        _hookHandler.RegisterHook(_lateSimulateHooks, owner, order, callback);

    internal IDisposable RegisterOnVisualize(string owner, int order, Action callback) =>
        _hookHandler.RegisterHook(_visualizeHooks, owner, order, callback);

    internal IDisposable RegisterOnReset(string owner, int order, Action callback) =>
        _hookHandler.RegisterHook(_resetHooks, owner, order, callback);

    internal IDisposable RegisterOnFrameRateChanged(string owner, int order, Action callback) =>
        _hookHandler.RegisterHook(_frameRateChangedHooks, owner, order, callback);

    internal void InvokeSimulate()
    {
        if (_simulateHooks.Count != 0)
            _hookHandler.InvokeHooks(_simulateHooks);
    }

    internal void InvokeLateSimulate()
    {
        if (_lateSimulateHooks.Count != 0)
            _hookHandler.InvokeHooks(_lateSimulateHooks);
    }

    internal void InvokeVisualize()
    {
        if (_visualizeHooks.Count != 0)
            _hookHandler.InvokeHooks(_visualizeHooks);
    }

    internal void InvokeReset()
    {
        if (_resetHooks.Count != 0)
            _hookHandler.InvokeHooks(_resetHooks);
    }

    internal void InvokeFrameRateChanged()
    {
        if (_frameRateChangedHooks.Count != 0)
            _hookHandler.InvokeHooks(_frameRateChangedHooks);
    }
}
