//=======================================================================
// NavigationCellLookupKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the measured baked-cell lookup representation.</summary>
public enum NavigationCellLookupKind : byte
{
    /// <summary>Canonical baked entries are searched without address-volume storage.</summary>
    Sorted = 0,
    /// <summary>A direct address table is cheaper at the authored navigation density.</summary>
    Direct = 1
}
