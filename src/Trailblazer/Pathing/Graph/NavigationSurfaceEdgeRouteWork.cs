//=======================================================================
// NavigationSurfaceEdgeRouteWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Reports resumable semantic and swept-geometry edge progress.</summary>
internal enum NavigationSurfaceEdgeRouteStatus : byte
{
    Pending = 0,
    Point = 1,
    Passable = 2,
    Impassable = 3,
    BudgetExceeded = 4,
    CostOverflow = 5
}

/// <summary>Evaluates and expands one exact authored surface edge without allocation.</summary>
internal struct NavigationSurfaceEdgeRouteWork
{
    private TraversalEvaluator _evaluator;
    private NavigationGraphEdge _edge;
    private NavigationNodeRef _source;
    private TraversalExplicitEdgeWork _explicitWork;
    private GridNavigationPortal _incomingPortal;
    private Vector3d _incomingTargetAnchor;
    private NavigationNodeRef _dependencyNode;
    private Fixed64 _cost;
    private NavigationCellAddress _pointAddress0;
    private NavigationCellAddress _pointAddress1;
    private Vector3d _pointPosition0;
    private Vector3d _pointPosition1;
    private Vector3d _pointPosition2;
    private Vector3d _pointPosition3;
    private Vector3d _pointPosition4;
    private NavigationSurfaceEdgeRouteStatus _completionStatus;
    private int _portalOrdinal;
    private int _pointOrdinal;
    private int _pointCount;
    private int _secondPointAddressOrdinal;
    private int _targetPointOrdinal;
    private bool _emitPoints;
    private bool _explicit;
    private bool _hasDependencyNode;

    internal NavigationSurfaceEdgeRouteStatus Begin(
        TraversalEvaluator evaluator,
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        bool emitPoints)
    {
        Reset();
        _evaluator = evaluator;
        _source = source;
        _edge = edge;
        _emitPoints = emitPoints;
        _completionStatus = NavigationSurfaceEdgeRouteStatus.Pending;
        if (edge.Kind != NavigationGraphEdgeKind.Explicit)
            return _completionStatus;

        TraversalExplicitEdgeStatus status = evaluator.BeginExplicitEdge(
            source,
            edge,
            out _explicitWork);
        if (status != TraversalExplicitEdgeStatus.Pending)
            return Complete(NavigationSurfaceEdgeRouteStatus.Impassable);
        _explicit = true;
        return _completionStatus;
    }

    internal NavigationSurfaceEdgeRouteStatus Advance(
        NavigationWorkMeter meter,
        ref int connectionStepRemaining)
    {
        if (_pointOrdinal < _pointCount)
            return NavigationSurfaceEdgeRouteStatus.Point;
        if (_completionStatus != NavigationSurfaceEdgeRouteStatus.Pending)
            return _completionStatus;
        return _explicit
            ? AdvanceExplicit(meter, ref connectionStepRemaining)
            : AdvanceSimple();
    }

    internal Fixed64 Cost => _cost;

    internal bool HasCurrentPoint => _pointOrdinal < _pointCount;

    internal NavigationAStarGuidePoint CurrentPoint => new(
        _pointOrdinal < _secondPointAddressOrdinal
            ? _pointAddress0
            : _pointAddress1,
        _pointOrdinal switch
        {
            0 => _pointPosition0,
            1 => _pointPosition1,
            2 => _pointPosition2,
            3 => _pointPosition3,
            _ => _pointPosition4
        },
        TraversalMedium.Solid);

    internal bool CurrentPointIsTargetFootAnchor =>
        _pointOrdinal == _targetPointOrdinal;

    internal void ConsumePoint()
    {
        System.Diagnostics.Debug.Assert(HasCurrentPoint);
        _pointOrdinal++;
    }

    internal bool TryTakeDependencyNode(out NavigationNodeRef node)
    {
        node = _dependencyNode;
        if (!_hasDependencyNode)
            return false;
        _dependencyNode = default;
        _hasDependencyNode = false;
        return true;
    }

    internal void Reset()
    {
        _evaluator = default;
        _edge = default;
        _source = default;
        _explicitWork = default;
        _incomingPortal = default;
        _incomingTargetAnchor = default;
        _dependencyNode = default;
        _cost = default;
        _pointAddress0 = default;
        _pointAddress1 = default;
        _pointPosition0 = default;
        _pointPosition1 = default;
        _pointPosition2 = default;
        _pointPosition3 = default;
        _pointPosition4 = default;
        _completionStatus = default;
        _portalOrdinal = 0;
        _pointOrdinal = 0;
        _pointCount = 0;
        _secondPointAddressOrdinal = 0;
        _targetPointOrdinal = -1;
        _emitPoints = false;
        _explicit = false;
        _hasDependencyNode = false;
    }

    private NavigationSurfaceEdgeRouteStatus AdvanceSimple()
    {
        TraversalEvaluationStatus evaluation = _evaluator.EvaluateEdge(
            _source,
            _edge,
            out TraversalEdgeEvidence evidence);
        if (evaluation == TraversalEvaluationStatus.CostOverflow)
            return Complete(NavigationSurfaceEdgeRouteStatus.CostOverflow);
        if (evaluation != TraversalEvaluationStatus.Passable)
            return Complete(NavigationSurfaceEdgeRouteStatus.Impassable);
        bool validSourceSegment = GridCellGeometry.IsNavigationBodySegmentValid(
                evidence.SourcePrism,
                evidence.SourceFootAnchor,
                evidence.SourcePortalAnchor,
                _evaluator.Profile.Shape.Radius,
                _evaluator.Profile.Shape.Height,
                default,
                evidence.Portal,
                GridNavigationBodySegmentEndpointAllowance.None);
        bool validTargetSegment = GridCellGeometry.IsNavigationBodySegmentValid(
                evidence.TargetPrism,
                evidence.TargetPortalAnchor,
                evidence.TargetFootAnchor,
                _evaluator.Profile.Shape.Radius,
                _evaluator.Profile.Shape.Height,
                evidence.Portal,
                default,
                GridNavigationBodySegmentEndpointAllowance.None);
        System.Diagnostics.Debug.Assert(
            evidence.Portal.FaceKind != VoxelContactFaceKind.Horizontal
            || (GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                    evidence.SourcePrism,
                    evidence.TargetPrism,
                    evidence.Portal,
                    evidence.SourcePortalAnchor,
                    evidence.TargetPortalAnchor,
                    _evaluator.Profile.Shape.Radius,
                    _evaluator.Profile.Shape.Height,
                    out Fixed64 sourceParameter,
                    out Fixed64 targetParameter)
                && sourceParameter == Fixed64.Zero
                && targetParameter == Fixed64.One),
            "published surface portals retain the exact validated profile crossing");
        System.Diagnostics.Debug.Assert(validSourceSegment && validTargetSegment);

        _cost = evidence.Cost;
        SetPoints(
            evidence.SourceAddress,
            evidence.TargetAddress,
            evidence.SourcePortalAnchor,
            evidence.TargetPortalAnchor,
            evidence.TargetFootAnchor);
        _targetPointOrdinal = 2;
        _completionStatus = NavigationSurfaceEdgeRouteStatus.Passable;
        return _pointCount == 0
            ? _completionStatus
            : NavigationSurfaceEdgeRouteStatus.Point;
    }

    private NavigationSurfaceEdgeRouteStatus AdvanceExplicit(
        NavigationWorkMeter meter,
        ref int connectionStepRemaining)
    {
        if (connectionStepRemaining == 0)
        {
            return meter.RemainingConnectionLegs == 0
                ? Complete(NavigationSurfaceEdgeRouteStatus.BudgetExceeded)
                : NavigationSurfaceEdgeRouteStatus.Pending;
        }
        if (!meter.TryConsumeConnectionLegs(1))
            return Complete(NavigationSurfaceEdgeRouteStatus.BudgetExceeded);
        connectionStepRemaining--;

        TraversalExplicitEdgeStatus semantic = _evaluator.AdvanceExplicitEdge(
            ref _explicitWork,
            out TraversalEdgeEvidence evidence);
        NavigationNodeRef dependencyNode = evidence.DependencyNode;
        System.Diagnostics.Debug.Assert(
            dependencyNode.IsValid,
            "every published explicit corridor leg resolves an immutable graph node");
        _dependencyNode = dependencyNode;
        _hasDependencyNode = true;
        if (semantic == TraversalExplicitEdgeStatus.CostOverflow)
            return Complete(NavigationSurfaceEdgeRouteStatus.CostOverflow);
        if (semantic == TraversalExplicitEdgeStatus.Impassable)
            return Complete(NavigationSurfaceEdgeRouteStatus.Impassable);

        NavigationExplicitConnectionRecord record = _edge.ExplicitConnection;
        NavigationConnection connection = record.Definition;
        GridNavigationPortal outgoing = evidence.Portal;
        Vector3d sourceAnchor = evidence.SourcePortalAnchor;
        Vector3d targetAnchor = evidence.TargetPortalAnchor;

        if (_portalOrdinal == 0)
        {
            if (!GridCellGeometry.IsNavigationBodySegmentValid(
                    evidence.SourcePrism,
                    evidence.SourceFootAnchor,
                    connection.EntryAnchor,
                    _evaluator.Profile.Shape.Radius,
                    _evaluator.Profile.Shape.Height,
                    default,
                    outgoing,
                    GridNavigationBodySegmentEndpointAllowance.None)
                || !GridCellGeometry.IsNavigationBodySegmentValid(
                    evidence.SourcePrism,
                    connection.EntryAnchor,
                    sourceAnchor,
                    _evaluator.Profile.Shape.Radius,
                    _evaluator.Profile.Shape.Height,
                    default,
                    outgoing,
                    GridNavigationBodySegmentEndpointAllowance.None))
            {
                return Complete(NavigationSurfaceEdgeRouteStatus.Impassable);
            }
            SetPoints(
                evidence.SourceAddress,
                connection.EntryAnchor,
                sourceAnchor);
        }
        else
        {
            if (!GridCellGeometry.IsNavigationBodySegmentValid(
                    evidence.SourcePrism,
                    _incomingTargetAnchor,
                    sourceAnchor,
                    _evaluator.Profile.Shape.Radius,
                    _evaluator.Profile.Shape.Height,
                    _incomingPortal,
                    outgoing,
                    GridNavigationBodySegmentEndpointAllowance.None))
            {
                return Complete(NavigationSurfaceEdgeRouteStatus.Impassable);
            }
            SetPoints(
                evidence.SourceAddress,
                _incomingTargetAnchor,
                sourceAnchor);
        }

        _portalOrdinal++;
        _incomingPortal = outgoing;
        _incomingTargetAnchor = targetAnchor;
        if (semantic == TraversalExplicitEdgeStatus.Passable)
        {
            if (!GridCellGeometry.IsNavigationBodySegmentValid(
                    evidence.TargetPrism,
                    targetAnchor,
                    connection.ExitAnchor,
                    _evaluator.Profile.Shape.Radius,
                    _evaluator.Profile.Shape.Height,
                    outgoing,
                    default,
                    GridNavigationBodySegmentEndpointAllowance.None)
                || !GridCellGeometry.IsNavigationBodySegmentValid(
                    evidence.TargetPrism,
                    connection.ExitAnchor,
                    evidence.TargetFootAnchor,
                    _evaluator.Profile.Shape.Radius,
                    _evaluator.Profile.Shape.Height,
                    outgoing,
                    default,
                    GridNavigationBodySegmentEndpointAllowance.None))
            {
                return Complete(NavigationSurfaceEdgeRouteStatus.Impassable);
            }
            AppendPoints(
                evidence.TargetAddress,
                targetAnchor,
                connection.ExitAnchor,
                evidence.TargetFootAnchor);
            _targetPointOrdinal = _pointCount - 1;
            _cost = evidence.Cost;
            _completionStatus = NavigationSurfaceEdgeRouteStatus.Passable;
        }
        return _pointCount == 0
            ? _completionStatus
            : NavigationSurfaceEdgeRouteStatus.Point;
    }

    private NavigationSurfaceEdgeRouteStatus Complete(
        NavigationSurfaceEdgeRouteStatus status)
    {
        _completionStatus = status;
        return status;
    }

    private void SetPoints(
        NavigationCellAddress address,
        Vector3d first,
        Vector3d second)
    {
        if (!_emitPoints)
            return;
        _pointAddress0 = address;
        _pointAddress1 = default;
        _pointPosition0 = first;
        _pointPosition1 = second;
        _pointPosition2 = default;
        _pointPosition3 = default;
        _pointPosition4 = default;
        _pointOrdinal = 0;
        _pointCount = 2;
        _secondPointAddressOrdinal = 2;
        _targetPointOrdinal = -1;
    }

    private void SetPoints(
        NavigationCellAddress firstAddress,
        NavigationCellAddress secondAddress,
        Vector3d first,
        Vector3d second,
        Vector3d third)
    {
        if (!_emitPoints)
            return;
        _pointAddress0 = firstAddress;
        _pointAddress1 = secondAddress;
        _pointPosition0 = first;
        _pointPosition1 = second;
        _pointPosition2 = third;
        _pointPosition3 = default;
        _pointPosition4 = default;
        _pointOrdinal = 0;
        _pointCount = 3;
        _secondPointAddressOrdinal = 1;
        _targetPointOrdinal = -1;
    }

    private void AppendPoints(
        NavigationCellAddress address,
        Vector3d first,
        Vector3d second,
        Vector3d third)
    {
        if (!_emitPoints)
            return;
        _pointAddress1 = address;
        _pointPosition2 = first;
        _pointPosition3 = second;
        _pointPosition4 = third;
        _pointCount = 5;
        _secondPointAddressOrdinal = 2;
    }
}
