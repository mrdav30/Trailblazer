//=======================================================================
// NavigationAStarWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns the reusable bounded buffers for one exclusive graph A* query.</summary>
internal sealed class NavigationAStarWorkspace
{
    internal NavigationAStarWorkspace(
        int mapCapacity,
        int endpointPageCapacity,
        int componentCapacity,
        int nodeCapacity,
        int rayCoveredAddressCapacity,
        int rayTraceIntervalCapacity,
        int guidePointCapacity)
    {
        SwiftThrowHelper.ThrowIfNegative(mapCapacity, nameof(mapCapacity));
        SwiftThrowHelper.ThrowIfNegative(endpointPageCapacity, nameof(endpointPageCapacity));
        SwiftThrowHelper.ThrowIfNegative(nodeCapacity, nameof(nodeCapacity));
        SwiftThrowHelper.ThrowIfNegative(componentCapacity, nameof(componentCapacity));
        SwiftThrowHelper.ThrowIfNegative(guidePointCapacity, nameof(guidePointCapacity));
        EndpointWorkspace = new NavigationEndpointWorkspace(
            mapCapacity,
            endpointPageCapacity,
            componentCapacity);
        NodeTable = new NavigationAStarNodeTable(nodeCapacity);
        HeapNodes = nodeCapacity == 0
            ? Array.Empty<NavigationNodeRef>()
            : new NavigationNodeRef[nodeCapacity];
        PathNodes = nodeCapacity == 0
            ? Array.Empty<NavigationNodeRef>()
            : new NavigationNodeRef[nodeCapacity];
        PathNodeGuidePointOrdinals = nodeCapacity == 0
            ? Array.Empty<int>()
            : new int[nodeCapacity];
        RayWorkspace = new NavigationRayWorkspace(
            mapCapacity,
            endpointPageCapacity,
            componentCapacity,
            rayCoveredAddressCapacity,
            rayTraceIntervalCapacity);
        GuidePoints = guidePointCapacity == 0
            ? Array.Empty<NavigationAStarGuidePoint>()
            : new NavigationAStarGuidePoint[guidePointCapacity];
    }

    internal NavigationEndpointWorkspace EndpointWorkspace { get; }

    internal GraphPageDependencyAddress[] EndpointPages => EndpointWorkspace.Pages;

    internal NavigationSurfaceComponentKey[] EndpointComponents =>
        EndpointWorkspace.Components;

    internal int EndpointPageCount => EndpointWorkspace.PageCount;

    internal int EndpointComponentCount => EndpointWorkspace.ComponentCount;

    internal NavigationAStarNodeTable NodeTable { get; }

    internal NavigationNodeRef[] HeapNodes { get; }

    internal NavigationNodeRef[] PathNodes { get; }

    internal int[] PathNodeGuidePointOrdinals { get; }

    internal NavigationRayWorkspace RayWorkspace { get; }

    internal NavigationAStarGuidePoint[] GuidePoints { get; }

    internal int HeapCount { get; set; }

    internal int PathNodeCount { get; set; }

    internal int GuidePointCount { get; set; }

    internal void Reset()
    {
        EndpointWorkspace.Reset();
        RayWorkspace.Reset();
        ResetSearch();
    }

    internal bool TryRecordEndpointComponent(NavigationSurfaceComponentKey componentKey)
    {
        return EndpointWorkspace.TryRecordComponent(componentKey);
    }

    internal bool TryRecordEndpointPage(string mapId, int pageIndex)
    {
        return EndpointWorkspace.TryRecordPage(mapId, pageIndex);
    }

    internal void ResetSearch()
    {
        NodeTable.Reset();
        HeapCount = 0;
        PathNodeCount = 0;
        GuidePointCount = 0;
    }
}
