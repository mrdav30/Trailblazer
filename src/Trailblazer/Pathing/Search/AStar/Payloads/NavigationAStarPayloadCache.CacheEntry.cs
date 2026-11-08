//=======================================================================
// NavigationAStarPayloadCache.CacheEntry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the CacheEntry companion type.</content>
internal sealed partial class NavigationAStarPayloadCache
{
    /// <summary>Stores one cached A* payload and its lease accounting.</summary>
    internal sealed class CacheEntry
    {
        internal CacheEntry(NavigationAStarPayload payload, bool isCached)
        {
            Payload = payload;
            IsCached = isCached;
        }

        internal NavigationAStarPayload Payload { get; }

        internal int LeaseCount { get; set; }

        internal bool IsCached { get; set; }
    }
}
