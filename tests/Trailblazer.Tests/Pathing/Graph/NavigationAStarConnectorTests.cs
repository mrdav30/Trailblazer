using System;
using System.Collections.Generic;
using System.Threading;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public class NavigationAStarConnectorTests
{
    [Fact]
    public void PositionAwareStepRead_DefaultLeaseMustRefuseGuidance()
    {
        NavigationGuideLease guide = default;
        guide.TryGetCurrentStep(Vector3d.Right, out var step).Should().Be(NavigationGuideStatus.Stale);
        step.Should().Be(default(NavigationGuideStep));
    }

    [Fact]
    public void Connector_SnapshotCapacityLostAfterBeginMustReleaseWorkspaceAndPermitRetry()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        guide.HasConnector.Should().BeFalse();
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var store = context.Pathing.NavigationGraphStore;
        long bytes = cache.LeasedBytes;
        var held = new List<NavigationWorldGraphLease>();
        Exception? failure = null;
        NavigationGuideStatus? result = null;
        var worker = new Thread(() =>
        {
            try { result = context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide); }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        try
        {
            lock (context.Pathing.ImmediateRayWorkspace.SyncRoot)
            {
                worker.Start();
                SpinWait.SpinUntil(() => guide.HasConnector, TimeSpan.FromSeconds(10)).Should().BeTrue(
                    "BeginConnector must finish its own snapshot validation before waiting for the shared ray workspace");
                store.ActiveLeaseCount.Should().Be(0);
                for (int i = 0; i < context.Settings.MaxConcurrentSnapshotLeases; i++)
                {
                    var snapshot = store.TryAcquire();
                    if (snapshot == null) break;
                    held.Add(snapshot);
                }
                held.Count.Should().Be(context.Settings.MaxConcurrentSnapshotLeases);
                using var rejected = store.TryAcquire();
                rejected.Should().BeNull();
            }
            worker.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
            failure.Should().BeNull();
            result.Should().Be(NavigationGuideStatus.CapacityExceeded);
            cache.LeasedBytes.Should().Be(bytes);
            cache.ReservedPayloadBytes.Should().Be(0);
            store.ActiveLeaseCount.Should().Be(held.Count);
            guide.CurrentStepIndex.Should().Be(0);
        }
        finally
        {
            foreach (var snapshot in held) snapshot.Dispose();
            worker.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        }
        store.ActiveLeaseCount.Should().Be(0);
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Success);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
    }

    [Fact]
    public void ConnectorValidation_ClosedSchedulerMustRefuseRealOwnerWithoutStealingProof()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var interest = new NavigationValidationInterest();
        var scheduler = context.Guides.Scheduler;
        scheduler.RegisterConnectorValidation(interest, guide).Should().BeFalse("an ordinary guide has no connector eligibility");
        scheduler.RetainedValidationInterestCount.Should().Be(0);
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Success);
        guide.HasConnector.Should().BeTrue();
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long bytes = cache.LeasedBytes;
        scheduler.Dispose();
        scheduler.RegisterConnectorValidation(interest, guide).Should().BeFalse();
        scheduler.RegisterValidation(interest).Should().BeFalse();
        scheduler.RetainedValidationInterestCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(bytes, "a refused registration cannot take ownership of the live external guide");
        guide.Dispose();
        cache.LeasedBytes.Should().Be(0);
    }
    [Fact]
    public void SharedCheckoutFinalization_VolumeEpochMutationMustReleaseOnlyCheckout()
    {
        using var context = CreateLine(TraversalMedium.Gas, out var query);
        var gate = context.Pathing.NavigationAStarAdmissionGate;
        var cache = gate.PayloadCache;
        var store = context.Pathing.NavigationGraphStore;
        gate.Begin(query, out var work).Should().Be(NavigationAStarQueryStatus.Pending);
        using (work)
        {
            for (int i = 0; i < 4096 && !work.IsAdmissionComplete; i++) work.AdvanceAdmission(4096, 4096);
            for (int i = 0; i < 4096 && !work.IsReadyToPublish(0); i++) work.AdvanceSearch(0, 4096, 4096, 4096, 4096);
            work.PublishReadyPrefix(1);
            work.GetStatus(0).Should().Be(NavigationAStarQueryStatus.Success);
            using var source = new NavigationCohortSource { AStar = work.TakeResult(0) };
            var payload = source.AStar.Payload;
            payload.WorldChangeSequence.Should().Be(context.World.ChangeSequence);
            cache.TryCheckoutShared(store, store.Current, payload.Key, source, null, out var checkout)
                .Should().Be(NavigationAStarQueryStatus.Success);
            context.World.ActiveGrids[0].TryRemoveVoxel(new VoxelIndex(0, 0, 0)).Should().BeTrue();
            cache.CompleteSharedCheckoutValidation(store, null, ref checkout).Should().Be(NavigationAStarQueryStatus.Stale);
            checkout.Should().BeNull();
            cache.ActiveLeaseCount.Should().Be(1);
            cache.LeasedBytes.Should().Be(payload.RetainedBytes);
            source.Dispose();
            cache.ActiveLeaseCount.Should().Be(0);
            cache.LeasedBytes.Should().Be(0);
        }
    }

    [Fact]
    public void Connector_SortBudgetExhaustionMustReleaseOldProofAndLeaveRouteReusable()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long bytes = cache.LeasedBytes;
        guide.TryGetCurrentStep(out var anchor).Should().Be(NavigationGuideStatus.Success);
        Vector3d foot = new(Fixed64.Zero, query.Start.Position.Y, Fixed64.Zero);
        var store = context.Pathing.NavigationGraphStore;
        int rayLookups;
        using (var snapshot = store.TryAcquire()!)
        {
            snapshot.Graph.AreaCatalog.TryGet(query.AreaPolicy, out var policy).Should().BeTrue();
            var immediate = context.Pathing.ImmediateRayWorkspace;
            lock (immediate.SyncRoot)
            {
                try
                {
                    immediate.WorkMeter.Reset(query.Budget);
                    immediate.RayWork.Begin(new NavigationRayRequest(context.World, store, snapshot.Graph,
                        query.Agent, policy, TraversalMedium.Solid, foot, anchor.Position,
                        NavigationRayEndpointAllowance.None, NavigationRayChainConstraint.FinishAt(anchor.Address)));
                    immediate.RayWork.Advance(immediate.WorkMeter).Should().Be(NavigationRayStatus.Success);
                    immediate.RayWork.TryGetSuccessfulDependencies(out var dependencies, out _).Should().BeTrue();
                    dependencies.PageCount.Should().BeGreaterThan(1);
                    rayLookups = immediate.WorkMeter.LookupProbes;
                }
                finally { immediate.RayWork.Reset(); }
            }
        }
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Success);
        context.Guides.TryProveAStarConnector(WithBudget(query, rayLookups), foot, guide)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        cache.LeasedBytes.Should().Be(bytes);
        cache.ReservedPayloadBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        guide.CurrentStepIndex.Should().Be(0);
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
    }

    [Fact]
    public void Connector_AdvancedOrEmptyLeaseMustNotRewindOrRetainProof()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        context.Guides.TryProveAStarConnector(query, query.Start.Position, default)
            .Should().Be(NavigationGuideStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        int cursor = guide.CurrentStepIndex;
        long bytes = cache.LeasedBytes;
        context.Guides.TryProveAStarConnector(query, query.Start.Position, guide)
            .Should().Be(NavigationGuideStatus.Unsupported);
        guide.CurrentStepIndex.Should().Be(cursor);
        guide.HasConnector.Should().BeFalse();
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        cache.LeasedBytes.Should().Be(bytes);
        cache.ReservedPayloadBytes.Should().Be(0);
    }

    [Fact]
    public void Connector_MismatchedMediumMustRefuseWithoutChangingRouteOrRetainingProof()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long bytes = cache.LeasedBytes;
        PathQuery differentMedium = query.WithStartState(query.Start.Position, TraversalMedium.Gas);
        context.Guides.TryProveAStarConnector(differentMedium, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Unsupported);
        guide.CurrentStepIndex.Should().Be(0);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        cache.LeasedBytes.Should().Be(bytes);
        cache.ReservedPayloadBytes.Should().Be(0);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void Connector_MissingQueryPolicyMustNotRetainProofOrAlterRoute()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var missingPolicy = new PathQuery(query.Start, query.End, query.Agent,
            new NavigationAreaPolicyKey("missing", 1), query.Traversal, query.Algorithm, query.Budget, false);
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long baseline = cache.LeasedBytes;
        context.Guides.TryProveAStarConnector(missingPolicy, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Stale);
        cache.LeasedBytes.Should().Be(baseline);
        cache.ReservedPayloadBytes.Should().Be(0);
        guide.CurrentStepIndex.Should().Be(0);
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void Connector_EveryInsufficientLookupBudgetMustReleaseReplacementAndPermitLaterReproof()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long bytes = cache.LeasedBytes;
        Vector3d foot = query.Start.Position + Vector3d.Left;
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
        int exact = context.Pathing.ImmediateRayWorkspace.WorkMeter.LookupProbes;
        for (int budget = 0; budget < exact; budget++)
        {
            context.Guides.TryProveAStarConnector(WithBudget(query, budget), foot, guide)
                .Should().Be(NavigationGuideStatus.BudgetExceeded, "lookup budget {0} is below {1}", budget, exact);
            cache.LeasedBytes.Should().Be(bytes);
            cache.ReservedPayloadBytes.Should().Be(0);
            context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
            guide.CurrentStepIndex.Should().Be(0);
            guide.Status.Should().Be(NavigationGuideStatus.Success);
        }
        context.Guides.TryProveAStarConnector(WithBudget(query, exact), foot, guide)
            .Should().Be(NavigationGuideStatus.Success);
        cache.LeasedBytes.Should().BeGreaterThan(bytes);
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
    }

    [Theory]
    [InlineData(TraversalMedium.Solid, true)]
    [InlineData(TraversalMedium.Gas, false)]
    [InlineData(TraversalMedium.Liquid, false)]
    public void Connector_WorkspaceExactAndOneBelowMustRefuseWithoutRetainingReplacement(
        TraversalMedium medium, bool traceCapacity)
    {
        // Centers x0..x96 emit 97 intervals. Prism candidate rounding also admits
        // x97, so covered-address scratch needs 98 slots, including that miss.
        int exactCapacity = traceCapacity ? 97 : 98;
        for (int capacity = exactCapacity - 1; capacity <= exactCapacity; capacity++)
        {
            using var context = CreateLine(medium, out var query,
                settings: ConnectorWorkspaceSettings(traceCapacity ? 512 : capacity, traceCapacity ? capacity : 97));
            context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
            var guide = acquired!.Value;
            var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
            long routeBytes = cache.LeasedBytes;
            try
            {
                context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
                    .Should().Be(NavigationGuideStatus.Success);
                cache.LeasedBytes.Should().BeGreaterThan(routeBytes);
                var foot = new Vector3d(Fixed64.Zero, query.Start.Position.Y, Fixed64.Zero);
                context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(capacity == exactCapacity
                    ? NavigationGuideStatus.Success : NavigationGuideStatus.CapacityExceeded);
                guide.CurrentStepIndex.Should().Be(0);
                guide.TryGetCurrentStep(out var anchor).Should().Be(NavigationGuideStatus.Success);
                anchor.Address.Should().Be(new NavigationCellAddress("connector", new VoxelIndex(96, 0, 0)));
                cache.ReservedPayloadBytes.Should().Be(0);
                context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
                if (capacity < exactCapacity)
                {
                    cache.LeasedBytes.Should().Be(routeBytes, "failed replacement dropped the old proof and retained no scratch");
                    context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
                        .Should().Be(NavigationGuideStatus.Success, "the refused longer segment must leave the shared workspace reusable");
                }
                cache.LeasedBytes.Should().BeGreaterThan(routeBytes);
                cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
            }
            finally { guide.Dispose(); }
            cache.LeasedBytes.Should().Be(0);
            cache.ReservedPayloadBytes.Should().Be(0);
        }
    }

    private static TrailblazerWorldContextSettings ConnectorWorkspaceSettings(int coveredCapacity, int traceCapacity)
    {
        var d = TrailblazerWorldContextSettings.Default;
        var q = d.QueryLimits;
        var limits = new NavigationQueryLimits(q.MaxBatchItems, q.MaxBatchDescriptorBytes, q.MaxConcurrentNavigationQueries,
            q.AStarWorkspaceMapCapacity, q.AStarWorkspaceEndpointPageCapacity, q.AStarWorkspaceComponentCapacity,
            q.AStarWorkspaceNodeCapacity, q.MaxAStarCacheEntries, q.MaxAStarReusablePayloadBytes,
            q.MaxAStarSinglePayloadBytes, q.MaxAStarActivePayloadBytes, q.MaxAStarActivePayloadLeases,
            q.FlowWorkspaceMapCapacity, q.FlowWorkspaceEndpointPageCapacity, q.FlowWorkspaceComponentCapacity,
            q.FlowWorkspaceNodeCapacity, coveredCapacity, traceCapacity, q.AStarWorkspaceGuidePointCapacity,
            q.MaxFlowCacheEntries, q.MaxFlowReusablePayloadBytes, q.MaxFlowSinglePayloadBytes,
            q.MaxFlowActivePayloadBytes, q.MaxFlowActivePayloadLeases);
        return new TrailblazerWorldContextSettings(d.OperationLimits, d.MaintenanceBudget, d.GuideSampleBudget,
            d.MovementGroupPadding, d.MaxIngressEntries, d.MaxIngressBytes, d.MaxActiveSnapshots, d.MaxActiveSnapshotBytes,
            d.MaxRetiredSnapshots, d.MaxRetiredSnapshotBytes, d.MaxPersistentGraphPages, d.MaxDynamicCellSlotsPerMap,
            d.MaxDynamicCellSlots, d.NavigationAreaCount, d.MaxAreaPolicies, d.MaxAreaRulesPerPolicy, d.MaxAreaRules,
            d.MaxConcurrentSnapshotLeases, limits);
    }

    [Theory]
    [InlineData("InstallPublication")]
    [InlineData("InstallRawMutation")]
    [InlineData("ServicePublication")]
    [InlineData("ServiceRawMutation")]
    [InlineData("ServiceStaleBeforeCapacity")]
    [InlineData("ServiceFreshCapacity")]
    public void Connector_InstallMustRejectMutationAfterRayAndCapture(string scenario)
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long baseline = cache.LeasedBytes;
        guide.BeginConnector(out var anchor).Should().Be(NavigationGuideStatus.Success);
        var store = context.Pathing.NavigationGraphStore;
        using var snapshot = store.TryAcquire()!;
        snapshot.Graph.AreaCatalog.TryGet(query.AreaPolicy, out NavigationAreaPolicy policy).Should().BeTrue();
        var immediate = context.Pathing.ImmediateRayWorkspace;
        Vector3d foot = new(Fixed64.Zero, query.Start.Position.Y, Fixed64.Zero);
        lock (immediate.SyncRoot)
        {
            var ray = immediate.RayWork;
            ray.Begin(new NavigationRayRequest(context.World, store, snapshot.Graph, query.Agent, policy,
                TraversalMedium.Solid, foot, anchor.Position, NavigationRayEndpointAllowance.None,
                NavigationRayChainConstraint.FinishAt(anchor.Address)));
            immediate.WorkMeter.Reset(query.Budget);
            ray.Advance(immediate.WorkMeter).Should().Be(NavigationRayStatus.Success);
            ray.TryGetSuccessfulDependencies(out var dependencies, out ulong epoch).Should().BeTrue();
            var sort = new NavigationDependencySortWork(dependencies.Components, dependencies.ComponentCount,
                dependencies.Pages, dependencies.PageCount);
            sort.Advance(immediate.WorkMeter, 65536).Should().BeTrue();
            long reservation = NavigationAStarConnectorProof.MetadataBytes + policy.RetainedBytes
                + GraphDependencyStamp.GetRetainedBytes(dependencies.ComponentCount, dependencies.PageCount);
            cache.TryReserveEndpointProof(reservation).Should().BeTrue();
            try
            {
                var capture = new NavigationDependencyStampWork(snapshot.Graph, policy, dependencies.Components,
                    dependencies.ComponentCount, dependencies.Pages, dependencies.PageCount);
                capture.Advance(immediate.WorkMeter, 65536).Should().BeTrue();
                var proof = new NavigationAStarConnectorProof(capture.Result, policy, anchor, foot, epoch);
                NavigationGuideLease empty = default;
                empty.HasConnector.Should().BeFalse();
                long beforeEmpty = reservation;
                empty.InstallConnector(proof, ref reservation).Should().Be(NavigationGuideStatus.Stale);
                reservation.Should().Be(beforeEmpty, "an empty handle cannot take ownership of the caller's proof reservation");
                ray.Reset();
                if (scenario.EndsWith("RawMutation")) context.World.ActiveGrids[0].TryRemoveVoxel(default).Should().BeTrue();
                else if (scenario != "ServiceFreshCapacity") PublishCost(context, 0, 3);
                guide.Status.Should().Be(NavigationGuideStatus.Success, "the unchanged payload does not certify the connector");
                var held = new System.Collections.Generic.List<NavigationWorldGraphLease>();
                try
                {
                    if (scenario.EndsWith("Capacity"))
                    {
                        for (int i = 0; i < context.Settings.MaxConcurrentSnapshotLeases; i++)
                        {
                            var retained = store.TryAcquire();
                            if (retained == null) break;
                            held.Add(retained);
                        }
                        store.TryAcquire().Should().BeNull();
                    }
                    (scenario.StartsWith("Service")
                        ? context.Guides.CompleteAStarConnectorProof(guide, proof, ref reservation)
                        : guide.InstallConnector(proof, ref reservation)).Should().Be(scenario == "ServiceFreshCapacity"
                            ? NavigationGuideStatus.CapacityExceeded : NavigationGuideStatus.Stale);
                    reservation.Should().Be(scenario.StartsWith("Service") && scenario != "ServiceFreshCapacity"
                        ? beforeEmpty : 0, "service precheck refusal leaves caller ownership; Install consumes and releases it");
                }
                finally { foreach (var retained in held) retained.Dispose(); }
                cache.LeasedBytes.Should().Be(baseline);
                guide.CurrentStepIndex.Should().Be(0);
            }
            finally { cache.ReleaseEndpointProof(ref reservation); ray.Reset(); }
        }
    }

    [Theory]
    [InlineData(TraversalMedium.Gas)]
    [InlineData(TraversalMedium.Liquid)]
    public void Connector_VolumeProofMustIncludeBodyOnlyPagesAndFullCoveredBudget(TraversalMedium medium)
    {
        using var context = CreateLine(medium, out var query, wide: true);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        Vector3d foot = query.Start.Position - Vector3d.Right * 75;
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
        int covered = context.Pathing.ImmediateRayWorkspace.WorkMeter.CoveredVoxelIntervals;
        covered.Should().BeGreaterThan(76, "the body covers more than the centerline");
        var exact = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy, query.Traversal,
            query.Algorithm, new NavigationWorkBudget(65536, 65536, 65536, 65536, 65536, 0, 0, 0, 65536, covered, 0), false);
        context.Guides.TryProveAStarConnector(exact, foot, guide).Should().Be(NavigationGuideStatus.Success);
        var shortBudget = new PathQuery(query.Start, query.End, query.Agent, query.AreaPolicy, query.Traversal,
            query.Algorithm, new NavigationWorkBudget(65536, 65536, 65536, 65536, 65536, 0, 0, 0, 65536, covered - 1, 0), false);
        context.Guides.TryProveAStarConnector(shortBudget, foot, guide).Should().Be(NavigationGuideStatus.BudgetExceeded);
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
        // x21/z1 is the first centerline cell (slot64/page1). Its radius reaches
        // x20/z1 (slot61/page0), which no centerline interval visits.
        PublishCost(context, 20, 3, NavigationCell.ToMedia(medium), 1);
        guide.Status.Should().Be(NavigationGuideStatus.Success, "the original route is near x96");
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Stale);
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
    }

    [Fact]
    public void DisposedConnectorGeneration_ShouldNotReleaseAReusedGuidesProof()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var old).Should().Be(NavigationGuideStatus.Success);
        var stale = old!.Value;
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, stale).Should().Be(NavigationGuideStatus.Success);
        stale.Dispose();
        context.Guides.RequestGuide(query, out var next).Should().Be(NavigationGuideStatus.Success);
        using var active = next!.Value;
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, active).Should().Be(NavigationGuideStatus.Success);
        long charged = context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes;
        stale.HasConnector.Should().BeFalse("a disposed generation must not inherit the reused guide's connector");
        stale.Dispose();
        stale.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, stale).Should().Be(NavigationGuideStatus.Stale);
        active.Status.Should().Be(NavigationGuideStatus.Success);
        active.HasConnector.Should().BeTrue();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.LeasedBytes.Should().Be(charged);
    }

    [Fact]
    public void Connector_ShouldFinishAtExactMapAddressAmongCoincidentMaps()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(191, 1, 0), storageKind: GridStorageKind.Sparse);
        var overlapIndices = new VoxelIndex[192];
        for (int x = 0; x < overlapIndices.Length; x++) overlapIndices[x] = new VoxelIndex(x, 0, 0);
        context.World.TryAddGrid(configuration, overlapIndices, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("aaa-overlap", binding);
        for (int x = 0; x < 192; x++) builder.AddCell(new VoxelIndex(x, 0, 0), GuidedPathTestScene.Cell(TraversalMedia.Solid));
        var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear, 3, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        context.Guides.TryProveAStarConnector(query, query.Start.Position + Vector3d.Left, guide)
            .Should().Be(NavigationGuideStatus.Success);
        guide.TryGetCurrentStep(out var step).Should().Be(NavigationGuideStatus.Success);
        step.Address.Should().Be(new NavigationCellAddress("connector", new VoxelIndex(96, 0, 0)));
        guide.CurrentStepIndex.Should().Be(0);
    }

    [Fact]
    public void ConnectorOnlyPublication_ShouldInvalidateOnlyItsOwnProofAndUnrelatedPagesRemainReusable()
    {
        using var context = CreateLine(TraversalMedium.Solid, out var query);
        context.Guides.RequestGuide(query, out var a).Should().Be(NavigationGuideStatus.Success);
        context.Guides.RequestGuide(query, out var b).Should().Be(NavigationGuideStatus.Success);
        using var connector = a!.Value;
        using var independent = b!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long baseline = cache.LeasedBytes;
        Vector3d foot = new(Fixed64.Zero, query.Start.Position.Y, Fixed64.Zero);
        context.Guides.TryProveAStarConnector(query, foot, connector).Should().Be(NavigationGuideStatus.Success);
        long charged = cache.LeasedBytes;
        charged.Should().BeGreaterThan(baseline);
        PublishCost(context, 160, 3);
        independent.Status.Should().Be(NavigationGuideStatus.Success);
        context.Guides.TryProveAStarConnector(query, foot, connector).Should().Be(NavigationGuideStatus.Success);
        cache.LeasedBytes.Should().Be(charged);
        PublishCost(context, 0, 4);
        independent.Status.Should().Be(NavigationGuideStatus.Success);
        context.Guides.TryProveAStarConnector(query, foot, connector).Should().Be(NavigationGuideStatus.Stale);
        cache.LeasedBytes.Should().Be(baseline);
        connector.CurrentStepIndex.Should().Be(0);
        context.Guides.TryProveAStarConnector(query, foot, connector).Should().Be(NavigationGuideStatus.Success);
        connector.TotalCost.Should().Be(independent.TotalCost, "connector costs never rewrite the route payload");
        cache.AcquisitionDiagnostics.BuildStarts.Should().Be(1);
    }

    [Theory]
    [InlineData(TraversalMedium.Solid)]
    [InlineData(TraversalMedium.Gas)]
    [InlineData(TraversalMedium.Liquid)]
    public void Connector_ShouldUseOrdinaryQueryBudgetAndRejectRawMutation(TraversalMedium medium)
    {
        using var context = CreateLine(medium, out var query);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        long baseline = cache.LeasedBytes;
        Vector3d foot = query.Start.Position + Vector3d.Left;
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
        int lookups = context.Pathing.ImmediateRayWorkspace.WorkMeter.LookupProbes;
        lookups.Should().BeGreaterThan(0);
        var exact = WithBudget(query, lookups);
        context.Guides.TryProveAStarConnector(exact, foot, guide).Should().Be(NavigationGuideStatus.Success);
        context.Guides.TryProveAStarConnector(WithBudget(query, lookups - 1), foot, guide)
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        cache.LeasedBytes.Should().Be(baseline);
        cache.ReservedPayloadBytes.Should().Be(0);
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Success);
        context.World.ActiveGrids[0].TryRemoveVoxel(new VoxelIndex(95, 0, 0)).Should().BeTrue();
        context.Guides.TryProveAStarConnector(query, foot, guide).Should().Be(NavigationGuideStatus.Stale);
        guide.CurrentStepIndex.Should().Be(0);
        guide.Dispose();
        cache.LeasedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Connector_ShouldPreserveExactOrdinaryAnchorForSameCellAndSimplifiedRoutes(bool sameCell, bool simplify)
    {
        using var context = CreateLine(TraversalMedium.Solid, out var original);
        Vector3d submitted = original.Start.Position + Vector3d.Right * Fixed64.Quarter;
        var query = new PathQuery(new NavigationEndpoint(submitted, "connector"),
            sameCell ? new NavigationEndpoint(original.Start.Position + Vector3d.Left * Fixed64.Quarter, "connector") : original.End,
            original.Agent, original.AreaPolicy, original.Traversal, original.Algorithm,
            new NavigationWorkBudget(65536, 65536, 65536, 65536, 65536, 0, 0, 0, 65536, 65536, simplify ? 65536 : 0), false);
        context.Guides.RequestGuide(query, out var acquired).Should().Be(NavigationGuideStatus.Success);
        using var guide = acquired!.Value;
        guide.TryGetCurrentStep(out var before).Should().Be(NavigationGuideStatus.Success);
        before.HasTransition.Should().BeFalse();
        before.Position.Should().Be(original.Start.Position);
        before.Position.Should().NotBe(submitted);
        Fixed64 cost = guide.TotalCost;
        context.Guides.TryProveAStarConnector(query, submitted + Vector3d.Left, guide).Should().Be(NavigationGuideStatus.Success);
        guide.CurrentStepIndex.Should().Be(0);
        guide.TryGetCurrentStep(out var after).Should().Be(NavigationGuideStatus.Success);
        after.Address.Should().Be(before.Address);
        after.Position.Should().Be(before.Position);
        guide.TotalCost.Should().Be(cost);
    }

    private static PathQuery WithBudget(PathQuery query, int lookups) => new(query.Start, query.End, query.Agent,
        query.AreaPolicy, query.Traversal, query.Algorithm,
        new NavigationWorkBudget(lookups, 65536, 65536, 65536, 65536, 0, 0, 0, 65536, 65536, 0), false);

    internal static void PublishCost(TrailblazerWorldContext context, int x, long sequence,
        TraversalMedia media = TraversalMedia.Solid, int z = 0)
    {
        var cell = new NavigationCell(media, TraversalCapability.None, default,
            (Fixed64)2, (Fixed64)2, (Fixed64)2);
        var delta = new NavigationMapOverlayDelta("connector", cells: new[]
            { NavigationCellOverlayOperation.Set(new VoxelIndex(x, 0, z), cell) });
        var operation = new NavigationOverlayCommitOperation(new PreparedNavigationOverlay(
            new NavigationOverlayTransaction(new[] { delta })), sequence, context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        GuidedPathTestScene.AdvanceUntilApplied(context, operation.Receipt);
    }

    internal static TrailblazerWorldContext CreateLine(TraversalMedium medium, out PathQuery query, bool wide = false,
        TrailblazerWorldContextSettings? settings = null)
    {
        var context = TrailblazerWorldContext.CreateOwned(settings: settings);
        int rows = wide ? 3 : 1;
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(191, 0, rows - 1), storageKind: GridStorageKind.Sparse);
        var indices = new VoxelIndex[192 * rows];
        for (int x = 0; x < 192; x++)
            for (int z = 0; z < rows; z++) indices[x * rows + z] = new VoxelIndex(x, 0, z);
        context.World.TryAddGrid(configuration, indices, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        TraversalMedia media = NavigationCell.ToMedia(medium);
        var builder = new NavigationMapBuilder("connector", binding);
        foreach (VoxelIndex index in indices)
            builder.AddCell(index, new NavigationCell(media, TraversalCapability.None,
                default, Fixed64.One, (Fixed64)2, (Fixed64)2));
        var policy = new NavigationAreaPolicy(new NavigationAreaPolicyKey("connector", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(context, builder.Build(), 1, OverlayReplacementPolicy.Clear, 1, policy, 2);
        var profile = new NavigationAgentProfile(new KinematicBodyShape(wide ? (Fixed64)0.6m : Fixed64.Zero,
            wide ? Fixed64.Half : Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, media, TraversalCapability.None);
        Fixed64 footY = wide ? -Fixed64.Quarter : -Fixed64.Half;
        Fixed64 footZ = wide ? Fixed64.One : Fixed64.Zero;
        query = new PathQuery(new NavigationEndpoint(new Vector3d((Fixed64)96, footY, footZ), "connector"),
            new NavigationEndpoint(new Vector3d((Fixed64)97, footY, footZ), "connector"), profile, policy.Key,
            new TraversalIntent(medium, media), PathAlgorithm.AStar,
            new NavigationWorkBudget(65536, 65536, 65536, 65536, 65536, 0, 0, 0, 65536, 65536, 0), false);
        return context;
    }
}
