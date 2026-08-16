//=======================================================================
// NavigationEndpointWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Owns reusable endpoint-resolution buffers and exact dependencies.</summary>
internal sealed class NavigationEndpointWorkspace
{
    internal NavigationEndpointWorkspace(
        int mapCapacity,
        int pageCapacity,
        int componentCapacity)
    {
        SwiftThrowHelper.ThrowIfNegative(mapCapacity, nameof(mapCapacity));
        SwiftThrowHelper.ThrowIfNegative(pageCapacity, nameof(pageCapacity));
        SwiftThrowHelper.ThrowIfNegative(componentCapacity, nameof(componentCapacity));
        CoveredAddressCursor = new GridCoveredAddressCursor(mapCapacity);
        CoveredAddressGenerations = mapCapacity == 0
            ? Array.Empty<GridCoveredAddressGeneration>()
            : new GridCoveredAddressGeneration[mapCapacity];
        CoveredAddressOutput = new GridCoveredAddress[1];
        Dependencies = new NavigationDependencyWorkspace(pageCapacity, componentCapacity);
    }

    internal NavigationDependencyWorkspace Dependencies { get; }
    internal GridCoveredAddressCursor CoveredAddressCursor { get; }
    internal GridCoveredAddressGeneration[] CoveredAddressGenerations { get; }
    internal GridCoveredAddress[] CoveredAddressOutput { get; }
    internal GraphPageDependencyAddress[] Pages => Dependencies.Pages;
    internal NavigationSurfaceComponentKey[] Components => Dependencies.Components;
    internal int CoveredAddressGenerationCount { get; set; }
    internal int PageCount => Dependencies.PageCount;
    internal int ComponentCount => Dependencies.ComponentCount;

    internal void Reset()
    {
        ResetResolution();
        Dependencies.Reset();
    }

    internal void ResetResolution()
    {
        if (CoveredAddressGenerationCount > 0)
        {
            Array.Clear(
                CoveredAddressGenerations,
                0,
                CoveredAddressGenerationCount);
        }
        CoveredAddressOutput[0] = default;
        CoveredAddressGenerationCount = 0;
    }

    internal bool TryRecordComponent(NavigationSurfaceComponentKey component)
        => Dependencies.TryRecordComponent(component);

    internal bool TryRecordPage(string mapId, int pageIndex)
        => Dependencies.TryRecordPage(mapId, pageIndex);
}
