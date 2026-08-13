//=======================================================================
// LifecycleHookHandler.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using SwiftCollections;

namespace Trailblazer.Support;

/// <summary>
/// Provides functionality to register, unregister, and invoke lifecycle hooks in a thread-safe manner.
/// </summary>
public class LifecycleHookHandler
{
    private readonly object _lifecycleHookLock = new();

    /// <summary>
    /// Registers a lifecycle hook with the specified owner, order, and callback.
    /// Hooks are executed in order of their specified order, and if orders are equal, they are sorted by owner name.
    /// The returned IDisposable can be used to unregister the hook when it is no longer needed.
    /// </summary>
    /// <param name="hooks">The list of hooks to register the new hook with.</param>
    /// <param name="owner">The owner of the hook, used to identify and manage the hook.</param>
    /// <param name="order">The order in which the hook should be executed relative to other hooks.</param>
    /// <param name="callback">The callback to invoke when the hook is executed.</param>
    /// <returns>An IDisposable that can be used to unregister the hook.</returns>
    /// <exception cref="ArgumentException">Thrown if the owner is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a hook with the same owner is already registered.</exception>
    public IDisposable RegisterHook(
        SwiftList<OrderedLifecycleHook> hooks,
        string owner,
        int order,
        Action callback)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Lifecycle hook owner cannot be null or whitespace.", nameof(owner));

        SwiftThrowHelper.ThrowIfNull(callback, nameof(callback));

        lock (_lifecycleHookLock)
        {
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i].Owner == owner)
                    throw new InvalidOperationException($"Lifecycle hook '{owner}' is already registered.");
            }

            hooks.Add(new OrderedLifecycleHook(owner, order, callback));
            hooks.SortInPlace(CompareHook());
        }

        return new LifecycleHookRegistration(() => UnregisterHook(hooks, owner));
    }

    /// <summary>
    /// Unregisters a lifecycle hook based on the specified owner.
    /// </summary>
    /// <param name="hooks">The list of hooks to unregister the hook from.</param>
    /// <param name="owner">The owner of the hook to unregister.</param>
    public void UnregisterHook(SwiftList<OrderedLifecycleHook> hooks, string owner)
    {
        lock (_lifecycleHookLock)
        {
            hooks.RemoveAll(hook => hook.Owner == owner);
        }
    }

    /// <summary>
    /// Invokes all registered lifecycle hooks in the order they were registered.
    /// </summary>
    /// <param name="hooks">The list of hooks to invoke.</param>
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
