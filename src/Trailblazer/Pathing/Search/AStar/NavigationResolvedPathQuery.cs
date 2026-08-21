//=======================================================================
// NavigationResolvedPathQuery.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns one resolved immutable query and its exact graph snapshot lease.</summary>
internal sealed class NavigationResolvedPathQuery : IDisposable
{
    private NavigationWorldGraphLease? _lease;

    internal NavigationResolvedPathQuery()
    {
    }

    internal void Bind(
        NavigationWorldGraphLease lease,
        PathQuery query,
        NavigationResolvedEndpoint start,
        NavigationResolvedEndpoint end,
        NavigationAreaPolicy areaPolicy,
        TraversalMedium startMedium,
        TraversalMedia targetMedia,
        NavigationWorkMeter meter)
    {
        SwiftThrowHelper.ThrowIfNull(lease, nameof(lease));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        SwiftThrowHelper.ThrowIfNull(meter, nameof(meter));
        if (_lease != null)
            throw new InvalidOperationException("The resolved path query is already active.");
        _lease = lease;
        Query = query;
        Start = start;
        End = end;
        AreaPolicy = areaPolicy;
        StartMedium = startMedium;
        TargetMedia = targetMedia;
        Meter = meter;
    }

    internal NavigationWorldGraph Graph => _lease?.Graph
        ?? throw new ObjectDisposedException(nameof(NavigationResolvedPathQuery));

    internal PathQuery Query { get; private set; }

    internal NavigationResolvedEndpoint Start { get; private set; }

    internal NavigationResolvedEndpoint End { get; private set; }

    internal NavigationAreaPolicy AreaPolicy { get; private set; } = null!;

    internal TraversalMedium StartMedium { get; private set; }

    internal TraversalMedia TargetMedia { get; private set; }

    internal NavigationWorkMeter Meter { get; private set; } = null!;

    internal void ReleaseLease()
    {
        NavigationWorldGraphLease? lease = _lease;
        _lease = null;
        lease?.Dispose();
    }

    public void Dispose()
    {
        ReleaseLease();
        Query = default;
        Start = default;
        End = default;
        AreaPolicy = null!;
        StartMedium = default;
        TargetMedia = default;
        Meter = null!;
    }
}
