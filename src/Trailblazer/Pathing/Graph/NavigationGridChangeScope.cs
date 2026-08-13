//=======================================================================
// NavigationGridChangeScope.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Configuration;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Identifies one configuration-local GridForge change scope and its observed generation.</summary>
internal readonly struct NavigationGridChangeScope : IEquatable<NavigationGridChangeScope>
{
    internal NavigationGridChangeScope(in GridEventInfo eventInfo)
    {
        ConfigurationKey = eventInfo.Configuration.ToGridKey();
        WorldSpawnToken = eventInfo.WorldSpawnToken;
        GridIndex = eventInfo.GridIndex;
        GridSpawnToken = eventInfo.GridSpawnToken;
    }

    internal NavigationGridChangeScope(
        GridConfigurationKey configurationKey,
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken)
    {
        ConfigurationKey = configurationKey;
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
    }

    internal GridConfigurationKey ConfigurationKey { get; }

    internal long WorldSpawnToken { get; }

    internal ushort GridIndex { get; }

    internal long GridSpawnToken { get; }

    public bool Equals(NavigationGridChangeScope other) =>
        ConfigurationKey.Equals(other.ConfigurationKey);

    public override bool Equals(object? obj) =>
        obj is NavigationGridChangeScope other && Equals(other);

    public override int GetHashCode() => ConfigurationKey.GetHashCode();
}
