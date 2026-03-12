using System;

namespace Trailblazer.Support;

public sealed class LifecycleHookRegistration : IDisposable
{
    private Action _dispose;

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