//=======================================================================
// Navigator2D.Actions.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Holds a lease-issued action until the host commits its accepted landing.</content>
public abstract partial class Navigator2D
{
    private NavigationTransitionInstruction2D? _heldTransition;
    private bool _actionFrame;
    private bool _actionStarted;
    private bool _actionLandingAccepted;

    /// <summary>Gets the exact current action stamp after its source center has been accepted.</summary>
    public NavigationTransitionInstruction2D? PendingTransition =>
        _heldTransition is NavigationTransitionInstruction2D instruction
            && ValidateCurrentAction(instruction) == NavigationGuideStatus.Success
            ? instruction : null;

    /// <summary>Submits a host-owned target for the current held action.</summary>
    public NavigationGuideStatus PrepareActionFrameMotion(in NavigationTransitionInstruction2D instruction, NavigationPose2D target)
    {
        EnsureIdle();
        var status = ValidateCurrentAction(instruction);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (!_actionStarted && Pose.Center != instruction.SourcePosition)
            return NavigationGuideStatus.InvalidStart;
        BeginPrepareFrame();
        try
        {
            SubmitPreparedTarget(target, isAction: true);
            return NavigationGuideStatus.Success;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Completes the exact held action after an accepted, admissible destination landing.</summary>
    public NavigationGuideStatus CompletePendingTransition(in NavigationTransitionInstruction2D instruction)
    {
        EnsureIdle();
        if (!MatchesHeldAction(instruction))
            return NavigationGuideStatus.Stale;
        if (!_actionLandingAccepted || Pose.Center != instruction.DestinationPosition)
        {
            var current = _guide!.Value.Status;
            return current == NavigationGuideStatus.Success ? NavigationGuideStatus.InvalidEnd : current;
        }
        // The current lease already certifies the exact landing center in this medium.
        var status = _guide!.Value.CompletePendingTransition(instruction);
        if (status == NavigationGuideStatus.Success)
        {
            ClearHeldAction();
            if (_guide.Value.CurrentStepIndex == _guide.Value.StepCount - 1)
                FinishArrival();
        }
        ApplyGuideStatus(status);
        return status;
    }

    private NavigationGuideStatus ValidateCurrentAction(in NavigationTransitionInstruction2D instruction) =>
        MatchesHeldAction(instruction) ? _guide!.Value.Status : NavigationGuideStatus.Stale;

    private bool MatchesHeldAction(in NavigationTransitionInstruction2D instruction) =>
        _heldTransition is NavigationTransitionInstruction2D held && held.Matches(instruction);

    private bool HoldActionIfPresent(NavigationTopDownGuideLease guide)
    {
        if (guide.TryGetPendingTransition(out var instruction) != NavigationGuideStatus.Success)
            return false;
        _heldTransition = instruction;
        ReleaseConnectorValidation();
        GuidanceStatus = NavigationGuideStatus.Pending;
        return true;
    }

    private string? GetAcceptedActionMap(Vector2d accepted) =>
        _heldTransition is NavigationTransitionInstruction2D instruction
        && (_actionFrame || _actionLandingAccepted)
        && accepted == instruction.DestinationPosition
            ? instruction.DestinationAddress.MapId : null;

    private void CommitActionMotion(bool acceptedLanding)
    {
        if (!_heldTransition.HasValue)
            return;
        _actionStarted |= _actionFrame;
        _actionLandingAccepted = acceptedLanding;
    }

    private void ClearHeldAction()
    {
        _heldTransition = null;
        _actionStarted = false;
        _actionLandingAccepted = false;
    }
}
