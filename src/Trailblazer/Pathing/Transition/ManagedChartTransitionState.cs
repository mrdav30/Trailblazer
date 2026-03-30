using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Tracks the registered managed generated transitions currently associated with one chart.
/// </summary>
internal sealed class ManagedChartTransitionState
{
    public ManagedChartTransitionState(string transitionIdPrefix, int priority)
    {
        if (string.IsNullOrWhiteSpace(transitionIdPrefix))
            throw new ArgumentException("Transition id prefix cannot be null or whitespace.", nameof(transitionIdPrefix));

        TransitionIdPrefix = transitionIdPrefix;
        Priority = priority;
    }

    public string TransitionIdPrefix { get; }

    public int Priority { get; }

    public SwiftHashSet<string> TransitionIds { get; } = new();
}
