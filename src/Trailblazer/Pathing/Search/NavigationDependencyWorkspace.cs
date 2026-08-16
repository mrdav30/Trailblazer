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

    internal void Reset()
    {
        if (PageCount > 0)
            Array.Clear(Pages, 0, PageCount);
        if (ComponentCount > 0)
            Array.Clear(Components, 0, ComponentCount);
        PageCount = 0;
        ComponentCount = 0;
        _componentSet.Reset();
        _pageSet.Reset();
    }

    internal bool TryRecordComponent(NavigationSurfaceComponentKey component)
    {
        if (!_componentSet.Add(component.Representative))
            return true;
        if (ComponentCount >= Components.Length)
            return false;
        Components[ComponentCount++] = component;
        return true;
    }

    internal bool TryRecordPage(string mapId, int pageIndex)
    {
        if (_pageSet.Contains(mapId, pageIndex))
            return true;
        if (PageCount >= Pages.Length)
            return false;
        _pageSet.Add(mapId, pageIndex);
        Pages[PageCount++] = new GraphPageDependencyAddress(mapId, pageIndex);
        return true;
    }
}
