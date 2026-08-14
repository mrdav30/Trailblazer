//=======================================================================
// NavigationNodeState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Exposes effective semantic and mirrored physical state for one snapshot node.</summary>
internal readonly struct NavigationNodeState
{
    internal NavigationNodeState(NavigationCell cell, bool isPresent, byte obstacleCount)
    {
        Cell = cell;
        IsPresent = isPresent;
        ObstacleCount = obstacleCount;
    }

    internal NavigationCell Cell { get; }

    internal bool IsPresent { get; }

    internal bool IsBlocked => ObstacleCount > 0;

    internal byte ObstacleCount { get; }
}
