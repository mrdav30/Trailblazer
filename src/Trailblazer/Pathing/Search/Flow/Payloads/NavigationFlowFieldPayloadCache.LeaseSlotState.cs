//=======================================================================
// NavigationFlowFieldPayloadCache.LeaseSlotState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the LeaseSlotState companion type.</content>
internal sealed partial class NavigationFlowFieldPayloadCache
{
    /// <summary>Identifies the lifecycle state of a flow-field payload lease slot.</summary>
    internal enum LeaseSlotState : byte
    {
        Free = 0,
        Reserved = 1,
        ActivePayload = 2,
        ActiveGuide = 3,
        Retired = 4
    }
}
