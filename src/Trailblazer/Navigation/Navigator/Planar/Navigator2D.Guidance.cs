//=======================================================================
// Navigator2D.Guidance.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Owns durable trek intent, scheduled acquisition, and moved-origin validation.</content>
public abstract partial class Navigator2D
{
    private NavigationCellAddress _requestedGoal;
    private NavigationCellAddress _routeGoal;
    private PathAlgorithm _algorithm;
    private bool _allowTransitions;
    private bool _hasIntent;
    private long _lastAcquisitionFrame = -1;
    private NavigationGuideRequest? _guideRequest;
    private Vector2d _queuedOrigin;
    private NavigationValidationInterest? _connectorValidation;
    private bool _requiresConnector;

    /// <summary>Gets the policy used for future guide acquisition.</summary>
    public NavigationAcquisitionMode AcquisitionMode { get; private set; }

    /// <summary>Changes acquisition policy, preserving any adopted guide and its connector obligations.</summary>
    public void ConfigureAcquisitionMode(NavigationAcquisitionMode mode)
    {
        EnsureIdle();
        ChronicleGuard.ThrowIfArgumentOutOfRange(mode is not NavigationAcquisitionMode.Immediate
            and not NavigationAcquisitionMode.Scheduled, (int)mode, nameof(mode));
        if (AcquisitionMode == mode)
            return;
        AcquisitionMode = mode;
        if (_guideRequest != null)
        {
            ReleaseRequest();
            _lastAcquisitionFrame = -1;
        }
    }

    /// <summary>Replaces guidance with a route from the accepted center's cell to the destination.</summary>
    public NavigationGuideStatus ApplyGuidedTrekRequest(NavigationCellAddress destination, NavigationAreaPolicyKey policy,
        PathAlgorithm algorithm, NavigationWorkBudget budget) =>
        ApplyGuidedTrekRequest(destination, policy, algorithm, budget, -1);

    /// <summary>Replaces guidance and optionally joins a movement group or admits authored actions.</summary>
    /// <remarks>The supplied budget bounds acquisition and each separate guided movement proof.</remarks>
    public NavigationGuideStatus ApplyGuidedTrekRequest(NavigationCellAddress destination, NavigationAreaPolicyKey policy,
        PathAlgorithm algorithm, NavigationWorkBudget budget, int groupId = -1, bool allowTransitions = false)
    {
        EnsureIdle();
        if (_binding.Mode != NavigationMode2D.TopDown)
            return NavigationGuideStatus.InvalidProfile;
        policy.Validate(nameof(policy));
        ChronicleGuard.ThrowIfArgumentOutOfRange(groupId < -1, groupId, nameof(groupId));
        BeginTrek(destination, policy, algorithm, budget);
        _allowTransitions = allowTransitions;
        JoinGroup(groupId);
        RefreshGroupTarget();
        return StartAcquisition();
    }

    private void BeginTrek(NavigationCellAddress destination, NavigationAreaPolicyKey policy,
        PathAlgorithm algorithm, NavigationWorkBudget budget)
    {
        ResetGuidance();
        _hasIntent = true;
        _requestedGoal = _routeGoal = destination;
        _policy = policy;
        _budget = budget;
        _algorithm = algorithm;
        _allowTransitions = false;
    }

    private NavigationGuideStatus StartAcquisition()
    {
        _lastAcquisitionFrame = _context.FrameCount;
        var pathing = _context.Pathing;
        var graph = pathing.NavigationGraphStore.Current;
        var world = _context.World;
        var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
        var status = _binding.Mode == NavigationMode2D.SideView
            ? BeginSideViewAcquisition(out var guide) : BeginTopDownAcquisition(out guide);
        status = TrailblazerPathingService.ResolvePlanarSnapshotStatus(status, graph, pathing.NavigationGraphStore.Current,
            epoch, (world.SpawnToken, world.Version, world.ChangeSequence));
        if (status == NavigationGuideStatus.Success)
        {
            if (_resumeSideView)
                status = AdoptPlanarGuide(guide);
            else
                AdoptGuide(guide);
        }
        else if (status != NavigationGuideStatus.Pending)
        {
            guide.Dispose();
            ReleaseRequest();
        }
        return RecordAcquisitionStatus(status);
    }

    private NavigationGuideStatus BeginTopDownAcquisition(out NavigationGuideLease guide)
    {
        guide = default;
        var status = _context.Pathing.ResolveTopDownControllerStart(_mapId, _binding, Pose.Center, NavigationProfile.Radius, _policy,
            _medium, NavigationProfile.Capabilities, _budget, out NavigationCellAddress start, CurrentMapId);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (AcquisitionMode == NavigationAcquisitionMode.Scheduled)
        {
            _queuedOrigin = Pose.Center;
            status = _context.Guides.ScheduleTopDownGuide(start, _routeGoal, NavigationProfile.Radius, _policy, _medium,
                NavigationProfile.Capabilities, _algorithm, _budget, out var request, _allowTransitions);
            _guideRequest = request;
            return status;
        }
        status = _context.Guides.RequestTopDownGuide(start, _routeGoal, NavigationProfile.Radius, _policy, _medium,
            NavigationProfile.Capabilities, _algorithm, _budget, out var acquired, _allowTransitions);
        guide = acquired.RouteLease;
        return status;
    }

    private void AdvanceAcquisition()
    {
        if (!_hasIntent || HasArrived || _heldTransition.HasValue)
            return;
        if (RefreshGroupTarget())
        {
            ReleaseGuide();
            ReleaseRequest();
            StartAcquisition();
        }
        if (_guideRequest is NavigationGuideRequest request)
        {
            NavigationGuideStatus status = request.Status;
            if (status == NavigationGuideStatus.Pending)
                return;
            status = request is NavigationSideViewGuideRequest sideView
                ? AdoptScheduledSideView(sideView) : AdoptScheduledTopDown((NavigationTopDownGuideRequest)request);
            ReleaseRequest();
            RecordAcquisitionStatus(status);
            _lastAcquisitionFrame = _context.FrameCount;
        }
        if (!_guide.HasValue && _lastAcquisitionFrame != _context.FrameCount
            && GuidanceStatus.GetValueOrDefault() is NavigationGuideStatus.Stale or NavigationGuideStatus.CapacityExceeded
                or NavigationGuideStatus.Pending or NavigationGuideStatus.Cancelled)
            StartAcquisition();
    }

    private NavigationGuideStatus AdoptScheduledTopDown(NavigationTopDownGuideRequest request)
    {
        if (!request.TryTakeTopDownGuide(out var guide))
            return FailedScheduledTransfer(request);
        return AdoptPlanarGuide(guide.RouteLease);
    }

    private NavigationGuideStatus AdoptPlanarGuide(NavigationGuideLease guide)
    {
        AdoptGuide(guide);
        var status = NavigationGuideStatus.Success;
        if (Pose.Center != _queuedOrigin)
        {
            _connectorValidation ??= new NavigationValidationInterest();
            _guide = _context.Guides.Scheduler.RegisterPlanarConnectorValidation(_connectorValidation, guide, ref status);
            _requiresConnector = _guide.HasValue;
        }
        return status;
    }

    private static NavigationGuideStatus FailedScheduledTransfer(NavigationGuideRequest request) =>
        request.Status == NavigationGuideStatus.Success ? NavigationGuideStatus.Stale : request.Status;

    private NavigationGuideStatus RecordAcquisitionStatus(NavigationGuideStatus status)
    {
        if ((status is NavigationGuideStatus.NoPath or NavigationGuideStatus.InvalidEnd) && _routeGoal != _requestedGoal)
        {
            _routeGoal = _requestedGoal;
            status = NavigationGuideStatus.Pending;
        }
        GuidanceStatus = status;
        return status;
    }

    private void AdoptGuide(NavigationGuideLease guide)
    {
        _guide = guide;
        GuideCost = guide.TotalCost;
        ArmVisibilityCheck();
    }

    private bool TryValidateMovedOrigin(Vector2d waypoint, string waypointMapId)
    {
        if (!_requiresConnector)
            return true;
        if (!TryConsumeConnectorValidationTurn())
            return false;
        var status = _context.Pathing.ValidateTopDownMotion(_mapId, _binding, Pose.Center, waypoint, NavigationProfile.Radius,
            _policy, _medium, NavigationProfile.Capabilities, _budget, (CurrentMapId, waypointMapId));
        if (status == NavigationGuideStatus.Success)
            return true;
        ReleaseGuide();
        GuidanceStatus = status == NavigationGuideStatus.NoPath ? NavigationGuideStatus.Stale : status;
        return false;
    }

    private bool TryConsumeConnectorValidationTurn()
    {
        if (_context.Guides.Scheduler.TryConsumeValidationTurn(_connectorValidation!))
            return true;
        GuidanceStatus = NavigationGuideStatus.Pending;
        return false;
    }

    private void ReleaseConnectorValidation()
    {
        if (_requiresConnector)
            _context.Guides.Scheduler.ReleaseValidation(_connectorValidation!);
        _requiresConnector = false;
    }

    private void FinishArrival()
    {
        HasArrived = true;
        ReleaseGuide();
        LeaveGroup();
    }

    private void ReleaseRequest()
    {
        _guideRequest?.Dispose();
        _guideRequest = null;
    }

    private void CancelAcquisition()
    {
        ReleaseRequest();
        _hasIntent = false;
        _resumeSideView = false;
        _lastAcquisitionFrame = -1;
    }
}
