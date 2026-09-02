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
    public void DescriptorByteAccounting_ShouldSaturateInsteadOfWrappingOrThrowing()
    {
        NavigationByteCount.SaturatingAdd(100, 23).Should().Be(123);
        NavigationByteCount.SaturatingAdd(long.MaxValue - 4, 5).Should().Be(long.MaxValue);
        NavigationByteCount.SaturatingAdd(long.MaxValue, long.MaxValue).Should().Be(long.MaxValue);
    }

    [Fact]
    public void Delta_CanonicallySortsAndDefensivelyCopiesEveryOperationCollection()
    {
        var cells = new[]
        {
            NavigationCellOverlayOperation.Suppress(new VoxelIndex(2, 0, 0)),
            NavigationCellOverlayOperation.Set(new VoxelIndex(0, 0, 0), SolidCell)
        };
        var connections = new[]
        {
            NavigationConnectionOverlayOperation.Upsert(CreateConnection("b"))
        };
        var transitions = new[]
        {
            TraversalTransitionOverlayOperation.Upsert(CreateTransition("t", "future"))
        };

        var delta = new NavigationMapOverlayDelta("map", cells, connections, transitions);
        cells[0] = NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0));
        connections[0] = NavigationConnectionOverlayOperation.RevertToBake("x");
        transitions[0] = TraversalTransitionOverlayOperation.RevertToBake("x");

        delta.Cells[0].Index.Should().Be(new VoxelIndex(0, 0, 0));
        delta.Cells[1].Index.Should().Be(new VoxelIndex(2, 0, 0));
        delta.Connections.Should().ContainSingle();
        delta.Connections[0].Kind.Should().Be(NavigationConnectionOverlayOperationKind.Upsert);
        delta.Connections[0].Id.Should().Be("b");
        delta.Transitions.Should().ContainSingle();
        delta.Transitions[0].Kind.Should().Be(TraversalTransitionOverlayOperationKind.Upsert);
        delta.Transitions[0].Id.Should().Be("t");
        delta.EstimatedDescriptorBytes.Should().Be(546);

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
    public void Transaction_DefensivelyCopiesSubmissionAndAccountsImmutableDescriptor()
    {
        var first = new NavigationMapOverlayDelta(
            "a-map",
            new[] { NavigationCellOverlayOperation.Suppress(default) });
        var last = new NavigationMapOverlayDelta(
            "z-map",
            new[] { NavigationCellOverlayOperation.Suppress(default) });
        var submitted = new[] { last, first };

        var transaction = new NavigationOverlayTransaction(submitted);
        submitted[0] = first;

        transaction.Maps[0].MapId.Should().Be("a-map");
        transaction.Maps[1].MapId.Should().Be("z-map");
        transaction.EstimatedDescriptorBytes.Should().Be(
            32 + first.EstimatedDescriptorBytes + last.EstimatedDescriptorBytes);
        transaction.Maps.Should().NotBeAssignableTo<NavigationMapOverlayDelta[]>();
        Action mutateView = () => ((IList<NavigationMapOverlayDelta>)transaction.Maps)[0] = last;
        mutateView.Should().Throw<NotSupportedException>();
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

    [Fact]
    public void OverlayState_RemovingMiddleConnectionPreservesCanonicalSurvivors()
    {
        NavigationMapOverlayState state = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "map",
                connections: new[]
                {
                    NavigationConnectionOverlayOperation.Upsert(CreateConnection("c")),
                    NavigationConnectionOverlayOperation.Upsert(CreateConnection("a")),
                    NavigationConnectionOverlayOperation.Upsert(CreateConnection("b"))
                }),
            operationSequence: 1);

        state = state.Apply(
            new NavigationMapOverlayDelta(
                "map",
                connections: new[] { NavigationConnectionOverlayOperation.RevertToBake("b") }),
            operationSequence: 2);

        state.ConnectionCount.Should().Be(2);
        state.GetConnectionAt(0).Id.Should().Be("a");
        state.GetConnectionAt(1).Id.Should().Be("c");
        state.TryGetConnection("b", out _).Should().BeFalse();
    }

    [Fact]
    public void OverlayState_TransitionReplacementAndReversionKeepExactPayloadAccounting()
    {
        TraversalTransitionOverlayOperation initial = TraversalTransitionOverlayOperation.Upsert(
            CreateTransition("t", "future"));
        NavigationMapOverlayState state = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta("map", transitions: new[] { initial }),
            operationSequence: 1);

        state.TransitionCount.Should().Be(1);
        state.GetTransitionAt(0).Should().Be(initial);
        state.RetainedPayloadBytes.Should().Be(174);

        TraversalTransitionOverlayOperation replacement = TraversalTransitionOverlayOperation.Upsert(
            CreateTransition("t", "other"));
        state = state.Apply(
            new NavigationMapOverlayDelta("map", transitions: new[] { replacement }),
            operationSequence: 2);

        state.GetTransitionAt(0).Should().Be(replacement);
        state.RetainedPayloadBytes.Should().Be(172);

        state = state.Apply(
            new NavigationMapOverlayDelta(
                "map",
                transitions: new[] { TraversalTransitionOverlayOperation.RevertToBake("t") }),
            operationSequence: 3);

        state.TransitionCount.Should().Be(0);
        state.TryGetTransition("t", out _).Should().BeFalse();
        state.RetainedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void OverlayState_CellReplacementAndReversionShouldRemoveRetainedOverride()
    {
        VoxelIndex index = new(3, 0, 0);
        NavigationMapOverlayState state = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "map",
                new[] { NavigationCellOverlayOperation.Set(index, SolidCell) }),
            operationSequence: 1);
        long setPayloadBytes = state.RetainedPayloadBytes;

        state = state.Apply(
            new NavigationMapOverlayDelta(
                "map",
                new[] { NavigationCellOverlayOperation.Suppress(index) }),
            operationSequence: 2);

        state.CellCount.Should().Be(1);
        state.TryGetCell(index, out NavigationCellOverlayOperation suppressed).Should().BeTrue();
        suppressed.Kind.Should().Be(NavigationCellOverlayOperationKind.Suppress);
        setPayloadBytes.Should().Be(64);
        state.RetainedPayloadBytes.Should().Be(64);

        state = state.Apply(
            new NavigationMapOverlayDelta(
                "map",
                new[] { NavigationCellOverlayOperation.RevertToBake(index) }),
            operationSequence: 3);

        state.CellCount.Should().Be(0);
        state.TryGetCell(index, out _).Should().BeFalse();
        state.RetainedPayloadBytes.Should().Be(0);
    }

    private static NavigationConnection CreateConnection(string id) => new(
        id,
        default,
        new NavigationCellAddress("future", default),
        Vector3d.Zero,
        Vector3d.Zero,
        Fixed64.Zero,
        Fixed64.One);

    private static TraversalTransitionDefinition CreateTransition(string id, string destinationMapId) => new(
        id,
        TraversalTransitionType.Jump,
        default,
        TraversalMedium.Solid,
        new NavigationCellAddress(destinationMapId, default),
        TraversalMedium.Solid);
}
