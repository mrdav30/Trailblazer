using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chronicler;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class Phase7VolumeTransitionDeterminismMatrixTests
{
    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("phase7-determinism", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    private static readonly NavigationWorkBudget Budget = new(
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536);

    private static readonly GuideSampleWorkBudget SampleBudget = new(
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536,
        65_536);

    private readonly ITestOutputHelper _output;

    public Phase7VolumeTransitionDeterminismMatrixTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData(
        "rect-2d-gas-astar",
        "4646FBA0496445099C112C890E59FB0D3E9E94BA5228ADD7D2BA881E9B1E03E5")]
    [InlineData(
        "rect-3d-liquid-flow",
        "97EC20AC5EFD39ECF19556E22ECFBA7CABE5713DABFD76F177407F0E1809FDD0")]
    [InlineData(
        "hex-pointy-gas-astar",
        "B920544577985B842F0F7A0A39E6FB2F74B9FEC7F9CF039C87081062F2DDAA28")]
    [InlineData(
        "hex-flat-liquid-flow",
        "10BBAA3C3A777ECEAA00489BE0032889A9D295D64540D9830ADAC47E9D165209")]
    [InlineData(
        "semantic-actions",
        "5B83DA6F84E08450168C637A4FBCEACED1FA367581D1AAB66EB565588F6E79E4")]
    [InlineData(
        "dynamic-publication",
        "3A406EA2C889970BB83F77F165AFED46F968160E916BFEF454367D1E33ADDAB2")]
    public void CanonicalPhase7Case_ShouldMatchCheckedInDigest(
        string caseName,
        string expectedDigest)
    {
        string canonical = BuildCanonical(caseName);
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        _output.WriteLine($"PHASE7_CANONICAL={caseName}|{canonical}");
        _output.WriteLine($"PHASE7_DIGEST={digest} CASE={caseName}");

        Assert.Equal(expectedDigest, digest);
    }

    [Fact]
    public void CanonicalPhase7Hashes_ShouldBeCultureInvariant()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            AssertDigest(
                "rect-2d-gas-astar",
                "4646FBA0496445099C112C890E59FB0D3E9E94BA5228ADD7D2BA881E9B1E03E5");
            AssertDigest(
                "rect-3d-liquid-flow",
                "97EC20AC5EFD39ECF19556E22ECFBA7CABE5713DABFD76F177407F0E1809FDD0");
            AssertDigest(
                "hex-pointy-gas-astar",
                "B920544577985B842F0F7A0A39E6FB2F74B9FEC7F9CF039C87081062F2DDAA28");
            AssertDigest(
                "hex-flat-liquid-flow",
                "10BBAA3C3A777ECEAA00489BE0032889A9D295D64540D9830ADAC47E9D165209");
            AssertDigest(
                "semantic-actions",
                "5B83DA6F84E08450168C637A4FBCEACED1FA367581D1AAB66EB565588F6E79E4");
            AssertDigest(
                "dynamic-publication",
                "3A406EA2C889970BB83F77F165AFED46F968160E916BFEF454367D1E33ADDAB2");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void AssertDigest(string caseName, string expectedDigest)
    {
        string canonical = BuildCanonical(caseName);
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(expectedDigest, digest);
    }

    private static string BuildCanonical(string caseName) => caseName switch
    {
        "rect-2d-gas-astar" => BuildRectangularGasAStarCanonical(),
        "rect-3d-liquid-flow" => BuildVolumeTopologyCanonical(
            caseName,
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop,
            TraversalMedium.Liquid,
            PathAlgorithm.FlowField),
        "hex-pointy-gas-astar" => BuildVolumeTopologyCanonical(
            caseName,
            GridTopologyKind.HexPrism,
            HexOrientation.PointyTop,
            TraversalMedium.Gas,
            PathAlgorithm.AStar),
        "hex-flat-liquid-flow" => BuildVolumeTopologyCanonical(
            caseName,
            GridTopologyKind.HexPrism,
            HexOrientation.FlatTop,
            TraversalMedium.Liquid,
            PathAlgorithm.FlowField),
        "semantic-actions" => BuildSemanticActionsCanonical(),
        "dynamic-publication" => BuildDynamicPublicationCanonical(),
        _ => throw new InvalidOperationException(
            $"Unknown Phase 7 digest case '{caseName}'.")
    };

    private static string BuildRectangularGasAStarCanonical()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(1, 0, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        Assert.True(context.World.TryAddGrid(configuration, out _));
        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));

        var builder = new NavigationMapBuilder("phase7-det-rect-2d", binding);
        for (int x = 0; x <= 1; x++)
        {
            for (int z = 0; z <= 1; z++)
            {
                builder.AddCell(
                    new VoxelIndex(x, 0, z),
                    GuidedPathTestScene.Cell(TraversalMedia.Gas));
            }
        }
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            builder.Build(),
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            Policy,
            policySequence: 2);

        var start = default(VoxelIndex);
        var destination = new VoxelIndex(1, 0, 1);
        PathQuery query = CreateQuery(
            binding,
            "phase7-det-rect-2d",
            start,
            destination,
            TraversalMedium.Gas,
            TraversalMedia.Gas,
            PathAlgorithm.AStar,
            allowTransitions: false);
        NavigationGuideStatus status = RequestSettledGuide(
            context,
            query,
            out NavigationGuideLease? acquired);
        Assert.Equal(NavigationGuideStatus.Success, status);
        Assert.NotNull(acquired);
        using NavigationGuideLease guide = acquired.Value;

        var canonical = new StringBuilder("rect-2d-gas-astar");
        Append(canonical, "status", (int)status);
        Append(canonical, "cost", guide.TotalCost.m_rawValue);
        AppendGuide(canonical, guide);
        return canonical.ToString();
    }

    private static string BuildDynamicPublicationCanonical()
    {
        const string mapId = "phase7-det-dynamic";
        const string unaffectedMapId = "phase7-det-unaffected";
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var affectedConfiguration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(9, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var unaffectedConfiguration = new GridConfiguration(
            new Vector3d(30, 0, 0),
            new Vector3d(32, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        VoxelIndex cliff = default;
        var water = new VoxelIndex(2, 0, 0);
        var movedWater = new VoxelIndex(4, 0, 0);
        var gasStart = new VoxelIndex(6, 0, 0);
        var gasMiddle = new VoxelIndex(7, 0, 0);
        var gasEnd = new VoxelIndex(8, 0, 0);
        var dualMedia = new VoxelIndex(9, 0, 0);
        VoxelIndex[] affectedIndices =
        {
            cliff,
            water,
            movedWater,
            gasStart,
            gasMiddle,
            gasEnd,
            dualMedia
        };
        Assert.True(context.World.TryAddGrid(
            affectedConfiguration,
            affectedIndices,
            out _));
        Assert.True(context.World.TryAddGrid(unaffectedConfiguration, out _));
        Assert.True(affectedConfiguration.TryNormalize(
            out NormalizedGridConfiguration affectedBinding));
        Assert.True(unaffectedConfiguration.TryNormalize(
            out NormalizedGridConfiguration unaffectedBinding));

        NavigationMap affectedMap = BuildDynamicMap(
            mapId,
            affectedBinding,
            cliff,
            water,
            movedWater,
            gasStart,
            gasMiddle,
            gasEnd,
            dualMedia,
            Fixed64.One);
        var affectedOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(affectedMap, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        NavigationMap unaffectedMap = new NavigationMapBuilder(
                unaffectedMapId,
                unaffectedBinding)
            .AddCell(default, GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .AddCell(
                new VoxelIndex(1, 0, 0),
                GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .AddCell(
                new VoxelIndex(2, 0, 0),
                GuidedPathTestScene.Cell(TraversalMedia.Solid))
            .Build();
        var unaffectedOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(unaffectedMap, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            Policy,
            publicationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        Assert.True(context.Pathing.Admit(affectedOperation));
        Assert.True(context.Pathing.Admit(unaffectedOperation));
        Assert.True(context.Pathing.Admit(policyOperation));
        GuidedPathTestScene.AdvanceUntilApplied(
            context,
            affectedOperation.Receipt,
            unaffectedOperation.Receipt,
            policyOperation.Receipt);

        var canonical = new StringBuilder("dynamic-publication");
        PathQuery ladderQuery = CreateQuery(
            affectedBinding,
            mapId,
            cliff,
            water,
            TraversalMedium.Solid,
            TraversalMedia.Liquid,
            PathAlgorithm.AStar,
            allowTransitions: true);
        Append(
            canonical,
            "ladder-absent",
            (int)RequestSettledGuide(context, ladderQuery, out _));
        PublishTransitionOverlay(
            context,
            mapId,
            operationSequence: 4,
            TraversalTransitionOverlayOperation.Upsert(new TraversalTransitionDefinition(
                "ladder-down",
                TraversalTransitionType.Climb,
                cliff,
                TraversalMedium.Solid,
                new NavigationCellAddress(mapId, water),
                TraversalMedium.Liquid,
                TraversalCapability.Climb,
                locomotionHints: TraversalTransitionLocomotionHints.RequestClimb)),
            TraversalTransitionOverlayOperation.Upsert(new TraversalTransitionDefinition(
                "ladder-up",
                TraversalTransitionType.Climb,
                water,
                TraversalMedium.Liquid,
                new NavigationCellAddress(mapId, cliff),
                TraversalMedium.Solid,
                TraversalCapability.Climb,
                locomotionHints: TraversalTransitionLocomotionHints.RequestClimb)));
        AppendTransitionQuery(canonical, "ladder", context, ladderQuery);

        Assert.Equal(
            NavigationGuideStatus.Success,
            RequestSettledGuide(
                context,
                ladderQuery,
                out NavigationGuideLease? heldAcquired));
        Assert.NotNull(heldAcquired);
        using NavigationGuideLease held = heldAcquired.Value;
        NavigationGuideStep heldStep = GuidedPathTestScene.AdvanceToTransition(held);
        PublishTransitionOverlay(
            context,
            mapId,
            operationSequence: 5,
            TraversalTransitionOverlayOperation.Suppress("ladder-down"),
            TraversalTransitionOverlayOperation.Suppress("ladder-up"));
        Append(canonical, "ladder-after-suppress", (int)held.Status);
        Append(
            canonical,
            "ladder-stale-completion",
            (int)held.CompletePendingTransition(heldStep.Transition));

        PathQuery affectedQuery = CreateQuery(
            affectedBinding,
            mapId,
            gasStart,
            gasEnd,
            TraversalMedium.Gas,
            TraversalMedia.Gas,
            PathAlgorithm.AStar,
            allowTransitions: false);
        PathQuery unaffectedQuery = CreateQuery(
            unaffectedBinding,
            unaffectedMapId,
            default,
            new VoxelIndex(2, 0, 0),
            TraversalMedium.Solid,
            TraversalMedia.Solid,
            PathAlgorithm.AStar,
            allowTransitions: false);
        Assert.Equal(
            NavigationGuideStatus.Success,
            RequestSettledGuide(
                context,
                affectedQuery,
                out NavigationGuideLease? affectedAcquired));
        Assert.NotNull(affectedAcquired);
        using NavigationGuideLease affected = affectedAcquired.Value;
        Assert.Equal(
            NavigationGuideStatus.Success,
            RequestSettledGuide(
                context,
                unaffectedQuery,
                out NavigationGuideLease? unaffectedAcquired));
        Assert.NotNull(unaffectedAcquired);
        using NavigationGuideLease unaffected = unaffectedAcquired.Value;
        PublishCellOverlay(
            context,
            mapId,
            operationSequence: 6,
            NavigationCellOverlayOperation.Set(
                gasMiddle,
                GuidedPathTestScene.Cell(TraversalMedia.Liquid)));
        Append(canonical, "flood-affected", (int)affected.Status);
        Append(canonical, "flood-unaffected", (int)unaffected.Status);
        Append(
            canonical,
            "flood-query",
            (int)RequestSettledGuide(context, affectedQuery, out _));
        PublishCellOverlay(
            context,
            mapId,
            operationSequence: 7,
            NavigationCellOverlayOperation.RevertToBake(gasMiddle));
        Append(
            canonical,
            "drain-query",
            (int)RequestSettledGuide(
                context,
                affectedQuery,
                out NavigationGuideLease? drainedAcquired));
        Assert.NotNull(drainedAcquired);
        using (NavigationGuideLease drained = drainedAcquired.Value)
            Append(canonical, "drain-cost", drained.TotalCost.m_rawValue);

        PathQuery ruleQuery = CreateQuery(
            affectedBinding,
            mapId,
            dualMedia,
            dualMedia,
            TraversalMedium.Liquid,
            TraversalMedia.Gas,
            PathAlgorithm.AStar,
            allowTransitions: true);
        Assert.Equal(
            NavigationGuideStatus.Success,
            RequestSettledGuide(
                context,
                ruleQuery,
                out NavigationGuideLease? oldRuleAcquired));
        Assert.NotNull(oldRuleAcquired);
        using NavigationGuideLease oldRule = oldRuleAcquired.Value;
        Append(canonical, "rule-old-cost", oldRule.TotalCost.m_rawValue);
        NavigationMap changedMap = BuildDynamicMap(
            mapId,
            affectedBinding,
            cliff,
            water,
            movedWater,
            gasStart,
            gasMiddle,
            gasEnd,
            dualMedia,
            (Fixed64)5);
        var changedOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(changedMap, bakeVersion: 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            operationSequence: 8,
            effectiveFrame: context.FrameCount + 1);
        Assert.True(context.Pathing.Admit(changedOperation));
        GuidedPathTestScene.AdvanceUntilApplied(context, changedOperation.Receipt);
        Append(canonical, "rule-old-status", (int)oldRule.Status);
        AppendTransitionQuery(canonical, "rule-new", context, ruleQuery);

        var sourceRecord = new PathQueryRecord(affectedQuery);
        string json = JsonRecordSerializer.Serialize(sourceRecord);
        var restoredRecord = new PathQueryRecord();
        JsonRecordSerializer.Populate(restoredRecord, json);
        Append(canonical, "json-query-equal", restoredRecord.Query == affectedQuery);
        Append(
            canonical,
            "json-sha256",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        return canonical.ToString();
    }

    private static NavigationMap BuildDynamicMap(
        string mapId,
        NormalizedGridConfiguration binding,
        VoxelIndex cliff,
        VoxelIndex water,
        VoxelIndex movedWater,
        VoxelIndex gasStart,
        VoxelIndex gasMiddle,
        VoxelIndex gasEnd,
        VoxelIndex dualMedia,
        Fixed64 actionCost) => new NavigationMapBuilder(mapId, binding)
        .AddCell(cliff, GuidedPathTestScene.Cell(TraversalMedia.Solid))
        .AddCell(water, GuidedPathTestScene.Cell(TraversalMedia.Liquid))
        .AddCell(movedWater, GuidedPathTestScene.Cell(TraversalMedia.Liquid))
        .AddCell(gasStart, GuidedPathTestScene.Cell(TraversalMedia.Gas))
        .AddCell(gasMiddle, GuidedPathTestScene.Cell(TraversalMedia.Gas))
        .AddCell(gasEnd, GuidedPathTestScene.Cell(TraversalMedia.Gas))
        .AddCell(
            dualMedia,
            GuidedPathTestScene.Cell(TraversalMedia.Liquid | TraversalMedia.Gas))
        .AddTransitionRule(new TraversalTransitionRule(
            "mutable-takeoff",
            TraversalTransitionType.Takeoff,
            TraversalMedium.Liquid,
            TraversalMedium.Gas,
            TraversalTransitionRuleScope.SameCell,
            TraversalCapability.Fly,
            actionCost,
            TraversalTransitionLocomotionHints.None))
        .Build();

    private static string BuildSemanticActionsCanonical()
    {
        const string mapId = "phase7-det-actions";
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(23, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var sameCell = default(VoxelIndex);
        var jumpSource = new VoxelIndex(2, 0, 0);
        var jumpTarget = new VoxelIndex(4, 0, 0);
        var climbSource = new VoxelIndex(6, 0, 0);
        var climbTarget = new VoxelIndex(8, 0, 0);
        var teleporterSource = new VoxelIndex(10, 0, 0);
        var teleporterTarget = new VoxelIndex(20, 0, 0);
        var duckWater = new VoxelIndex(22, 0, 0);
        var duckAir = new VoxelIndex(23, 0, 0);
        VoxelIndex[] indices =
        {
            sameCell,
            jumpSource,
            jumpTarget,
            climbSource,
            climbTarget,
            teleporterSource,
            teleporterTarget,
            duckWater,
            duckAir
        };
        Assert.True(context.World.TryAddGrid(configuration, indices, out _));
        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));
        NavigationCell solid = GuidedPathTestScene.Cell(TraversalMedia.Solid);
        NavigationMap map = new NavigationMapBuilder(mapId, binding)
            .AddCell(
                sameCell,
                GuidedPathTestScene.Cell(TraversalMedia.Liquid | TraversalMedia.Gas))
            .AddCell(jumpSource, solid)
            .AddCell(jumpTarget, solid)
            .AddCell(climbSource, solid)
            .AddCell(climbTarget, solid)
            .AddCell(teleporterSource, solid)
            .AddCell(
                teleporterTarget,
                new NavigationCell(
                    TraversalMedia.Solid,
                    TraversalCapability.None,
                    default,
                    enterCost: (Fixed64)2,
                    radiusClearance: Fixed64.One,
                    heightClearance: Fixed64.One))
            .AddCell(duckWater, GuidedPathTestScene.Cell(TraversalMedia.Liquid))
            .AddCell(duckAir, GuidedPathTestScene.Cell(TraversalMedia.Gas))
            .AddTransitionRule(new TraversalTransitionRule(
                "same-cell-takeoff",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.Fly,
                actionCost: (Fixed64)2,
                TraversalTransitionLocomotionHints.None))
            .AddTransitionRule(new TraversalTransitionRule(
                "duck-takeoff",
                TraversalTransitionType.Takeoff,
                TraversalMedium.Liquid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.PositiveFaceContact,
                TraversalCapability.Swim | TraversalCapability.Fly,
                actionCost: (Fixed64)2,
                TraversalTransitionLocomotionHints.None))
            .AddTransition(new TraversalTransitionDefinition(
                "same-medium-jump",
                TraversalTransitionType.Jump,
                jumpSource,
                TraversalMedium.Solid,
                new NavigationCellAddress(mapId, jumpTarget),
                TraversalMedium.Solid,
                TraversalCapability.Jump,
                actionCost: (Fixed64)3))
            .AddTransition(new TraversalTransitionDefinition(
                "same-medium-climb",
                TraversalTransitionType.Climb,
                climbSource,
                TraversalMedium.Solid,
                new NavigationCellAddress(mapId, climbTarget),
                TraversalMedium.Solid,
                TraversalCapability.Climb,
                actionCost: (Fixed64)4,
                TraversalTransitionLocomotionHints.RequestClimb))
            .AddTransition(new TraversalTransitionDefinition(
                "cheap-teleporter",
                TraversalTransitionType.Custom,
                teleporterSource,
                TraversalMedium.Solid,
                new NavigationCellAddress(mapId, teleporterTarget),
                TraversalMedium.Solid,
                TraversalCapability.Teleport,
                actionCost: Fixed64.One))
            .Build();
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            Policy,
            policySequence: 2);

        var canonical = new StringBuilder("semantic-actions");
        AppendTransitionQuery(
            canonical,
            "same-cell",
            context,
            CreateQuery(
                binding,
                mapId,
                sameCell,
                sameCell,
                TraversalMedium.Liquid,
                TraversalMedia.Gas,
                PathAlgorithm.AStar,
                allowTransitions: true));
        AppendTransitionQuery(
            canonical,
            "jump",
            context,
            CreateQuery(
                binding,
                mapId,
                jumpSource,
                jumpTarget,
                TraversalMedium.Solid,
                TraversalMedia.Solid,
                PathAlgorithm.AStar,
                allowTransitions: true));
        AppendTransitionQuery(
            canonical,
            "climb",
            context,
            CreateQuery(
                binding,
                mapId,
                climbSource,
                climbTarget,
                TraversalMedium.Solid,
                TraversalMedia.Solid,
                PathAlgorithm.AStar,
                allowTransitions: true));
        AppendTransitionQuery(
            canonical,
            "teleporter",
            context,
            CreateQuery(
                binding,
                mapId,
                teleporterSource,
                teleporterTarget,
                TraversalMedium.Solid,
                TraversalMedia.Solid,
                PathAlgorithm.AStar,
                allowTransitions: true));

        NavigationTransitionInstruction duckAction = AppendTransitionQuery(
            canonical,
            "duck-astar",
            context,
            CreateQuery(
                binding,
                mapId,
                duckWater,
                duckAir,
                TraversalMedium.Liquid,
                TraversalMedia.Gas,
                PathAlgorithm.AStar,
                allowTransitions: true));
        PathQuery duckQuery = CreateQuery(
            binding,
            mapId,
            duckWater,
            duckAir,
            TraversalMedium.Liquid,
            TraversalMedia.Gas,
            PathAlgorithm.FlowField,
            allowTransitions: true);
        NavigationGuideStatus duckStatus = RequestSettledFlow(
            context,
            duckQuery,
            out NavigationFlowFieldLease? duckAcquired);
        Assert.Equal(NavigationGuideStatus.Success, duckStatus);
        Assert.NotNull(duckAcquired);
        using (NavigationFlowFieldLease duck = duckAcquired.Value)
        {
            NavigationGuideStatus sampleStatus = duck.TrySample(
                duckAction.SourcePosition,
                SampleBudget,
                out NavigationFlowSample sample);
            Assert.Equal(NavigationGuideStatus.Success, sampleStatus);
            Assert.True(sample.HasTransition);
            Append(canonical, "duck-status", (int)duckStatus);
            AppendTransition(canonical, "duck", sample.Transition);
            Assert.Equal(
                NavigationGuideStatus.Success,
                duck.CompletePendingTransition(sample.Transition));
            Append(canonical, "duck-completed", true);
        }

        PathQuery blockedDuck = new(
            duckQuery.Start,
            duckQuery.End,
            new NavigationAgentProfile(
                duckQuery.Agent.Shape,
                duckQuery.Agent.MaxStepUp,
                duckQuery.Agent.MaxDropDown,
                duckQuery.Agent.ArrivalRadius,
                duckQuery.Agent.AllowedMedia,
                TraversalCapability.Swim),
            duckQuery.AreaPolicy,
            duckQuery.Traversal,
            duckQuery.Algorithm,
            duckQuery.Budget,
            duckQuery.AllowTransitions,
            duckQuery.FlowField);
        Append(
            canonical,
            "swimmer-status",
            (int)RequestSettledFlow(context, blockedDuck, out _));
        return canonical.ToString();
    }

    private static string BuildVolumeTopologyCanonical(
        string caseName,
        GridTopologyKind topology,
        HexOrientation orientation,
        TraversalMedium medium,
        PathAlgorithm algorithm)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = topology == GridTopologyKind.HexPrism
            ? GridTopologyMetrics.Hex((Fixed64)2, (Fixed64)3, orientation)
            : GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)3,
                (Fixed64)4);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(12, 12, 12),
            topologyKind: topology,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));

        VoxelIndex source;
        VoxelIndex target;
        if (topology == GridTopologyKind.HexPrism)
        {
            source = FindCompleteHexCenter(binding);
            VoxelIndex offset = FindHexVerticalDiagonal();
            target = new VoxelIndex(
                source.x + offset.x,
                source.y + offset.y,
                source.z + offset.z);
        }
        else
        {
            source = new VoxelIndex(2, 2, 2);
            target = new VoxelIndex(3, 3, 3);
        }

        VoxelIndex[] indices;
        if (topology == GridTopologyKind.HexPrism)
        {
            indices = new VoxelIndex[HexDirectionUtility.Offsets.Length + 1];
            indices[0] = source;
            for (int i = 0; i < HexDirectionUtility.Offsets.Length; i++)
            {
                VoxelIndex offset = HexDirectionUtility.Offsets[i];
                indices[i + 1] = new VoxelIndex(
                    source.x + offset.x,
                    source.y + offset.y,
                    source.z + offset.z);
            }
        }
        else
        {
            indices = new VoxelIndex[8];
            int write = 0;
            for (int x = source.x; x <= target.x; x++)
            {
                for (int y = source.y; y <= target.y; y++)
                {
                    for (int z = source.z; z <= target.z; z++)
                        indices[write++] = new VoxelIndex(x, y, z);
                }
            }
        }
        Assert.True(context.World.TryAddGrid(configuration, indices, out _));
        TraversalMedia media = medium == TraversalMedium.Gas
            ? TraversalMedia.Gas
            : TraversalMedia.Liquid;
        string mapId = $"phase7-det-{caseName}";
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int i = 0; i < indices.Length; i++)
            builder.AddCell(indices[i], GuidedPathTestScene.Cell(media));
        NavigationMap map = builder.Build();
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            Policy,
            policySequence: 2);

        PathQuery query = CreateQuery(
            binding,
            mapId,
            source,
            target,
            medium,
            media,
            algorithm,
            allowTransitions: false);
        var canonical = new StringBuilder(caseName);
        if (algorithm == PathAlgorithm.AStar)
        {
            NavigationGuideStatus status = RequestSettledGuide(
                context,
                query,
                out NavigationGuideLease? acquired);
            Assert.Equal(NavigationGuideStatus.Success, status);
            Assert.NotNull(acquired);
            using NavigationGuideLease guide = acquired.Value;
            Append(canonical, "status", (int)status);
            Append(canonical, "cost", guide.TotalCost.m_rawValue);
            AppendGuide(canonical, guide);
        }
        else
        {
            NavigationGuideStatus status = RequestSettledFlow(
                context,
                query,
                out NavigationFlowFieldLease? acquired);
            Assert.Equal(NavigationGuideStatus.Success, status);
            Assert.NotNull(acquired);
            using NavigationFlowFieldLease guide = acquired.Value;
            NavigationGuideStatus sampleStatus = guide.TrySample(
                GuidedPathTestScene.Anchor(binding, source),
                SampleBudget,
                out NavigationFlowSample sample);
            Assert.Equal(NavigationGuideStatus.Success, sampleStatus);
            Append(canonical, "status", (int)status);
            Append(canonical, "sample-status", (int)sampleStatus);
            AppendVector(canonical, "heading", sample.Heading);
            AppendVector(canonical, "target", sample.Target);
            Append(canonical, "medium", (int)sample.Medium);
            Append(canonical, "transition", sample.HasTransition);
        }
        return canonical.ToString();
    }

    private static PathQuery CreateQuery(
        NormalizedGridConfiguration binding,
        string mapId,
        VoxelIndex start,
        VoxelIndex destination,
        TraversalMedium startMedium,
        TraversalMedia targetMedia,
        PathAlgorithm algorithm,
        bool allowTransitions)
    {
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.Jump
                | TraversalCapability.Climb
                | TraversalCapability.Swim
                | TraversalCapability.Fly
                | TraversalCapability.Teleport);
        return new PathQuery(
            new NavigationEndpoint(GuidedPathTestScene.Anchor(binding, start), mapId),
            new NavigationEndpoint(
                GuidedPathTestScene.Anchor(binding, destination),
                mapId),
            profile,
            Policy.Key,
            new TraversalIntent(startMedium, targetMedia),
            algorithm,
            Budget,
            allowTransitions,
            algorithm == PathAlgorithm.FlowField
                ? new FlowFieldQueryOptions(Fixed64.Zero)
                : default);
    }

    private static NavigationGuideStatus RequestSettledGuide(
        TrailblazerWorldContext context,
        PathQuery query,
        out NavigationGuideLease? guide)
    {
        NavigationGuideStatus status = NavigationGuideStatus.Stale;
        guide = null;
        for (int frame = 0;
            frame < 1_024 && status == NavigationGuideStatus.Stale;
            frame++)
        {
            context.Simulate();
            status = context.Guides.RequestGuide(query, out guide);
        }
        return status;
    }

    private static void PublishTransitionOverlay(
        TrailblazerWorldContext context,
        string mapId,
        long operationSequence,
        params TraversalTransitionOverlayOperation[] transitions)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[]
                {
                    new NavigationMapOverlayDelta(
                        mapId,
                        transitions: transitions)
                })),
            operationSequence,
            effectiveFrame: context.FrameCount + 1);
        Assert.True(context.Pathing.Admit(operation));
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }

    private static void PublishCellOverlay(
        TrailblazerWorldContext context,
        string mapId,
        long operationSequence,
        params NavigationCellOverlayOperation[] cells)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta(mapId, cells) })),
            operationSequence,
            effectiveFrame: context.FrameCount + 1);
        Assert.True(context.Pathing.Admit(operation));
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }

    private static NavigationTransitionInstruction AppendTransitionQuery(
        StringBuilder canonical,
        string prefix,
        TrailblazerWorldContext context,
        PathQuery query)
    {
        NavigationGuideStatus status = RequestSettledGuide(
            context,
            query,
            out NavigationGuideLease? acquired);
        Assert.Equal(NavigationGuideStatus.Success, status);
        Assert.NotNull(acquired);
        using NavigationGuideLease guide = acquired.Value;
        Append(canonical, $"{prefix}-status", (int)status);
        Append(canonical, $"{prefix}-cost", guide.TotalCost.m_rawValue);

        NavigationGuideStep step = GuidedPathTestScene.AdvanceToTransition(guide);

        Assert.Equal(NavigationGuideStatus.Success, guide.Status);
        AppendVector(canonical, $"{prefix}-action-step", step.Position);
        AppendTransition(canonical, prefix, step.Transition);
        NavigationGuideStatus completion = guide.CompletePendingTransition(
            step.Transition);
        Assert.Equal(NavigationGuideStatus.Success, completion);
        Append(canonical, $"{prefix}-complete", (int)completion);
        Assert.Equal(
            NavigationGuideStatus.Success,
            guide.TryGetCurrentStep(out NavigationGuideStep destination));
        AppendAddress(canonical, $"{prefix}-destination", destination.Address);
        Append(canonical, $"{prefix}-destination-medium", (int)destination.Medium);
        return step.Transition;
    }

    private static void AppendTransition(
        StringBuilder canonical,
        string prefix,
        NavigationTransitionInstruction transition)
    {
        Append(canonical, $"{prefix}-identity-kind", (int)transition.IdentityKind);
        Append(canonical, $"{prefix}-id", transition.Id);
        Append(canonical, $"{prefix}-type", (int)transition.Type);
        AppendAddress(canonical, $"{prefix}-source", transition.SourceAddress);
        AppendAddress(
            canonical,
            $"{prefix}-transition-destination",
            transition.DestinationAddress);
        Append(canonical, $"{prefix}-source-medium", (int)transition.SourceMedium);
        Append(
            canonical,
            $"{prefix}-transition-destination-medium",
            (int)transition.DestinationMedium);
        AppendVector(
            canonical,
            $"{prefix}-source-position",
            transition.SourcePosition);
        AppendVector(
            canonical,
            $"{prefix}-destination-position",
            transition.DestinationPosition);
        Append(canonical, $"{prefix}-hints", (int)transition.LocomotionHints);
    }

    private static NavigationGuideStatus RequestSettledFlow(
        TrailblazerWorldContext context,
        PathQuery query,
        out NavigationFlowFieldLease? guide)
    {
        NavigationGuideStatus status = NavigationGuideStatus.Stale;
        guide = null;
        for (int frame = 0;
            frame < 1_024 && status == NavigationGuideStatus.Stale;
            frame++)
        {
            context.Simulate();
            status = context.Guides.RequestFlowField(query, out guide);
        }
        return status;
    }

    private static VoxelIndex FindCompleteHexCenter(
        NormalizedGridConfiguration binding)
    {
        for (int y = 1; y < binding.Height - 1; y++)
        {
            for (int q = 1; q < binding.Width - 1; q++)
            {
                for (int r = 1; r < binding.Length - 1; r++)
                {
                    var candidate = new VoxelIndex(q, y, r);
                    bool complete = binding.IsValidIndex(candidate);
                    for (int i = 0;
                        complete && i < HexDirectionUtility.Offsets.Length;
                        i++)
                    {
                        VoxelIndex offset = HexDirectionUtility.Offsets[i];
                        complete = binding.IsValidIndex(new VoxelIndex(
                            candidate.x + offset.x,
                            candidate.y + offset.y,
                            candidate.z + offset.z));
                    }
                    if (complete)
                        return candidate;
                }
            }
        }
        throw new InvalidOperationException(
            "The determinism configuration has no complete hex neighborhood.");
    }

    private static VoxelIndex FindHexVerticalDiagonal()
    {
        for (int i = 0; i < HexDirectionUtility.Offsets.Length; i++)
        {
            var direction = (HexDirection)i;
            if (!HexDirectionUtility.IsPlanar(direction)
                && !HexDirectionUtility.IsVertical(direction))
            {
                return HexDirectionUtility.Offsets[i];
            }
        }
        throw new InvalidOperationException(
            "GridForge exposed no hex vertical-diagonal direction.");
    }

    private static void AppendGuide(StringBuilder builder, NavigationGuideLease guide)
    {
        Append(builder, "steps", guide.StepCount);
        for (int ordinal = 0; ordinal < guide.StepCount; ordinal++)
        {
            Assert.Equal(
                NavigationGuideStatus.Success,
                guide.TryGetCurrentStep(out NavigationGuideStep step));
            AppendAddress(builder, $"step-{ordinal}-address", step.Address);
            AppendVector(builder, $"step-{ordinal}-position", step.Position);
            Append(builder, $"step-{ordinal}-medium", (int)step.Medium);
            Append(builder, $"step-{ordinal}-transition", step.HasTransition);
            if (ordinal + 1 < guide.StepCount)
            {
                Assert.Equal(
                    NavigationGuideStatus.Success,
                    guide.TryAdvanceStep());
            }
        }
    }

    private static void Append(StringBuilder builder, string name, object value) =>
        builder.Append('|').Append(name).Append('=')
            .Append(Convert.ToString(value, CultureInfo.InvariantCulture));

    private static void AppendVector(
        StringBuilder builder,
        string name,
        Vector3d value)
    {
        Append(builder, $"{name}-x", value.X.m_rawValue);
        Append(builder, $"{name}-y", value.Y.m_rawValue);
        Append(builder, $"{name}-z", value.Z.m_rawValue);
    }

    private static void AppendAddress(
        StringBuilder builder,
        string name,
        NavigationCellAddress value)
    {
        Append(builder, $"{name}-map", value.MapId);
        Append(builder, $"{name}-x", value.Index.x);
        Append(builder, $"{name}-y", value.Index.y);
        Append(builder, $"{name}-z", value.Index.z);
    }
}
