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
        default,
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

        NavigationCellEntry firstEntry = forward.Cells[0];
        NavigationCellEntry sameEntry = new(first, SolidCell);
        NavigationCellEntry differentEntry = new(second, SolidCell);
        firstEntry.Equals((object)sameEntry).Should().BeTrue();
        (firstEntry == sameEntry).Should().BeTrue();
        (firstEntry != differentEntry).Should().BeTrue();
        (firstEntry.Cell == SolidCell).Should().BeTrue();
        (firstEntry.Cell != default).Should().BeTrue();
        var firstAddress = new NavigationCellAddress("map", first);
        firstAddress.ToString().Should().Be("map:(0, 0, 0)");
        SolidCell.Equals((object)"solid cell").Should().BeFalse();
        firstEntry.Equals((object)"cell entry").Should().BeFalse();
        firstAddress.Equals((object)"map:(0, 0, 0)").Should().BeFalse();
        connectionA.Equals((object)connectionA).Should().BeTrue();
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

    [Fact]
    public void ImportDenseRectangular_ShouldRejectEachMismatchedAxis()
    {
        GridConfiguration configuration = CreateRectangularConfiguration();

        Action wrongX = () => NavigationMapBuilder.ImportDenseRectangular(
            "map", configuration, new NavigationCell?[4, 2, 2]);
        Action wrongY = () => NavigationMapBuilder.ImportDenseRectangular(
            "map", configuration, new NavigationCell?[3, 3, 2]);
        Action wrongZ = () => NavigationMapBuilder.ImportDenseRectangular(
            "map", configuration, new NavigationCell?[3, 2, 3]);

        wrongX.Should().Throw<ArgumentException>();
        wrongY.Should().Throw<ArgumentException>();
        wrongZ.Should().Throw<ArgumentException>();
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
    public void Build_RejectsDuplicateConnectionIdsAndMissingLocalSources()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex first = new(0, 0, 0);
        VoxelIndex second = new(1, 0, 0);
        NavigationConnection connection = CreateConnection(
            "link",
            first,
            second,
            GetFootAnchor(binding, first),
            GetFootAnchor(binding, second));
        NavigationConnection missingSource = CreateConnection(
            "missing-source",
            first,
            second,
            GetFootAnchor(binding, first),
            GetFootAnchor(binding, second));

        Action duplicateId = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddConnection(connection)
            .AddConnection(connection)
            .Build();
        Action danglingSource = () => new NavigationMapBuilder("map", binding)
            .AddCell(second, SolidCell)
            .AddConnection(missingSource)
            .Build();

        duplicateId.Should().Throw<ArgumentException>();
        danglingSource.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RejectsDuplicateMissingAndMediumIncompatibleLocalTransitions()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex first = new(0, 0, 0);
        VoxelIndex second = new(1, 0, 0);
        TraversalTransitionDefinition transition = CreateTransition("jump", first, second);
        var wrongSourceMedium = new TraversalTransitionDefinition(
            "wrong-source-medium",
            TraversalTransitionType.Takeoff,
            first,
            TraversalMedium.Gas,
            new NavigationCellAddress("map", second),
            TraversalMedium.Solid);
        var wrongDestinationMedium = new TraversalTransitionDefinition(
            "wrong-destination-medium",
            TraversalTransitionType.Takeoff,
            first,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", second),
            TraversalMedium.Liquid);
        var outsideSourcePoint = new TraversalTransitionDefinition(
            "outside-source",
            TraversalTransitionType.Jump,
            first,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", second),
            TraversalMedium.Solid,
            sourcePointOverride: new Vector3d(100, 100, 100),
            hasSourcePointOverride: true);
        var outsideDestinationPoint = new TraversalTransitionDefinition(
            "outside-destination",
            TraversalTransitionType.Jump,
            first,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", second),
            TraversalMedium.Solid,
            destinationPointOverride: new Vector3d(100, 100, 100),
            hasDestinationPointOverride: true);

        Action duplicateId = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddTransition(transition)
            .AddTransition(transition)
            .Build();
        Action missingSource = () => new NavigationMapBuilder("map", binding)
            .AddCell(second, SolidCell)
            .AddTransition(transition)
            .Build();
        Action missingDestination = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddTransition(transition)
            .Build();
        Action incompatibleSource = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddTransition(wrongSourceMedium)
            .Build();
        Action incompatibleDestination = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddTransition(wrongDestinationMedium)
            .Build();
        Action invalidSourcePoint = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddTransition(outsideSourcePoint)
            .Build();
        Action invalidDestinationPoint = () => new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddTransition(outsideDestinationPoint)
            .Build();

        duplicateId.Should().Throw<ArgumentException>();
        missingSource.Should().Throw<ArgumentException>();
        missingDestination.Should().Throw<ArgumentException>();
        incompatibleSource.Should().Throw<ArgumentException>();
        incompatibleDestination.Should().Throw<ArgumentException>();
        invalidSourcePoint.Should().Throw<ArgumentException>();
        invalidDestinationPoint.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RejectsRepeatedAndMissingLocalConnectionWitnesses()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex witness = new(1, 0, 0);
        VoxelIndex destination = new(2, 0, 0);
        var repeatedWitness = new NavigationConnection(
            "repeated",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.One,
            new[]
            {
                new NavigationCellAddress("map", witness),
                new NavigationCellAddress("map", witness)
            });
        var missingWitness = new NavigationConnection(
            "missing",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("map", witness) });

        Action repeated = () => new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(witness, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(repeatedWitness)
            .Build();
        Action missing = () => new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(missingWitness)
            .Build();

        repeated.Should().Throw<ArgumentException>();
        missing.Should().Throw<ArgumentException>();
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
        var foreignWitness = new NavigationConnection(
            "streamed-witness",
            source,
            new NavigationCellAddress("map", source),
            center,
            center,
            Fixed64.Zero,
            Fixed64.One,
            new[] { new NavigationCellAddress("future-map", new VoxelIndex(8, 0, 3)) });

        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddConnection(connection)
            .AddConnection(foreignWitness)
            .Build();

        map.Connections.Should().HaveCount(2);
        map.Connections.Should().Contain(item => item.Destination.MapId == "future-map");
        map.Connections.Should().Contain(item => item.Witnesses.Count == 1);
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
    public void Build_RejectsConnectionClearanceBeyondAuthoredCells()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex source = new(0, 0, 0);
        VoxelIndex destination = new(1, 0, 0);
        var oversizedPortal = new NavigationConnection(
            "oversized-portal",
            source,
            new NavigationCellAddress("map", destination),
            GetFootAnchor(binding, source),
            GetFootAnchor(binding, destination),
            portalRadiusClearance: (Fixed64)2,
            portalHeightClearance: Fixed64.One);

        Action build = () => new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidCell)
            .AddCell(destination, SolidCell)
            .AddConnection(oversizedPortal)
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
        first.Equals((object)second).Should().BeTrue();
        first.Equals((object)"transition").Should().BeFalse();
        (first == second).Should().BeTrue();
        (first != new TraversalTransitionDefinition(
            "other-transition",
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            destination,
            TraversalMedium.Solid)).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        var defaults = new HashSet<TraversalTransitionDefinition> { default };
        defaults.Add(default).Should().BeFalse(
            "failed transition lookups use the default value as a stable key");

        Action belowKnownTypeRange = () => _ = new TraversalTransitionDefinition(
            "invalid",
            (TraversalTransitionType)(-1),
            source,
            TraversalMedium.Solid,
            destination,
            TraversalMedium.Solid);
        belowKnownTypeRange.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NavigationCell_RejectsNegativeAndUnknownValues()
    {
        Action missingMedia = () => _ = new NavigationCell(
            TraversalMedia.None,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        Action negative = () => _ = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            -Fixed64.One,
            Fixed64.Zero,
            Fixed64.One);
        Action unknownMedia = () => _ = new NavigationCell(
            (TraversalMedia)(1 << 12),
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        Action unknownCapability = () => _ = new NavigationCell(
            TraversalMedia.Solid,
            (TraversalCapability)(1 << 12),
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);

        missingMedia.Should().Throw<ArgumentException>();
        negative.Should().Throw<ArgumentException>();
        unknownMedia.Should().Throw<ArgumentException>();
        unknownCapability.Should().Throw<ArgumentException>();
        NavigationCell.ToMedia(TraversalMedium.Unknown).Should().Be(TraversalMedia.None);
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

    [Fact]
    public void MapEquality_ShouldIncludeEveryCanonicalPayload()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        VoxelIndex first = default;
        VoxelIndex second = new(1, 0, 0);
        NavigationConnection connection = CreateConnection(
            "link", first, second, GetFootAnchor(binding, first), GetFootAnchor(binding, second));
        TraversalTransitionDefinition transition = CreateTransition("jump", first, second);
        NavigationMap baseline = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddConnection(connection)
            .AddTransition(transition)
            .Build();

        var costlyCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.One,
            Fixed64.One);
        NavigationMap differentCell = new NavigationMapBuilder("map", binding)
            .AddCell(first, costlyCell)
            .AddCell(second, SolidCell)
            .AddConnection(connection)
            .AddTransition(transition)
            .Build();
        var costlyConnection = new NavigationConnection(
            "link",
            first,
            new NavigationCellAddress("map", second),
            GetFootAnchor(binding, first),
            GetFootAnchor(binding, second),
            Fixed64.Zero,
            Fixed64.One,
            additionalCost: Fixed64.One);
        NavigationMap differentConnection = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddConnection(costlyConnection)
            .AddTransition(transition)
            .Build();
        var differentTransition = new TraversalTransitionDefinition(
            "jump",
            TraversalTransitionType.Jump,
            first,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", second),
            TraversalMedium.Solid,
            TraversalCapability.Climb);
        NavigationMap transitionMap = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddConnection(connection)
            .AddTransition(differentTransition)
            .Build();
        NavigationMap differentCellCount = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .Build();
        NavigationMap differentConnectionCount = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddTransition(transition)
            .Build();
        NavigationMap differentTransitionCount = new NavigationMapBuilder("map", binding)
            .AddCell(first, SolidCell)
            .AddCell(second, SolidCell)
            .AddConnection(connection)
            .Build();

        baseline.Equals(baseline).Should().BeTrue();
        baseline.Should().NotBe(differentCell);
        baseline.Should().NotBe(differentConnection);
        baseline.Should().NotBe(transitionMap);
        baseline.Should().NotBe(differentCellCount);
        baseline.Should().NotBe(differentConnectionCount);
        baseline.Should().NotBe(differentTransitionCount);
    }

    [Fact]
    public void MapEquality_ShouldIncludeIdentityBindingAndDefaultCell()
    {
        NormalizedGridConfiguration binding = CreateRectangularBinding();
        NavigationMap baseline = new NavigationMapBuilder("map", binding).Build();
        NavigationMap equal = new NavigationMapBuilder("map", binding).Build();
        NavigationMap differentId = new NavigationMapBuilder("other", binding).Build();
        NavigationMap differentDefault = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(SolidCell)
            .Build();
        var differentDimensions = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(4, 2, 2),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        NavigationMap differentBinding = new NavigationMapBuilder("map", differentDimensions).Build();

        baseline.Equals(null).Should().BeFalse();
        baseline.Should().Be(equal);
        baseline.GetHashCode().Should().Be(equal.GetHashCode());
        baseline.Should().NotBe(differentId);
        baseline.Should().NotBe(differentDefault);
        baseline.Should().NotBe(differentBinding);
    }

    [Fact]
    public void ConnectionEquality_ShouldMaterializeAndIncludeStreamedWitnesses()
    {
        NavigationCellAddress first = new("map", new VoxelIndex(1, 0, 0));
        NavigationCellAddress second = new("map", new VoxelIndex(2, 0, 0));
        NavigationConnection Create(
            string id = "link",
            VoxelIndex source = default,
            NavigationCellAddress destination = default,
            Vector3d entry = default,
            Vector3d exit = default,
            Fixed64 radius = default,
            Fixed64 height = default,
            IEnumerable<NavigationCellAddress>? witnesses = null,
            Fixed64 cost = default,
            bool certified = false) => new(
            id,
            source,
            destination == default
                ? new NavigationCellAddress("map", new VoxelIndex(3, 0, 0))
                : destination,
            entry,
            exit == default ? Vector3d.Right : exit,
            radius,
            height == Fixed64.Zero ? Fixed64.One : height,
            witnesses ?? StreamWitnesses(first, second),
            cost,
            certified);
        NavigationConnection baseline = Create();
        NavigationConnection equal = Create();
        NavigationConnection[] variants =
        {
            Create(id: "other"),
            Create(source: new VoxelIndex(1, 0, 0)),
            Create(destination: new NavigationCellAddress("other", new VoxelIndex(3, 0, 0))),
            Create(entry: Vector3d.Right),
            Create(exit: new Vector3d(2, 0, 0)),
            Create(radius: Fixed64.One),
            Create(height: (Fixed64)2),
            Create(cost: Fixed64.One),
            Create(certified: true),
            Create(witnesses: new[] { first }),
            Create(witnesses: StreamWitnesses(
                first,
                new NavigationCellAddress("map", new VoxelIndex(4, 0, 0))))
        };

        baseline.Witnesses.Should().Equal(first, second);
        baseline.Should().Be(equal);
        baseline.GetHashCode().Should().Be(equal.GetHashCode());
        baseline.Equals(null).Should().BeFalse();
        foreach (NavigationConnection variant in variants)
            baseline.Should().NotBe(variant);
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

    private static IEnumerable<NavigationCellAddress> StreamWitnesses(
        params NavigationCellAddress[] witnesses)
    {
        foreach (NavigationCellAddress witness in witnesses)
            yield return witness;
    }
}
