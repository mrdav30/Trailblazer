using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationOverlayOperationTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.One,
        Fixed64.One);

    [Fact]
    public void Delta_CanonicallySortsKeysWithoutRetainingCallerStorage()
    {
        var cells = new[]
        {
            NavigationCellOverlayOperation.Suppress(new VoxelIndex(2, 0, 0)),
            NavigationCellOverlayOperation.Set(new VoxelIndex(0, 0, 0), SolidCell)
        };

        var delta = new NavigationMapOverlayDelta("map", cells);
        cells[0] = NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0));

        delta.Cells[0].Index.Should().Be(new VoxelIndex(0, 0, 0));
        delta.Cells[1].Index.Should().Be(new VoxelIndex(2, 0, 0));

        delta.Cells.Should().NotBeAssignableTo<NavigationCellOverlayOperation[]>();
        Action mutateView = () => ((IList<NavigationCellOverlayOperation>)delta.Cells)[0] =
            NavigationCellOverlayOperation.Suppress(default);
        mutateView.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Delta_RejectsDuplicateKeysAfterCanonicalSorting()
    {
        Action construct = () => _ = new NavigationMapOverlayDelta(
            "map",
            new[]
            {
                NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0)),
                NavigationCellOverlayOperation.RevertToBake(new VoxelIndex(1, 0, 0))
            });

        construct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Transaction_CanonicallySortsMapsAndRejectsDuplicates()
    {
        var last = new NavigationMapOverlayDelta(
            "z-map",
            new[] { NavigationCellOverlayOperation.Suppress(default) });
        var first = new NavigationMapOverlayDelta(
            "a-map",
            new[] { NavigationCellOverlayOperation.Suppress(default) });

        var transaction = new NavigationOverlayTransaction(new[] { last, first });
        transaction.Maps[0].MapId.Should().Be("a-map");

        Action duplicate = () => _ = new NavigationOverlayTransaction(new[] { first, first });
        duplicate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Delta_RejectsDefaultSetAndNullConnectionUpsert()
    {
        Action defaultSet = () => _ = new NavigationMapOverlayDelta(
            "map",
            new[] { default(NavigationCellOverlayOperation) });
        Action nullConnection = () => NavigationConnectionOverlayOperation.Upsert(null!);

        defaultSet.Should().Throw<ArgumentException>();
        nullConnection.Should().Throw<ArgumentNullException>();
    }
}
