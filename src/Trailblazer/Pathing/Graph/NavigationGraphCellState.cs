//=======================================================================
// NavigationGraphCellState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Copies one composed graph cell state for tests and bounded diagnostics.</summary>
internal readonly struct NavigationGraphCellState
{
    internal NavigationGraphCellState(
        string mapId,
        VoxelIndex index,
        int slot,
        bool isDynamic,
        bool hasCell,
        NavigationCell cell,
        bool isMaterialized,
        bool isPresent,
        byte obstacleCount,
        long gridSpawnToken)
    {
        MapId = mapId;
        Index = index;
        Slot = slot;
        IsDynamic = isDynamic;
        HasCell = hasCell;
        Cell = cell;
        IsMaterialized = isMaterialized;
        IsPresent = isPresent;
        ObstacleCount = obstacleCount;
        GridSpawnToken = gridSpawnToken;
    }

    internal string MapId { get; }
    internal VoxelIndex Index { get; }
    internal int Slot { get; }
    internal bool IsDynamic { get; }
    internal bool HasCell { get; }
    internal NavigationCell Cell { get; }
    internal bool IsMaterialized { get; }
    internal bool IsPresent { get; }
    internal bool IsBlocked => ObstacleCount > 0;
    internal byte ObstacleCount { get; }
    internal long GridSpawnToken { get; }
}
