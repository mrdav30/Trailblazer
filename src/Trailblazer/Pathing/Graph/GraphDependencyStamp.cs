//=======================================================================
// GraphDependencyStamp.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Captures exact immutable component and page dependencies without a global graph version.</summary>
internal sealed class GraphDependencyStamp
{
    internal GraphDependencyStamp(
        NavigationAreaPolicyKey areaPolicy,
        GraphComponentDependency[] components,
        GraphPageDependency[] pages)
    {
        AreaPolicy = areaPolicy;
        Components = components;
        Pages = pages;
    }

    internal NavigationAreaPolicyKey AreaPolicy { get; }

    internal GraphComponentDependency[] Components { get; }

    internal GraphPageDependency[] Pages { get; }
}

/// <summary>Identifies one structural component generation.</summary>
internal readonly struct GraphComponentDependency : IEquatable<GraphComponentDependency>
{
    internal GraphComponentDependency(string representativeMapId, long version)
    {
        RepresentativeMapId = representativeMapId;
        Version = version;
    }

    internal string RepresentativeMapId { get; }

    internal long Version { get; }

    public bool Equals(GraphComponentDependency other) =>
        string.Equals(RepresentativeMapId, other.RepresentativeMapId, StringComparison.Ordinal)
        && Version == other.Version;

    public override bool Equals(object? obj) => obj is GraphComponentDependency other && Equals(other);

    public override int GetHashCode()
    {
        int mapHash = RepresentativeMapId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(RepresentativeMapId);
        return SwiftHashTools.CombineHashCodes(mapHash, Version.GetHashCode());
    }
}

/// <summary>Names one exact map-local page whose versions a query consumed.</summary>
internal readonly struct GraphPageDependencyAddress
{
    internal GraphPageDependencyAddress(string mapId, int pageIndex)
    {
        MapId = mapId;
        PageIndex = pageIndex;
    }

    internal string MapId { get; }

    internal int PageIndex { get; }
}

/// <summary>Identifies one map-local semantic and physical page generation.</summary>
internal readonly struct GraphPageDependency : IEquatable<GraphPageDependency>
{
    internal GraphPageDependency(
        string mapId,
        long bakeVersion,
        long dynamicSlotGeneration,
        int pageIndex,
        long semanticVersion,
        long physicalVersion)
    {
        MapId = mapId;
        BakeVersion = bakeVersion;
        DynamicSlotGeneration = dynamicSlotGeneration;
        PageIndex = pageIndex;
        SemanticVersion = semanticVersion;
        PhysicalVersion = physicalVersion;
    }

    internal string MapId { get; }
    internal long BakeVersion { get; }
    internal long DynamicSlotGeneration { get; }
    internal int PageIndex { get; }
    internal long SemanticVersion { get; }
    internal long PhysicalVersion { get; }

    public bool Equals(GraphPageDependency other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && BakeVersion == other.BakeVersion
        && DynamicSlotGeneration == other.DynamicSlotGeneration
        && PageIndex == other.PageIndex
        && SemanticVersion == other.SemanticVersion
        && PhysicalVersion == other.PhysicalVersion;

    public override bool Equals(object? obj) => obj is GraphPageDependency other && Equals(other);

    public override int GetHashCode()
    {
        int mapHash = MapId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(MapId);
        int hash = SwiftHashTools.CombineHashCodes(mapHash, BakeVersion.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, DynamicSlotGeneration.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, PageIndex);
        hash = SwiftHashTools.CombineHashCodes(hash, SemanticVersion.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, PhysicalVersion.GetHashCode());
    }
}
