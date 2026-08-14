//=======================================================================
// NavigationMapInstanceTestFactory.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
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
        var addresses = new VoxelIndex[composed.AddressCount];
        int count = composed.CopyCanonicalAddresses(addresses);
        if (count != addresses.Length
            || !world.TryCaptureNavigationBaseline(
                composed.Map.GridBinding.Key,
                addresses,
                out GridNavigationBaseline? baseline)
            || baseline == null)
        {
            return composed;
        }
        return composed.Materialize(
            new NavigationGridBaselineCapture(addresses, baseline),
            instanceVersion);
    }
}
