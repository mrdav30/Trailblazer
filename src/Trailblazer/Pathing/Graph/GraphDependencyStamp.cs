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
    private const long ObjectHeaderBytes = 16L;
    private const long ArrayHeaderBytes = 24L;
    private const long ReferenceSlotBytes = 8L;
    private const long Int64Bytes = 8L;
    private const long NavigationAreaPolicyKeyBytes = ReferenceSlotBytes + Int64Bytes;
    private const long GraphComponentDependencyBytes = 32L;
    private const long GraphPageDependencyBytes = 48L;
    private static readonly long BaseRetainedBytes = Align8(
        ObjectHeaderBytes
        + NavigationAreaPolicyKeyBytes
        + (2L * ReferenceSlotBytes));

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

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + GetArrayRetainedBytes(Components, GraphComponentDependencyBytes)
        + GetArrayRetainedBytes(Pages, GraphPageDependencyBytes));

    internal static long GetRetainedBytes(int componentCount, int pageCount)
    {
        SwiftThrowHelper.ThrowIfNegative(componentCount, nameof(componentCount));
        SwiftThrowHelper.ThrowIfNegative(pageCount, nameof(pageCount));
        return checked(
            BaseRetainedBytes
            + GetArrayRetainedBytes(componentCount, GraphComponentDependencyBytes)
            + GetArrayRetainedBytes(pageCount, GraphPageDependencyBytes));
    }

    private static long GetArrayRetainedBytes<T>(T[] values, long elementBytes) =>
        values.Length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)values.Length * elementBytes)));

    private static long GetArrayRetainedBytes(int length, long elementBytes) =>
        length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)length * elementBytes)));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}

/// <summary>Identifies one structural component generation.</summary>
internal readonly struct GraphComponentDependency : IEquatable<GraphComponentDependency>
{
    internal GraphComponentDependency(NavigationSurfaceComponentKey key, long version)
    {
        Key = key;
        Version = version;
    }

    internal NavigationSurfaceComponentKey Key { get; }

    internal long Version { get; }

    public bool Equals(GraphComponentDependency other) =>
        Key.Equals(other.Key)
        && Version == other.Version;

    public override bool Equals(object? obj) => obj is GraphComponentDependency other && Equals(other);

    public override int GetHashCode()
    {
        return SwiftHashTools.CombineHashCodes(Key.GetHashCode(), Version.GetHashCode());
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
