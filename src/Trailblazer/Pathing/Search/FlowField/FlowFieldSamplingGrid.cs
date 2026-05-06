using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores the grid-space transform required to sample a flow-field result without resolving voxels through the world manager.
/// </summary>
internal sealed class FlowFieldSamplingGrid
{
    private readonly int _worldSpawnToken;
    private readonly ushort _gridIndex;
    private readonly int _gridSpawnToken;
    private readonly Vector3d _originWorldPosition;
    private readonly Fixed64 _voxelSize;
    private readonly SwiftDictionary<FlowFieldLocalIndex, Vector3d> _directions;

    public FlowFieldSamplingGrid(
        WorldVoxelIndex sampleIndex,
        Vector3d sampleWorldPosition,
        Fixed64 voxelSize,
        int capacity)
    {
        _worldSpawnToken = sampleIndex.WorldSpawnToken;
        _gridIndex = sampleIndex.GridIndex;
        _gridSpawnToken = sampleIndex.GridSpawnToken;
        _voxelSize = voxelSize;
        _directions = capacity > 0
            ? new SwiftDictionary<FlowFieldLocalIndex, Vector3d>(capacity)
            : new SwiftDictionary<FlowFieldLocalIndex, Vector3d>();

        VoxelIndex localIndex = sampleIndex.VoxelIndex;
        _originWorldPosition = new Vector3d(
            sampleWorldPosition.x - (voxelSize * (Fixed64)localIndex.x),
            sampleWorldPosition.y - (voxelSize * (Fixed64)localIndex.y),
            sampleWorldPosition.z - (voxelSize * (Fixed64)localIndex.z));
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
        _directions.Add(FlowFieldLocalIndex.FromVoxelIndex(index.VoxelIndex), direction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetDirection(Vector3d worldPosition, out Vector3d direction)
    {
        Fixed64 localX = (worldPosition.x - _originWorldPosition.x) / _voxelSize;
        Fixed64 localY = (worldPosition.y - _originWorldPosition.y) / _voxelSize;
        Fixed64 localZ = (worldPosition.z - _originWorldPosition.z) / _voxelSize;

        return _directions.TryGetValue(
            new FlowFieldLocalIndex(
                localX.FloorToInt(),
                localY.FloorToInt(),
                localZ.FloorToInt()),
            out direction);
    }
}
