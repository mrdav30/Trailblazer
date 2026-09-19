using System;
using FixedMathSharp;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class NavigatorOccupancyResolutionTests
{
    [Theory]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void CommitFrameMotion_ShouldRetainSameCellTicketThenAddBeforeRemovingPreviousCell(
        GridStorageKind storage,
        GridTopologyKind topology,
        HexOrientation orientation)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = topology == GridTopologyKind.RectangularPrism
            ? GridTopologyMetrics.Rectangular(Fixed64.One)
            : GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        GridConfiguration configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(6, 1, 6),
            topologyKind: topology, topologyMetrics: metrics, storageKind: storage);
        Assert.True(context.World.TryAddGrid(configuration,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0) }, out ushort gridIndex));
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel? previous));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel? target));
        Vector3d rootOffset = Vector3d.Up * PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        TestNavigator navigator = CreateNavigator(context, previous!.WorldPosition + rootOffset);
        var events = new SwiftList<(string Kind, OccupantEventInfo Info, byte PreviousCount, byte TargetCount)>();
        Action<OccupantEventInfo> added = info => events.Add(("add", info, previous.OccupantCount, target!.OccupantCount));
        Action<OccupantEventInfo> removed = info => events.Add(("remove", info, previous.OccupantCount, target!.OccupantCount));
        previous.OnOccupantAdded += added;
        previous.OnOccupantRemoved += removed;
        target!.OnOccupantAdded += added;
        target.OnOccupantRemoved += removed;

        try
        {
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, previous.WorldIndex, out OccupantTicket original));
            navigator.AddPositionDelta(Vector3d.Right * Fixed64.FromFraction(1, 8));
            navigator.CommitFrameMotion();

            Assert.Empty(events);
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, previous.WorldIndex, out OccupantTicket retained));
            Assert.Equal(original, retained);
            Assert.Equal(new[] { previous.WorldIndex }, GridOccupantManager.GetOccupiedIndices(context.World, navigator));

            Vector3d targetRoot = target.WorldPosition + rootOffset;
            navigator.AddPositionDelta(targetRoot - navigator.Position);
            navigator.CommitFrameMotion();

            Assert.Equal(targetRoot, navigator.Position);
            Assert.Equal(2, events.Count);
            Assert.Equal(("add", target.WorldIndex, (byte)1, (byte)1),
                (events[0].Kind, events[0].Info.VoxelIndex, events[0].PreviousCount, events[0].TargetCount));
            Assert.Equal(("remove", previous.WorldIndex, (byte)0, (byte)1),
                (events[1].Kind, events[1].Info.VoxelIndex, events[1].PreviousCount, events[1].TargetCount));
            Assert.Same(navigator, events[0].Info.Occupant);
            Assert.Same(navigator, events[1].Info.Occupant);
            Assert.Equal(original, events[1].Info.Ticket);
            Assert.False(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, previous.WorldIndex, out _));
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, target.WorldIndex, out OccupantTicket current));
            Assert.Equal(events[0].Info.Ticket, current);
            Assert.Equal(new[] { target.WorldIndex }, GridOccupantManager.GetOccupiedIndices(context.World, navigator));
        }
        finally
        {
            previous.OnOccupantAdded -= added;
            previous.OnOccupantRemoved -= removed;
            target.OnOccupantAdded -= added;
            target.OnOccupantRemoved -= removed;
            navigator.Reset();
        }
    }

    [Fact]
    public void CommitFrameMotion_FromSparseHole_ShouldRetainSeparatelyRegisteredNearestOldCell()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        VoxelGrid grid = CreateSparseLine(context, out Voxel previous, out Voxel target);
        Vector3d holeRoot = new Vector3d(Fixed64.One,
            PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY, Fixed64.Zero);
        TestNavigator navigator = CreateNavigator(context, holeRoot);
        try
        {
            Assert.Empty(GridOccupantManager.GetOccupiedIndices(context.World, navigator));
            Assert.True(grid.TryGetClosestVoxel(holeRoot, out Voxel? closest));
            Assert.Same(previous, closest);
            Assert.True(GridCellGeometry.TryGetPrism(grid, previous.Index, out GridCellPrism prism));
            Assert.False(prism.Contains(holeRoot));
            Assert.True(grid.TryAddVoxelOccupant(previous, navigator));
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, previous.WorldIndex, out OccupantTicket original));

            Vector3d targetRoot = target.WorldPosition + Vector3d.Up * holeRoot.Y;
            navigator.AddPositionDelta(targetRoot - navigator.Position);
            navigator.CommitFrameMotion();

            Assert.Equal(targetRoot, navigator.Position);
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, previous.WorldIndex, out OccupantTicket retained));
            Assert.Equal(original, retained);
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, target.WorldIndex, out _));
            Assert.Equal(new[] { previous.WorldIndex, target.WorldIndex }, GridOccupantManager.GetOccupiedIndices(context.World, navigator));
            Assert.Equal((byte)1, previous.OccupantCount);
            Assert.Equal((byte)1, target.OccupantCount);
        }
        finally { navigator.Reset(); }
    }

    [Fact]
    public void CommitFrameMotion_WhenTargetAdditionReplacesPreviousGrid_ShouldRetireLiveReplacementRegistration()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration previousConfiguration = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        Assert.True(context.World.TryAddGrid(previousConfiguration, out ushort previousGridIndex));
        Assert.True(context.World.TryAddGrid(new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(10, 0, 0)), out ushort targetGridIndex));
        Assert.True(context.World.ActiveGrids[previousGridIndex].TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel? previous));
        Assert.True(context.World.ActiveGrids[targetGridIndex].TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel? target));
        Vector3d rootOffset = Vector3d.Up * PathTestFactory.DefaultNavigationProfile.Shape.RootToFootOffsetY;
        TestNavigator navigator = CreateNavigator(context, previous!.WorldPosition + rootOffset);
        WorldVoxelIndex previousAddress = previous.WorldIndex;
        Voxel? replacement = null;
        OccupantTicket replacementTicket = default;
        bool replacedAndRegistered = false;
        int additions = 0;
        int targetRemovals = 0;
        Action<OccupantEventInfo> added = ignoredEvent =>
        {
            additions++;
            if (!context.World.TryRemoveGrid(previousGridIndex))
                return;
            // Consume the retired shells at a different position so stale
            // object references cannot accidentally address the replacement.
            if (!context.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 0), new Vector3d(20, 0, 0)), out _)
                || !context.World.TryAddGrid(previousConfiguration, out ushort replacementGridIndex))
                return;
            VoxelGrid replacementGrid = context.World.ActiveGrids[replacementGridIndex];
            replacedAndRegistered = replacementGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out replacement)
                && replacementGrid.TryAddVoxelOccupant(replacement!, navigator)
                && GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, replacement!.WorldIndex, out replacementTicket);
        };
        Action<OccupantEventInfo> removed = _ => targetRemovals++;
        target!.OnOccupantAdded += added;
        target.OnOccupantRemoved += removed;
        try
        {
            Vector3d targetRoot = target.WorldPosition + rootOffset;
            navigator.AddPositionDelta(targetRoot - navigator.Position);
            navigator.CommitFrameMotion();

            Assert.Equal(1, additions);
            Assert.True(replacedAndRegistered);
            Assert.NotNull(replacement);
            Assert.NotSame(previous, replacement);
            Assert.NotEqual(previousAddress, replacement.WorldIndex);
            Assert.True(replacementTicket.IsValid);
            Assert.Equal((byte)0, replacement.OccupantCount);
            Assert.False(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, replacement.WorldIndex, out _));
            Assert.Equal(0, targetRemovals);
            Assert.Equal(targetRoot, navigator.Position);
            Assert.Equal((byte)1, target.OccupantCount);
            Assert.True(GridOccupantManager.TryGetOccupancyTicket(context.World, navigator, target.WorldIndex, out _));
            Assert.Equal(new[] { target.WorldIndex }, GridOccupantManager.GetOccupiedIndices(context.World, navigator));
        }
        finally
        {
            target.OnOccupantAdded -= added;
            target.OnOccupantRemoved -= removed;
            navigator.Reset();
        }
    }

    private static VoxelGrid CreateSparseLine(TrailblazerWorldContext context, out Voxel previous, out Voxel target)
    {
        GridConfiguration configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(3, 1, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(context.World.TryAddGrid(configuration,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(3, 0, 0) }, out ushort gridIndex));
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel? first));
        Assert.True(grid.TryGetVoxel(new VoxelIndex(3, 0, 0), out Voxel? last));
        previous = first!;
        target = last!;
        return grid;
    }

    private static TestNavigator CreateNavigator(TrailblazerWorldContext context, Vector3d position)
    {
        TestNavigator navigator = new TestNavigator(context);
        navigator.Activate(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        }, position, PathTestFactory.DefaultNavigationProfile);
        return navigator;
    }
}
