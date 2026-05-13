using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using Xunit;

namespace Trailblazer.Tests;

internal static class TestRequire
{
    public static T NotNull<T>(T? value) where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    public static T NotNull<T>(T? value) where T : struct
    {
        Assert.True(value.HasValue);
        return value.Value;
    }

    public static T Created<T>(bool success, T? value) where T : class
    {
        Assert.True(success);
        return NotNull(value);
    }

    public static T OfType<T>(object? value) where T : class
    {
        return Assert.IsType<T>(value);
    }

    public static (TFirst First, TSecond Second) Created<TFirst, TSecond>(
        bool success,
        TFirst? first,
        TSecond? second)
        where TFirst : class
        where TSecond : class
    {
        Assert.True(success);
        return (NotNull(first), NotNull(second));
    }

    public static Voxel VoxelAt(Vector3d position)
    {
        return Created(TestWorld.World.TryGetVoxel(position, out Voxel? voxel), voxel);
    }

    public static VoxelGrid Grid(ushort gridIndex)
    {
        return Created(TestWorld.World.TryGetGrid(gridIndex, out VoxelGrid? grid), grid);
    }

    public static (VoxelGrid Grid, Voxel Voxel) GridAndVoxelAt(Vector3d position)
    {
        return Created(
            TestWorld.World.TryGetGridAndVoxel(position, out VoxelGrid? grid, out Voxel? voxel),
            grid,
            voxel);
    }

    public static TPartition Partition<TPartition>(Voxel voxel) where TPartition : class, IVoxelPartition
    {
        return Created(voxel.TryGetPartition(out TPartition? partition), partition);
    }
}
