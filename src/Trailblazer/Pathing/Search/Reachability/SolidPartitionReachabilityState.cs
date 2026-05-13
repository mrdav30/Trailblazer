using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores conservative solid-partition connectivity snapshots for one pathing context.
/// </summary>
internal sealed class SolidPartitionReachabilityState
{
    internal object Lock { get; } = new();

    internal SwiftDictionary<WorldVoxelIndex, SolidChartPartition> PassablePartitions { get; } = new();

    internal SwiftList<SolidChartPartition> ComponentRoots { get; } = new();

    internal SwiftQueue<SolidChartPartition> ComponentQueue { get; } = new();

    internal ReachabilitySnapshotKey ActiveSnapshotKey;

    internal bool HasActiveSnapshot;

    internal int ActiveSnapshotId;

    internal int ActiveSnapshotVersion = -1;

    internal long SnapshotBuildCount;

    internal int Version;
}

internal readonly struct ReachabilitySnapshotKey : System.IEquatable<ReachabilitySnapshotKey>
{
    internal ReachabilitySnapshotKey(Fixed64 unitSize, Fixed64 maxClimbHeight)
    {
        UnitSize = unitSize;
        MaxClimbHeight = maxClimbHeight;
    }

    internal Fixed64 UnitSize { get; }

    internal Fixed64 MaxClimbHeight { get; }

    public bool Equals(ReachabilitySnapshotKey other)
    {
        return UnitSize == other.UnitSize && MaxClimbHeight == other.MaxClimbHeight;
    }

    public override bool Equals(object? obj)
    {
        return obj is ReachabilitySnapshotKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + UnitSize.GetHashCode();
            hash = (hash * 31) + MaxClimbHeight.GetHashCode();
            return hash;
        }
    }
}
