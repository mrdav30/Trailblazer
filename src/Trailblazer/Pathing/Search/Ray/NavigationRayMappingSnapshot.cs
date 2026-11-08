//=======================================================================
// NavigationRayMappingSnapshot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Caches the node, component, and obstacle count resolved for one ray voxel.</summary>
internal struct NavigationRayMappingSnapshot
{
    internal VoxelIndex Index;
    internal NavigationNodeRef Node;
    internal NavigationSurfaceComponentKey Component;
    internal byte ObstacleCount;
    internal bool IsValid;
}
