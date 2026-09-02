//=======================================================================
// NavigationDependencyWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns fixed page and component dependency accumulation.</summary>
internal sealed class NavigationDependencyWorkspace
{
    private readonly NavigationAddressStampSet _componentSet;
    private readonly NavigationPageStampSet _pageSet;

    internal NavigationDependencyWorkspace(int pageCapacity, int componentCapacity)
    {
        SwiftThrowHelper.ThrowIfNegative(pageCapacity, nameof(pageCapacity));
        SwiftThrowHelper.ThrowIfNegative(componentCapacity, nameof(componentCapacity));
        Pages = pageCapacity == 0
            ? Array.Empty<GraphPageDependencyAddress>()
            : new GraphPageDependencyAddress[pageCapacity];
        Components = componentCapacity == 0
            ? Array.Empty<NavigationSurfaceComponentKey>()
            : new NavigationSurfaceComponentKey[componentCapacity];
        _componentSet = new NavigationAddressStampSet(Math.Max(1, componentCapacity));
        _pageSet = new NavigationPageStampSet(Math.Max(1, pageCapacity));
    }

    internal GraphPageDependencyAddress[] Pages { get; }

    internal NavigationSurfaceComponentKey[] Components { get; }

    internal int PageCount { get; private set; }

    internal int ComponentCount { get; private set; }

    internal bool HasTransitionDependency { get; private set; }

    internal void Reset()
    {
        if (PageCount > 0)
            Array.Clear(Pages, 0, PageCount);
        if (ComponentCount > 0)
            Array.Clear(Components, 0, ComponentCount);
        PageCount = 0;
        ComponentCount = 0;
        HasTransitionDependency = false;
        _componentSet.Reset();
        _pageSet.Reset();
    }

    internal bool TryRecordComponent(NavigationSurfaceComponentKey component)
    {
        if (ComponentCount >= Components.Length)
            return _componentSet.Contains(component.Representative);
        if (!_componentSet.Add(component.Representative))
            return true;
        Components[ComponentCount++] = component;
        return true;
    }

    internal bool TryRecordPage(string mapId, int pageIndex)
    {
        if (PageCount >= Pages.Length)
            return _pageSet.Contains(mapId, pageIndex);
        if (!_pageSet.Add(mapId, pageIndex))
            return true;
        Pages[PageCount++] = new GraphPageDependencyAddress(mapId, pageIndex);
        return true;
    }

    internal void RecordTransitionDependency() => HasTransitionDependency = true;

    internal bool TryCountMissing(
        NavigationDependencyWorkspace source,
        NavigationWorkMeter meter,
        out int missingComponents,
        out int missingPages)
    {
        SwiftThrowHelper.ThrowIfNull(source, nameof(source));
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        missingComponents = 0;
        missingPages = 0;
        int probeCount = source.ComponentCount + source.PageCount;
        if (!meter.TryConsumeLookupProbes(probeCount))
            return false;
        for (int i = 0; i < source.ComponentCount; i++)
        {
            if (!_componentSet.Contains(source.Components[i].Representative))
                missingComponents++;
        }
        for (int i = 0; i < source.PageCount; i++)
        {
            GraphPageDependencyAddress page = source.Pages[i];
            if (!_pageSet.Contains(page.MapId, page.PageIndex))
                missingPages++;
        }
        return true;
    }

    internal bool CanFit(int additionalComponents, int additionalPages) =>
        additionalComponents >= 0
        && additionalPages >= 0
        && additionalComponents <= Components.Length - ComponentCount
        && additionalPages <= Pages.Length - PageCount;

    internal void CommitMerge(NavigationDependencyWorkspace source)
    {
        SwiftThrowHelper.ThrowIfNull(source, nameof(source));
        for (int i = 0; i < source.ComponentCount; i++)
            TryRecordComponent(source.Components[i]);
        for (int i = 0; i < source.PageCount; i++)
        {
            GraphPageDependencyAddress page = source.Pages[i];
            TryRecordPage(page.MapId, page.PageIndex);
        }
        if (source.HasTransitionDependency)
            HasTransitionDependency = true;
    }
}
