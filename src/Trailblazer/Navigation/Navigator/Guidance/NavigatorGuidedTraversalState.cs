//=======================================================================
// NavigatorGuidedTraversalState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

internal static class NavigatorGuidedTraversalState
{
    public static bool ResolveInitialClimbIntent(
        IPathRequest pathRequest,
        GuidedVolumeExitHandoff? pendingVolumeExitHandoff,
        bool? requestedClimb,
        out GuidedClimbIntentMode intentMode)
    {
        if (requestedClimb.HasValue)
        {
            intentMode = GuidedClimbIntentMode.Explicit;
            return requestedClimb.Value;
        }

        intentMode = GuidedClimbIntentMode.Auto;
        return GuidedClimbIntentResolver.Resolve(pathRequest, pendingVolumeExitHandoff);
    }

    public static void PrepareFrame(
        bool isGuided,
        ref TrekRequest frameRequest,
        NavSteering? steering,
        GuidedVolumeExitHandoff? pendingVolumeExitHandoff,
        ref bool climbIntent,
        ref GuidedClimbIntentMode climbIntentMode,
        ref int lastSeenRouteTopologyVersion)
    {
        if (!isGuided)
            return;

        if (TryClearInactiveClimbIntent(
            ref frameRequest,
            steering,
            pendingVolumeExitHandoff,
            ref climbIntent,
            ref climbIntentMode,
            ref lastSeenRouteTopologyVersion))
        {
            return;
        }

        frameRequest.IsRequestingClimb = climbIntent;
    }

    public static void SyncFromSteering(
        bool isGuided,
        ref TrekRequest frameRequest,
        NavSteering? steering,
        GuidedVolumeExitHandoff? pendingVolumeExitHandoff,
        bool activatedVolumeExitHandoff,
        bool handoffRequestedClimb,
        ref bool climbIntent,
        ref GuidedClimbIntentMode climbIntentMode,
        ref int lastSeenRouteTopologyVersion)
    {
        if (!isGuided)
            return;

        if (TryClearInactiveClimbIntent(
            ref frameRequest,
            steering,
            pendingVolumeExitHandoff,
            ref climbIntent,
            ref climbIntentMode,
            ref lastSeenRouteTopologyVersion))
        {
            return;
        }

        if (climbIntentMode == GuidedClimbIntentMode.Auto
            && steering != null
            && steering.CurrentRouteTopologyVersion != lastSeenRouteTopologyVersion)
        {
            bool resolvedRouteRequestsClimb = steering.CurrentRouteRequestsClimbIntent;
            bool shouldDeferHandoffBootstrapClear =
                activatedVolumeExitHandoff
                && handoffRequestedClimb
                && !resolvedRouteRequestsClimb;
            if (!shouldDeferHandoffBootstrapClear)
            {
                climbIntent = resolvedRouteRequestsClimb;
                lastSeenRouteTopologyVersion = steering.CurrentRouteTopologyVersion;
            }
        }

        frameRequest.IsRequestingClimb = climbIntent;
    }

    public static bool TryActivatePendingVolumeExitHandoff(
        bool isGuided,
        TrailblazerWorldContext context,
        Vector3d currentFootPosition,
        NavigationAgentProfile profile,
        ref TrekRequest frameRequest,
        NavSteering? steering,
        ref GuidedVolumeExitHandoff? pendingVolumeExitHandoff,
        ref bool climbIntent,
        GuidedClimbIntentMode climbIntentMode,
        ref int lastSeenRouteTopologyVersion,
        out bool handoffRequestedClimb)
    {
        handoffRequestedClimb = false;
        if (!isGuided
            || pendingVolumeExitHandoff == null
            || steering == null
            || steering.ShouldMove
            || steering.CurrentRequest != null
            || steering.CurrentQuery.HasValue)
        {
            return false;
        }

        if (!pendingVolumeExitHandoff.TryCreateFollowupRequest(
                context,
                currentFootPosition,
                profile,
                out IPathRequest? followupRequest)
            || followupRequest == null)
        {
            return false;
        }

        GuidedVolumeExitHandoff handoff = pendingVolumeExitHandoff;
        pendingVolumeExitHandoff = null;

        steering.ApplyPathRequest(followupRequest, handoff.MovementGroupId);
        CaptureRouteTopologyVersion(steering, ref lastSeenRouteTopologyVersion);
        frameRequest.IsRequestingFlight = false;
        frameRequest.IsRequestingSwim = false;
        handoffRequestedClimb = handoff.IsRequestingClimb;
        if (climbIntentMode == GuidedClimbIntentMode.Auto)
            climbIntent = handoffRequestedClimb;

        frameRequest.IsRequestingClimb = climbIntent;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetClimbIntent(
        ref TrekRequest frameRequest,
        bool status,
        GuidedClimbIntentMode mode,
        ref bool climbIntent,
        ref GuidedClimbIntentMode climbIntentMode)
    {
        climbIntent = status;
        climbIntentMode = mode;
        frameRequest.IsRequestingClimb = status;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ResetClimbIntent(
        ref bool climbIntent,
        ref GuidedClimbIntentMode climbIntentMode,
        ref int lastSeenRouteTopologyVersion)
    {
        climbIntent = false;
        climbIntentMode = GuidedClimbIntentMode.Auto;
        lastSeenRouteTopologyVersion = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CaptureRouteTopologyVersion(
        NavSteering? steering,
        ref int lastSeenRouteTopologyVersion)
    {
        lastSeenRouteTopologyVersion = steering?.CurrentRouteTopologyVersion ?? 0;
    }

    private static bool TryClearInactiveClimbIntent(
        ref TrekRequest frameRequest,
        NavSteering? steering,
        GuidedVolumeExitHandoff? pendingVolumeExitHandoff,
        ref bool climbIntent,
        ref GuidedClimbIntentMode climbIntentMode,
        ref int lastSeenRouteTopologyVersion)
    {
        if (steering?.CurrentRequest != null
            || steering?.CurrentQuery.HasValue == true
            || pendingVolumeExitHandoff != null)
        {
            return false;
        }

        ResetClimbIntent(ref climbIntent, ref climbIntentMode, ref lastSeenRouteTopologyVersion);
        frameRequest.IsRequestingClimb = false;
        return true;
    }
}
