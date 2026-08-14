//=======================================================================
// NavigationGridBaselineCapture.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Owns one detached GridForge address baseline captured during a short maintenance gate.</summary>
internal readonly struct NavigationGridBaselineCapture
{
    internal NavigationGridBaselineCapture(
        VoxelIndex[] addresses,
        GridNavigationBaseline? baseline,
        bool isDelta = false)
        : this(addresses, addresses.Length, baseline, isDelta)
    {
    }

    internal NavigationGridBaselineCapture(
        VoxelIndex[] addresses,
        int addressCount,
        GridNavigationBaseline? baseline,
        bool isDelta = false)
    {
        Addresses = addresses;
        AddressCount = addressCount;
        Baseline = baseline;
        PreparedPages = null;
        HighWaterSequence = baseline?.HighWaterSequence ?? 0;
        WorldSpawnToken = baseline?.WorldSpawnToken ?? 0;
        GridIndex = baseline?.GridIndex ?? ushort.MaxValue;
        GridSpawnToken = baseline?.GridSpawnToken ?? 0;
        GridHighWaterSequence = baseline?.GridHighWaterSequence ?? 0;
        ConfigurationKey = baseline?.ConfigurationKey ?? default;
        IsDelta = isDelta;
        IsRequested = true;
    }

    internal NavigationGridBaselineCapture(
        int addressCount,
        GridNavigationBaseline? baseline,
        bool isDelta)
    {
        Addresses = null;
        AddressCount = addressCount;
        Baseline = baseline;
        PreparedPages = null;
        HighWaterSequence = baseline?.HighWaterSequence ?? 0;
        WorldSpawnToken = baseline?.WorldSpawnToken ?? 0;
        GridIndex = baseline?.GridIndex ?? ushort.MaxValue;
        GridSpawnToken = baseline?.GridSpawnToken ?? 0;
        GridHighWaterSequence = baseline?.GridHighWaterSequence ?? 0;
        ConfigurationKey = baseline?.ConfigurationKey ?? default;
        IsDelta = isDelta;
        IsRequested = true;
    }

    internal NavigationGridBaselineCapture(
        int addressCount,
        PersistentIntMap<NavigationPhysicalPage> preparedPages,
        ulong highWaterSequence,
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken,
        ulong gridHighWaterSequence,
        GridConfigurationKey configurationKey)
    {
        Addresses = null;
        AddressCount = addressCount;
        Baseline = null;
        PreparedPages = preparedPages;
        HighWaterSequence = highWaterSequence;
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        GridHighWaterSequence = gridHighWaterSequence;
        ConfigurationKey = configurationKey;
        IsDelta = false;
        IsRequested = true;
    }

    internal VoxelIndex[]? Addresses { get; }

    internal int AddressCount { get; }

    internal GridNavigationBaseline? Baseline { get; }

    internal PersistentIntMap<NavigationPhysicalPage>? PreparedPages { get; }

    internal ulong HighWaterSequence { get; }

    internal long WorldSpawnToken { get; }

    internal ushort GridIndex { get; }

    internal long GridSpawnToken { get; }

    internal ulong GridHighWaterSequence { get; }

    internal GridConfigurationKey ConfigurationKey { get; }

    internal bool IsDelta { get; }

    internal bool IsRequested { get; }

    internal bool HasBaseline => Baseline != null || PreparedPages != null;
}
