using SwiftCollections;
using System;
using System.Collections.Generic;

// TODO: these should be in a seperate utility project

namespace Trailblazer.Support;

public class LifecycleHookHandler
{
    private readonly object _lifecycleHookLock = new();

    public IDisposable RegisterHook(
        SwiftList<OrderedLifecycleHook> hooks,
        string owner,
        int order,
        Action callback)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Lifecycle hook owner cannot be null or whitespace.", nameof(owner));

        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        lock (_lifecycleHookLock)
        {
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i].Owner == owner)
                    throw new InvalidOperationException($"Lifecycle hook '{owner}' is already registered.");
            }

            hooks.Add(new OrderedLifecycleHook(owner, order, callback));
            hooks.Sort(CompareHook());
        }

        return new LifecycleHookRegistration(() => UnregisterHook(hooks, owner));
    }

    public void UnregisterHook(SwiftList<OrderedLifecycleHook> hooks, string owner)
    {
        lock (_lifecycleHookLock)
        {
            hooks.RemoveAll(hook => hook.Owner == owner);
        }
    }

    public void InvokeHooks(SwiftList<OrderedLifecycleHook> hooks)
    {
        OrderedLifecycleHook[] snapshot;
        lock (_lifecycleHookLock)
        {
            snapshot = hooks.ToArray();
        }

        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i].Callback();
    }

    private static Comparer<OrderedLifecycleHook> CompareHook()
    {
        return Comparer<OrderedLifecycleHook>.Create((left, right) =>
        {
            int orderCompare = left.Order.CompareTo(right.Order);
            if (orderCompare != 0)
                return orderCompare;

            return StringComparer.Ordinal.Compare(left.Owner, right.Owner);
        });
    }
}