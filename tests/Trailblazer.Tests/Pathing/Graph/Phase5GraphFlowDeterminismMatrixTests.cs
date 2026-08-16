using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Chronicler;
using FixedMathSharp;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class Phase5GraphFlowDeterminismMatrixTests
{
    private readonly ITestOutputHelper _output;

    public Phase5GraphFlowDeterminismMatrixTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData("near-far", "1568835008C4A5EE26053D4FDD0CBD5B65DF6BFFF0D5C524F6C82482928EDEF4")]
    [InlineData("same-key", "D033DC914653746879E0B0D0D3110A47AE58570C07753380DE6DD7D0C67F027D")]
    [InlineData("mutation", "1E0DC9604A056385BACD04049E8241CF654679A88B1488A1B05E3743517291AF")]
    [InlineData("sampling", "EC2A4F1DD42DAE7A9DE1147D39FE5F9C341CE50BC9437416AABC9D3ED7FB36BD")]
    [InlineData("hybrid", "255298CF18C1B54296EDCFAB015E88B73D1D8B695F5A41B4372E245C51ACB9D4")]
    [InlineData("volume-exit", "812576B2D2DC6A5356540F353BF79A6AC7A7AE29402054921A7BBF19C629756F")]
    [InlineData("navigator", "5A2D9C9B7822F97F6E64F2E9C11A84AC937249378B7CB5517DE214891EA9FFD9")]
    [InlineData("serialization", "9D339B4E46FE9459469BE4957EA6B71DDDF6134737C9FF72331F94C0EDCA9418")]
    public void CanonicalPhase5Case_ShouldMatchCheckedInDigest(
        string caseName,
        string expectedDigest)
    {
        string canonical = caseName switch
        {
            "near-far" => BuildNearFarCanonical(),
            "same-key" => BuildSameKeyCanonical(),
            "mutation" => BuildMutationCanonical(),
            "sampling" => BuildSamplingCanonical(),
            "hybrid" => BuildHybridCanonical(),
            "volume-exit" => BuildVolumeExitCanonical(),
            "navigator" => BuildNavigatorCanonical(),
            "serialization" => BuildSerializationCanonical(),
            _ => throw new InvalidOperationException($"Unknown Phase 5 digest case '{caseName}'.")
        };
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        _output.WriteLine($"PHASE5_FLOW_CANONICAL={caseName}|{canonical}");
        _output.WriteLine($"PHASE5_FLOW_DIGEST={digest} CASE={caseName}");

        Assert.Equal(expectedDigest, digest);
    }

    private static string BuildNearFarCanonical()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, 2, guideCapacity: 0);
        NavigationFlowFieldPayloadLease nearLease = Publish(
            cache,
            fixture,
            fixture.Near,
            fixture.NearOrigin);
        NavigationFlowFieldPayloadLease farLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        var builder = Begin("near-far", NavigationFlowFieldStatus.Success);
        AppendPayload(builder, "near", fixture.Near);
        AppendPayload(builder, "far", fixture.Far);
        Append(builder, "prefix", IsPrefix(fixture.Near, fixture.Far));
        AppendCache(builder, cache);
        nearLease.Dispose();
        farLease.Dispose();
        AppendCache(builder, cache);
        return builder.ToString();
    }

    private static string BuildSameKeyCanonical()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.One);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, 2, guideCapacity: 0);
        Assert.True(
            cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation firstReservation),
            "The first same-key reservation was rejected.");
        Assert.True(
            cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation secondReservation),
            "The second same-key reservation was rejected.");
        NavigationFlowFieldStatus firstStatus = default;
        NavigationFlowFieldStatus secondStatus = default;
        NavigationFlowFieldPayloadLease firstLease = default;
        NavigationFlowFieldPayloadLease secondLease = default;
        Exception? firstFailure = null;
        Exception? secondFailure = null;
        using var start = new ManualResetEventSlim();
        var first = new Thread(() =>
        {
            try
            {
                start.Wait();
                firstStatus = cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Far,
                    fixture.FarOrigin,
                    ref firstReservation,
                    out firstLease);
            }
            catch (Exception exception)
            {
                firstFailure = exception;
            }
        }) { IsBackground = true };
        var second = new Thread(() =>
        {
            try
            {
                start.Wait();
                secondStatus = cache.TryPublishOrPromote(
                    fixture.Store,
                    fixture.Far,
                    fixture.FarOrigin,
                    ref secondReservation,
                    out secondLease);
            }
            catch (Exception exception)
            {
                secondFailure = exception;
            }
        }) { IsBackground = true };

        first.Start();
        second.Start();
        start.Set();
        Assert.True(first.Join(TimeSpan.FromSeconds(5)), "The first same-key worker did not stop.");
        Assert.True(second.Join(TimeSpan.FromSeconds(5)), "The second same-key worker did not stop.");
        if (firstFailure != null)
            throw new InvalidOperationException("The first same-key worker failed.", firstFailure);
        if (secondFailure != null)
            throw new InvalidOperationException("The second same-key worker failed.", secondFailure);
        Assert.True(firstStatus == NavigationFlowFieldStatus.Success, "The first same-key publish failed.");
        Assert.True(secondStatus == NavigationFlowFieldStatus.Success, "The second same-key publish failed.");
        Assert.True(
            firstLease.TryGetPayload(out NavigationFlowFieldPayload firstPayload)
                == NavigationFlowFieldStatus.Success,
            "The first same-key payload is not readable.");
        Assert.True(
            secondLease.TryGetPayload(out NavigationFlowFieldPayload secondPayload)
                == NavigationFlowFieldStatus.Success,
            "The second same-key payload is not readable.");

        var builder = Begin("same-key", firstStatus);
        Append(builder, "second-status", secondStatus);
        Append(builder, "same-payload", ReferenceEquals(firstPayload, secondPayload));
        AppendPayload(builder, "canonical", firstPayload);
        AppendCache(builder, cache);
        firstLease.Dispose();
        secondLease.Dispose();
        AppendCache(builder, cache);
        return builder.ToString();
    }

    private static string BuildMutationCanonical()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture affected =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldCacheTestHarness.LineFixture unaffected =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache affectedCache = CreateCache(affected, 2, 0);
        using NavigationFlowFieldPayloadCache unaffectedCache = CreateCache(unaffected, 2, 0);
        NavigationFlowFieldPayloadLease affectedLease = Publish(
            affectedCache,
            affected,
            affected.Far,
            affected.FarOrigin);
        NavigationFlowFieldPayloadLease unaffectedLease = Publish(
            unaffectedCache,
            unaffected,
            unaffected.Far,
            unaffected.FarOrigin);
        NavigationWorldGraph changed = affected.Graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(affected.Store.Current.GraphVersion + 1);
        Assert.True(
            affected.Store.TryPublish(changed) == NavigationCandidatePublication.Published,
            "The affected graph mutation was not published.");
        NavigationFlowFieldStatus affectedStatus = affectedCache.TryCheckout(
            affected.Store,
            affected.Store.Current,
            affected.Far.Key,
            affected.FarOrigin,
            out NavigationFlowFieldPayloadLease affectedCheckout);
        NavigationFlowFieldStatus unaffectedStatus = unaffectedCache.TryCheckout(
            unaffected.Store,
            unaffected.Store.Current,
            unaffected.Far.Key,
            unaffected.FarOrigin,
            out NavigationFlowFieldPayloadLease unaffectedCheckout);

        var builder = Begin("mutation", affectedStatus);
        Append(builder, "unaffected-status", unaffectedStatus);
        Append(builder, "affected-active-status", affectedLease.TryGetPayload(out _));
        Append(builder, "unaffected-active-status", unaffectedLease.TryGetPayload(out _));
        AppendPayload(builder, "unaffected", unaffected.Far);
        AppendCache(builder, affectedCache);
        AppendCache(builder, unaffectedCache);
        affectedCheckout.Dispose();
        unaffectedCheckout.Dispose();
        affectedLease.Dispose();
        unaffectedLease.Dispose();
        AppendCache(builder, affectedCache);
        AppendCache(builder, unaffectedCache);
        return builder.ToString();
    }

    private static string BuildSamplingCanonical()
    {
        using NavigationFlowFieldCacheTestHarness.LineFixture fixture =
            NavigationFlowFieldCacheTestHarness.CreateLine(Fixed64.Zero);
        using NavigationFlowFieldPayloadCache cache = CreateCache(fixture, 1, guideCapacity: 8);
        NavigationFlowFieldPayloadLease payloadLease = Publish(
            cache,
            fixture,
            fixture.Far,
            fixture.FarOrigin);
        NavigationGuideStatus createStatus = cache.TryCreateGuide(
            fixture.World,
            fixture.Store,
            new NavigationFlowQueryResult(fixture.FarOrigin, payloadLease),
            out NavigationFlowFieldLease guide);
        Assert.True(
            fixture.Graph.TryGetNodeRef(fixture.FarOrigin, out NavigationNodeRef sourceRef),
            "The sampling source was not found.");
        Assert.True(
            fixture.Graph.TryGetNodeState(sourceRef, out NavigationNodeState source),
            "The sampling source state was not found.");
        NavigationGuideStatus sampleStatus = guide.TrySample(
            source.FootAnchor,
            new GuideSampleWorkBudget(128, 128, 8, 32, 32, 32, 1),
            out Vector3d heading);

        var builder = Begin("sampling", createStatus);
        Append(builder, "sample-status", sampleStatus);
        Append(builder, "origin-cost", guide.OriginIntegrationCost.m_rawValue);
        AppendVector(builder, "heading", heading);
        AppendCache(builder, cache);
        guide.Dispose();
        AppendCache(builder, cache);
        return builder.ToString();
    }

    private static string BuildHybridCanonical()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        PathQuery query = CreateFlowQuery(
            Vector3d.Zero,
            Vector3d.Right,
            "hybrid-map",
            allowTransitions: false);
        HybridRouteStep surface = HybridRouteStep.Surface(context, query);
        HybridRouteStep waypoint = HybridRouteStep.Waypoint(context, Vector3d.Right);
        var plan = new HybridRoutePlan(
            new[] { surface, waypoint },
            Array.Empty<TraversalTransition>(),
            Fixed64.Half);

        var builder = Begin("hybrid", NavigationGuideStatus.Success);
        Append(builder, "steps", plan.Steps.Length);
        Append(builder, "transitions", plan.DirectedTransitions.Length);
        Append(builder, "cost", plan.TotalPathCost.m_rawValue);
        Append(builder, "surface-query", plan.Steps[0].SurfaceQuery == query);
        Append(builder, "surface-volume", plan.Steps[0].VolumeRequest == null);
        AppendVector(builder, "waypoint", plan.Steps[1].WaypointPosition);
        return builder.ToString();
    }

    private static string BuildVolumeExitCanonical()
    {
        PathQuery query = CreateFlowQuery(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            "volume-exit-map",
            allowTransitions: true);
        var handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = "phase5-volume-exit",
            MovementGroupId = 17,
            IsRequestingClimb = true,
            FollowupQuery = query
        };
        bool created = handoff.TryCreateFollowupQuery(
            new Vector3d(2, 0, 0),
            out PathQuery? followup);

        var builder = Begin("volume-exit", created);
        Append(builder, "valid", handoff.IsValid);
        Append(builder, "group", handoff.MovementGroupId);
        Append(builder, "climb", handoff.IsRequestingClimb);
        AppendVector(builder, "start", followup!.Value.Start.Position);
        AppendVector(builder, "end", followup.Value.End.Position);
        Append(builder, "algorithm", followup.Value.Algorithm);
        Append(builder, "transitions", followup.Value.AllowTransitions);
        return builder.ToString();
    }

    private static string BuildNavigatorCanonical()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var navigator = new TestNavigator(context);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        navigator.Setup(Vector3d.Zero, profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        PathQuery query = CreateFlowQuery(
            navigator.FootPosition,
            Vector3d.Right,
            "navigator-map",
            allowTransitions: false,
            profile);
        navigator.ApplyGuidedTrekRequest(query, rate: TrekRate.Moderate, groupId: 12);
        NavSteering steering = navigator.Steering!;

        var builder = Begin("navigator", navigator.IsGuideded);
        Append(builder, "query", steering.CurrentQuery == query);
        Append(builder, "legacy-request", steering.CurrentRequest == null);
        Append(builder, "group", steering.MovementGroupID);
        Append(builder, "rate", navigator.FrameRequest.Rate);
        Append(builder, "move", steering.ShouldMove);
        navigator.Reset();
        return builder.ToString();
    }

    private static string BuildSerializationCanonical()
    {
        PathQuery query = CreateFlowQuery(
            Vector3d.Left,
            Vector3d.Right,
            "serialization-map",
            allowTransitions: true);
        var source = new PathQueryRecord();
        source.Capture(query, guide: null);
        string json = JsonRecordSerializer.Serialize(source);
        var target = new PathQueryRecord();
        JsonRecordSerializer.Populate(target, json);
        bool recreated = target.TryCreateQuery(out PathQuery? restored);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var record = new PathRequestRecord
        {
            Origin = Vector3d.Zero,
            TargetPosition = Vector3d.Right,
            UnitSize = Fixed64.One
        };

        var builder = Begin("serialization", recreated);
        Append(builder, "equal", restored == query);
        Append(builder, "algorithm", restored!.Value.Algorithm);
        Append(builder, "extra-cost", restored.Value.FlowField.ExtraIntegrationCost.m_rawValue);
        Append(builder, "flow-enum", (int)PathAlgorithm.FlowField);
        foreach (int retiredKind in new[] { 1, 2, 4, 99 })
        {
            record.Kind = (PathRequestRecordKind)retiredKind;
            Append(
                builder,
                $"retired-{retiredKind}",
                record.TryCreateRequest(context, out IPathRequest? request));
            Assert.True(request == null, $"Retired record kind {retiredKind} created a request.");
        }
        return builder.ToString();
    }

    private static NavigationFlowFieldPayloadCache CreateCache(
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        int activeLeases,
        int guideCapacity) => new(
        maxEntries: 1,
        maxReusableBytes: fixture.Far.RetainedBytes,
        maxSinglePayloadBytes: fixture.Far.RetainedBytes,
        maxActivePayloadBytes: checked(fixture.Far.RetainedBytes * activeLeases),
        maxActiveLeases: activeLeases,
        guideMapCapacity: guideCapacity);

    private static NavigationFlowFieldPayloadLease Publish(
        NavigationFlowFieldPayloadCache cache,
        NavigationFlowFieldCacheTestHarness.LineFixture fixture,
        NavigationFlowFieldPayload payload,
        NavigationCellAddress origin)
    {
        Assert.True(
            cache.TryReservePayload(
                fixture.Far.RetainedBytes,
                out NavigationFlowFieldReservation reservation),
            "The flow payload reservation was rejected.");
        NavigationFlowFieldStatus status = cache.TryPublishOrPromote(
            fixture.Store,
            payload,
            origin,
            ref reservation,
            out NavigationFlowFieldPayloadLease lease);
        Assert.True(status == NavigationFlowFieldStatus.Success, $"Flow publication failed with {status}.");
        return lease;
    }

    private static PathQuery CreateFlowQuery(
        Vector3d start,
        Vector3d end,
        string mapId,
        bool allowTransitions,
        NavigationAgentProfile? profile = null) => new(
        new NavigationEndpoint(start, mapId),
        new NavigationEndpoint(end, mapId),
        profile ?? PathTestFactory.DefaultNavigationProfile,
        new NavigationAreaPolicyKey("phase5-digest-policy", 7),
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        PathAlgorithm.FlowField,
        new NavigationWorkBudget(128, 8, 128, 512, 8, 16, 16, 8, 0, 0, 0),
        allowTransitions,
        new FlowFieldQueryOptions(Fixed64.Half));

    private static bool IsPrefix(
        NavigationFlowFieldPayload prefix,
        NavigationFlowFieldPayload longer)
    {
        if (prefix.Nodes.Length >= longer.Nodes.Length
            || prefix.Dependencies.Components.Length > longer.Dependencies.Components.Length
            || prefix.Dependencies.Pages.Length > longer.Dependencies.Pages.Length)
        {
            return false;
        }
        for (int i = 0; i < prefix.Nodes.Length; i++)
        {
            if (!prefix.Nodes[i].Equals(longer.Nodes[i]))
                return false;
        }
        for (int i = 0; i < prefix.Dependencies.Components.Length; i++)
        {
            if (!prefix.Dependencies.Components[i].Equals(longer.Dependencies.Components[i]))
                return false;
        }
        for (int i = 0; i < prefix.Dependencies.Pages.Length; i++)
        {
            if (!prefix.Dependencies.Pages[i].Equals(longer.Dependencies.Pages[i]))
                return false;
        }
        return true;
    }

    private static StringBuilder Begin<T>(string caseName, T status) =>
        new StringBuilder().Append("case=").Append(caseName).Append(";status=").Append(status).Append(';');

    private static void AppendPayload(
        StringBuilder builder,
        string name,
        NavigationFlowFieldPayload payload)
    {
        Append(builder, $"{name}-nodes", payload.Nodes.Length);
        Append(builder, $"{name}-complete", payload.IsComplete);
        Append(builder, $"{name}-bytes", payload.RetainedBytes);
        builder.Append(name).Append("-ordered=");
        for (int i = 0; i < payload.Nodes.Length; i++)
        {
            NavigationFlowFieldNode node = payload.Nodes[i];
            AppendAddress(builder, node.Address);
            builder.Append('@').Append(node.IntegrationCost.m_rawValue.ToString(CultureInfo.InvariantCulture));
            builder.Append('>').Append(node.SelectedEdge.Target.MapId == null ? "-" : string.Empty);
            if (node.SelectedEdge.Target.MapId != null)
                AppendAddress(builder, node.SelectedEdge.Target);
            builder.Append('#').Append(
                node.SelectedEdge.CanonicalOutgoingOrdinal.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
        }
        builder.Append(';');
        builder.Append(name).Append("-components=");
        foreach (GraphComponentDependency dependency in payload.Dependencies.Components)
        {
            AppendAddress(builder, dependency.Key.Representative);
            builder.Append('@').Append(dependency.Version.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
        builder.Append(';');
        builder.Append(name).Append("-pages=");
        foreach (GraphPageDependency dependency in payload.Dependencies.Pages)
        {
            builder.Append(dependency.MapId).Append(':')
                .Append(dependency.PageIndex.ToString(CultureInfo.InvariantCulture)).Append('@')
                .Append(dependency.BakeVersion.ToString(CultureInfo.InvariantCulture)).Append('/')
                .Append(dependency.DynamicSlotGeneration.ToString(CultureInfo.InvariantCulture)).Append('/')
                .Append(dependency.SemanticVersion.ToString(CultureInfo.InvariantCulture)).Append('/')
                .Append(dependency.PhysicalVersion.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
        builder.Append(';');
    }

    private static void AppendCache(StringBuilder builder, NavigationFlowFieldPayloadCache cache)
    {
        Append(builder, "cache-count", cache.Count);
        Append(builder, "cache-bytes", cache.CachedBytes);
        Append(builder, "active-leases", cache.ActiveLeaseCount);
        Append(builder, "leased-bytes", cache.LeasedBytes);
        Append(builder, "detached-bytes", cache.DetachedBytes);
        Append(builder, "reserved-leases", cache.ReservedLeaseCount);
        Append(builder, "reserved-bytes", cache.ReservedPayloadBytes);
    }

    private static void AppendAddress(StringBuilder builder, NavigationCellAddress address) =>
        builder.Append(address.MapId).Append(':')
            .Append(address.Index.x.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(address.Index.y.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(address.Index.z.ToString(CultureInfo.InvariantCulture));

    private static void AppendVector(StringBuilder builder, string name, Vector3d value)
    {
        builder.Append(name).Append('=')
            .Append(value.X.m_rawValue.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(value.Y.m_rawValue.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(value.Z.m_rawValue.ToString(CultureInfo.InvariantCulture)).Append(';');
    }

    private static void Append<T>(StringBuilder builder, string name, T value) =>
        builder.Append(name).Append('=').Append(value).Append(';');

}
