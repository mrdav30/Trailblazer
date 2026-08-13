//=======================================================================
// NavigationCompositionUpdateCounters.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports deterministic structural work and persistent record-copy counts.</summary>
internal readonly struct NavigationCompositionUpdateCounters
{
    internal NavigationCompositionUpdateCounters(
        int changedMaps,
        int visitedMaps,
        int visitedEdges,
        int copiedNodeRecords,
        int copiedReverseRecords,
        int copiedComponentRecords,
        int copiedMembershipRecords,
        int reusedComponents)
    {
        ChangedMaps = changedMaps;
        VisitedMaps = visitedMaps;
        VisitedEdges = visitedEdges;
        CopiedNodeRecords = copiedNodeRecords;
        CopiedReverseRecords = copiedReverseRecords;
        CopiedComponentRecords = copiedComponentRecords;
        CopiedMembershipRecords = copiedMembershipRecords;
        ReusedComponents = reusedComponents;
    }

    internal int ChangedMaps { get; }
    internal int VisitedMaps { get; }
    internal int VisitedEdges { get; }
    internal int CopiedNodeRecords { get; }
    internal int CopiedReverseRecords { get; }
    internal int CopiedComponentRecords { get; }
    internal int CopiedMembershipRecords { get; }
    internal int ReusedComponents { get; }
}
