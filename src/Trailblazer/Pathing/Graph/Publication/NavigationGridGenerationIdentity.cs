//=======================================================================
// NavigationGridGenerationIdentity.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Configuration;

namespace Trailblazer.Pathing;

/// <summary>Identifies one exact runtime GridForge grid generation.</summary>
internal readonly struct NavigationGridGenerationIdentity
{
    internal NavigationGridGenerationIdentity(
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken,
        GridConfigurationKey configurationKey)
    {
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        ConfigurationKey = configurationKey;
    }

    internal long WorldSpawnToken { get; }

    internal ushort GridIndex { get; }

    internal long GridSpawnToken { get; }

    internal GridConfigurationKey ConfigurationKey { get; }

    internal bool IsValid => WorldSpawnToken > 0 && GridSpawnToken > 0;

    internal bool Matches(long worldSpawnToken, ushort gridIndex, long gridSpawnToken) =>
        WorldSpawnToken == worldSpawnToken
        && GridIndex == gridIndex
        && GridSpawnToken == gridSpawnToken;
}
