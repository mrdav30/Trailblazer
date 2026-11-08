//=======================================================================
// NavigationGraphCellDiagnostic.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Copies one addressed composed cell for bounded diagnostics.</summary>
public readonly struct NavigationGraphCellDiagnostic
{
    internal NavigationGraphCellDiagnostic(
        VoxelIndex index,
        int slot,
        NavigationCellSemanticSource semanticSource,
        bool hasCell,
        NavigationCell cell,
        bool isPresent,
        byte obstacleCount)
    {
        Index = index;
        Slot = slot;
        SemanticSource = semanticSource;
        HasCell = hasCell;
        Cell = cell;
        IsPresent = isPresent;
        ObstacleCount = obstacleCount;
    }

    /// <summary>Gets the topology-local address.</summary>
    public VoxelIndex Index { get; }
    /// <summary>Gets the stable map-local baked or dynamic slot.</summary>
    public int Slot { get; }
    /// <summary>Gets the baked, override, tombstone, or dynamic source.</summary>
    public NavigationCellSemanticSource SemanticSource { get; }
    /// <summary>Gets whether the address has an effective semantic cell.</summary>
    public bool HasCell { get; }
    /// <summary>Gets the effective cell payload when <see cref="HasCell"/> is true.</summary>
    public NavigationCell Cell { get; }
    /// <summary>Gets the mirrored GridForge presence state.</summary>
    public bool IsPresent { get; }
    /// <summary>Gets the mirrored exact obstacle count.</summary>
    public byte ObstacleCount { get; }
    /// <summary>Gets whether physical blockage suppresses traversal.</summary>
    public bool IsBlocked => ObstacleCount > 0;
}
