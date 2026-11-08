//=======================================================================
// NavigationEndpointRole.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the directed query endpoint being resolved.</summary>
internal enum NavigationEndpointRole : byte
{
    Start,
    Destination
}
