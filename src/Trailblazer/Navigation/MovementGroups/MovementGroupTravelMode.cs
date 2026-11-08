//=======================================================================
// MovementGroupTravelMode.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Navigation.MovementGroups;

/// <summary>Identifies whether navigation proceeds individually or in formation.</summary>
internal enum MovementGroupTravelMode : byte
{
    None,
    Individual,
    Formation,
    GroupIndividual
}
