//=======================================================================
// NavigationCellOverlayOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Describes one immutable addressed semantic-cell overlay operation.</summary>
public readonly struct NavigationCellOverlayOperation
{
    private NavigationCellOverlayOperation(
        VoxelIndex index,
        NavigationCellOverlayOperationKind kind,
        NavigationCell cell)
    {
        Index = index;
        Kind = kind;
        Cell = cell;
    }

    /// <summary>Gets the topology-local address changed by this operation.</summary>
    public VoxelIndex Index { get; }

    /// <summary>Gets the final-state operation kind.</summary>
    public NavigationCellOverlayOperationKind Kind { get; }

    /// <summary>
    /// Gets the complete payload for <see cref="NavigationCellOverlayOperationKind.Set"/>.
    /// The value is ignored for suppression and reversion.
    /// </summary>
    public NavigationCell Cell { get; }

    /// <summary>Creates a complete-payload Set operation.</summary>
    public static NavigationCellOverlayOperation Set(VoxelIndex index, NavigationCell cell) =>
        new(index, NavigationCellOverlayOperationKind.Set, cell);

    /// <summary>Creates a tombstone operation.</summary>
    public static NavigationCellOverlayOperation Suppress(VoxelIndex index) =>
        new(index, NavigationCellOverlayOperationKind.Suppress, default);

    /// <summary>Creates an operation that restores explicit, then default, authoring.</summary>
    public static NavigationCellOverlayOperation RevertToBake(VoxelIndex index) =>
        new(index, NavigationCellOverlayOperationKind.RevertToBake, default);

}
