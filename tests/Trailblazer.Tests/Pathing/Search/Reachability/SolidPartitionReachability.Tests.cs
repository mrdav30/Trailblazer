using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class SolidPartitionReachabilityTests : IDisposable
{
    public SolidPartitionReachabilityTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-1, -1, -1), new Vector3d(5, 5, 5)),
            out _).Should().BeTrue();
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MarkedStartTraversal_ShouldRequireClearDiagonalLegsAndClimbRange()
    {
        RegisterSolidCube("ReachabilityMarkedStartDiagonal");
        SolidChartPartition current = PartitionAt(new Vector3d(1, 1, 1));
        RectangularDirection diagonal = FindDirection(dx: 1, dy: 1, dz: 1);
        SolidChartPartition neighbor = TestRequire.NotNull(current.Neighbors?[(int)diagonal]);
        const int snapshotId = 97;
        const int version = 11;
        const int component = 3;

        MarkComponent(neighbor, snapshotId, version, component);
        MarkComponent(PartitionAt(new Vector3d(2, 1, 1)), snapshotId, version, component);
        MarkComponent(PartitionAt(new Vector3d(1, 2, 1)), snapshotId, version, component);
        MarkComponent(PartitionAt(new Vector3d(1, 1, 2)), snapshotId, version, component);

        InvokeCanTraverseFromMarkedStart(current, neighbor, diagonal, snapshotId, version, Fixed64.One)
            .Should()
            .BeTrue();
        InvokeCanTraverseFromMarkedStart(current, neighbor, diagonal, snapshotId, version, Fixed64.Zero)
            .Should()
            .BeFalse();

        MarkComponent(PartitionAt(new Vector3d(1, 1, 2)), snapshotId, version, componentId: 0);

        InvokeCanTraverseFromMarkedStart(current, neighbor, diagonal, snapshotId, version, Fixed64.One)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Invalidate_ShouldRejectNullState()
    {
        Action act = () => SolidPartitionReachability.Invalidate(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("state");
    }

    [Fact]
    public void MarkedStartNeighborSearch_ShouldOnlyAcceptNeighborsInReachableComponent()
    {
        RegisterSolidCube("ReachabilityMarkedStartNeighbors");
        SolidChartPartition current = PartitionAt(new Vector3d(1, 1, 1));
        RectangularDirection diagonal = FindDirection(dx: 1, dy: 1, dz: 1);
        SolidChartPartition neighbor = TestRequire.NotNull(current.Neighbors?[(int)diagonal]);
        const int snapshotId = 101;
        const int version = 5;
        const int targetComponent = 8;

        MarkComponent(neighbor, snapshotId, version, targetComponent);
        MarkComponent(PartitionAt(new Vector3d(2, 1, 1)), snapshotId, version, targetComponent);
        MarkComponent(PartitionAt(new Vector3d(1, 2, 1)), snapshotId, version, targetComponent);
        MarkComponent(PartitionAt(new Vector3d(1, 1, 2)), snapshotId, version, targetComponent);

        ReflectionUtility.InvokePrivateStatic<bool>(
                typeof(SolidPartitionReachability),
                "HasReachableNeighborInComponent",
                current,
                targetComponent,
                snapshotId,
                version,
                Fixed64.One)
            .Should()
            .BeTrue();
        ReflectionUtility.InvokePrivateStatic<bool>(
                typeof(SolidPartitionReachability),
                "HasReachableNeighborInComponent",
                current,
                targetComponent + 1,
                snapshotId,
                version,
                Fixed64.One)
            .Should()
            .BeFalse();

        var detached = new SolidChartPartition();
        ReflectionUtility.InvokePrivateStatic<bool>(
                typeof(SolidPartitionReachability),
                "HasReachableNeighborInComponent",
                detached,
                targetComponent,
                snapshotId,
                version,
                Fixed64.One)
            .Should()
            .BeFalse();
    }

    private static bool InvokeCanTraverseFromMarkedStart(
        SolidChartPartition current,
        SolidChartPartition neighbor,
        RectangularDirection direction,
        int snapshotId,
        int version,
        Fixed64 maxClimbHeight)
    {
        return ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(SolidPartitionReachability),
            "CanTraverseFromMarkedStart",
            current,
            neighbor,
            direction,
            snapshotId,
            version,
            maxClimbHeight);
    }

    private static void RegisterSolidCube(string chartName)
    {
        var data = new bool[3, 3, 3];
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    data[x, y, z] = true;

        PathTestFactory.RegisterFromData(TestWorld.Context, chartName, data, Vector3d.Zero);
    }

    private static SolidChartPartition PartitionAt(Vector3d position)
    {
        return TestRequire.Partition<SolidChartPartition>(
            TestRequire.VoxelAt(TestWorld.Context, position));
    }

    private static void MarkComponent(
        SolidChartPartition partition,
        int snapshotId,
        int version,
        int componentId)
    {
        partition.SetReachabilityComponent(snapshotId, version, componentId);
    }

    private static RectangularDirection FindDirection(int dx, int dy, int dz)
    {
        ReadOnlySpan<RectangularDirection> directions = RectangularDirectionUtility.Diagonal;
        for (int i = 0; i < directions.Length; i++)
        {
            RectangularDirection direction = directions[i];
            (int x, int y, int z) = RectangularDirectionUtility.Offsets[(int)direction];
            if (x == dx && y == dy && z == dz)
                return direction;
        }

        throw new InvalidOperationException("Expected diagonal direction was not available.");
    }
}
