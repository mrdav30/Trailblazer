//=======================================================================
// GraphPageDependency.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one map-local semantic and physical page generation.</summary>
internal readonly struct GraphPageDependency : IEquatable<GraphPageDependency>
{
    internal GraphPageDependency(
        string mapId,
        long bakeVersion,
        long dynamicSlotGeneration,
        int pageIndex,
        long semanticVersion,
        long physicalVersion,
        long transitionVersion = 0)
    {
        MapId = mapId;
        BakeVersion = bakeVersion;
        DynamicSlotGeneration = dynamicSlotGeneration;
        PageIndex = pageIndex;
        SemanticVersion = semanticVersion;
        PhysicalVersion = physicalVersion;
        TransitionVersion = transitionVersion;
    }

    internal string MapId { get; }
    internal long BakeVersion { get; }
    internal long DynamicSlotGeneration { get; }
    internal int PageIndex { get; }
    internal long SemanticVersion { get; }
    internal long PhysicalVersion { get; }
    internal long TransitionVersion { get; }

    public bool Equals(GraphPageDependency other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && BakeVersion == other.BakeVersion
        && DynamicSlotGeneration == other.DynamicSlotGeneration
        && PageIndex == other.PageIndex
        && SemanticVersion == other.SemanticVersion
        && PhysicalVersion == other.PhysicalVersion
        && TransitionVersion == other.TransitionVersion;

    public override int GetHashCode()
    {
        int mapHash = MapId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(MapId);
        int hash = SwiftHashTools.CombineHashCodes(mapHash, BakeVersion.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, DynamicSlotGeneration.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, PageIndex);
        hash = SwiftHashTools.CombineHashCodes(hash, SemanticVersion.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, PhysicalVersion.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, TransitionVersion.GetHashCode());
    }
}
