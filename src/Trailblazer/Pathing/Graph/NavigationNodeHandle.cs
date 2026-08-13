//=======================================================================
// NavigationNodeHandle.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies one stable node slot within one immutable graph generation.</summary>
internal readonly struct NavigationNodeHandle
{
    internal NavigationNodeHandle(int mapOrdinal, int slot, long graphVersion)
    {
        MapOrdinal = mapOrdinal;
        Slot = slot;
        GraphVersion = graphVersion;
    }

    internal int MapOrdinal { get; }

    internal int Slot { get; }

    internal long GraphVersion { get; }
}
