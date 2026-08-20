//=======================================================================
// NavSteering.LineOfSight.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    #region Line-of-Sight & Reachability

    /// <summary>
    /// Whether the destination is currently visible and reachable for raw-volume travel in the supplied context.
    /// </summary>
    public static bool IsVolumeDestinationInSight(
        TrailblazerWorldContext context,
        Vector3d position,
        Vector3d destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        TraversalMedium medium = TraversalMedium.Gas,
        Voxel? startNode = null,
        Voxel? endNode = null)
    {
        return VolumeVoxelFinder.IsDirectPathClear(
            context,
            position,
            destination,
            unitSize,
            allowUnwalkableEndpoints,
            medium,
            startNode,
            endNode);
    }

    #endregion
}
