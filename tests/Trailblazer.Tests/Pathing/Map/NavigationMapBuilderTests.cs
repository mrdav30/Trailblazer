using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationMapBuilderTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        Fixed64.Zero,
        Fixed64.One,
        Fixed64.One);

    [Fact]
    public void Build_NormalizesContentAndValueEqualityAcrossInsertionOrders()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex first = new(0, 0, 0);
        VoxelIndex second = new(1, 0, 0);
        Vector3d firstCenter = GetFootAnchor(binding, first);
        Vector3d secondCenter = GetFootAnchor(binding, second);

        NavigationConnection connectionA = CreateConnection(
            "b-link", first, second, firstCenter, secondCenter);
        NavigationConnection connectionB = CreateConnection(
            "a-link", second, first, secondCenter, firstCenter);
        TraversalTransitionDefinition transitionA = CreateTransition("b-transition", first, second);
        TraversalTransitionDefinition transitionB = CreateTransition("a-transition", second, first);

        NavigationMap forward = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddConnection(connectionA)
            .AddConnection(connectionB)
            .AddTransition(transitionA)
            .AddTransition(transitionB)
            .Build();
        NavigationMap reverse = new NavigationMapBuilder("map", binding)
            .AddTransition(transitionB)
            .AddTransition(transitionA)
            .AddConnection(connectionB)
            .AddConnection(connectionA)
            .AddCell(second, SolidCell)
            .AddCell(first, SolidCell)
            .Build();

        reverse.Should().Be(forward);
        reverse.GetHashCode().Should().Be(forward.GetHashCode());
        forward.Cells[0].Index.Should().Be(first);
        forward.Cells[1].Index.Should().Be(second);
        forward.Connections[0].Id.Should().Be("a-link");
        forward.Transitions[0].Id.Should().Be("a-transition");
    }

    [Fact]
    public void Build_UsesStorageNeutralGridBindingIdentity()
    {
        GridConfiguration dense = CreateRectangularConfiguration(GridStorageKind.Dense);
        GridConfiguration sparse = CreateRectangularConfiguration(GridStorageKind.Sparse);

        NavigationMap denseMap = new NavigationMapBuilder("map", dense)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .Build();
        NavigationMap sparseMap = new NavigationMapBuilder("map", sparse)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .Build();

        sparseMap.Should().Be(denseMap);
        sparseMap.GetHashCode().Should().Be(denseMap.GetHashCode());
    }

    [Fact]
    public void ImportDenseRectangular_ProducesCanonicalSparseMap()
    {
        GridConfiguration configuration = CreateRectangularConfiguration();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var source = new NavigationCell?[binding.Width, binding.Height, binding.Length];
        source[1, 0, 0] = SolidCell;
        source[0, 0, 0] = SolidCell;

        NavigationMap imported = NavigationMapBuilder.ImportDenseRectangular(
            "map", configuration, source);
        NavigationMap explicitMap = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .Build();

        imported.Should().Be(explicitMap);
        imported.Cells.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void ImportAxialHex_ProducesCanonicalSparseMap(HexOrientation orientation)
    {
        GridConfiguration configuration = CreateHexConfiguration(orientation);
        var reversed = new[]
        {
            new NavigationCellEntry(new VoxelIndex(1, 0, 0), SolidCell),
            new NavigationCellEntry(new VoxelIndex(0, 0, 0), SolidCell)
        };

        NavigationMap map = NavigationMapBuilder.ImportAxialHex("hex", configuration, reversed);

        map.GridBinding.Configuration.TopologyMetrics.HexOrientation.Should().Be(orientation);
        map.Cells[0].Index.Should().Be(new VoxelIndex(0, 0, 0));
        map.Cells[1].Index.Should().Be(new VoxelIndex(1, 0, 0));
    }

    [Fact]
    public void Build_RejectsDuplicateAndOutOfBoundsCells()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex valid = new(0, 0, 0);

        Action duplicate = () => new NavigationMapBuilder("map", binding)
            .AddCell(valid, SolidCell)
            .AddCell(valid, SolidCell)
            .Build();
        Action outOfBounds = () => new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(binding.Width, 0, 0), SolidCell)
            .Build();
        Action defaultPayload = () => new NavigationMapBuilder("map", binding)
            .AddCell(valid, default)
            .Build();

        duplicate.Should().Throw<ArgumentException>();
        outOfBounds.Should().Throw<ArgumentException>();
        defaultPayload.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RejectsInvalidLocalConnectionReferencesAndAnchors()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex first = new(0, 0, 0);
        VoxelIndex second = new(1, 0, 0);
        Vector3d firstCenter = GetFootAnchor(binding, first);
        Vector3d secondCenter = GetFootAnchor(binding, second);
        NavigationConnection missingDestination = CreateConnection(
            "missing", first, second, firstCenter, secondCenter);
        NavigationConnection outside = CreateConnection(
            "outside",
            first,
            first,
            firstCenter + new Vector3d(100, 0, 0),
            firstCenter);

        Action dangling = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddConnection(missingDestination)
            .Build();
        Action badAnchor = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddConnection(outside)
            .Build();

        dangling.Should().Throw<ArgumentException>();
        badAnchor.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RetainsDormantCrossMapReferences()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        Vector3d center = GetFootAnchor(binding, source);
        var connection = new NavigationConnection(
            "streamed-link",
            source,
            new NavigationCellAddress("future-map", new VoxelIndex(7, 0, 3)),
            center,
            new Vector3d(50, 0, 50),
            Fixed64.Zero,
            Fixed64.One);

        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddConnection(connection)
            .Build();

        map.Connections.Should().ContainSingle();
        map.Connections[0].Destination.MapId.Should().Be("future-map");
    }

    [Fact]
    public void Build_ValidatesCompleteWitnessCorridorAndLowerBoundDeclaration()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex witness = new(1, 0, 0);
        VoxelIndex destination = new(2, 0, 0);
        var valid = new NavigationConnection(
            "witness-link",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("map", witness) },
            isLowerBoundCertified: true);

        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(witness, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(valid)
            .Build();

        map.Connections.Should().ContainSingle();
        map.Connections[0].IsLowerBoundCertified.Should().BeTrue();
    }

    [Fact]
    public void Build_RejectsConnectionThatSkipsARequiredWitness()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex destination = new(2, 0, 0);
        NavigationConnection shortcut = CreateConnection(
            "invalid-shortcut",
            source,
            destination,
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination));

        Action build = () => new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(shortcut)
            .Build();

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RejectsCellCenterUsedAsFootAnchorForFullHeightBody()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex destination = new(1, 0, 0);
        binding.TryGetCellPrism(source, out GridCellPrism sourcePrism).Should().BeTrue();
        binding.TryGetCellPrism(destination, out GridCellPrism destinationPrism).Should().BeTrue();
        NavigationConnection centerAsFoot = CreateConnection(
            "center-as-foot",
            source,
            destination,
            sourcePrism.Center,
            destinationPrism.Center);

        Action build = () => new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(centerAsFoot)
            .Build();

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Connection_RejectsZeroPortalHeight()
    {
        Action create = () => _ = new NavigationConnection(
            "zero-height",
            new VoxelIndex(0, 0, 0),
            new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.Zero);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RejectsUnprovableLowerBoundDeclarationOnCostOverflow()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex destination = new(1, 0, 0);
        var connection = new NavigationConnection(
            "overflow",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.One,
            additionalCost: Fixed64.MaxValue,
            isLowerBoundCertified: true);

        Action build = () => new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(connection)
            .Build();

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TransitionDefinition_IgnoresDisabledPointOverridePayloadsInValueIdentity()
    {
        VoxelIndex source = new(0, 0, 0);
        NavigationCellAddress destination = new("map", new VoxelIndex(1, 0, 0));
        var first = new TraversalTransitionDefinition(
            "transition",
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            destination,
            TraversalMedium.Solid,
            sourcePointOverride: new Vector3d(10, 20, 30),
            hasSourcePointOverride: false);
        var second = new TraversalTransitionDefinition(
            "transition",
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            destination,
            TraversalMedium.Solid);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void NavigationCell_RejectsNegativeAndUnknownValues()
    {
        Action negative = () => _ = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            -Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        Action unknownMedia = () => _ = new NavigationCell(
            (TraversalMedia)(1 << 12),
            TraversalCapability.None,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        Action unknownCapability = () => _ = new NavigationCell(
            TraversalMedia.Solid,
            (TraversalCapability)(1 << 12),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);

        negative.Should().Throw<ArgumentException>();
        unknownMedia.Should().Throw<ArgumentException>();
        unknownCapability.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MapAndConnection_DefensivelyCopyMutableInput()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex first = new(0, 0, 0);
        VoxelIndex second = new(1, 0, 0);
        var witnesses = new List<NavigationCellAddress>
        {
            new("future-map", new VoxelIndex(3, 0, 0))
        };
        var connection = new NavigationConnection(
            "link",
            first,
            new NavigationCellAddress("future-map", second),
            GetFootAnchor(binding, first),
            new Vector3d(50, 0, 0),
            Fixed64.Zero,
            Fixed64.One,
            witnesses);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddConnection(connection)
            .Build();

        witnesses.Clear();
        Action mutateCells = () => ((IList<NavigationCellEntry>)map.Cells).Add(
            new NavigationCellEntry(second, SolidCell));

        map.Connections[0].Witnesses.Should().ContainSingle();
        mutateCells.Should().Throw<NotSupportedException>();
    }

    private static NavigationConnection CreateConnection(
        string id,
        VoxelIndex source,
        VoxelIndex destination,
        Vector3d entry,
        Vector3d exit) => new(
            id,
            source,
            new NavigationCellAddress("map", destination),
            entry,
            exit,
            Fixed64.Zero,
            Fixed64.One);

    private static TraversalTransitionDefinition CreateTransition(
        string id,
        VoxelIndex source,
        VoxelIndex destination) => new(
            id,
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", destination),
            TraversalMedium.Solid,
            TraversalCapability.Jump);

    private static NormalizedGridConfiguration CreateRectangularBinding()
    {
        CreateRectangularConfiguration().TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        return binding;
    }

    private static GridConfiguration CreateRectangularConfiguration(
        GridStorageKind storageKind = GridStorageKind.Dense) => new(
            Vector3d.Zero,
            new Vector3d(3, 2, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: storageKind);

    private static GridConfiguration CreateHexConfiguration(HexOrientation orientation) => new(
        Vector3d.Zero,
        new Vector3d(4, 2, 4),
        topologyKind: GridTopologyKind.HexPrism,
        topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation));

    private static Vector3d GetFootAnchor(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }
}
