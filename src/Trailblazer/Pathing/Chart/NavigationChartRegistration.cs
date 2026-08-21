//=======================================================================
// NavigationChartRegistration.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores live registration state for one authored <see cref="NavigationChart"/> inside a pathing owner.
/// </summary>
/// <remarks>
/// Authored chart data is reusable. Initialization state and same-priority overlap order belong to
/// the registration owned by a runtime context.
/// </remarks>
public sealed class NavigationChartRegistration
{
    /// <summary>
    /// Creates live registration state for an authored chart.
    /// </summary>
    /// <param name="chart">The authored chart data.</param>
    /// <param name="registrationOrder">The deterministic order assigned by the owner.</param>
    public NavigationChartRegistration(
        NavigationChart chart,
        int registrationOrder)
    {
        Chart = chart ?? throw new ArgumentNullException(nameof(chart));
        RegistrationOrder = registrationOrder;
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

}
