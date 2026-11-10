//=======================================================================
// Navigator2D.Visibility.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Geometry;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Checks the next ordinary leg on an optional fixed visibility phase.</content>
public abstract partial class Navigator2D
{
    private int _visibilityInterval;
    private int? _visibilityPhase;
    private int _visibilityDelay;
    private long _visibilityArmedFrame;
    private Vector2d? _preparedVisibleAnchor;

    /// <summary>Sets an optional fixed-frame phase for looking ahead from an already accepted route center.</summary>
    /// <remarks>A null phase disables lookahead. Lookahead only follows the next certified ordinary leg.</remarks>
    public void ConfigurePathRechecks(int intervalFrames, int? phase = null)
    {
        EnsureIdle();
        NavigationFramePhase.ValidateConfiguration(intervalFrames, phase);
        if (_visibilityInterval == intervalFrames && _visibilityPhase == phase)
            return;
        _visibilityInterval = intervalFrames;
        _visibilityPhase = phase;
        ArmVisibilityCheck();
    }

    private void ArmVisibilityCheck()
    {
        if (_visibilityPhase is not int phase)
            return;
        _visibilityArmedFrame = _context.FrameCount;
        _visibilityDelay = NavigationFramePhase.NextDelay(_visibilityArmedFrame, _visibilityInterval, phase);
    }

    private bool TrySelectVisibleLeg(NavigationTopDownGuideLease guide, out NavigationCellAddress address, out Vector2d center)
    {
        address = default;
        center = default;
        long frame = _context.FrameCount;
        if (_visibilityPhase is not int phase || frame - _visibilityArmedFrame < _visibilityDelay
            || frame % _visibilityInterval != phase)
            return false;
        ArmVisibilityCheck();
        return !_requiresConnector && guide.TryGetNextOrdinaryStep(out address, out center);
    }

    private bool CommitVisibleLeg(NavigationTopDownGuideLease guide, bool acceptedRequestedTarget,
        Vector2d previousCenter, Vector2d requestedCenter, ref bool reachedTarget)
    {
        if (_preparedVisibleAnchor is not Vector2d next || Pose.Center == previousCenter)
            return true;
        // Only a clipped point on the proved segment can retire the source waypoint.
        if (!acceptedRequestedTarget
            && !new FixedSegment2d(previousCenter, requestedCenter)
                .TryGetUniqueIntersection(new FixedSegment2d(Pose.Center, Pose.Center), out _))
            return true;
        NavigationGuideStatus status = guide.TryAdvanceStep();
        if (status != NavigationGuideStatus.Success)
        {
            ApplyGuideStatus(status);
            return false;
        }
        reachedTarget = acceptedRequestedTarget && Pose.Center == next;
        return true;
    }
}
