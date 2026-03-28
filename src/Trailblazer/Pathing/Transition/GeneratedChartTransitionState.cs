using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Tracks the generated transitions currently owned by one build-result chart.
/// </summary>
internal sealed class GeneratedChartTransitionState
{
    public GeneratedChartTransitionState(string transitionIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(transitionIdPrefix))
            throw new ArgumentException("Transition id prefix cannot be null or whitespace.", nameof(transitionIdPrefix));

        TransitionIdPrefix = transitionIdPrefix;
    }

    public string TransitionIdPrefix { get; }

    public SwiftHashSet<string> TransitionIds { get; } = new();
}
