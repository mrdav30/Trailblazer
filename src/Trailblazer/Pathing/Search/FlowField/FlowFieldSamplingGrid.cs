//=======================================================================
// FlowFieldSamplingGrid.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores the grid-space transform required to sample a flow-field result without resolving voxels through the world manager.
/// </summary>
internal sealed class FlowFieldSamplingGrid
{
    private const int DenseSparsityFactor = 4;

    private readonly long _worldSpawnToken;
    private readonly ushort _gridIndex;
    private readonly long _gridSpawnToken;
    private readonly Vector3d _originWorldPosition;
    private readonly Fixed64 _voxelSize;
    private readonly int _minX;
    private readonly int _minY;
    private readonly int _minZ;
    private readonly int _sizeX;
    private readonly int _sizeY;
    private readonly int _sizeZ;
    private readonly Vector3d[]? _denseDirections;
    private readonly bool[]? _denseOccupied;
    private readonly SwiftDictionary<FlowFieldLocalIndex, Vector3d>? _sparseDirections;

    public FlowFieldSamplingGrid(
        WorldVoxelIndex sampleIndex,
        Vector3d originWorldPosition,
        Fixed64 voxelSize,
        int minX,
        int minY,
        int minZ,
        int maxX,
        int maxY,
        int maxZ,
        int fieldCount)
    {
        _worldSpawnToken = sampleIndex.WorldSpawnToken;
        _gridIndex = sampleIndex.GridIndex;
        _gridSpawnToken = sampleIndex.GridSpawnToken;
        _originWorldPosition = originWorldPosition;
        _voxelSize = voxelSize;
        _minX = minX;
        _minY = minY;
        _minZ = minZ;
        _sizeX = maxX - minX + 1;
        _sizeY = maxY - minY + 1;
        _sizeZ = maxZ - minZ + 1;

        long denseLength = (long)_sizeX * _sizeY * _sizeZ;
        long denseLimit = Math.Max((long)fieldCount + 32L, (long)fieldCount * DenseSparsityFactor);
        if (denseLength > 0 && denseLength <= denseLimit && denseLength <= int.MaxValue)
        {
            _denseDirections = new Vector3d[(int)denseLength];
            _denseOccupied = new bool[(int)denseLength];
        }
        else
        {
            _sparseDirections = fieldCount > 0
                ? new SwiftDictionary<FlowFieldLocalIndex, Vector3d>(fieldCount)
                : new SwiftDictionary<FlowFieldLocalIndex, Vector3d>();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MatchesGrid(WorldVoxelIndex index)
    {
        return index.WorldSpawnToken == _worldSpawnToken
            && index.GridIndex == _gridIndex
            && index.GridSpawnToken == _gridSpawnToken;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddDirection(WorldVoxelIndex index, Vector3d direction)
    {
        FlowFieldLocalIndex localIndex = FlowFieldLocalIndex.FromVoxelIndex(index.VoxelIndex);
        if (_denseDirections != null && _denseOccupied != null)
        {
            int denseIndex = GetDenseIndex(localIndex.X, localIndex.Y, localIndex.Z);
            _denseDirections[denseIndex] = direction;
            _denseOccupied[denseIndex] = true;
            return;
        }

        _sparseDirections!.Add(localIndex, direction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetDirection(Vector3d worldPosition, out Vector3d direction)
    {
        Fixed64 localX = (worldPosition.X - _originWorldPosition.X) / _voxelSize;
        Fixed64 localY = (worldPosition.Y - _originWorldPosition.Y) / _voxelSize;
        Fixed64 localZ = (worldPosition.Z - _originWorldPosition.Z) / _voxelSize;

        int x = localX.FloorToInt();
        int y = localY.FloorToInt();
        int z = localZ.FloorToInt();

        if (_denseDirections != null && _denseOccupied != null)
        {
            if (x < _minX
                || x >= _minX + _sizeX
                || y < _minY
                || y >= _minY + _sizeY
                || z < _minZ
                || z >= _minZ + _sizeZ)
            {
                direction = Vector3d.Zero;
                return false;
            }

            int denseIndex = GetDenseIndex(x, y, z);
            if (!_denseOccupied[denseIndex])
            {
                direction = Vector3d.Zero;
                return false;
            }

            direction = _denseDirections[denseIndex];
            return true;
        }

        return _sparseDirections!.TryGetValue(new FlowFieldLocalIndex(x, y, z), out direction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetDenseIndex(int x, int y, int z)
    {
        return (((y - _minY) * _sizeZ) + (z - _minZ)) * _sizeX + (x - _minX);
    }
}
