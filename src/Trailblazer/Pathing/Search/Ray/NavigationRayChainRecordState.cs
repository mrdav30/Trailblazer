//=======================================================================
// NavigationRayChainRecordState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the discovery state of a navigation ray chain record.</summary>
internal enum NavigationRayChainRecordState : byte
{
    Unavailable,
    Unreached,
    Ready,
    Expanded
}
