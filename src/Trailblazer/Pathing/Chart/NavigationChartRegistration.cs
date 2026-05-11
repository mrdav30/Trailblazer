using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores live registration state for one authored <see cref="NavigationChart"/> inside a pathing owner.
/// </summary>
/// <remarks>
/// Authored chart data is reusable. Initialization state, same-priority overlap order, and managed
/// generated transition ids belong to the registration owned by a runtime context.
/// </remarks>
public sealed class NavigationChartRegistration
{
    /// <summary>
    /// Creates live registration state for an authored chart.
    /// </summary>
    /// <param name="chart">The authored chart data.</param>
    /// <param name="registrationOrder">The deterministic order assigned by the owner.</param>
    /// <param name="generatedTransitionIdPrefix">The prefix used for generated transition ids.</param>
    public NavigationChartRegistration(
        NavigationChart chart,
        int registrationOrder,
        string generatedTransitionIdPrefix)
    {
        Chart = chart ?? throw new ArgumentNullException(nameof(chart));
        if (string.IsNullOrWhiteSpace(generatedTransitionIdPrefix))
        {
            throw new ArgumentException(
                "Generated transition id prefix cannot be null or whitespace.",
                nameof(generatedTransitionIdPrefix));
        }

        RegistrationOrder = registrationOrder;
        TransitionIdPrefix = generatedTransitionIdPrefix;
    }

    /// <summary>
    /// Gets the authored chart data.
    /// </summary>
    public NavigationChart Chart { get; }

    /// <summary>
    /// Gets the deterministic registration order. Higher values win same-priority chart overlaps.
    /// </summary>
    public int RegistrationOrder { get; }

    /// <summary>
    /// Gets whether the chart is currently materialized into live voxel state for this registration.
    /// </summary>
    public bool IsInitialized { get; internal set; }

    /// <summary>
    /// Gets the generated transition id prefix for this chart registration.
    /// </summary>
    internal string TransitionIdPrefix { get; }

    /// <summary>
    /// Gets the priority used for managed generated transitions created by this chart registration.
    /// </summary>
    internal int Priority => Chart.Priority;

    /// <summary>
    /// Gets the managed generated transition ids currently associated with this registration.
    /// </summary>
    internal SwiftHashSet<string> TransitionIds { get; } = new();
}
