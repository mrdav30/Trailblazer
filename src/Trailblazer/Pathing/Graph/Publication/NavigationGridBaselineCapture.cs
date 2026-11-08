//=======================================================================
// NavigationGridBaselineCapture.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Owns one detached GridForge address baseline captured during a short maintenance gate.</summary>
internal readonly struct NavigationGridBaselineCapture
{
    internal NavigationGridBaselineCapture(
        int addressCount,
        GridNavigationBaseline? baseline,
        bool isDelta)
    {
        AddressCount = addressCount;
        Baseline = baseline;
        PreparedPages = null;
        CapturedChangeSequence = baseline?.CapturedChangeSequence ?? 0;
        WorldSpawnToken = baseline?.WorldSpawnToken ?? 0;
        GridIndex = baseline?.GridIndex ?? ushort.MaxValue;
        GridSpawnToken = baseline?.GridSpawnToken ?? 0;
        GridLastChangeSequence = baseline?.GridLastChangeSequence ?? 0;
        ConfigurationKey = baseline?.ConfigurationKey ?? default;
        IsDelta = isDelta;
        IsRequested = true;
    }

    internal NavigationGridBaselineCapture(
        int addressCount,
        PersistentIntMap<NavigationPhysicalPage> preparedPages,
        ulong capturedChangeSequence,
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken,
        ulong gridLastChangeSequence,
        GridConfigurationKey configurationKey)
    {
        AddressCount = addressCount;
        Baseline = null;
        PreparedPages = preparedPages;
        CapturedChangeSequence = capturedChangeSequence;
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        GridLastChangeSequence = gridLastChangeSequence;
        ConfigurationKey = configurationKey;
        IsDelta = false;
        IsRequested = true;
    }

    internal NavigationGridBaselineCapture(
        NavigationMapInstance preparedInstance,
        NavigationSurfaceComponentKeySet structuralChangedStates,
        bool defaultPhysicalAddressSetChanged,
        int addressCount,
        ulong capturedChangeSequence,
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken,
        ulong gridLastChangeSequence,
        GridConfigurationKey configurationKey)
    {
        AddressCount = addressCount;
        Baseline = null;
        PreparedPages = null;
        PreparedInstance = preparedInstance;
        StructuralChangedStates = structuralChangedStates;
        DefaultPhysicalAddressSetChanged = defaultPhysicalAddressSetChanged;
        CapturedChangeSequence = capturedChangeSequence;
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        GridLastChangeSequence = gridLastChangeSequence;
        ConfigurationKey = configurationKey;
        IsDelta = false;
        IsRequested = true;
    }

    internal int AddressCount { get; }

    internal GridNavigationBaseline? Baseline { get; }

    internal PersistentIntMap<NavigationPhysicalPage>? PreparedPages { get; }

    internal NavigationMapInstance? PreparedInstance { get; }

    internal NavigationSurfaceComponentKeySet? StructuralChangedStates { get; }

    internal bool DefaultPhysicalAddressSetChanged { get; }

    internal ulong CapturedChangeSequence { get; }

    internal long WorldSpawnToken { get; }

    internal ushort GridIndex { get; }

    internal long GridSpawnToken { get; }

    internal ulong GridLastChangeSequence { get; }

    internal GridConfigurationKey ConfigurationKey { get; }

    internal bool IsDelta { get; }

    internal bool IsRequested { get; }

    internal bool HasBaseline => Baseline != null
        || PreparedPages != null
        || PreparedInstance != null;
}
