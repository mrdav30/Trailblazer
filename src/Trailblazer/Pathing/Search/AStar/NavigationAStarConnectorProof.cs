//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>One lease's immutable addressed current-foot proof; never shared with its route.</summary>
internal sealed class NavigationAStarConnectorProof
{
    // Aligned logical storage: object header, stamp/policy references, two vectors,
    // addressed anchor, medium, epoch, guide reference/required flag, and the
    // scheduler interest's generation-checked guide handle (no additional lease).
    internal const long MetadataBytes = 152;

    internal NavigationAStarConnectorProof(GraphDependencyStamp dependencies, NavigationAreaPolicy policy,
        NavigationGuideStep anchor, Vector3d foot, ulong worldSequence)
    {
        Dependencies = dependencies;
        Policy = policy;
        Anchor = anchor.Address;
        Position = anchor.Position;
        Medium = anchor.Medium;
        Foot = foot;
        WorldSequence = worldSequence;
    }

    internal GraphDependencyStamp Dependencies { get; }
    internal NavigationAreaPolicy Policy { get; }
    internal NavigationCellAddress Anchor { get; }
    internal Vector3d Position { get; }
    internal TraversalMedium Medium { get; }
    internal Vector3d Foot { get; }
    internal ulong WorldSequence { get; }
    internal long RetainedBytes => MetadataBytes + Dependencies.RetainedBytes + Policy.RetainedBytes;

    internal bool IsCurrent(GridWorld world, NavigationWorldGraphStore store) =>
        world.ChangeSequence == WorldSequence
        && store.Current.IsDependencyCurrent(Dependencies)
        && store.Current.AreaCatalog.TryGet(Policy.Key, out NavigationAreaPolicy currentPolicy)
        && currentPolicy.ContentEquals(Policy)
        && world.ChangeSequence == WorldSequence;
}
