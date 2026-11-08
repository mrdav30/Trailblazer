//=======================================================================
// NavigationAStarPayloadReservation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies capacity reserved for one in-flight A* payload.</summary>
internal struct NavigationAStarPayloadReservation
{
    internal NavigationAStarPayloadReservation(
        NavigationAStarPayloadCache owner,
        NavigationAStarPayloadReservationSlot slot,
        ulong generation,
        long maximumBytes)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
        MaximumBytes = maximumBytes;
    }

    internal readonly NavigationAStarPayloadCache? Owner;
    internal readonly NavigationAStarPayloadReservationSlot? Slot;
    internal readonly ulong Generation;
    internal readonly long MaximumBytes;
    internal readonly bool HasLeaseSlot => Owner != null;
}
