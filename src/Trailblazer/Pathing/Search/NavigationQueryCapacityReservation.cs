//=======================================================================
// NavigationQueryCapacityReservation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies one generation-safe aggregate query-capacity reservation.</summary>
internal readonly struct NavigationQueryCapacityReservation
{
    internal NavigationQueryCapacityReservation(
        NavigationQueryAdmissionCoordinator owner,
        int lane,
        ulong generation,
        int count)
    {
        Owner = owner;
        Lane = lane;
        Generation = generation;
        Count = count;
    }

    internal NavigationQueryAdmissionCoordinator? Owner { get; }
    internal int Lane { get; }
    internal ulong Generation { get; }
    internal int Count { get; }
}
