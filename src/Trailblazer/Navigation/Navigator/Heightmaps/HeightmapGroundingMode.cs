//=======================================================================
// HeightmapGroundingMode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation;

/// <summary>
/// Controls how a navigator consumes registered heightmap samples when explicitly asked to ground itself.
/// </summary>
public enum HeightmapGroundingMode
{
    /// <summary>
    /// Heightmap grounding is disabled.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Heightmap samples update traversal surface state without moving the navigator root.
    /// </summary>
    SurfaceLevelOnly = 1,

    /// <summary>
    /// Heightmap samples update traversal surface state and project the navigator root onto the sampled ground.
    /// </summary>
    SurfaceLevelAndPosition = 2
}
