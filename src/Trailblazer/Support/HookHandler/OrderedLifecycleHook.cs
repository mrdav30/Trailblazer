using System;

namespace Trailblazer.Support;

public sealed class OrderedLifecycleHook
{
    public OrderedLifecycleHook(string owner, int order, Action callback)
    {
        Owner = owner;
        Order = order;
        Callback = callback;
    }

    public string Owner { get; }

    public int Order { get; }

    public Action Callback { get; }
}
