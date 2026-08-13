//=======================================================================
// NavigationCellOverlayOperation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Identifies the final-state action for a semantic cell overlay entry.</summary>
public enum NavigationCellOverlayOperationKind
{
    /// <summary>Write a complete effective cell payload.</summary>
    Set = 0,

    /// <summary>Tombstone effective traversal at the address.</summary>
    Suppress = 1,

    /// <summary>Remove the override or tombstone and restore the baked value.</summary>
    RevertToBake = 2
}

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

    /// <summary>Creates an operation that restores the baked value.</summary>
    public static NavigationCellOverlayOperation RevertToBake(VoxelIndex index) =>
        new(index, NavigationCellOverlayOperationKind.RevertToBake, default);

    internal static void ValidateKind(NavigationCellOverlayOperationKind kind)
    {
        SwiftThrowHelper.ThrowIfArgument(
            kind is < NavigationCellOverlayOperationKind.Set or > NavigationCellOverlayOperationKind.RevertToBake,
            nameof(kind),
            "Unknown cell overlay operation kind.");
    }
}
