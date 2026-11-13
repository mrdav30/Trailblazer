//=======================================================================
// NavigationPlanarRegionGuidePayload.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Retains the original region center alongside its selected route.</summary>
internal sealed class NavigationPlanarRegionGuidePayload : NavigationPlanarGuidePayload
{
    internal const long GoalCenterBytes = 2 * sizeof(long);

    internal NavigationPlanarRegionGuidePayload(NavigationAStarGuidePoint[] points, Fixed64 cost,
        NavigationAreaPolicyKey policy, long graphVersion, (long Spawn, uint Version, ulong Sequence) epoch,
        NavigationTransitionInstruction[] transitions, Vector2d goalCenter)
        : base(points, cost, policy, graphVersion, epoch, transitions)
    {
        GoalCenter = goalCenter;
    }

    internal Vector2d GoalCenter { get; }

    internal override long RetainedBytes => base.RetainedBytes + GoalCenterBytes;
}
