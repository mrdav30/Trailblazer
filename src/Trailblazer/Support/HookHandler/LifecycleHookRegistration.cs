using System;

namespace Trailblazer.Support;

/// <summary>
/// Represents a registration for a lifecycle hook, allowing it to be unregistered when disposed.
/// </summary>
public sealed class LifecycleHookRegistration : IDisposable
{
    private Action _dispose;

    /// <summary>
    /// Initializes a new instance of the LifecycleHookRegistration class with the specified dispose action.
    /// </summary>
    /// <param name="dispose">The action to execute when the registration is disposed.</param>
    public LifecycleHookRegistration(Action dispose) => _dispose = dispose;

    public void Dispose()
    {
        Action dispose = _dispose;
        if (dispose == null)
            return;

        _dispose = null;
        dispose();
    }
}