using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one endpoint of an authored traversal transition.
/// </summary>
[Serializable]
public readonly struct TraversalTransitionAnchor
{
    /// <summary>
    /// Identifies the traversal medium this anchor belongs to.
    /// </summary>
    public TraversalMedium Medium { get; }

    /// <summary>
    /// The canonical voxel identity this anchor resolves through.
    /// </summary>
    public WorldVoxelIndex VoxelIndex { get; }

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
    /// Creates a solid anchor for the provided voxel index.
    /// </summary>
    public static TraversalTransitionAnchor Solid(WorldVoxelIndex voxelIndex) =>
        Create(TraversalMedium.Solid, voxelIndex);

    /// <summary>
    /// Creates a solid anchor for the provided voxel index with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Solid(WorldVoxelIndex voxelIndex, Vector3d pointOverride) =>
        Create(TraversalMedium.Solid, voxelIndex, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a solid anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor Solid(Vector3d position) =>
        CreateFromPosition(TraversalMedium.Solid, position);

    /// <summary>
    /// Creates a solid anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Solid(Vector3d position, Vector3d pointOverride) =>
        CreateFromPosition(TraversalMedium.Solid, position, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a gas anchor for the provided voxel index.
    /// </summary>
    public static TraversalTransitionAnchor Gas(WorldVoxelIndex voxelIndex) =>
        Create(TraversalMedium.Gas, voxelIndex);

    /// <summary>
    /// Creates a gas anchor for the provided voxel index with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Gas(WorldVoxelIndex voxelIndex, Vector3d pointOverride) =>
        Create(TraversalMedium.Gas, voxelIndex, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a gas anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor Gas(Vector3d position) =>
        CreateFromPosition(TraversalMedium.Gas, position);

    /// <summary>
    /// Creates a gas anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Gas(Vector3d position, Vector3d pointOverride) =>
        CreateFromPosition(TraversalMedium.Gas, position, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a liquid anchor for the provided voxel index.
    /// </summary>
    public static TraversalTransitionAnchor Liquid(WorldVoxelIndex voxelIndex) =>
        Create(TraversalMedium.Liquid, voxelIndex);

    /// <summary>
    /// Creates a liquid anchor for the provided voxel index with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Liquid(WorldVoxelIndex voxelIndex, Vector3d pointOverride) =>
        Create(TraversalMedium.Liquid, voxelIndex, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Creates a liquid anchor at the provided world position.
    /// </summary>
    public static TraversalTransitionAnchor Liquid(Vector3d position) =>
        CreateFromPosition(TraversalMedium.Liquid, position);

    /// <summary>
    /// Creates a liquid anchor with an explicit world-space point override.
    /// </summary>
    public static TraversalTransitionAnchor Liquid(Vector3d position, Vector3d pointOverride) =>
        CreateFromPosition(TraversalMedium.Liquid, position, pointOverride, hasPointOverride: true);

    /// <summary>
    /// Returns true when this anchor belongs to any raw-volume traversal medium.
    /// </summary>
    public bool IsVolumeMedium => Medium == TraversalMedium.Gas || Medium == TraversalMedium.Liquid;

    /// <summary>
    /// Attempts to get the volume medium carried by this anchor.
    /// </summary>
    public bool TryGetVolumeMedium(out TraversalMedium volumeMedium)
    {
        switch (Medium)
        {
            case TraversalMedium.Gas:
                volumeMedium = TraversalMedium.Gas;
                return true;
            case TraversalMedium.Liquid:
                volumeMedium = TraversalMedium.Liquid;
                return true;
            default:
                volumeMedium = TraversalMedium.Unknown;
                return false;
        }
    }

    private TraversalTransitionAnchor(
        TraversalMedium medium,
        WorldVoxelIndex voxelIndex,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        Medium = medium;
        VoxelIndex = voxelIndex;
        HasPointOverride = hasPointOverride;
        PointOverride = pointOverride;
    }

    internal static bool TryResolveVoxelIndex(Vector3d position, out WorldVoxelIndex voxelIndex)
    {
        if (TrailblazerWorldManager.TryGetVoxel(position, out Voxel? voxel))
        {
            voxelIndex = voxel!.WorldIndex;
            return true;
        }

        voxelIndex = default;
        return false;
    }

    private static TraversalTransitionAnchor CreateFromPosition(
        TraversalMedium medium,
        Vector3d position,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        if (!TryResolveVoxelIndex(position, out WorldVoxelIndex voxelIndex))
        {
            throw new ArgumentException(
                "Anchor position must resolve to a voxel in the active grid setup.",
                nameof(position));
        }

        return Create(medium, voxelIndex, pointOverride, hasPointOverride);
    }

    private static TraversalTransitionAnchor Create(
        TraversalMedium medium,
        WorldVoxelIndex voxelIndex,
        Vector3d pointOverride = default,
        bool hasPointOverride = false)
    {
        if (hasPointOverride && !PointOverrideMatchesVoxel(voxelIndex, pointOverride))
        {
            throw new ArgumentException(
                "Point override must resolve to the same voxel as the anchor.",
                nameof(pointOverride));
        }

        return new TraversalTransitionAnchor(medium, voxelIndex, pointOverride, hasPointOverride);
    }

    private static bool PointOverrideMatchesVoxel(WorldVoxelIndex voxelIndex, Vector3d pointOverride)
    {
        return TryResolveVoxelIndex(pointOverride, out WorldVoxelIndex pointOverrideVoxelIndex)
            && pointOverrideVoxelIndex == voxelIndex;
    }

    private static Vector3d GetVoxelWorldPosition(WorldVoxelIndex voxelIndex)
    {
        if (TrailblazerWorldManager.TryGetGridAndVoxel(voxelIndex, out _, out Voxel voxel))
            return voxel.WorldPosition;

        throw new InvalidOperationException(
            $"Transition anchor voxel {voxelIndex} is not available in the current grid setup.");
    }
}
