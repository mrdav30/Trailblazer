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
            && _guide!.Value.Status == NavigationGuideStatus.Success
            ? instruction : null;

    /// <summary>Submits a host-owned target for the current held action.</summary>
    public NavigationGuideStatus PrepareActionFrameMotion(in NavigationTransitionInstruction2D instruction, NavigationPose2D target)
    {
        EnsureIdle();
        if (!MatchesHeldAction(instruction))
            return NavigationGuideStatus.Stale;
        var status = ValidateHeldAction();
        if (status != NavigationGuideStatus.Success)
            return status;
        if (!_actionStarted && (Pose.Center != instruction.SourcePosition || !HasActionSupport()))
            return NavigationGuideStatus.InvalidStart;
        ChronicleGuard.ThrowIfArgument(_binding.Mode == NavigationMode2D.SideView && target.RotationRadians != Fixed64.Zero,
            nameof(target), "A side-view action target must be upright.");
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
        // Every committed pose updates the exact landing flag. A support refresh can change grounding between commits.
        if (!_actionLandingAccepted || !HasActionSupport())
        {
            var current = ValidateHeldAction();
            return current == NavigationGuideStatus.Success ? NavigationGuideStatus.InvalidEnd : current;
        }
        // The current lease already certifies the exact landing center in this medium.
        var status = _guide!.Value.CompletePendingTransition(instruction.Instruction);
        if (status == NavigationGuideStatus.Success)
        {
            ClearHeldAction();
            if (_guide.Value.CurrentStepIndex == _guide.Value.StepCount - 1)
                FinishArrival();
        }
        ApplyGuideStatus(status);
        return status;
    }

    private NavigationGuideStatus ValidateHeldAction()
    {
        var status = _guide!.Value.Status;
        if (status == NavigationGuideStatus.Stale)
            ApplyGuideStatus(status);
        return status;
    }

    private bool MatchesHeldAction(in NavigationTransitionInstruction2D instruction) =>
        _heldTransition is NavigationTransitionInstruction2D held && held.Matches(instruction);

    private bool HoldActionIfPresent(NavigationGuideLease guide)
    {
        if (guide.TryGetPendingPlanarTransition(out var instruction) != NavigationGuideStatus.Success)
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
        if (_heldTransition is not NavigationTransitionInstruction2D action)
            return;
        _actionStarted |= _actionFrame;
        _actionLandingAccepted = acceptedLanding && HasActionSupport();
        // A stale lease can still leave the body at a candidate for a fresh anchor proof.
        if (_actionLandingAccepted && _binding.Mode == NavigationMode2D.SideView)
            _sideViewSource = action.DestinationAddress;
    }

    private bool HasActionSupport() => _binding.Mode == NavigationMode2D.TopDown || IsGrounded;

    private void ClearHeldAction()
    {
        _heldTransition = null;
        _actionStarted = false;
        _actionLandingAccepted = false;
    }
}
