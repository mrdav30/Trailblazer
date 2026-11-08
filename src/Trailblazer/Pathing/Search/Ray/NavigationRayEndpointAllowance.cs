//=======================================================================
// NavigationRayEndpointAllowance.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies which ray endpoints can use their endpoint-specific admission rules.</summary>
internal enum NavigationRayEndpointAllowance : byte
{
    None,
    StartPrefix,
    DestinationSuffix
}
