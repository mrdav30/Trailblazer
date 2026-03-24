using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one endpoint of an authored traversal transition.
/// </summary>
[Serializable]
public readonly struct TraversalTransitionAnchor
{
    /// <summary>
    /// Identifies the traversal space this anchor belongs to.
    /// </summary>
    public TraversalTransitionAnchorSpace Space { get; }

    /// <summary>
    /// The canonical voxel position this anchor resolves through.
    /// </summary>
    public Vector3d VoxelPosition { get; }

    /// <summary>
    /// Indicates whether this anchor uses a world-space point override inside the resolved voxel.
    /// </summary>
    public bool HasPointOverride { get; }

    /// <summary>
    /// The exact world-space point used for route steps when <see cref="HasPointOverride"/> is true.
    /// </summary>
    public Vector3d PointOverride { get; }

    /// <summary>
    /// The effective world-space point used by planners.
    /// </summary>
    public Vector3d Position => HasPointOverride
        ? PointOverride
        : VoxelPosition;

    /// <summary>
    /// Creates a chart-backed anchor at the provided voxel position.
    /// </summary>
    public static TraversalTransitionAnchor Chart(Vector3d voxelPosition) =>
        new(TraversalTransitionAnchorSpace.Chart, voxelPosition);

    /// <summary>
    /// Creates a chart-backed anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Chart(Vector3d voxelPosition, Vector3d pointOverride) =>
        new(TraversalTransitionAnchorSpace.Chart, voxelPosition, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates an open-volume anchor at the provided voxel position.
    /// </summary>
    public static TraversalTransitionAnchor OpenVolume(Vector3d voxelPosition) =>
        new(TraversalTransitionAnchorSpace.OpenVolume, voxelPosition);

    /// <summary>
    /// Creates an open-volume anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor OpenVolume(Vector3d voxelPosition, Vector3d pointOverride) =>
        new(TraversalTransitionAnchorSpace.OpenVolume, voxelPosition, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a water-volume anchor at the provided voxel position.
    /// </summary>
    public static TraversalTransitionAnchor WaterVolume(Vector3d voxelPosition) =>
        new(TraversalTransitionAnchorSpace.WaterVolume, voxelPosition);

    /// <summary>
    /// Creates a water-volume anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor WaterVolume(Vector3d voxelPosition, Vector3d pointOverride) =>
        new(TraversalTransitionAnchorSpace.WaterVolume, voxelPosition, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a raw-volume anchor at the provided voxel position.
    /// </summary>
    public static TraversalTransitionAnchor Volume(Vector3d voxelPosition, VolumeTraversalMode volumeMode) =>
        new(ToAnchorSpace(volumeMode), voxelPosition);

    /// <summary>
    /// Creates a raw-volume anchor at the provided voxel position with an explicit point override.
    /// </summary>
    public static TraversalTransitionAnchor Volume(
        Vector3d voxelPosition,
        VolumeTraversalMode volumeMode,
        Vector3d pointOverride) =>
        new(ToAnchorSpace(volumeMode), voxelPosition, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Returns true when this anchor belongs to any raw-volume traversal space.
    /// </summary>
    public bool IsVolumeSpace => Space != TraversalTransitionAnchorSpace.Chart;

    /// <summary>
    /// Attempts to convert this anchor's traversal space into a raw volume traversal mode.
    /// </summary>
    public bool TryGetVolumeTraversalMode(out VolumeTraversalMode volumeMode)
    {
        switch (Space)
        {
            case TraversalTransitionAnchorSpace.OpenVolume:
                volumeMode = VolumeTraversalMode.Open;
                return true;
            case TraversalTransitionAnchorSpace.WaterVolume:
                volumeMode = VolumeTraversalMode.Water;
                return true;
            default:
                volumeMode = VolumeTraversalMode.Open;
                return false;
        }
    }

    /// <summary>
    /// Converts this anchor's traversal space into a raw volume traversal mode.
    /// </summary>
    public VolumeTraversalMode GetRequiredVolumeTraversalMode()
    {
        if (TryGetVolumeTraversalMode(out VolumeTraversalMode volumeMode))
            return volumeMode;

        throw new InvalidOperationException("Chart anchors do not map to a raw volume traversal mode.");
    }

    public TraversalTransitionAnchor(
        TraversalTransitionAnchorSpace space,
        Vector3d voxelPosition,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        Space = space;
        VoxelPosition = voxelPosition;
        HasPointOverride = hasPointOverride;
        PointOverride = pointOverride;
    }

    private static TraversalTransitionAnchorSpace ToAnchorSpace(VolumeTraversalMode volumeMode)
    {
        return volumeMode switch
        {
            VolumeTraversalMode.Open => TraversalTransitionAnchorSpace.OpenVolume,
            VolumeTraversalMode.Water => TraversalTransitionAnchorSpace.WaterVolume,
            _ => throw new ArgumentOutOfRangeException(nameof(volumeMode), volumeMode, "Unsupported volume traversal mode.")
        };
    }
}
