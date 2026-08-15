//=======================================================================
// NavigationAStarWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Owns the reusable bounded buffers for one exclusive graph A* query.</summary>
internal sealed class NavigationAStarWorkspace
{
    internal NavigationAStarWorkspace(
        int mapCapacity,
        int endpointPageCapacity,
        int nodeCapacity = 0)
    {
        SwiftThrowHelper.ThrowIfNegative(mapCapacity, nameof(mapCapacity));
        SwiftThrowHelper.ThrowIfNegative(endpointPageCapacity, nameof(endpointPageCapacity));
        SwiftThrowHelper.ThrowIfNegative(nodeCapacity, nameof(nodeCapacity));
        CoveredAddressCursor = new GridCoveredAddressCursor(mapCapacity);
        CoveredAddressGenerations = mapCapacity == 0
            ? Array.Empty<GridCoveredAddressGeneration>()
            : new GridCoveredAddressGeneration[mapCapacity];
        CoveredAddressOutput = new GridCoveredAddress[1];
        EndpointPages = endpointPageCapacity == 0
            ? Array.Empty<GraphPageDependencyAddress>()
            : new GraphPageDependencyAddress[endpointPageCapacity];
        EndpointComponents = mapCapacity == 0
            ? Array.Empty<string>()
            : new string[mapCapacity];
        EndpointComponentSet = new NavigationStringStampSet(Math.Max(1, mapCapacity));
        EndpointPageSet = new NavigationPageStampSet(Math.Max(1, endpointPageCapacity));
        NodeTable = new NavigationAStarNodeTable(nodeCapacity);
        HeapNodes = nodeCapacity == 0
            ? Array.Empty<NavigationNodeRef>()
            : new NavigationNodeRef[nodeCapacity];
        PathNodes = nodeCapacity == 0
            ? Array.Empty<NavigationNodeRef>()
            : new NavigationNodeRef[nodeCapacity];
    }

    internal GridCoveredAddressCursor CoveredAddressCursor { get; }

    internal GridCoveredAddressGeneration[] CoveredAddressGenerations { get; }

    internal GridCoveredAddress[] CoveredAddressOutput { get; }

    internal GraphPageDependencyAddress[] EndpointPages { get; }

    internal string[] EndpointComponents { get; }

    internal NavigationStringStampSet EndpointComponentSet { get; }

    internal NavigationPageStampSet EndpointPageSet { get; }

    internal NavigationAStarNodeTable NodeTable { get; }

    internal NavigationNodeRef[] HeapNodes { get; }

    internal NavigationNodeRef[] PathNodes { get; }

    internal int CoveredAddressGenerationCount { get; set; }

    internal int EndpointPageCount { get; set; }

    internal int EndpointComponentCount { get; set; }

    internal int HeapCount { get; set; }

    internal int PathNodeCount { get; set; }

    internal void Reset()
    {
        ResetEndpointResolution();
        EndpointComponentSet.Reset();
        EndpointPageSet.Reset();
        if (EndpointPageCount > 0)
            Array.Clear(EndpointPages, 0, EndpointPageCount);
        if (EndpointComponentCount > 0)
            Array.Clear(EndpointComponents, 0, EndpointComponentCount);
        EndpointPageCount = 0;
        EndpointComponentCount = 0;
        ResetSearch();
    }

    internal void ResetEndpointResolution()
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

    internal bool TryRecordEndpointComponent(string componentKey)
    {
        if (EndpointComponentSet.Contains(componentKey))
            return true;
        if (EndpointComponentCount >= EndpointComponents.Length)
            return false;
        EndpointComponentSet.Add(componentKey);
        EndpointComponents[EndpointComponentCount++] = componentKey;
        return true;
    }

    internal bool TryRecordEndpointPage(string mapId, int pageIndex)
    {
        if (EndpointPageSet.Contains(mapId, pageIndex))
            return true;
        if (EndpointPageCount >= EndpointPages.Length)
            return false;
        EndpointPageSet.Add(mapId, pageIndex);
        EndpointPages[EndpointPageCount++] = new GraphPageDependencyAddress(mapId, pageIndex);
        return true;
    }

    internal void ResetSearch()
    {
        NodeTable.Reset();
        HeapCount = 0;
        PathNodeCount = 0;
    }
}
