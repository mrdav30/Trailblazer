//=======================================================================
// NavigationBakedCellLookup.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Identifies the measured baked-cell lookup representation.</summary>
public enum NavigationCellLookupKind
{
    /// <summary>Canonical baked entries are searched without address-volume storage.</summary>
    Sorted = 0,
    /// <summary>A direct address table is cheaper at the authored navigation density.</summary>
    Direct = 1
}

/// <summary>Selects lookup representation from authored density, independent of GridForge storage kind.</summary>
internal sealed class NavigationBakedCellLookup
{
    // Phase 0 measured the direct table at 4 MiB for a 1M-address map. Keep that
    // per-map ceiling and require at least 50% authored density.
    internal const long MaximumDirectTableBytes = 4_194_304;
    internal const int MinimumDirectDensityDivisor = 2;
    private readonly NavigationMap _map;
    private readonly int[]? _directSlots;

    private NavigationBakedCellLookup(NavigationMap map, int[]? directSlots)
    {
        _map = map;
        _directSlots = directSlots;
        Kind = directSlots == null ? NavigationCellLookupKind.Sorted : NavigationCellLookupKind.Direct;
    }

    internal NavigationCellLookupKind Kind { get; }

    internal long RetainedBytes => _directSlots == null ? 16L : 24L + ((long)_directSlots.Length * sizeof(int));

    internal static NavigationBakedCellLookup Create(NavigationMap map)
    {
        int addressCount = map.GridBinding.AddressCount;
        long directBytes = (long)addressCount * sizeof(int);
        if (addressCount > map.CellSpan.Length * MinimumDirectDensityDivisor
            || directBytes > MaximumDirectTableBytes)
            return new NavigationBakedCellLookup(map, null);

        var slots = new int[addressCount];
        Array.Fill(slots, -1);
        for (int i = 0; i < map.CellSpan.Length; i++)
            slots[GetLinearAddress(map, map.CellSpan[i].Index)] = i;
        return new NavigationBakedCellLookup(map, slots);
    }

    internal int Find(VoxelIndex index)
    {
        if (_directSlots == null)
            return _map.FindCellIndex(index);
        if (!_map.GridBinding.IsValidIndex(index))
            return -1;
        return _directSlots[GetLinearAddress(_map, index)];
    }

    private static int GetLinearAddress(NavigationMap map, VoxelIndex index) => checked(
        ((index.x * map.GridBinding.Height) + index.y) * map.GridBinding.Length + index.z);
}
