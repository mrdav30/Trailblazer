//=======================================================================
// HeightmapWorldState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using SwiftCollections;

namespace Trailblazer.Heightmaps;

/// <summary>
/// Owns mutable heightmap state for one <see cref="TrailblazerWorldContext"/>.
/// </summary>
internal sealed class HeightmapWorldState
{
    internal SwiftDictionary<string, HeightmapLayerRegistration> LayersByName { get; } = new();

    internal int NextRegistrationOrder { get; set; }

    internal void Reset()
    {
        LayersByName.Clear();
        NextRegistrationOrder = 0;
    }
}
