//=======================================================================
// NavigationDependencyStampWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Captures one immutable dependency stamp with bounded graph reads.</summary>
internal sealed class NavigationDependencyStampWork
{
    private readonly NavigationWorldGraph _graph;
    private readonly NavigationAreaPolicyKey _areaPolicy;
    private readonly NavigationSurfaceComponentKey[] _componentAddresses;
    private readonly GraphPageDependencyAddress[] _pageAddresses;
    private readonly GraphComponentDependency[] _components;
    private readonly GraphPageDependency[] _pages;
    private int _componentOrdinal;
    private int _pageOrdinal;

    internal NavigationDependencyStampWork(
        NavigationWorldGraph graph,
        NavigationAreaPolicy areaPolicy,
        NavigationSurfaceComponentKey[] componentAddresses,
        int componentCount,
        GraphPageDependencyAddress[] pageAddresses,
        int pageCount)
    {
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        SwiftThrowHelper.ThrowIfNull(componentAddresses, nameof(componentAddresses));
        SwiftThrowHelper.ThrowIfNull(pageAddresses, nameof(pageAddresses));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (uint)componentCount > (uint)componentAddresses.Length,
            componentCount,
            nameof(componentCount));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            (uint)pageCount > (uint)pageAddresses.Length,
            pageCount,
            nameof(pageCount));
        _graph = graph;
        _areaPolicy = areaPolicy.Key;
        _componentAddresses = componentAddresses;
        _pageAddresses = pageAddresses;
        _components = new GraphComponentDependency[componentCount];
        _pages = new GraphPageDependency[pageCount];
        IsValid = true;
    }

    internal bool IsComplete { get; private set; }

    internal bool IsValid { get; private set; }

    internal GraphDependencyStamp Result { get; private set; } = null!;

    internal bool Advance(NavigationWorkMeter meter, int lookupStepLimit)
    {
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        SwiftThrowHelper.ThrowIfNegative(lookupStepLimit, nameof(lookupStepLimit));
        if (IsComplete)
            return true;
        if (!IsValid)
            return CompleteInvalid();
        int remaining = Math.Min(lookupStepLimit, meter.RemainingLookupProbes);
        while (_componentOrdinal < _components.Length)
        {
            if (remaining == 0 || !meter.TryConsumeLookupProbes(1))
                return false;
            remaining--;
            NavigationSurfaceComponentKey representative =
                _componentAddresses[_componentOrdinal];
            if ((_componentOrdinal > 0
                    && _componentAddresses[_componentOrdinal - 1]
                        .CompareTo(representative) >= 0)
                || !_graph.TryGetComponentDependency(
                    representative,
                    out _components[_componentOrdinal]))
            {
                return CompleteInvalid();
            }
            _componentOrdinal++;
        }
        while (_pageOrdinal < _pages.Length)
        {
            if (remaining == 0 || !meter.TryConsumeLookupProbes(1))
                return false;
            remaining--;
            GraphPageDependencyAddress address = _pageAddresses[_pageOrdinal];
            if ((_pageOrdinal > 0
                    && ComparePageAddresses(
                        _pageAddresses[_pageOrdinal - 1],
                        address) >= 0)
                || !_graph.TryGetPageDependency(
                    address,
                    out _pages[_pageOrdinal]))
            {
                return CompleteInvalid();
            }
            _pageOrdinal++;
        }
        Result = new GraphDependencyStamp(
            _areaPolicy,
            _components,
            _pages);
        IsComplete = true;
        return true;
    }

    private bool CompleteInvalid()
    {
        IsValid = false;
        IsComplete = true;
        Result = null!;
        return true;
    }

    private static int ComparePageAddresses(
        GraphPageDependencyAddress left,
        GraphPageDependencyAddress right)
    {
        int mapComparison = string.CompareOrdinal(left.MapId, right.MapId);
        return mapComparison != 0
            ? mapComparison
            : left.PageIndex.CompareTo(right.PageIndex);
    }
}
