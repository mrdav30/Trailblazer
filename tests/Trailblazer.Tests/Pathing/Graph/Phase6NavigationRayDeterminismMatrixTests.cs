using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chronicler;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class Phase6NavigationRayDeterminismMatrixTests
{
    private readonly ITestOutputHelper _output;

    public Phase6NavigationRayDeterminismMatrixTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData("rectangular", "E1BF2BD10AADF42E82698A34B5CC90A67DAE9F0CEDE294341F6FB9D8F1A1BE0D")]
    [InlineData("pointy", "9DB457CAAF426CB3E7455C20CD217FDC6B0E45169AACD3F0A6B5B854AB00A938")]
    [InlineData("flat", "F832D7C1407131AEC631F7F32755EA10FC8C55F3F5A6D9094B9F91B08AD2FBEC")]
    [InlineData("tie-overlap", "7386AE04968E41FCFDDF58094A55C69B115EF9B81F928AC178D658AA1293A04D")]
    [InlineData("endpoint", "B49DA360BC80EF8F6CFABE44D6D31643463876048C1ABDA6FAB476A4912681DF")]
    [InlineData("simplified-a-star", "D6CCF7174648A3492B5BF470D819D40E6989ED737D89D2BB9919D988CD40F7A4")]
    [InlineData("direct-steering", "84606A6ABE11DEA6386228D31A454C081B3F27252CBBAA8A9DF42115FFDAB3E4")]
    [InlineData("flow-rejoin", "6A8C08BEC0ED1392F9C625FCA1923D1D7EC9618EEBA8B0EA0E8CE1267BBDA949")]
    [InlineData("mutation", "65B30E3C16D68122BB2ED09FC5C09359B37A489982B8C1A501FC2C5A4A362E50")]
    [InlineData("serialization", "9EC3269D86D31F74437A408544174A01D33F78D5835EB6B10556A48B3A02010B")]
    public void CanonicalPhase6Case_ShouldMatchCheckedInDigest(
        string caseName,
        string expectedDigest)
    {
        string canonical = BuildCanonical(caseName);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        _output.WriteLine($"PHASE6_RAY_CANONICAL={caseName}|{canonical}");
        _output.WriteLine($"PHASE6_RAY_DIGEST={digest} CASE={caseName}");

        Assert.Equal(expectedDigest, digest);
    }

    [Fact]
    public void CanonicalPhase6Hashes_ShouldBeCultureInvariant()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            AssertDigest("rectangular", "E1BF2BD10AADF42E82698A34B5CC90A67DAE9F0CEDE294341F6FB9D8F1A1BE0D");
            AssertDigest("pointy", "9DB457CAAF426CB3E7455C20CD217FDC6B0E45169AACD3F0A6B5B854AB00A938");
            AssertDigest("flat", "F832D7C1407131AEC631F7F32755EA10FC8C55F3F5A6D9094B9F91B08AD2FBEC");
            AssertDigest("tie-overlap", "7386AE04968E41FCFDDF58094A55C69B115EF9B81F928AC178D658AA1293A04D");
            AssertDigest("endpoint", "B49DA360BC80EF8F6CFABE44D6D31643463876048C1ABDA6FAB476A4912681DF");
            AssertDigest("simplified-a-star", "D6CCF7174648A3492B5BF470D819D40E6989ED737D89D2BB9919D988CD40F7A4");
            AssertDigest("direct-steering", "84606A6ABE11DEA6386228D31A454C081B3F27252CBBAA8A9DF42115FFDAB3E4");
            AssertDigest("flow-rejoin", "6A8C08BEC0ED1392F9C625FCA1923D1D7EC9618EEBA8B0EA0E8CE1267BBDA949");
            AssertDigest("mutation", "65B30E3C16D68122BB2ED09FC5C09359B37A489982B8C1A501FC2C5A4A362E50");
            AssertDigest("serialization", "9EC3269D86D31F74437A408544174A01D33F78D5835EB6B10556A48B3A02010B");
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
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(expectedDigest, digest);
    }

    private static string BuildCanonical(string caseName) => caseName switch
    {
        "rectangular" => BuildTopologyCanonical(
            GridTopologyKind.RectangularPrism,
            HexOrientation.PointyTop),
        "pointy" => BuildTopologyCanonical(
            GridTopologyKind.HexPrism,
            HexOrientation.PointyTop),
        "flat" => BuildTopologyCanonical(
            GridTopologyKind.HexPrism,
            HexOrientation.FlatTop),
        "tie-overlap" => BuildTieOverlapCanonical(),
        "endpoint" => BuildEndpointCanonical(),
        "simplified-a-star" => BuildSimplifiedAStarCanonical(),
        "direct-steering" => BuildDirectSteeringCanonical(),
        "flow-rejoin" => BuildFlowRejoinCanonical(),
        "mutation" => BuildMutationCanonical(),
        "serialization" => BuildSerializationCanonical(),
        _ => throw new InvalidOperationException($"Unknown Phase 6 digest case '{caseName}'.")
    };

    private static string BuildTopologyCanonical(
        GridTopologyKind topology,
        HexOrientation orientation)
    {
        using var world = new GridWorld();
        GridTopologyMetrics metrics = topology == GridTopologyKind.RectangularPrism
            ? GridTopologyMetrics.Rectangular(Fixed64.One)
            : GridTopologyMetrics.Hex((Fixed64)2, Fixed64.One, orientation);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(12, 2, 12),
            topologyKind: topology,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));
        VoxelIndex start;
        VoxelIndex middle;
        VoxelIndex end;
        if (topology == GridTopologyKind.RectangularPrism)
        {
            start = default;
            middle = new VoxelIndex(1, 0, 0);
            end = new VoxelIndex(2, 0, 0);
        }
        else
        {
            FindHexLine(binding, out start, out middle, out end);
        }
        string mapId = topology == GridTopologyKind.RectangularPrism
            ? "phase6-rect"
            : orientation == HexOrientation.PointyTop
                ? "phase6-pointy"
                : "phase6-flat";
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                configuration,
                new[] { start, middle, end },
                mapId);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        RayRun run = RunRay(
            world,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            NavigationAStarExitTestHarness.GetFoot(binding, start),
            NavigationAStarExitTestHarness.GetFoot(binding, end));
        Assert.Equal(NavigationRayStatus.Success, run.Result.Status);

        var builder = Begin("topology");
        Append(builder, "kind", (int)topology);
        Append(builder, "orientation", (int)orientation);
        AppendRay(builder, run);
        return builder.ToString();
    }

    private static string BuildTieOverlapCanonical()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Assert.True(fixture.Context.World.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(16))),
            out _));
        RayRun run = RunRay(
            fixture.Context.World,
            store,
            fixture.Graph,
            fixture.DefaultProfile,
            fixture.Start,
            fixture.End,
            mapCapacity: 4);
        Assert.Equal(NavigationRayStatus.Success, run.Result.Status);

        int largestTieGroup = 0;
        int currentTieCount = 0;
        int currentTieId = -1;
        for (int i = 0; i < run.Workspace.TraceIntervals.Count; i++)
        {
            int tieId = run.Workspace.TraceIntervals[i].TieGroupId;
            if (tieId != currentTieId)
            {
                largestTieGroup = Math.Max(largestTieGroup, currentTieCount);
                currentTieId = tieId;
                currentTieCount = 0;
            }
            currentTieCount++;
        }
        largestTieGroup = Math.Max(largestTieGroup, currentTieCount);
        Assert.True(largestTieGroup >= 3);

        var builder = Begin("tie-overlap");
        AppendRay(builder, run);
        Append(builder, "largest-tie", largestTieGroup);
        return builder.ToString();
    }

    private static string BuildEndpointCanonical()
    {
        using var world = new GridWorld();
        VoxelIndex cell = default;
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(1),
                new[] { cell },
                "phase6-endpoint");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        Vector3d foot = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cell);
        PathQuery query = CreateQuery(
            new NavigationEndpoint(
                foot - Vector3d.Right,
                fixture.MapId,
                EndpointResolutionPolicy.NearestNavigable,
                (Fixed64)2),
            new NavigationEndpoint(foot, fixture.MapId),
            fixture.DefaultProfile,
            maxSimplificationRays: 0);
        var workspace = new NavigationAStarWorkspace(1, 16, 16, 16, 16, 16, 31);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(int.MaxValue, int.MaxValue);
        Assert.Equal(NavigationQueryAdmissionStatus.Success, admission.Status);
        NavigationResolvedPathQuery result = admission.Result;

        var builder = Begin("endpoint");
        Append(builder, "status", (int)admission.Status);
        AppendAddress(builder, "start", result.Start.Address);
        Append(builder, "start-distance", result.Start.ResolutionDistance.m_rawValue);
        AppendAddress(builder, "end", result.End.Address);
        AppendMeter(builder, admission.Meter);
        result.Dispose();
        return builder.ToString();
    }

    private static string BuildSimplifiedAStarCanonical()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "phase6-simplified");
        PathQuery query = CreateQuery(
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[0]),
                fixture.MapId),
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[^1]),
                fixture.MapId),
            fixture.DefaultProfile,
            maxSimplificationRays: 1);
        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(world, fixture.Graph, query);
        Assert.Equal(NavigationSurfaceAStarStatus.Success, result.Status);
        Assert.Equal(2, result.Payload!.GuidePoints.Length);

        var builder = Begin("simplified-a-star");
        Append(builder, "status", (int)result.Status);
        Append(builder, "cost", result.Cost.m_rawValue);
        Append(builder, "points", result.Payload.GuidePoints.Length);
        for (int i = 0; i < result.Payload.GuidePoints.Length; i++)
        {
            NavigationAStarGuidePoint point = result.Payload.GuidePoints[i];
            AppendAddress(builder, $"point-{i}-address", point.Address);
            AppendVector(builder, $"point-{i}-position", point.Position);
        }
        Append(builder, "world-sequence", result.Payload.WorldChangeSequence ?? 0UL);
        return builder.ToString();
    }

    private static string BuildDirectSteeringCanonical()
    {
        using TrailblazerWorldContext context = CreateLineContext(
            "phase6-direct",
            out PathQuery query);
        var navigator = new TestNavigator(context);
        navigator.Setup(query.Start.Position, query.Agent);
        navigator.Initialize(CreateTrekCondition());
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = navigator.Steering!;
        Vector3d heading = steering.GetHeading(navigator);
        Assert.Equal(Vector3d.Right, heading);
        Assert.True(steering.HasLineOfSightPath);
        Assert.False(steering.HasNavigationGuidance);

        var builder = Begin("direct-steering");
        AppendVector(builder, "heading", heading);
        Append(builder, "line-of-sight", steering.HasLineOfSightPath);
        Append(builder, "guide", steering.HasNavigationGuidance);
        Append(builder, "a-star-leases", context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount);
        Append(builder, "flow-leases", context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount);
        navigator.Reset();
        return builder.ToString();
    }

    private static string BuildFlowRejoinCanonical()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateFlowCache(fixture);
        NavigationFlowFieldPayloadLease payloadLease = PublishFlow(cache, fixture);
        NavigationGuideStatus createStatus = cache.TryCreateGuide(
            fixture.World,
            fixture.Store,
            new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
            out NavigationFlowFieldLease guide);
        Assert.Equal(NavigationGuideStatus.Success, createStatus);
        Assert.True(fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef));
        Assert.True(fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source));
        Vector3d actualFoot = source.FootAnchor
            + Vector3d.Forward * ((Fixed64)3 / (Fixed64)4);
        var meter = new GuideSampleWorkMeter(GenerousSampleBudget);
        NavigationGuideStatus sampleStatus = guide.TrySample(
            actualFoot,
            ref meter,
            out Vector3d heading);
        Assert.Equal(NavigationGuideStatus.Success, sampleStatus);
        Assert.Equal(Vector3d.Backward, heading);

        var builder = Begin("flow-rejoin");
        Append(builder, "create", (int)createStatus);
        Append(builder, "sample", (int)sampleStatus);
        AppendVector(builder, "heading", heading);
        Append(builder, "origin-cost", guide.OriginIntegrationCost.m_rawValue);
        Append(builder, "guide-status", (int)guide.Status);
        Append(builder, "active-leases", cache.ActiveLeaseCount);
        NavigationGuideStatus exhaustedStatus = guide.TrySample(
            actualFoot,
            ref meter,
            out Vector3d exhaustedHeading);
        Assert.Equal(NavigationGuideStatus.BudgetExceeded, exhaustedStatus);
        Assert.Equal(Vector3d.Zero, exhaustedHeading);
        Append(builder, "second-sample", (int)exhaustedStatus);
        AppendVector(builder, "second-heading", exhaustedHeading);
        guide.Dispose();
        Append(builder, "released-leases", cache.ActiveLeaseCount);
        return builder.ToString();
    }

    private static string BuildMutationCanonical()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        Assert.True(fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef startRef));
        NavigationCellAddress endAddress = fixture.Near.Nodes[0].Address;
        Assert.True(fixture.Graph.TryGetNodeRef(endAddress, out NavigationNodeRef endRef));
        Assert.True(fixture.Graph.TryGetNodeState(startRef, out NavigationNodeState start));
        Assert.True(fixture.Graph.TryGetNodeState(endRef, out NavigationNodeState end));
        var request = new NavigationRayRequest(
            fixture.World,
            fixture.Store,
            fixture.Graph,
            NavigationAStarExitTestHarness.Profile(),
            NavigationAStarExitTestHarness.Policy,
            SurfaceIntent,
            allowTransitions: false,
            start.FootAnchor,
            end.FootAnchor,
            NavigationRayEndpointAllowance.None);
        ulong before = fixture.World.ChangeSequence;
        var workspace = new NavigationRayWorkspace(4, 64, 64, 128, 128);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateBudget(maxSimplificationRays: 0));
        work.Begin(request);
        Assert.True(fixture.Graph.TryGetMap(
            fixture.FarOrigin.MapId,
            out NavigationMapInstance? instance));
        Assert.NotNull(instance);
        Assert.True(fixture.World.ActiveGrids[instance!.GridIdentity.GridIndex]
            .TryRemoveVoxel(new VoxelIndex(2, 0, 0)));
        ulong after = fixture.World.ChangeSequence;
        while (work.Status == NavigationRayStatus.Pending)
            work.Advance(meter);
        var run = new RayRun(work.Result, meter, workspace);
        Assert.Equal(NavigationRayStatus.Stale, run.Result.Status);

        var builder = Begin("mutation");
        Append(builder, "before", before);
        Append(builder, "after", after);
        AppendRay(builder, run);
        return builder.ToString();
    }

    private static string BuildSerializationCanonical()
    {
        using TrailblazerWorldContext context = CreateLineContext(
            "phase6-serialization",
            out PathQuery query);
        var source = new TestNavigator(context);
        source.Setup(query.Start.Position, query.Agent);
        source.Initialize(CreateTrekCondition());
        source.ApplyGuidedTrekRequest(query, TrekRate.Moderate, groupId: 17);
        Vector3d sourceHeading = source.Steering!.GetHeading(source);
        Assert.True(source.Steering.HasLineOfSightPath);
        string json = JsonRecordSerializer.Serialize(source);
        var restored = new TestNavigator(context);
        restored.Setup(query.Start.Position, query.Agent);
        restored.Initialize(CreateTrekCondition());
        JsonRecordSerializer.Populate(restored, json);
        NavSteering steering = restored.Steering!;

        var builder = Begin("serialization");
        AppendVector(builder, "source-heading", sourceHeading);
        Append(builder, "query", steering.CurrentQuery == query);
        Append(builder, "line-of-sight", steering.HasLineOfSightPath);
        Append(builder, "guide", steering.HasNavigationGuidance);
        Append(builder, "move", steering.ShouldMove);
        Append(builder, "group", steering.MovementGroupID);
        AppendVector(builder, "destination", steering.Destination);
        source.Reset();
        restored.Reset();
        return builder.ToString();
    }

    private static TrailblazerWorldContext CreateLineContext(
        string mapId,
        out PathQuery query)
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var world = new GridWorld();
        Assert.True(world.TryAddGrid(configuration, out _));
        TrailblazerWorldContext context = TrailblazerWorldContext.Attach(
            world,
            takeOwnership: true);
        Assert.True(configuration.TryNormalize(out NormalizedGridConfiguration binding));
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationMap map = new NavigationMapBuilder(mapId, binding)
            .AddCell(default, cell)
            .AddCell(new VoxelIndex(1, 0, 0), cell)
            .AddCell(new VoxelIndex(2, 0, 0), cell)
            .Build();
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            1,
            context.FrameCount + 1);
        var policyKey = new NavigationAreaPolicyKey(mapId, 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            2,
            context.FrameCount + 1);
        Assert.True(context.Pathing.Admit(mapOperation));
        Assert.True(context.Pathing.Admit(policyOperation));
        while (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
            || policyOperation.Receipt.Status == NavigationOperationStatus.Pending)
        {
            context.Simulate();
        }
        Assert.Equal(NavigationOperationStatus.Applied, mapOperation.Receipt.Status);
        Assert.Equal(NavigationOperationStatus.Applied, policyOperation.Receipt.Status);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        query = CreateQuery(
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(binding, default),
                mapId),
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(binding, new VoxelIndex(2, 0, 0)),
                mapId),
            profile,
            maxSimplificationRays: 1,
            policyKey);
        return context;
    }

    private static TrekCondition CreateTrekCondition() => new()
    {
        Medium = TraversalMedium.Solid,
        SurfaceLevel = Fixed64.Zero,
        GroundState = new GroundCondition()
    };

    private static NavigationFlowFieldPayloadCache CreateFlowCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture) => new(
        1,
        fixture.Far.RetainedBytes,
        fixture.Far.RetainedBytes,
        fixture.Far.RetainedBytes,
        1,
        8,
        NavigationFlowFieldCacheTestHarness.CreateImmediateRayWorkspace());

    private static NavigationFlowFieldPayloadLease PublishFlow(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture)
    {
        Assert.True(cache.TryReservePayload(
            fixture.Far.RetainedBytes,
            out NavigationFlowFieldReservation reservation));
        Assert.Equal(
            NavigationFlowFieldStatus.Success,
            cache.TryPublishOrPromote(
                fixture.Store,
                fixture.Far,
                fixture.FarOrigin,
                ref reservation,
                out NavigationFlowFieldPayloadLease lease));
        return lease;
    }

    private static RayRun RunRay(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        Vector3d start,
        Vector3d end,
        int mapCapacity = 1) => RunRay(
        new NavigationRayRequest(
            world,
            store,
            graph,
            profile,
            NavigationAStarExitTestHarness.Policy,
            SurfaceIntent,
            allowTransitions: false,
            start,
            end,
            NavigationRayEndpointAllowance.None),
        mapCapacity);

    private static RayRun RunRay(
        NavigationRayRequest request,
        int mapCapacity)
    {
        var workspace = new NavigationRayWorkspace(mapCapacity, 64, 64, 128, 128);
        var work = new NavigationRayWork(workspace);
        var meter = new NavigationWorkMeter(CreateBudget(maxSimplificationRays: 0));
        work.Begin(request);
        while (work.Status == NavigationRayStatus.Pending)
            work.Advance(meter);
        return new RayRun(work.Result, meter, workspace);
    }

    private static PathQuery CreateQuery(
        NavigationEndpoint start,
        NavigationEndpoint end,
        NavigationAgentProfile profile,
        int maxSimplificationRays,
        NavigationAreaPolicyKey? policyKey = null) => new(
        start,
        end,
        profile,
        policyKey ?? NavigationAStarExitTestHarness.Policy.Key,
        SurfaceIntent,
        PathAlgorithm.AStar,
        CreateBudget(maxSimplificationRays),
        allowTransitions: false);

    private static NavigationWorkBudget CreateBudget(int maxSimplificationRays) => new(
        8_192,
        32,
        128,
        1_024,
        1_024,
        0,
        0,
        0,
        128,
        128,
        maxSimplificationRays);

    private static TraversalIntent SurfaceIntent => new(
        TraversalDomain.Surface,
        TraversalMedium.Solid,
        TraversalDomain.Surface);

    private static GuideSampleWorkBudget GenerousSampleBudget => new(
        128,
        128,
        8,
        32,
        32,
        32,
        1);

    private static void FindHexLine(
        NormalizedGridConfiguration binding,
        out VoxelIndex start,
        out VoxelIndex middle,
        out VoxelIndex end)
    {
        HexDirection[] directions =
        {
            HexDirection.QNegative,
            HexDirection.QNegativeRPositive,
            HexDirection.RNegative,
            HexDirection.RPositive,
            HexDirection.QPositiveRNegative,
            HexDirection.QPositive
        };
        for (int q = 0; q < binding.Width; q++)
        {
            for (int r = 0; r < binding.Length; r++)
            {
                var candidate = new VoxelIndex(q, 0, r);
                for (int direction = 0; direction < directions.Length; direction++)
                {
                    VoxelIndex offset = HexDirectionUtility.GetOffset(directions[direction]);
                    var next = new VoxelIndex(
                        candidate.x + offset.x,
                        candidate.y + offset.y,
                        candidate.z + offset.z);
                    var last = new VoxelIndex(
                        next.x + offset.x,
                        next.y + offset.y,
                        next.z + offset.z);
                    if (!binding.IsValidIndex(candidate)
                        || !binding.IsValidIndex(next)
                        || !binding.IsValidIndex(last))
                    {
                        continue;
                    }
                    start = candidate;
                    middle = next;
                    end = last;
                    return;
                }
            }
        }
        throw new InvalidOperationException("The normalized hex grid has no native three-cell line.");
    }

    private static StringBuilder Begin(string name) => new StringBuilder(name);

    private static void Append(StringBuilder builder, string name, object value) =>
        builder.Append('|').Append(name).Append('=')
            .Append(Convert.ToString(value, CultureInfo.InvariantCulture));

    private static void AppendVector(StringBuilder builder, string name, Vector3d value)
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

    private static void AppendRay(StringBuilder builder, RayRun run)
    {
        Append(builder, "status", (int)run.Result.Status);
        AppendAddress(builder, "start", run.Result.StartAddress);
        AppendAddress(builder, "end", run.Result.EndAddress);
        Append(builder, "cost", run.Result.TraversalCost.m_rawValue);
        Append(builder, "neutral", run.Result.IsSemanticCostNeutral);
        Append(builder, "intervals", run.Workspace.TraceIntervals.Count);
        AppendMeter(builder, run.Meter);
    }

    private static void AppendMeter(StringBuilder builder, NavigationWorkMeter meter)
    {
        Append(builder, "lookup", meter.LookupProbes);
        Append(builder, "endpoints", meter.EndpointCandidates);
        Append(builder, "expanded", meter.ExpandedNodes);
        Append(builder, "edges", meter.EvaluatedEdges);
        Append(builder, "legs", meter.ConnectionLegs);
        Append(builder, "trace", meter.TraceIntervals);
        Append(builder, "covered", meter.CoveredVoxelIntervals);
        Append(builder, "simplification", meter.SimplificationRays);
    }

    private readonly struct RayRun
    {
        internal RayRun(
            NavigationRayResult result,
            NavigationWorkMeter meter,
            NavigationRayWorkspace workspace)
        {
            Result = result;
            Meter = meter;
            Workspace = workspace;
        }

        internal NavigationRayResult Result { get; }
        internal NavigationWorkMeter Meter { get; }
        internal NavigationRayWorkspace Workspace { get; }
    }
}
