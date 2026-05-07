using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Allocation-free local voxel key for flow-field sampling within one grid.
/// </summary>
internal readonly struct FlowFieldLocalIndex : IEquatable<FlowFieldLocalIndex>
{
    private readonly int _x;
    private readonly int _y;
    private readonly int _z;

    public FlowFieldLocalIndex(int x, int y, int z)
    {
        _x = x;
        _y = y;
        _z = z;
    }

    public int X => _x;

    public int Y => _y;

    public int Z => _z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FlowFieldLocalIndex FromVoxelIndex(VoxelIndex index)
    {
        return new FlowFieldLocalIndex(index.x, index.y, index.z);
    }

    /// <inheritdoc/>
    public bool Equals(FlowFieldLocalIndex other)
    {
        return _x == other._x
            && _y == other._y
            && _z == other._z;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is FlowFieldLocalIndex other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + _x;
            hash = (hash * 31) + _y;
            hash = (hash * 31) + _z;
            return hash;
        }
    }
}
