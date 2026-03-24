using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;

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
    /// The canonical voxel identity this anchor resolves through.
    /// </summary>
    public GlobalVoxelIndex VoxelIndex { get; }

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
        : GetVoxelWorldPosition(VoxelIndex);

    /// <summary>
    /// Creates a chart-backed anchor for the provided voxel index.
    /// </summary>
    public static TraversalTransitionAnchor Chart(GlobalVoxelIndex voxelIndex) =>
        Create(TraversalTransitionAnchorSpace.Chart, voxelIndex);

    /// <summary>
    /// Creates a chart-backed anchor for the provided voxel index with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Chart(GlobalVoxelIndex voxelIndex, Vector3d pointOverride) =>
        Create(TraversalTransitionAnchorSpace.Chart, voxelIndex, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a chart-backed anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor Chart(Vector3d position) =>
        CreateFromPosition(TraversalTransitionAnchorSpace.Chart, position);

    /// <summary>
    /// Creates a chart-backed anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Chart(Vector3d position, Vector3d pointOverride) =>
        CreateFromPosition(TraversalTransitionAnchorSpace.Chart, position, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates an open-volume anchor for the provided voxel index.
    /// </summary>
    public static TraversalTransitionAnchor OpenVolume(GlobalVoxelIndex voxelIndex) =>
        Create(TraversalTransitionAnchorSpace.OpenVolume, voxelIndex);

    /// <summary>
    /// Creates an open-volume anchor for the provided voxel index with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor OpenVolume(GlobalVoxelIndex voxelIndex, Vector3d pointOverride) =>
        Create(TraversalTransitionAnchorSpace.OpenVolume, voxelIndex, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates an open-volume anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor OpenVolume(Vector3d position) =>
        CreateFromPosition(TraversalTransitionAnchorSpace.OpenVolume, position);

    /// <summary>
    /// Creates an open-volume anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor OpenVolume(Vector3d position, Vector3d pointOverride) =>
        CreateFromPosition(TraversalTransitionAnchorSpace.OpenVolume, position, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a water-volume anchor for the provided voxel index.
    /// </summary>
    public static TraversalTransitionAnchor WaterVolume(GlobalVoxelIndex voxelIndex) =>
        Create(TraversalTransitionAnchorSpace.WaterVolume, voxelIndex);

    /// <summary>
    /// Creates a water-volume anchor for the provided voxel index with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor WaterVolume(GlobalVoxelIndex voxelIndex, Vector3d pointOverride) =>
        Create(TraversalTransitionAnchorSpace.WaterVolume, voxelIndex, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a water-volume anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor WaterVolume(Vector3d position) =>
        CreateFromPosition(TraversalTransitionAnchorSpace.WaterVolume, position);

    /// <summary>
    /// Creates a water-volume anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor WaterVolume(Vector3d position, Vector3d pointOverride) =>
        CreateFromPosition(TraversalTransitionAnchorSpace.WaterVolume, position, pointOverride, hasPointOverride: true);

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

    private TraversalTransitionAnchor(
        TraversalTransitionAnchorSpace space,
        GlobalVoxelIndex voxelIndex,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        Space = space;
        VoxelIndex = voxelIndex;
        HasPointOverride = hasPointOverride;
        PointOverride = pointOverride;
    }

    internal static bool TryResolveVoxelIndex(Vector3d position, out GlobalVoxelIndex voxelIndex)
    {
        if (GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
        {
            voxelIndex = voxel.GlobalIndex;
            return true;
        }

        voxelIndex = default;
        return false;
    }

    private static TraversalTransitionAnchor CreateFromPosition(
        TraversalTransitionAnchorSpace space,
        Vector3d position,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        if (!TryResolveVoxelIndex(position, out GlobalVoxelIndex voxelIndex))
        {
            throw new ArgumentException(
                "Anchor position must resolve to a voxel in the active grid setup.",
                nameof(position));
        }

        return Create(space, voxelIndex, pointOverride, hasPointOverride);
    }

    private static TraversalTransitionAnchor Create(
        TraversalTransitionAnchorSpace space,
        GlobalVoxelIndex voxelIndex,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        if (hasPointOverride && !PointOverrideMatchesVoxel(voxelIndex, pointOverride))
        {
            throw new ArgumentException(
                "Point override must resolve to the same voxel as the anchor.",
                nameof(pointOverride));
        }

        return new TraversalTransitionAnchor(space, voxelIndex, pointOverride, hasPointOverride);
    }

    private static bool PointOverrideMatchesVoxel(GlobalVoxelIndex voxelIndex, Vector3d pointOverride)
    {
        return TryResolveVoxelIndex(pointOverride, out GlobalVoxelIndex pointOverrideVoxelIndex)
            && pointOverrideVoxelIndex == voxelIndex;
    }

    private static Vector3d GetVoxelWorldPosition(GlobalVoxelIndex voxelIndex)
    {
        if (GlobalGridManager.TryGetGridAndVoxel(voxelIndex, out _, out Voxel voxel))
            return voxel.WorldPosition;

        throw new InvalidOperationException(
            $"Transition anchor voxel {voxelIndex} is not available in the current grid setup.");
    }
}
