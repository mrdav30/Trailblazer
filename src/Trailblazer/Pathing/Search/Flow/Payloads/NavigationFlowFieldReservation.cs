//=======================================================================
// NavigationFlowFieldReservation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies capacity reserved for one in-flight flow-field payload.</summary>
internal readonly struct NavigationFlowFieldReservation
{
    internal NavigationFlowFieldReservation(
        NavigationFlowFieldPayloadCache owner,
        int slot,
        ulong generation)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
    }

    internal readonly NavigationFlowFieldPayloadCache? Owner;
    internal readonly int Slot;
    internal readonly ulong Generation;
}
