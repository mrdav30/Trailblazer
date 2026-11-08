//=======================================================================
// NavigationAStarPayloadReservationSlot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Stores the generation and capacity charged to an A* payload reservation.</summary>
internal sealed class NavigationAStarPayloadReservationSlot
{
    internal ulong Generation;
    internal long MaximumBytes;
    internal bool IsReserved;
    internal NavigationAStarPayloadReservationSlot? NextPooled;
}
