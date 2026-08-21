using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Map;

public sealed class NavigationMapDefaultAndTransitionRuleTests
{
    private static readonly NavigationCell SolidExplicit = new(
        TraversalMedia.Solid,
        TraversalCapability.Jump,
        new NavigationAreaId(1),
        Fixed64.FromFraction(1, 4),
        Fixed64.Half,
        Fixed64.One,
        NavigationCellFlags.TransitionSourceHint);

    private static readonly NavigationCell GasDefault = new(
        TraversalMedia.Gas,
        TraversalCapability.Fly,
        new NavigationAreaId(3),
        Fixed64.FromFraction(3, 2),
        Fixed64.FromFraction(3, 4),
        Fixed64.One + Fixed64.One,
        NavigationCellFlags.TransitionDestinationHint);

    [Fact]
    public void Build_DefaultCellIsOptionalCompleteImmutableAuthoringTruth()
    {
        NavigationMap dense = new NavigationMapBuilder(
                "map",
                CreateConfiguration(GridStorageKind.Dense))
            .SetDefaultCell(GasDefault)
            .Build();
        NavigationMap sparse = new NavigationMapBuilder(
                "map",
                CreateConfiguration(GridStorageKind.Sparse))
            .SetDefaultCell(GasDefault)
            .Build();
        NavigationMap absent = new NavigationMapBuilder(
                "map",
                CreateConfiguration(GridStorageKind.Dense))
            .Build();

        dense.DefaultCell.Should().Be(GasDefault);
        dense.Cells.Should().BeEmpty("a default is one complete fallback fact, not an importer-history mask");
        sparse.Should().Be(dense);
        sparse.GetHashCode().Should().Be(dense.GetHashCode());
        absent.DefaultCell.Should().BeNull();
        absent.Should().NotBe(dense);

        Action malformed = () => new NavigationMapBuilder(
                "invalid",
                CreateConfiguration(GridStorageKind.Dense))
            .SetDefaultCell(default(NavigationCell))
            .Build();
        malformed.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void CandidateCellTruth_UsesOverlayThenExplicitThenDefaultWithoutFieldMerging(
        GridStorageKind storageKind)
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        CreateConfiguration(storageKind).TryNormalize(
            out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex explicitIndex = default;
        VoxelIndex fallbackIndex = new(1, 0, 0);
        var overlayCell = new NavigationCell(
            TraversalMedia.Liquid,
            TraversalCapability.Swim,
            new NavigationAreaId(7),
            Fixed64.FromFraction(7, 2),
            Fixed64.FromFraction(1, 8),
            Fixed64.Half,
            NavigationCellFlags.ClimbSurfaceHint);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(GasDefault)
            .AddCell(explicitIndex, SolidExplicit)
            .Build();

        NavigationMapCommitOperation install = Commit(map, 1, 0);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);

        GetCell(processor, "map", explicitIndex).Should().Be(SolidExplicit);
        processor.Candidate.TryGetSemanticState(
            new NavigationCellAddress("map", fallbackIndex),
            out _, out _, out _).Should().BeFalse(
                "a default must not synthesize an absent sparse address");

        NavigationOverlayCommitOperation set = Overlay(
            "map",
            new[]
            {
                NavigationCellOverlayOperation.Set(explicitIndex, overlayCell),
                NavigationCellOverlayOperation.Set(fallbackIndex, overlayCell)
            },
            2,
            1);
        processor.Admit(set).Should().BeTrue();
        processor.ProcessFrame(1);

        GetCell(processor, "map", explicitIndex).Should().Be(overlayCell);
        GetCell(processor, "map", fallbackIndex).Should().Be(overlayCell);

        NavigationOverlayCommitOperation suppress = Overlay(
            "map",
            new[]
            {
                NavigationCellOverlayOperation.Suppress(explicitIndex),
                NavigationCellOverlayOperation.Suppress(fallbackIndex)
            },
            3,
            2);
        processor.Admit(suppress).Should().BeTrue();
        processor.ProcessFrame(2);

        HasCell(processor, "map", explicitIndex).Should().BeFalse();
        HasCell(processor, "map", fallbackIndex).Should().BeFalse();

        NavigationOverlayCommitOperation revert = Overlay(
            "map",
            new[]
            {
                NavigationCellOverlayOperation.RevertToBake(explicitIndex),
                NavigationCellOverlayOperation.RevertToBake(fallbackIndex)
            },
            4,
            3);
        processor.Admit(revert).Should().BeTrue();
        processor.ProcessFrame(3);

        GetCell(processor, "map", explicitIndex).Should().Be(SolidExplicit);
        GetCell(processor, "map", fallbackIndex).Should().Be(GasDefault);

        CreateConfiguration(GridStorageKind.Sparse, originX: 10).TryNormalize(
            out NormalizedGridConfiguration noneBinding).Should().BeTrue();
        processor.Admit(Commit(
            new NavigationMapBuilder("none", noneBinding).Build(),
            5,
            4)).Should().BeTrue();
        processor.ProcessFrame(4);
        processor.Admit(Overlay(
            "none",
            new[] { NavigationCellOverlayOperation.Set(fallbackIndex, overlayCell) },
            6,
            5)).Should().BeTrue();
        processor.ProcessFrame(5);
        processor.Admit(Overlay(
            "none",
            new[] { NavigationCellOverlayOperation.RevertToBake(fallbackIndex) },
            7,
            6)).Should().BeTrue();
        processor.ProcessFrame(6);
        HasCell(processor, "none", fallbackIndex).Should().BeFalse();
    }

    [Fact]
    public void Replacement_ChangesTheCompleteDefaultTransactionally()
    {
        var processor = new NavigationOperationProcessor(CreateLimits());
        CreateConfiguration(GridStorageKind.Sparse).TryNormalize(
            out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex fallbackIndex = new(1, 0, 0);
        NavigationCell liquidDefault = new(
            TraversalMedia.Liquid,
            TraversalCapability.Swim,
            new NavigationAreaId(4),
            Fixed64.FromFraction(5, 4),
            Fixed64.Half,
            Fixed64.One,
            NavigationCellFlags.None);
        NavigationMap gas = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(GasDefault)
            .Build();
        NavigationMap liquid = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(liquidDefault)
            .Build();

        processor.Admit(Commit(gas, 1, 0)).Should().BeTrue();
        processor.ProcessFrame(0);
        processor.Admit(Overlay(
            "map",
            new[] { NavigationCellOverlayOperation.Set(fallbackIndex, SolidExplicit) },
            2,
            1)).Should().BeTrue();
        processor.ProcessFrame(1);
        processor.Admit(Overlay(
            "map",
            new[] { NavigationCellOverlayOperation.RevertToBake(fallbackIndex) },
            3,
            2)).Should().BeTrue();
        processor.ProcessFrame(2);
        GetCell(processor, "map", fallbackIndex).Should().Be(GasDefault);

        var replacement = new NavigationMapCommitOperation(
            new PreparedNavigationMap(liquid, 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            4,
            3);
        processor.Admit(replacement).Should().BeTrue();
        processor.ProcessFrame(3);

        replacement.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        GetCell(processor, "map", fallbackIndex).Should().Be(liquidDefault);
    }

    [Fact]
    public void InvalidDefaultArea_RejectsReplacementWithoutMutatingThePublishedMap()
    {
        var processor = new NavigationOperationProcessor(CreateLimits(), navigationAreaCount: 1);
        CreateConfiguration(GridStorageKind.Dense).TryNormalize(
            out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell validDefault = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationCell unknownAreaDefault = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            new NavigationAreaId(1),
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMap initial = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(validDefault)
            .Build();
        NavigationMap malformedForCatalog = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(unknownAreaDefault)
            .Build();

        NavigationMapCommitOperation install = Commit(initial, 1, 0);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);
        NavigationMapCommitOperation replacement = Commit(
            malformedForCatalog,
            2,
            1,
            bakeVersion: 2);
        processor.Admit(replacement).Should().BeTrue();
        processor.ProcessFrame(1);

        replacement.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        processor.Candidate.TryGetMap("map", out NavigationMap retained).Should().BeTrue();
        retained.Should().BeSameAs(initial);
    }

    [Fact]
    public void TransitionRule_ValidatesTheCompleteShapeAndSupportsSameMediumValueIdentity()
    {
        TraversalTransitionLocomotionHints hints =
            TraversalTransitionLocomotionHints.RequestClimb
            | TraversalTransitionLocomotionHints.PreserveClimbAfterCompletion;
        var expected = new TraversalTransitionRule(
            "climb",
            TraversalTransitionType.Climb,
            TraversalMedium.Solid,
            TraversalMedium.Solid,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.Climb,
            Fixed64.FromFraction(3, 2),
            hints);
        var equal = new TraversalTransitionRule(
            "climb",
            TraversalTransitionType.Climb,
            TraversalMedium.Solid,
            TraversalMedium.Solid,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.Climb,
            Fixed64.FromFraction(3, 2),
            hints);

        equal.Should().Be(expected);
        equal.GetHashCode().Should().Be(expected.GetHashCode());
        expected.SourceMedium.Should().Be(expected.DestinationMedium);
        expected.LocomotionHints.Should().Be(hints);

        Action blankId = () => _ = CreateRule(" ");
        Action unknownType = () => _ = CreateRule("type", (TraversalTransitionType)99);
        Action unknownSource = () => _ = CreateRule(
            "source", sourceMedium: TraversalMedium.Unknown);
        Action unknownDestination = () => _ = CreateRule(
            "destination", destinationMedium: (TraversalMedium)99);
        Action unknownScope = () => _ = CreateRule(
            "scope", scope: (TraversalTransitionRuleScope)99);
        Action unknownCapability = () => _ = CreateRule(
            "capability", capabilities: (TraversalCapability)(1 << 20));
        Action negativeCost = () => _ = CreateRule("cost", actionCost: -Fixed64.One);
        Action unknownHint = () => _ = CreateRule(
            "hint", hints: (TraversalTransitionLocomotionHints)(1 << 20));

        blankId.Should().Throw<ArgumentException>();
        unknownType.Should().Throw<ArgumentException>();
        unknownSource.Should().Throw<ArgumentException>();
        unknownDestination.Should().Throw<ArgumentException>();
        unknownScope.Should().Throw<ArgumentException>();
        unknownCapability.Should().Throw<ArgumentException>();
        negativeCost.Should().Throw<ArgumentException>();
        unknownHint.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_CanonicallyOwnsRulesAndKeepsDefinitionIdentityDistinct()
    {
        CreateConfiguration(GridStorageKind.Dense).TryNormalize(
            out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex source = default;
        VoxelIndex destination = new(1, 0, 0);
        TraversalTransitionRule first = CreateRule(
            "b-rule",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.PositiveFaceContact,
            TraversalCapability.Swim | TraversalCapability.Fly,
            Fixed64.One,
            TraversalTransitionLocomotionHints.None);
        TraversalTransitionRule second = CreateRule("a-rule");
        var sameIdDefinition = new TraversalTransitionDefinition(
            "a-rule",
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", destination),
            TraversalMedium.Solid,
            TraversalCapability.Jump);

        NavigationMap forward = new NavigationMapBuilder("map", binding)
            .AddCell(source, SolidExplicit)
            .AddCell(destination, SolidExplicit)
            .AddTransition(sameIdDefinition)
            .AddTransitionRule(first)
            .AddTransitionRule(second)
            .Build();
        NavigationMap reverse = new NavigationMapBuilder("map", binding)
            .AddTransitionRule(second)
            .AddTransitionRule(first)
            .AddTransition(sameIdDefinition)
            .AddCell(destination, SolidExplicit)
            .AddCell(source, SolidExplicit)
            .Build();

        reverse.Should().Be(forward);
        reverse.GetHashCode().Should().Be(forward.GetHashCode());
        forward.TransitionRuleSpan[0].Id.Should().Be("a-rule");
        forward.TransitionRuleSpan[1].Id.Should().Be("b-rule");
        forward.Transitions.Should().ContainSingle(transition => transition.Id == "a-rule");
        TraversalTransitionRuleComparer.Instance.Compare(
            second,
            CreateRule("a-rule", actionCost: Fixed64.One)).Should().NotBe(0);

        Action duplicate = () => new NavigationMapBuilder("duplicate", binding)
            .AddTransitionRule(CreateRule("same"))
            .AddTransitionRule(CreateRule(
                "same",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas))
            .Build();
        Action defaultRule = () => new NavigationMapBuilder("default", binding)
            .AddTransitionRule(default)
            .Build();

        duplicate.Should().Throw<ArgumentException>();
        defaultRule.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OperationLimits_UseExplicitPositiveFiniteRuleCeilingsAndRejectExactOneOverTransactionally()
    {
        NavigationOperationLimits limits = CreateLimits(
            maxOverlayTransitionsPerMap: 2,
            maxOverlayTransitions: 3,
            maxTransitionRulesPerMap: 2,
            maxTransitionRules: 3);
        limits.MaxTransitionRulesPerMap.Should().Be(2);
        limits.MaxTransitionRules.Should().Be(3);
        Action zeroPerMap = () => _ = CreateLimits(maxTransitionRulesPerMap: 0);
        Action totalBelowPerMap = () => _ = CreateLimits(
            maxTransitionRulesPerMap: 2,
            maxTransitionRules: 1);

        zeroPerMap.Should().Throw<ArgumentException>();
        totalBelowPerMap.Should().Throw<ArgumentException>();
        var perMapProcessor = new NavigationOperationProcessor(limits);
        NavigationMap overPerMap = CreateRuleMap(
            "over",
            originX: 0,
            CreateRule("one"),
            CreateRule("two"),
            CreateRule("three"));
        NavigationMapCommitOperation rejectedAtAdmission = Commit(overPerMap, 1, 0);

        perMapProcessor.Admit(rejectedAtAdmission).Should().BeFalse();
        rejectedAtAdmission.Receipt.Rejection.Should().Be(
            NavigationOperationRejection.CapacityExceeded);
        perMapProcessor.Candidate.MapCount.Should().Be(0);
        perMapProcessor.Candidate.TransitionRuleCount.Should().Be(0);

        var totalProcessor = new NavigationOperationProcessor(limits);
        NavigationMapCommitOperation first = Commit(
            CreateRuleMap("first", 0, CreateRule("first-a"), CreateRule("first-b")),
            1,
            0);
        NavigationMapCommitOperation exact = Commit(
            CreateRuleMap("exact", 10, CreateRule("exact-a")),
            2,
            1);
        NavigationMapCommitOperation oneOver = Commit(
            CreateRuleMap("one-over", 20, CreateRule("one-over-a")),
            3,
            2);

        totalProcessor.Admit(first).Should().BeTrue();
        totalProcessor.ProcessFrame(0);
        totalProcessor.Admit(exact).Should().BeTrue();
        totalProcessor.ProcessFrame(1);
        totalProcessor.Candidate.TransitionRuleCount.Should().Be(3);
        totalProcessor.Admit(oneOver).Should().BeTrue();
        totalProcessor.ProcessFrame(2);

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        exact.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        oneOver.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        totalProcessor.Candidate.MapCount.Should().Be(2);
        totalProcessor.Candidate.TransitionRuleCount.Should().Be(3);
        totalProcessor.Candidate.TryGetMap("one-over", out _).Should().BeFalse();
    }

    [Fact]
    public void PreparedAndCandidateRetainedBytes_ChargeDefaultAndCanonicalRuleFactsExactly()
    {
        CreateConfiguration(GridStorageKind.Dense).TryNormalize(
            out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap emptyMap = new NavigationMapBuilder("map", binding).Build();
        NavigationMap authoredMap = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(GasDefault)
            .AddTransitionRule(CreateRule("rule"))
            .Build();
        var emptyPrepared = new PreparedNavigationMap(emptyMap, 1);
        var authoredPrepared = new PreparedNavigationMap(authoredMap, 2);

        long expectedAuthoringBytes = 64L + 80L + ("rule".Length * sizeof(char));
        authoredPrepared.RetainedBytes.Should().Be(
            emptyPrepared.RetainedBytes + expectedAuthoringBytes);

        var exactAdmission = new NavigationOperationProcessor(
            CreateLimits(maxPreparedMapBytes: authoredPrepared.RetainedBytes));
        NavigationMapCommitOperation exact = new(
            authoredPrepared,
            OverlayReplacementPolicy.Clear,
            1,
            0);
        exactAdmission.Admit(exact).Should().BeTrue();
        exactAdmission.ProcessFrame(0);
        long candidateBytesWithFacts = exactAdmission.Candidate.RetainedBytes;

        var oneBelowAdmission = new NavigationOperationProcessor(
            CreateLimits(maxPreparedMapBytes: authoredPrepared.RetainedBytes - 1));
        NavigationMapCommitOperation oneBelow = new(
            authoredPrepared,
            OverlayReplacementPolicy.Clear,
            1,
            0);
        oneBelowAdmission.Admit(oneBelow).Should().BeFalse();
        oneBelow.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        oneBelowAdmission.Candidate.MapCount.Should().Be(0);

        NavigationMapCommitOperation removeFacts = new(
            new PreparedNavigationMap(emptyMap, 3),
            OverlayReplacementPolicy.Clear,
            2,
            1);
        exactAdmission.Admit(removeFacts).Should().BeTrue();
        exactAdmission.ProcessFrame(1);

        (candidateBytesWithFacts - exactAdmission.Candidate.RetainedBytes)
            .Should().Be(expectedAuthoringBytes);
    }

    [Fact]
    public void Build_DefaultBacksBoundedExplicitDefinitionsWithoutMaterializingCellEntries()
    {
        CreateConfiguration(GridStorageKind.Sparse).TryNormalize(
            out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex source = default;
        VoxelIndex destination = new(1, 0, 0);
        binding.TryGetCellPrism(source, out GridCellPrism sourcePrism).Should().BeTrue();
        binding.TryGetCellPrism(destination, out GridCellPrism destinationPrism).Should().BeTrue();
        Vector3d sourceFoot = new(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z);
        Vector3d destinationFoot = new(
            destinationPrism.Center.X,
            destinationPrism.VerticalMin,
            destinationPrism.Center.Z);
        var connection = new NavigationConnection(
            "step",
            source,
            new NavigationCellAddress("map", destination),
            sourceFoot,
            destinationFoot,
            Fixed64.Zero,
            Fixed64.Half);
        var definition = new TraversalTransitionDefinition(
            "jump",
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            new NavigationCellAddress("map", destination),
            TraversalMedium.Solid,
            TraversalCapability.Jump);

        NavigationMap map = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(SolidExplicit)
            .AddConnection(connection)
            .AddTransition(definition)
            .Build();

        map.Cells.Should().BeEmpty();
        map.Connections.Should().ContainSingle();
        map.Transitions.Should().ContainSingle();

        var processor = new NavigationOperationProcessor(CreateLimits());
        NavigationMapCommitOperation install = Commit(map, 1, 0);
        processor.Admit(install).Should().BeTrue();
        processor.ProcessFrame(0);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void FinalAuthoringTypesAndLimits_ArePublicWithoutCompatibilityAliases()
    {
        Type ruleType = typeof(NavigationMap).Assembly.GetType(
            "Trailblazer.Pathing.TraversalTransitionRule",
            throwOnError: true)!;
        Type scopeType = typeof(NavigationMap).Assembly.GetType(
            "Trailblazer.Pathing.TraversalTransitionRuleScope",
            throwOnError: true)!;
        Type hintType = typeof(NavigationMap).Assembly.GetType(
            "Trailblazer.Pathing.TraversalTransitionLocomotionHints",
            throwOnError: true)!;

        ruleType.IsPublic.Should().BeTrue();
        scopeType.IsPublic.Should().BeTrue();
        hintType.IsPublic.Should().BeTrue();
        typeof(NavigationMap).GetProperty("DefaultCell").Should().NotBeNull();
        typeof(NavigationMap).GetProperty("TransitionRules").Should().NotBeNull();
        typeof(NavigationMapBuilder).GetMethod("SetDefaultCell").Should().NotBeNull();
        typeof(NavigationMapBuilder).GetMethod("AddTransitionRule").Should().NotBeNull();
        typeof(NavigationOperationLimits).GetProperty("MaxTransitionRulesPerMap")
            .Should().NotBeNull();
        typeof(NavigationOperationLimits).GetProperty("MaxTransitionRules")
            .Should().NotBeNull();
        typeof(TraversalTransitionDefinition).GetProperty("AdditionalCost")
            .Should().BeNull();
        typeof(TraversalTransitionDefinition).GetProperty("ActionCost")
            .Should().NotBeNull();
        typeof(TraversalTransitionDefinition).GetProperty("LocomotionHints")
            .Should().NotBeNull();
    }

    private static GridConfiguration CreateConfiguration(
        GridStorageKind storageKind,
        int originX = 0) => new(
        new Vector3d(originX, 0, 0),
        new Vector3d(originX + 3, 1, 1),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: storageKind);

    private static NavigationMapCommitOperation Commit(
        NavigationMap map,
        long sequence,
        int frame,
        long bakeVersion = 1) => new(
            new PreparedNavigationMap(map, bakeVersion),
            OverlayReplacementPolicy.Clear,
            sequence,
            frame);

    private static NavigationOverlayCommitOperation Overlay(
        string mapId,
        NavigationCellOverlayOperation[] cells,
        long sequence,
        int frame) => new(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta(mapId, cells) })),
            sequence,
            frame);

    private static NavigationCell GetCell(
        NavigationOperationProcessor processor,
        string mapId,
        VoxelIndex index)
    {
        processor.Candidate.TryGetSemanticState(
            new NavigationCellAddress(mapId, index),
            out _, out bool hasCell, out NavigationCell cell).Should().BeTrue();
        hasCell.Should().BeTrue();
        return cell;
    }

    private static bool HasCell(
        NavigationOperationProcessor processor,
        string mapId,
        VoxelIndex index)
    {
        processor.Candidate.TryGetSemanticState(
            new NavigationCellAddress(mapId, index),
            out _, out bool hasCell, out _).Should().BeTrue();
        return hasCell;
    }

    private static NavigationOperationLimits CreateLimits(
        long maxPreparedMapBytes = 1_000_000,
        int maxOverlayTransitionsPerMap = 16,
        int maxOverlayTransitions = 64,
        int maxTransitionRulesPerMap = 16,
        int maxTransitionRules = 64) => new(
        maxPendingOperations: 16,
        maxPendingDescriptorBytes: 1_000_000,
        maxPreparedMapBytes,
        maxBatchItems: 16,
        maxBatchDescriptorBytes: 1_000_000,
        maxBatchSortScratchBytes: 1_000_000,
        maxCorridorCells: 16,
        maxMaps: 8,
        maxRetainedMapIdentities: 16,
        maxOverlayCellsPerMap: 16,
        maxOverlayConnectionsPerMap: 16,
        maxOverlayTransitionsPerMap,
        maxOverlayCells: 64,
        maxOverlayConnections: 64,
        maxOverlayTransitions,
        maxTransitionRulesPerMap,
        maxTransitionRules);

    private static NavigationMap CreateRuleMap(
        string mapId,
        int originX,
        params TraversalTransitionRule[] rules)
    {
        var builder = new NavigationMapBuilder(
            mapId,
            CreateConfiguration(GridStorageKind.Dense, originX));
        for (int i = 0; i < rules.Length; i++)
            builder.AddTransitionRule(rules[i]);
        return builder.Build();
    }

    private static TraversalTransitionRule CreateRule(
        string id,
        TraversalTransitionType type = TraversalTransitionType.Climb,
        TraversalMedium sourceMedium = TraversalMedium.Solid,
        TraversalMedium destinationMedium = TraversalMedium.Solid,
        TraversalTransitionRuleScope scope = TraversalTransitionRuleScope.SameCell,
        TraversalCapability capabilities = TraversalCapability.Climb,
        Fixed64 actionCost = default,
        TraversalTransitionLocomotionHints hints =
            TraversalTransitionLocomotionHints.RequestClimb) => new(
                id,
                type,
                sourceMedium,
                destinationMedium,
                scope,
                capabilities,
                actionCost,
                hints);
}
