//=======================================================================
// NavigationNodeState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Exposes effective semantic and mirrored physical state for one snapshot node.</summary>
internal readonly struct NavigationNodeState
{
    internal NavigationNodeState(
        NavigationCell cell,
        bool isPresent,
        byte obstacleCount,
        Vector3d center,
        Vector3d footAnchor)
    {
        Cell = cell;
        IsPresent = isPresent;
        ObstacleCount = obstacleCount;
        Center = center;
        FootAnchor = footAnchor;
    }

    internal NavigationCell Cell { get; }

    internal bool IsPresent { get; }

    internal bool IsBlocked => ObstacleCount > 0;

    internal byte ObstacleCount { get; }

    internal Vector3d Center { get; }

    internal Vector3d FootAnchor { get; }
}
