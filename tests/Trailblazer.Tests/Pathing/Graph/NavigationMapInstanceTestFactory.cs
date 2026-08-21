//=======================================================================
// NavigationMapInstanceTestFactory.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Tests.Pathing.Graph;

internal static class NavigationMapInstanceTestFactory
{
    internal static NavigationMapInstance ComposeDetached(
        NavigationOperationCandidate.MapState state,
        NavigationMapInstance? previous,
        long instanceVersion)
    {
        var work = new NavigationMapInstance.ComposeWork(state, previous, instanceVersion);
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 0; frame < 4_096; frame++)
        {
            if (work.Advance(meter))
                return work.Result;
            meter.Reset();
        }
        throw new InvalidOperationException("Map composition did not complete.");
    }

    internal static NavigationMapInstance Compose(
        GridWorld world,
        NavigationOperationCandidate.MapState state,
        NavigationMapInstance? previous,
        long instanceVersion)
    {
        NavigationMapInstance composed = ComposeDetached(state, previous, instanceVersion);
        int capacity = Math.Max(composed.AddressCount, composed.Map.GridBinding.AddressCount);
        var addresses = new VoxelIndex[capacity];
        var coveredAddresses = new GridCoveredAddress[capacity];
        var rebuild = new NavigationBaselineRebuild(composed);
        for (int frame = 0; frame < 4_096; frame++)
        {
            rebuild.Advance(
                world,
                composed,
                capacity,
                long.MaxValue,
                int.MaxValue,
                addresses,
                coveredAddresses,
                out NavigationGridBaselineCapture capture,
                out bool completed);
            if (completed)
                return composed.Materialize(capture, instanceVersion);
        }
        throw new InvalidOperationException("Baseline composition did not complete.");
    }
}
