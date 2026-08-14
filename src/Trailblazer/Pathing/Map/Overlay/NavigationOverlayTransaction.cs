//=======================================================================
// NavigationOverlayTransaction.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trailblazer.Pathing;

/// <summary>
/// Defines the minimum atomic semantic-overlay publication unit across one or more maps.
/// </summary>
public sealed class NavigationOverlayTransaction
{
    private static readonly IComparer<NavigationMapOverlayDelta> MapComparer = new MapDeltaComparer();
    private readonly NavigationMapOverlayDelta[] _maps;
    private readonly ReadOnlyCollection<NavigationMapOverlayDelta> _mapView;

    /// <summary>Initializes and canonically orders an immutable multi-map overlay transaction.</summary>
    public NavigationOverlayTransaction(ReadOnlySpan<NavigationMapOverlayDelta> maps)
    {
        SwiftThrowHelper.ThrowIfArgument(
            maps.IsEmpty,
            nameof(maps),
            "An overlay transaction must contain at least one map delta.");

        _maps = maps.ToArray();
        for (int i = 0; i < _maps.Length; i++)
            SwiftThrowHelper.ThrowIfNull(_maps[i], nameof(maps));

        Array.Sort(_maps, MapComparer);
        long descriptorBytes = 32L;
        bool mayChangeExplicitConnections = false;
        for (int i = 0; i < _maps.Length; i++)
        {
            SwiftThrowHelper.ThrowIfArgument(
                i > 0 && string.Equals(_maps[i - 1].MapId, _maps[i].MapId, StringComparison.Ordinal),
                nameof(maps),
                "Map overlay ids must be unique within one transaction.");
            try
            {
                descriptorBytes = checked(descriptorBytes + _maps[i].EstimatedDescriptorBytes);
                mayChangeExplicitConnections |= !_maps[i].CellSpan.IsEmpty
                    || !_maps[i].ConnectionSpan.IsEmpty;
            }
            catch (OverflowException)
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(maps),
                    "Overlay transaction descriptor-byte accounting overflowed.");
            }
        }

        _mapView = Array.AsReadOnly(_maps);
        EstimatedDescriptorBytes = descriptorBytes;
        MayChangeExplicitConnections = mayChangeExplicitConnections;
    }

    /// <summary>Gets canonically ordered, unique per-map deltas.</summary>
    public IReadOnlyList<NavigationMapOverlayDelta> Maps => _mapView;

    internal ReadOnlySpan<NavigationMapOverlayDelta> MapSpan => _maps;

    /// <summary>Gets a deterministic conservative byte count for submission admission.</summary>
    public long EstimatedDescriptorBytes { get; }

    internal bool MayChangeExplicitConnections { get; }

    private sealed class MapDeltaComparer : IComparer<NavigationMapOverlayDelta>
    {
        public int Compare(NavigationMapOverlayDelta? left, NavigationMapOverlayDelta? right) =>
            string.CompareOrdinal(left?.MapId, right?.MapId);
    }
}
