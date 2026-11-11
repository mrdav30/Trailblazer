//=======================================================================
// NavigationPlanarGuidePayload.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Retains a selected planar route without pinning its graph generation.</summary>
internal sealed class NavigationPlanarGuidePayload : NavigationAStarPayload
{
    // Graph version, world spawn token and aligned world version.
    private const long SnapshotBytes = 24;
    private readonly long _graphVersion;
    private readonly long _spawn;
    private readonly uint _worldVersion;

    internal NavigationPlanarGuidePayload(NavigationAStarGuidePoint[] points, Fixed64 cost,
        NavigationAreaPolicyKey policy, long graphVersion, (long Spawn, uint Version, ulong Sequence) epoch,
        NavigationTransitionInstruction[]? transitions = null)
        : base(default, points, transitions ?? Array.Empty<NavigationTransitionInstruction>(), cost,
            new GraphDependencyStamp(policy, Array.Empty<GraphComponentDependency>(), Array.Empty<GraphPageDependency>()),
            epoch.Sequence, NavigationSurfaceAStarStatus.Success)
    {
        _graphVersion = graphVersion;
        _spawn = epoch.Spawn;
        _worldVersion = epoch.Version;
    }

    internal override long RetainedBytes => MeasureBytes(GuidePoints.Length, TransitionInstructions.Length);

    internal static long MeasureBytes(int points, int transitions = 0) =>
        GetMaximumRetainedBytes(points, transitions, 0, 0) + SnapshotBytes;

    internal bool IsCurrent(GridWorld world, NavigationWorldGraphStore store) =>
        (_spawn, _worldVersion, WorldChangeSequence.GetValueOrDefault()).Equals(
            (world.SpawnToken, world.Version, world.ChangeSequence))
        && _graphVersion == store.Current.GraphVersion;
}
