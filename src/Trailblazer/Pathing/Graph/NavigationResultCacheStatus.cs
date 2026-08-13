//=======================================================================
// NavigationResultCacheStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Describes one bounded result-entry lifecycle transition.</summary>
internal enum NavigationResultCacheStatus : byte
{
    Detached,
    Published,
    ReusedExisting,
    Stale,
    CapacityExceeded,
    Disposed
}
