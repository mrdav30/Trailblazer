//=======================================================================
// NavMotor.ProfileReplacement.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Motor;

/// <content>Stages and applies motor state for locomotion profile replacement.</content>
public partial class NavMotor
{
    internal bool ApplyNavigationCapabilityLoss(
        TraversalCapability previous,
        TraversalCapability replacement,
        ref TrekRequest request)
    {
        TraversalCapability lost = previous & ~replacement;
        if ((lost & TraversalCapability.Jump) != 0)
            request.IsRequestingJump = false;

        if ((lost & TraversalCapability.Fly) != 0)
        {
            request.IsRequestingFlight = false;
            if (FlyModule != null)
                FlyModule.IsFlying = false;
        }

        if ((lost & TraversalCapability.Swim) != 0)
        {
            request.IsRequestingSwim = false;
            if (WaterModule != null)
            {
                WaterModule.RequestedSwimThisTraversal = false;
                WaterModule.IsSwimming = false;
            }
        }

        bool stoppedClimb = false;
        if ((lost & TraversalCapability.Climb) != 0)
        {
            request.IsRequestingClimb = false;
            if (ClimbModule?.IsClimbing == true)
            {
                Handler.ClearTransientState<ClimbLocomotion>();
                stoppedClimb = true;
            }
        }

        return stoppedClimb;
    }

    internal void NotifyNavigationCapabilityLoss(bool stoppedClimb)
    {
        if (stoppedClimb)
            Events.OnStopClimb?.Invoke();
    }
}
