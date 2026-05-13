using System;

namespace Trailblazer.Support;

/// <summary>
/// Represents a lifecycle hook with an associated owner, execution order, and callback action.
/// </summary>
public sealed class OrderedLifecycleHook
{
    /// <summary>
    /// Initializes a new instance of the OrderedLifecycleHook class with the specified owner, order, and callback.
    /// </summary>
    /// <param name="owner">The owner of the lifecycle hook.</param>
    /// <param name="order">The execution order of the lifecycle hook.</param>
    /// <param name="callback">The callback action to be invoked for the lifecycle hook.</param>
    public OrderedLifecycleHook(string owner, int order, Action callback)
    {
        Owner = owner;
        Order = order;
        Callback = callback;
    }

    /// <summary>
    /// Gets the owner of the lifecycle hook, which is used to identify and manage the hook within the system.
    /// </summary>
    public string Owner { get; }

    /// <summary>
    /// Gets the execution order of the lifecycle hook, determining the sequence in which it will be invoked relative to other hooks. 
    /// Hooks with lower order values will be executed before those with higher values.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets the callback action associated with the lifecycle hook, which will be invoked when the hook is executed. 
    /// This action defines the behavior that will occur when the lifecycle event associated with the hook is triggered.
    /// </summary>
    public Action Callback { get; }
}
