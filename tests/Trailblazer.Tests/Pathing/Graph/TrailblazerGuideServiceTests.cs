using System;
using System.Reflection;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class TrailblazerGuideServiceTests
{
    [Fact]
    public void TryGetDirectHeading_ClearCostNeutralSurfaceRay_ShouldReturnExactHeading()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current;
        graph.TryGetNodeRef(
                new NavigationCellAddress("guide-allocation", new VoxelIndex(4, 4, 4)),
                out NavigationNodeRef startRef)
            .Should().BeTrue();
        graph.TryGetNodeRef(
                new NavigationCellAddress("guide-allocation", new VoxelIndex(6, 4, 4)),
                out NavigationNodeRef endRef)
            .Should().BeTrue();
        graph.TryGetNodeState(startRef, out NavigationNodeState start)
            .Should().BeTrue();
        graph.TryGetNodeState(endRef, out NavigationNodeState end)
            .Should().BeTrue();
        query = WithRayBudget(query, start.FootAnchor, end.FootAnchor);

        NavigationRayStatus status = context.Guides.TryGetDirectHeading(
            query,
            query.Start.Position,
            out Vector3d heading);

        status.Should().Be(NavigationRayStatus.Success);
        heading.Should().Be(Vector3d.Right);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
    }

    [Fact]
    public void TryGetDirectHeading_ZeroTraceBudget_ShouldNotExposeAHeading()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);

        NavigationRayStatus status = context.Guides.TryGetDirectHeading(
            query,
            query.Start.Position,
            out Vector3d heading);

        status.Should().Be(NavigationRayStatus.BudgetExceeded);
        heading.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void TryGetDirectHeading_WarmChecks_ShouldAllocateZeroBytes()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);
        query = ResolveDirectRoute(context, query);
        for (int i = 0; i < 16; i++)
        {
            context.Guides.TryGetDirectHeading(query, query.Start.Position, out _)
                .Should().Be(NavigationRayStatus.Success);
        }

        NavigationRayStatus status = default;
        Vector3d heading = default;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            status = context.Guides.TryGetDirectHeading(
                query,
                query.Start.Position,
                out heading);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        status.Should().Be(NavigationRayStatus.Success);
        heading.Should().Be(Vector3d.Right);
        allocated.Should().Be(0);
    }

    [Fact]
    public void TryGetDirectHeading_TwoBlockedThreads_ShouldReturnIdenticalResults()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);
        query = ResolveDirectRoute(context, query);
        object sync = context.Pathing.ImmediateRayWorkspace.SyncRoot;
        using var firstStarted = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        NavigationRayStatus firstStatus = default;
        NavigationRayStatus secondStatus = default;
        Vector3d firstHeading = default;
        Vector3d secondHeading = default;
        Exception? firstError = null;
        Exception? secondError = null;
        var first = new Thread(() =>
        {
            firstStarted.Set();
            try
            {
                firstStatus = context.Guides.TryGetDirectHeading(
                    query,
                    query.Start.Position,
                    out firstHeading);
            }
            catch (Exception error)
            {
                firstError = error;
            }
        })
        { IsBackground = true };
        var second = new Thread(() =>
        {
            secondStarted.Set();
            try
            {
                secondStatus = context.Guides.TryGetDirectHeading(
                    query,
                    query.Start.Position,
                    out secondHeading);
            }
            catch (Exception error)
            {
                secondError = error;
            }
        })
        { IsBackground = true };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        lock (sync)
        {
            first.Start();
            second.Start();
            firstStarted.Wait(TimeSpan.FromSeconds(5), cancellationToken).Should().BeTrue();
            secondStarted.Wait(TimeSpan.FromSeconds(5), cancellationToken).Should().BeTrue();
            SpinWait.SpinUntil(
                    () => (first.ThreadState & ThreadState.WaitSleepJoin) != 0
                        && (second.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5))
                .Should().BeTrue("both checks must block on the one immediate workspace");
        }
        first.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        second.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        firstError.Should().BeNull();
        secondError.Should().BeNull();
        firstStatus.Should().Be(NavigationRayStatus.Success);
        secondStatus.Should().Be(firstStatus);
        firstHeading.Should().Be(Vector3d.Right);
        secondHeading.Should().Be(firstHeading);
    }

    [Fact]
    public void RequestGuide_OpenPlane32_ShouldUseExpansionBudgetOnlyForExpansions()
    {
        using TrailblazerWorldContext context = CreateOpenPlane32Context(
            out PathQuery exactQuery,
            out PathQuery belowQuery);
        NavigationAStarPayloadCache cache =
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationWorldGraphStore store = context.Pathing.NavigationGraphStore;

        NavigationGuideStatus belowStatus = context.Guides.RequestGuide(
            belowQuery,
            out NavigationGuideLease? belowResult);
        NavigationGuideStatus exactStatus = context.Guides.RequestGuide(
            exactQuery,
            out NavigationGuideLease? exactResult);

        belowStatus.Should().Be(NavigationGuideStatus.BudgetExceeded);
        belowResult.Should().BeNull();
        exactStatus.Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease guide = TestRequire.NotNull(exactResult);
        try
        {
            guide.StepCount.Should().Be(125);
            guide.TotalCost.Should().Be((Fixed64)62);
            guide.TryGetCurrentStep(out NavigationGuideStep first)
                .Should().Be(NavigationGuideStatus.Success);
            first.Address.Should().Be(new NavigationCellAddress(
                "guide-open-plane-32",
                default));
            for (int ordinal = 1; ordinal < 125; ordinal++)
            {
                guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
            }
            guide.TryGetCurrentStep(out NavigationGuideStep last)
                .Should().Be(NavigationGuideStatus.Success);
            last.Address.Should().Be(new NavigationCellAddress(
                "guide-open-plane-32",
                new VoxelIndex(31, 0, 31)));
        }
        finally
        {
            guide.Dispose();
        }

        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void WarmCacheHitAcquireSampleAndReturn_ShouldAllocateZeroBytes()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);

        for (int i = 0; i < 8; i++)
        {
            context.Guides.RequestGuide(query, out NavigationGuideLease? warmGuide)
                .Should().Be(NavigationGuideStatus.Success);
            NavigationGuideLease warm = TestRequire.NotNull(warmGuide);
            warm.TryGetCurrentStep(out _)
                .Should().Be(NavigationGuideStatus.Success);
            warm.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        NavigationGuideStatus requestStatus = default;
        NavigationGuideStatus sampleStatus = default;
        NavigationGuideStep sampledStep = default;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            requestStatus = context.Guides.RequestGuide(
                query,
                out NavigationGuideLease? guide);
            if (!guide.HasValue)
                break;
            NavigationGuideLease activeGuide = guide.Value;
            sampleStatus = activeGuide.TryGetCurrentStep(out sampledStep);
            activeGuide.Dispose();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        requestStatus.Should().Be(NavigationGuideStatus.Success);
        sampleStatus.Should().Be(NavigationGuideStatus.Success);
        sampledStep.Address.Should().Be(new NavigationCellAddress(
            "guide-allocation",
            new VoxelIndex(4, 4, 4)));
        sampledStep.Position.Should().Be(new Vector3d(
            Fixed64.Zero,
            -Fixed64.Half,
            Fixed64.Zero));
        allocated.Should().Be(0,
            "warmed public cache-hit acquisition, sampling, and return reuse all bounded lease shells");
    }

    [Fact]
    public void DisposedHandleCopy_ShouldNotObserveOrReleaseAReusedLeaseShell()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);
        context.Guides.RequestGuide(query, out NavigationGuideLease? firstResult)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease first = TestRequire.NotNull(firstResult);
        NavigationGuideLease staleCopy = first;

        first.Dispose();
        context.Guides.RequestGuide(query, out NavigationGuideLease? secondResult)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease second = TestRequire.NotNull(secondResult);

        staleCopy.Status.Should().Be(NavigationGuideStatus.Stale);
        staleCopy.TryGetCurrentStep(out _)
            .Should().Be(NavigationGuideStatus.Stale);
        staleCopy.Dispose();
        second.Status.Should().Be(NavigationGuideStatus.Success);
        second.TryGetCurrentStep(out _)
            .Should().Be(NavigationGuideStatus.Success);
        second.Dispose();
    }

    [Fact]
    public void HandleCopies_ShouldReturnOneLeaseOnlyOnce()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);
        context.Guides.RequestGuide(query, out NavigationGuideLease? result)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease first = TestRequire.NotNull(result);
        NavigationGuideLease second = first;

        first.Dispose();
        second.Dispose();

        context.Guides.RequestGuide(query, out NavigationGuideLease? replacementResult)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease replacement = TestRequire.NotNull(replacementResult);
        replacement.Status.Should().Be(NavigationGuideStatus.Success);
        replacement.Dispose();
    }

    [Fact]
    public void ConcurrentSample_ShouldRemainBoundToItsValidatedGeneration()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery query);
        context.Guides.RequestGuide(query, out NavigationGuideLease? firstResult)
            .Should().Be(NavigationGuideStatus.Success);
        NavigationGuideLease first = TestRequire.NotNull(firstResult);
        NavigationGuideLease staleCopy = first;
        NavigationWorldGraphStore firstStore = context.Pathing.NavigationGraphStore;
        NavigationAStarPayloadCache cache =
            context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        FieldInfo innerField = TestRequire.NotNull(typeof(NavigationGuideLease).GetField(
            "_inner",
            BindingFlags.Instance | BindingFlags.NonPublic));
        NavigationAStarGuideLease firstInner = TestRequire.NotNull(
            innerField.GetValue(first) as NavigationAStarGuideLease);
        FieldInfo storeSyncField = TestRequire.NotNull(
            typeof(NavigationWorldGraphStore).GetField(
                "_sync",
                BindingFlags.Instance | BindingFlags.NonPublic));
        object storeSync = TestRequire.NotNull(storeSyncField.GetValue(firstStore));
        using NavigationWorldGraphStore replacementStore =
            NavigationAStarExitTestHarness.CreateStore(firstStore.Current, 4);
        var key = new NavigationAStarPayloadKey(
            query,
            new NavigationCellAddress("guide-allocation", new VoxelIndex(4, 4, 4)),
            new NavigationCellAddress("guide-allocation", new VoxelIndex(6, 4, 4)),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        using var sampleStarted = new ManualResetEventSlim();
        using var sampleCompleted = new ManualResetEventSlim();
        using var replacementStarted = new ManualResetEventSlim();
        using var replacementCompleted = new ManualResetEventSlim();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        NavigationGuideStatus sampleStatus = default;
        NavigationCellAddress sampledAddress = default;
        Exception? sampleError = null;
        Exception? replacementError = null;
        bool checkoutSucceeded = false;
        NavigationAStarQueryStatus replacementStatus = default;
        NavigationAStarGuideLease? replacementInner = null;
        var sampleThread = new Thread(() =>
        {
            sampleStarted.Set();
            try
            {
                sampleStatus = staleCopy.TryGetCurrentStep(out NavigationGuideStep step);
                sampledAddress = step.Address;
            }
            catch (Exception error)
            {
                sampleError = error;
            }
            finally
            {
                sampleCompleted.Set();
            }
        })
        {
            IsBackground = true
        };
        var replacementThread = new Thread(() =>
        {
            replacementStarted.Set();
            try
            {
                first.Dispose();
                checkoutSucceeded = cache.TryCheckout(
                    key,
                    firstStore.Current,
                    out NavigationAStarPayloadLease payloadLease);
                if (checkoutSucceeded)
                {
                    replacementStatus = cache.TryCreateGuide(
                        replacementStore,
                        payloadLease,
                        out replacementInner);
                    if (replacementInner != null)
                        new NavigationGuideLease(replacementInner).TryAdvanceStep();
                }
            }
            catch (Exception error)
            {
                replacementError = error;
            }
            finally
            {
                replacementCompleted.Set();
            }
        })
        {
            IsBackground = true
        };

        lock (storeSync)
        {
            sampleThread.Start();
            sampleStarted.Wait(TimeSpan.FromSeconds(5), cancellationToken).Should().BeTrue();
            SpinWait.SpinUntil(
                    () => (sampleThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5))
                .Should().BeTrue("the sample must pause inside the original store acquisition");

            replacementThread.Start();
            replacementStarted.Wait(TimeSpan.FromSeconds(5), cancellationToken).Should().BeTrue();
            SpinWait.SpinUntil(
                    () => replacementCompleted.IsSet
                        || (replacementThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5))
                .Should().BeTrue("replacement must either cross or wait on the guide boundary");
        }

        sampleCompleted.Wait(TimeSpan.FromSeconds(5), cancellationToken).Should().BeTrue();
        replacementCompleted.Wait(TimeSpan.FromSeconds(5), cancellationToken).Should().BeTrue();
        sampleError.Should().BeNull();
        replacementError.Should().BeNull();
        checkoutSucceeded.Should().BeTrue();
        replacementStatus.Should().Be(NavigationAStarQueryStatus.Success);
        NavigationAStarGuideLease replacement = TestRequire.NotNull(replacementInner);
        replacement.Should().BeSameAs(firstInner,
            "the forced schedule must reuse the exact guide shell");
        sampleStatus.Should().Be(NavigationGuideStatus.Success);
        sampledAddress.Should().Be(
            new NavigationCellAddress("guide-allocation", new VoxelIndex(4, 4, 4)),
            "an operation admitted by the old generation must not observe the rebound cursor");
        new NavigationGuideLease(replacement).Dispose();
    }

    [Fact]
    public void RequestGuide_ShouldReportNoMapForAnUnmappedAStarQuery()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        PublishPolicy(context);
        PathQuery query = CreateSurfaceAStarQuery();

        NavigationGuideStatus status = context.Guides.RequestGuide(query, out NavigationGuideLease? lease);

        status.Should().Be(NavigationGuideStatus.NoMap);
        lease.Should().BeNull();
    }

    [Fact]
    public void RequestGuide_ShouldReturnBudgetExceededWhenLookupBudgetIsZero()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        PublishPolicy(context);
        PathQuery query = CreateSurfaceAStarQuery(new NavigationWorkBudget(
            maxLookupProbes: 0,
            maxEndpointCandidates: 1,
            maxExpandedNodes: 1,
            maxEvaluatedEdges: 1,
            maxConnectionLegs: 1,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: 0,
            maxCoveredVoxelIntervals: 0,
            maxSimplificationRays: 0));

        NavigationGuideStatus status = context.Guides.RequestGuide(query, out NavigationGuideLease? lease);

        status.Should().Be(NavigationGuideStatus.BudgetExceeded);
        lease.Should().BeNull();
    }

    [Fact]
    public void RequestGuide_ShouldRejectFlowFieldQueriesWithoutCreatingALease()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var query = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.Right),
            profile,
            new NavigationAreaPolicyKey("test-policy", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(
                1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);

        NavigationGuideStatus status = context.Guides.RequestGuide(query, out NavigationGuideLease? lease);

        status.Should().Be(NavigationGuideStatus.Unsupported);
        lease.Should().BeNull();
    }

    [Fact]
    public void RequestFlowField_ShouldAcquireSampleAndReleaseGraphFlowLease()
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery aStarQuery);
        var flowProfile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var query = new PathQuery(
            aStarQuery.Start,
            aStarQuery.End,
            flowProfile,
            aStarQuery.AreaPolicy,
            aStarQuery.Traversal,
            PathAlgorithm.FlowField,
            aStarQuery.Budget,
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Zero));
        NavigationFlowFieldPayloadCache cache =
            context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current;
        graph.TryGetNodeRef(
                new NavigationCellAddress("guide-allocation", new VoxelIndex(4, 4, 4)),
                out NavigationNodeRef sourceRef)
            .Should().BeTrue();
        graph.TryGetNodeState(sourceRef, out NavigationNodeState source)
            .Should().BeTrue();

        NavigationGuideStatus status = context.Guides.RequestFlowField(
            query,
            out NavigationFlowFieldLease? result);

        status.Should().Be(NavigationGuideStatus.Success);
        NavigationFlowFieldLease lease = TestRequire.NotNull(result);
        lease.TrySample(
                source.FootAnchor,
                context.Settings.GuideSampleBudget,
                out NavigationFlowSample sample)
            .Should().Be(NavigationGuideStatus.Success);
        sample.Heading.Should().Be(Vector3d.Right);
        cache.ActiveLeaseCount.Should().Be(1);

        lease.Dispose();

        cache.ActiveLeaseCount.Should().Be(0);
        cache.ReservedLeaseCount.Should().Be(0);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Theory]
    [InlineData("algorithm", NavigationGuideStatus.Unsupported)]
    [InlineData("start", NavigationGuideStatus.InvalidStart)]
    [InlineData("volume", NavigationGuideStatus.BudgetExceeded)]
    public void RequestFlowField_ShouldRejectUnsupportedOrUnresolvableQueryShape(
        string mismatch,
        NavigationGuideStatus expected)
    {
        using TrailblazerWorldContext context = CreateThreeCellContext(out PathQuery aStarQuery);
        PathQuery flowQuery = new(
            aStarQuery.Start,
            aStarQuery.End,
            aStarQuery.Agent,
            aStarQuery.AreaPolicy,
            aStarQuery.Traversal,
            PathAlgorithm.FlowField,
            aStarQuery.Budget,
            allowTransitions: false);
        PathQuery query = mismatch switch
        {
            "algorithm" => aStarQuery,
            "start" => flowQuery.WithStartState(
                new Vector3d(100, 0, 0),
                flowQuery.Traversal.StartMedium),
            "volume" => new PathQuery(
                flowQuery.Start,
                flowQuery.End,
                new NavigationAgentProfile(
                    flowQuery.Agent.Shape,
                    flowQuery.Agent.MaxStepUp,
                    flowQuery.Agent.MaxDropDown,
                    flowQuery.Agent.ArrivalRadius,
                    flowQuery.Agent.AllowedMedia | TraversalMedia.Gas,
                    flowQuery.Agent.Capabilities),
                flowQuery.AreaPolicy,
                new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
                flowQuery.Algorithm,
                flowQuery.Budget,
                allowTransitions: false,
                flowQuery.FlowField),
            _ => throw new InvalidOperationException()
        };

        NavigationGuideStatus status = context.Guides.RequestFlowField(
            query,
            out NavigationFlowFieldLease? lease);

        status.Should().Be(expected);
        lease.Should().BeNull();
        context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
    }

    private static PathQuery CreateSurfaceAStarQuery(NavigationWorkBudget? budget = null) => new(
        new NavigationEndpoint(Vector3d.Zero),
        new NavigationEndpoint(Vector3d.Right),
        new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None),
        new NavigationAreaPolicyKey("test-policy", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        PathAlgorithm.AStar,
        budget ?? new NavigationWorkBudget(
            1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0),
        allowTransitions: false);

    private static PathQuery WithRayBudget(
        PathQuery query,
        Vector3d start,
        Vector3d end) => new(
        new NavigationEndpoint(start, query.Start.MapId),
        new NavigationEndpoint(end, query.End.MapId),
        query.Agent,
        query.AreaPolicy,
        query.Traversal,
        query.Algorithm,
        new NavigationWorkBudget(
            maxLookupProbes: 4_096,
            maxEndpointCandidates: query.Budget.MaxEndpointCandidates,
            maxExpandedNodes: query.Budget.MaxExpandedNodes,
            maxEvaluatedEdges: 4_096,
            maxConnectionLegs: 4_096,
            maxTransitionCandidates: query.Budget.MaxTransitionCandidates,
            maxTransitionPairs: query.Budget.MaxTransitionPairs,
            maxStagedLegAttempts: query.Budget.MaxStagedLegAttempts,
            maxTraceIntervals: 4_096,
            maxCoveredVoxelIntervals: 4_096,
            maxSimplificationRays: query.Budget.MaxSimplificationRays),
        query.AllowTransitions,
        query.FlowField);

    private static PathQuery ResolveDirectRoute(
        TrailblazerWorldContext context,
        PathQuery query)
    {
        NavigationWorldGraph graph = context.Pathing.NavigationGraphStore.Current;
        graph.TryGetNodeRef(
                new NavigationCellAddress("guide-allocation", new VoxelIndex(4, 4, 4)),
                out NavigationNodeRef startRef)
            .Should().BeTrue();
        graph.TryGetNodeRef(
                new NavigationCellAddress("guide-allocation", new VoxelIndex(6, 4, 4)),
                out NavigationNodeRef endRef)
            .Should().BeTrue();
        graph.TryGetNodeState(startRef, out NavigationNodeState start)
            .Should().BeTrue();
        graph.TryGetNodeState(endRef, out NavigationNodeState end)
            .Should().BeTrue();
        return WithRayBudget(query, start.FootAnchor, end.FootAnchor);
    }

    private static PathQuery PublishThreeCellRoute(
        TrailblazerWorldContext context,
        GridConfiguration configuration)
    {
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var start = new VoxelIndex(4, 4, 4);
        NavigationMap map = new NavigationMapBuilder("guide-allocation", binding)
            .AddCell(start, cell)
            .AddCell(new VoxelIndex(5, 4, 4), cell)
            .AddCell(new VoxelIndex(6, 4, 4), cell)
            .Build();
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();

        var policyKey = new NavigationAreaPolicyKey("guide-allocation", 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        while (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
            || policyOperation.Receipt.Status == NavigationOperationStatus.Pending)
        {
            context.Simulate();
        }
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Quarter),
            Fixed64.One,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid,
            TraversalCapability.None);
        return new PathQuery(
            new NavigationEndpoint(
                new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Half),
                "guide-allocation"),
            new NavigationEndpoint(
                new Vector3d((Fixed64)2.5f, Fixed64.Zero, Fixed64.Half),
                "guide-allocation"),
            profile,
            policyKey,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
    }

    private static TrailblazerWorldContext CreateThreeCellContext(out PathQuery query)
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        TrailblazerWorldContext context = TrailblazerWorldContext.Attach(
            world,
            takeOwnership: true);
        query = PublishThreeCellRoute(context, configuration);
        return context;
    }

    private static TrailblazerWorldContext CreateOpenPlane32Context(
        out PathQuery exactQuery,
        out PathQuery belowQuery)
    {
        const int side = 32;
        const string mapId = "guide-open-plane-32";
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(side - 1, 0, side - 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        TrailblazerWorldContext context = TrailblazerWorldContext.Attach(
            world,
            takeOwnership: true);
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int x = 0; x < side; x++)
        {
            for (int z = 0; z < side; z++)
                builder.AddCell(new VoxelIndex(x, 0, z), cell);
        }
        VoxelIndex destinationIndex = new(side - 1, 0, side - 1);
        VoxelIndex connectionDestinationIndex = new(side - 2, 0, side - 1);
        binding.TryGetCellPrism(destinationIndex, out GridCellPrism connectionSourcePrism)
            .Should().BeTrue();
        binding.TryGetCellPrism(
                connectionDestinationIndex,
                out GridCellPrism connectionDestinationPrism)
            .Should().BeTrue();
        builder.AddConnection(new NavigationConnection(
            "disable-euclidean-heuristic",
            destinationIndex,
            new NavigationCellAddress(mapId, connectionDestinationIndex),
            new Vector3d(
                connectionSourcePrism.Center.X,
                connectionSourcePrism.VerticalMin,
                connectionSourcePrism.Center.Z),
            new Vector3d(
                connectionDestinationPrism.Center.X,
                connectionDestinationPrism.VerticalMin,
                connectionDestinationPrism.Center.Z),
            portalRadiusClearance: Fixed64.Zero,
            portalHeightClearance: Fixed64.One,
            additionalCost: (Fixed64)4_096));
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        var policyKey = new NavigationAreaPolicyKey(mapId, revision: 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 2,
            effectiveFrame: 1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        for (int frame = 0;
            frame < 4_096
            && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
                || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
            frame++)
        {
            context.Simulate();
        }
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using (NavigationWorldGraphLease graphLease =
               TestRequire.NotNull(context.Pathing.TryAcquireNavigationGraph()))
        {
            graphLease.Graph.SurfaceComponents.TryGet(
                    new NavigationCellAddress(mapId, default),
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponent component)
                .Should().BeTrue();
            component.AllSurfaceEdgesEuclideanCertified.Should().BeFalse(
                    "the exact expansion boundary requires Dijkstra ordering across the full plane");
        }
        binding.TryGetCellPrism(default, out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(
                destinationIndex,
                out GridCellPrism endPrism)
            .Should().BeTrue();
        Vector3d start = new(startPrism.Center.X, startPrism.VerticalMin, startPrism.Center.Z);
        Vector3d end = new(endPrism.Center.X, endPrism.VerticalMin, endPrism.Center.Z);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Solid,
            TraversalCapability.None);
        var traversal = new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid);
        exactQuery = new PathQuery(
            new NavigationEndpoint(start, mapId),
            new NavigationEndpoint(end, mapId),
            profile,
            policyKey,
            traversal,
            PathAlgorithm.AStar,
            CreateOpenPlaneBudget(maxExpandedNodes: side * side),
            allowTransitions: false);
        belowQuery = new PathQuery(
            exactQuery.Start,
            exactQuery.End,
            profile,
            policyKey,
            traversal,
            PathAlgorithm.AStar,
            CreateOpenPlaneBudget(maxExpandedNodes: (side * side) - 1),
            allowTransitions: false);
        return context;
    }

    private static NavigationWorkBudget CreateOpenPlaneBudget(int maxExpandedNodes) => new(
        maxLookupProbes: 4_096,
        maxEndpointCandidates: 2,
        maxExpandedNodes,
        maxEvaluatedEdges: 8_192,
        maxConnectionLegs: 4,
        maxTransitionCandidates: 0,
        maxTransitionPairs: 0,
        maxStagedLegAttempts: 0,
        maxTraceIntervals: 0,
        maxCoveredVoxelIntervals: 0,
        maxSimplificationRays: 0);

    private static void PublishPolicy(TrailblazerWorldContext context)
    {
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("test-policy", 1),
            new[] { new NavigationAreaRule(isAllowed: true, additionalEnterCost: Fixed64.Zero) });
        var operation = new NavigationAreaPolicyCommitOperation(
            policy,
            publicationSequence: 1,
            effectiveFrame: context.FrameCount + 1);

        context.Pathing.Admit(operation).Should().BeTrue();
        context.Simulate();
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }
}
