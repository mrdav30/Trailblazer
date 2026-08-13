//=======================================================================
// TraversalBuildResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Contains the runtime objects produced from a tokenized traversable-state authoring map.
/// </summary>
public sealed class TraversalBuildResult
{
    /// <summary>
    /// Creates a new traversal build result with the specified chart and generated transitions.
    /// </summary>
    /// <param name="chart">The navigation chart built from the authoring map.</param>
    /// <param name="generatedTransitions">The transitions generated from explicit marker pairs in the authoring map.</param>
    /// <param name="generatedTransitionIdPrefix">The prefix used when generating stable ids for chart-owned transitions.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public TraversalBuildResult(
        NavigationChart chart,
        TraversalTransition[] generatedTransitions,
        string? generatedTransitionIdPrefix = null)
    {
        Chart = chart ?? throw new ArgumentNullException(nameof(chart));
        GeneratedTransitions = generatedTransitions ?? Array.Empty<TraversalTransition>();
        GeneratedTransitionIdPrefix = string.IsNullOrWhiteSpace(generatedTransitionIdPrefix)
            ? chart.Name
            : generatedTransitionIdPrefix;
    }

    /// <summary>
    /// The chart built from the authoring map.
    /// </summary>
    public NavigationChart Chart { get; }

    /// <summary>
    /// The transitions generated from explicit marker pairs in the authoring map.
    /// </summary>
    public TraversalTransition[] GeneratedTransitions { get; }

    /// <summary>
    /// Prefix used when generating stable ids for chart-owned transitions.
    /// </summary>
    public string GeneratedTransitionIdPrefix { get; }
}
