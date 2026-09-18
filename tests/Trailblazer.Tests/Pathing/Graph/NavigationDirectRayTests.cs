using System;
using System.Collections.Generic;
using System.Reflection;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationDirectRayTests
{
    [Theory]
    [InlineData(false, 1, 3, false)]
    [InlineData(true, 1, 3, false)]
    [InlineData(false, 2, 3, false)]
    [InlineData(true, 3, 3, false)]
    [InlineData(false, 4, 3, false)]
    [InlineData(false, 2, 8, true)]
    public void DirectService_ShouldStopAfterACompleteSeparatingWall(
        bool sparse, int wallKind, int wallX, bool reverse)
    {
        using var scene = new Scene(sparse, wallKind, wallX);
        NavigationRayRequest request = scene.Request(reverse);
        var complete = new NavigationRayWork(new NavigationRayWorkspace(1, 32, 32, 256, 256));
        var meter = new NavigationWorkMeter(Scene.Budget);
        complete.Begin(request);
        complete.Advance(meter).Should().Be(NavigationRayStatus.Blocked);

        scene.Context.Guides.TryGetDirectHeading(scene.Query(reverse), request.Start, out Vector3d heading)
            .Should().Be(NavigationRayStatus.Blocked);

        heading.Should().Be(Vector3d.Zero);
        scene.Context.Pathing.ImmediateRayWorkspace.WorkMeter.TraceIntervals.Should()
            .BeLessThan(meter.TraceIntervals, "the wall closes a strict slab before the complete suffix");
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void CertifiedInitialAndGuidedChecks_ShouldAcquireInTheSameCallAndKeepFreshGuidance(
        PathAlgorithm algorithm)
    {
        using var scene = new Scene(false, 2, 3);
        var navigator = new TestNavigator(scene.Context);
        navigator.Setup(scene.Start, Scene.Profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = scene.Start.Y,
            GroundState = new GroundCondition()
        });
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(-1);
        navigator.ApplyGuidedTrekRequest(scene.Query(algorithm: algorithm));

        Vector3d first = steering.GetHeading(navigator, out var transition);
        first.Should().NotBe(Vector3d.Zero);
        transition.Should().BeNull();
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.HasLineOfSightPath.Should().BeFalse();
        int firstTrace = scene.Context.Pathing.ImmediateRayWorkspace.WorkMeter.TraceIntervals;
        firstTrace.Should().BeLessThan(31, "the initial check should use the strict-slab decision");

        steering.GetHeading(navigator, out transition).Should().Be(first);
        transition.Should().BeNull();
        steering.HasNavigationGuidance.Should().BeTrue();
        steering.HasLineOfSightPath.Should().BeFalse();
        scene.Context.Pathing.ImmediateRayWorkspace.WorkMeter.TraceIntervals.Should().Be(firstTrace);
        navigator.Reset();
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 5)]
    [InlineData(false, true, 5)]
    public void ClearContinuation_ShouldPreserveCanonicalOutputCostDependenciesAndEveryDebit(
        bool sparse, bool reverse, int wallKind)
    {
        using var scene = new Scene(sparse, wallKind, endZ: 5);
        NavigationRayRequest request = scene.Request(reverse);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        var completeWorkspace = new NavigationRayWorkspace(1, 32, 32, 256, 256);
        var complete = new NavigationRayWork(completeWorkspace);
        var meter = new NavigationWorkMeter(Scene.Budget);
        complete.Begin(request);
        complete.Advance(meter).Should().Be(NavigationRayStatus.Success);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.Success);

        immediate.RayWork.Result.Should().BeEquivalentTo(complete.Result);
        Debits(immediate.WorkMeter).Should().Equal(Debits(meter));
        NavigationRayWorkspace directWorkspace = Workspace(immediate);
        directWorkspace.TraceIntervals.AsReadOnlySpan().ToArray().Should()
            .Equal(completeWorkspace.TraceIntervals.AsReadOnlySpan().ToArray());
        directWorkspace.ChainRecords.Should().Equal(completeWorkspace.ChainRecords);
        directWorkspace.Dependencies.Pages.AsSpan(0, directWorkspace.Dependencies.PageCount).ToArray()
            .Should().BeEquivalentTo(completeWorkspace.Dependencies.Pages.AsSpan(0, completeWorkspace.Dependencies.PageCount).ToArray());
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
        scene.Context.Guides.TryGetDirectHeading(scene.Query(reverse), request.Start, out Vector3d heading)
            .Should().Be(wallKind == 5 ? NavigationRayStatus.Blocked : NavigationRayStatus.Success);
        if (wallKind == 0)
            heading.Should().Be((request.End - request.Start).Normalized);
        else
            heading.Should().Be(Vector3d.Zero, "an expensive admitted cell disqualifies cost-neutral travel, not geometry");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void Certificate_ShouldRemainDistinctFromTheCompleteRay(bool reverse, int wallX)
    {
        using var scene = new Scene(wallKind: 2, wallX: wallX);
        NavigationRayRequest request = scene.Request(reverse);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.CertifiedObstruction);
        immediate.RayWork.Result.Status.Should().Be(NavigationRayStatus.CertifiedObstruction);
        immediate.RayWork.Begin(request);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Advance(immediate.WorkMeter).Should().Be(NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void LiteralEndpointMargin_ShouldRejectRoundedEndpointPeers(long gapRaw, bool certified)
    {
        using var scene = new Scene(wallKind: 2);
        Vector3d start = new((Fixed64)2 + Fixed64.Half - Fixed64.FromRaw(gapRaw), scene.Start.Y, (Fixed64)2);
        NavigationRayRequest request = Copy(scene.Request(), start: start);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(certified
            ? NavigationRayStatus.CertifiedObstruction : NavigationRayStatus.Blocked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClearContinuation_ShouldPreserveExactAndOneBelowQueryCeilings(int category)
    {
        using var scene = new Scene(endZ: 5);
        NavigationRayRequest request = scene.Request(reverse: true);
        var complete = new NavigationRayWork(new NavigationRayWorkspace(1, 32, 32, 256, 256));
        var meter = new NavigationWorkMeter(Scene.Budget);
        complete.Begin(request);
        complete.Advance(meter).Should().Be(NavigationRayStatus.Success);
        int[] costs = { meter.LookupProbes, meter.CoveredVoxelIntervals, meter.TraceIntervals,
            meter.EvaluatedEdges, meter.ConnectionLegs };
        for (int difference = 0; difference <= 1; difference++)
        {
            int limit = Math.Max(0, costs[category] - difference);
            var budget = new NavigationWorkBudget(
                category == 0 ? limit : 65536, 0, 0,
                category == 3 ? limit : 65536, category == 4 ? limit : 4096,
                0, 0, 0, category == 2 ? limit : 256, category == 1 ? limit : 256, 0);
            CompareFull(request, budget);
        }
    }

    [Theory]
    [InlineData(0, 32, 32, 256, 256)]
    [InlineData(1, 0, 32, 256, 256)]
    [InlineData(1, 32, 0, 256, 256)]
    [InlineData(1, 32, 32, 143, 31)]
    [InlineData(1, 32, 32, 256, 30)]
    [InlineData(1, 32, 32, 256, 31)]
    [InlineData(1, 2, 1, 256, 256)]
    [InlineData(1, 3, 1, 256, 256)]
    [InlineData(1, 32, 32, 83, 16)]
    [InlineData(1, 32, 32, 84, 16)]
    [InlineData(1, 32, 32, 256, 15)]
    [InlineData(1, 32, 32, 256, 16)]
    [InlineData(1, 0, 0, 256, 0)]
    public void ClearContinuation_ShouldPreserveScratchCeilings(
        int maps, int pages, int components, int covered, int traces)
    {
        using var scene = new Scene(endZ: 5);
        CompareFull(scene.Request(), Scene.Budget, maps, pages, components, covered, traces);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void IneligibleAttempts_ShouldPreserveCompleteResultAndCharges(int kind)
    {
        using var scene = new Scene(wallKind: 2);
        NavigationRayRequest request = scene.Request();
        if (kind == 0)
            request = Copy(request, end: new Vector3d(request.Start.X, request.End.Y, request.End.Z));
        if (kind == 1)
            request = Copy(request, end: new Vector3d(request.End.X, request.End.Y, request.Start.Z));
        if (kind == 2)
            request = Copy(request, allowance: NavigationRayEndpointAllowance.StartPrefix);
        if (kind == 3)
            request = Copy(request, constraint: NavigationRayChainConstraint.SourceOnly(new NavigationCellAddress("direct", default)));
        if (kind == 4)
            request = Copy(request, medium: TraversalMedium.Gas);
        if (kind == 5)
            request = Copy(request, medium: TraversalMedium.Liquid);
        if (kind == 6)
            scene.Context.World.TryAddGrid(new GridConfiguration(new Vector3d(30, 0, 30), new Vector3d(32, 0, 32)), out _).Should().BeTrue();
        if (kind == 7)
            scene.Context.Pathing.NavigationGraphStore.TryPublish(scene.Graph.WithGraphVersion(scene.Graph.GraphVersion + 1))
                .Should().Be(NavigationCandidatePublication.Published);
        CompareFull(request, Scene.Budget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAttempts_ShouldAllocateNothingAfterWarmupAndClearTransientStorage(bool blocked)
    {
        using var scene = new Scene(wallKind: blocked ? 2 : 0, endZ: 5);
        NavigationRayRequest request = scene.Request(reverse: true);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        for (int i = 0; i < 16; i++)
        {
            immediate.WorkMeter.Reset(Scene.Budget);
            immediate.RayWork.Begin(request);
            immediate.RayWork.AdvanceDirect(immediate);
            immediate.RayWork.Reset();
        }
        NavigationRayStatus status = default;
        long allocated = AllocationTestUtility.MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < 16; i++)
            {
                immediate.WorkMeter.Reset(Scene.Budget);
                immediate.RayWork.Begin(request);
                status = immediate.RayWork.AdvanceDirect(immediate);
                immediate.RayWork.Reset();
            }
        });
        allocated.Should().Be(0);
        status.Should().Be(blocked ? NavigationRayStatus.CertifiedObstruction : NavigationRayStatus.Success);
        immediate.SourceOrdinals.Length.Should().Be(256);
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
        immediate.UsedOrdinalCount.Should().Be(0);
        immediate.CertificateArmed.Should().BeFalse();
        Workspace(immediate).Dependencies.PageCount.Should().Be(0);
        Workspace(immediate).Dependencies.ComponentCount.Should().Be(0);
    }

    private static NavigationRayRequest Copy(NavigationRayRequest request,
        Vector3d? start = null, Vector3d? end = null,
        TraversalMedium? medium = null, NavigationRayEndpointAllowance? allowance = null,
        NavigationRayChainConstraint? constraint = null) => new(
        request.World, request.Store, request.ExpectedGraph, request.Profile, request.AreaPolicy,
        medium ?? request.Medium, start ?? request.Start, end ?? request.End,
        allowance ?? request.EndpointAllowance, constraint ?? request.ChainConstraint);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleMapsAndAutomaticSeams_ShouldPreserveFullRayResults(bool reverse)
    {
        using var fixture = NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: false);
        using var store = NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var request = new NavigationRayRequest(fixture.Context.World, store, fixture.Graph,
            fixture.DefaultProfile, NavigationAStarExitTestHarness.Policy, TraversalMedium.Solid,
            reverse ? fixture.End : fixture.Start, reverse ? fixture.Start : fixture.End,
            NavigationRayEndpointAllowance.None);
        fixture.Graph.MapCount.Should().Be(2);
        fixture.Graph.AutomaticSeams.PairCount.Should().BeGreaterThan(0);
        CompareFull(request, Scene.Budget, maps: 2);
    }

    [Fact]
    public void EvenInactiveExplicitOwners_ShouldDisableTheCertificate()
    {
        using var scene = new Scene(wallKind: 2);
        var definition = new NavigationConnection("authored", default,
            new NavigationCellAddress("direct", new VoxelIndex(10, 0, 10)),
            scene.Start, scene.End, Fixed64.Zero, Fixed64.One);
        var record = new NavigationExplicitConnectionRecord(new NavigationConnectionOwnerKey("direct", definition.Id),
            definition, false, Fixed64.Zero, NavigationPagedSequence<GridNavigationPortal>.Empty);
        NavigationWorldGraph graph = new(scene.Graph.GraphVersion + 1, new[] { scene.Graph.GetInstance(0) },
            scene.Graph.AreaCatalog, NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _), scene.Graph.SurfaceComponents);
        scene.Context.Pathing.NavigationGraphStore.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        CompareFull(scene.Request(), Scene.Budget);
    }

    [Fact]
    public void TiedPointContacts_ShouldPreserveTheCompleteRaysConservativeDecision()
    {
        using var scene = new Scene();
        CompareFull(scene.Request(), Scene.Budget);
        CompareFull(scene.Request(reverse: true), Scene.Budget);
    }

    [Fact]
    public void ObserverException_ShouldReleaseRequestTagsAndScratchBeforeReuse()
    {
        using var scene = new Scene(endZ: 5);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        Func<GridTraceSlab, bool> observer = immediate.ObserveSlab;
        SetObserver(immediate, slab =>
        {
            observer(slab);
            throw new InvalidOperationException("observer failure");
        });
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(scene.Request());
        Action run = () => immediate.RayWork.AdvanceDirect(immediate);
        run.Should().Throw<InvalidOperationException>().WithMessage("observer failure");
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
        Workspace(immediate).TraceIntervals.Count.Should().Be(0);
        NavigationRayRequest retained = (NavigationRayRequest)typeof(NavigationRayWork)
            .GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(immediate.RayWork)!;
        retained.World.Should().BeNull("an exception must not retain the world or graph request");
        Workspace(immediate).Dependencies.PageCount.Should().Be(0);
        SetObserver(immediate, observer);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.Success);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void PublicationDuringObservation_ShouldDisarmRatherThanInventStaleness(bool relevant, int wallKind)
    {
        using var scene = new Scene(wallKind: wallKind, endZ: 5);
        NavigationRayRequest request = scene.Request();
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        Func<GridTraceSlab, bool> observer = immediate.ObserveSlab;
        bool published = false;
        SetObserver(immediate, slab =>
        {
            if (!published)
            {
                NavigationWorldGraph next = relevant
                    ? request.ExpectedGraph.WithClosedStructuralComponents(NavigationSurfaceComponentKeySet.Empty,
                        closeAllStructuralComponents: true, request.ExpectedGraph.GraphVersion + 1)
                    : request.ExpectedGraph.WithGraphVersion(request.ExpectedGraph.GraphVersion + 1);
                request.Store.TryPublish(next).Should().Be(NavigationCandidatePublication.Published);
                published = true;
            }
            return observer(slab);
        });
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(relevant ? NavigationRayStatus.Stale
            : wallKind == 0 ? NavigationRayStatus.Success : NavigationRayStatus.Blocked);
        var complete = new NavigationRayWork(new NavigationRayWorkspace(1, 32, 32, 256, 256));
        var meter = new NavigationWorkMeter(Scene.Budget);
        complete.Begin(request);
        complete.Advance(meter).Should().Be(immediate.RayWork.Status);
        Debits(immediate.WorkMeter).Should().Equal(Debits(meter));
    }

    [Theory]
    [InlineData(8, (int)NavigationRayStatus.BudgetExceeded)]
    [InlineData(256, (int)NavigationRayStatus.Stale)]
    public void DeferredDependencyFailure_ShouldPreserveGeometryFailurePriority(int traceLimit, int expected)
    {
        using var scene = new Scene();
        NavigationWorldGraph graph = scene.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty, closeAllStructuralComponents: true, scene.Graph.GraphVersion + 1);
        scene.Context.Pathing.NavigationGraphStore.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        var budget = new NavigationWorkBudget(65536, 0, 0, 65536, 4096, 0, 0, 0, traceLimit, 256, 0);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        immediate.WorkMeter.Reset(budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be((NavigationRayStatus)expected);
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
        CompareFull(scene.Request(), budget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldMutationDuringOrAfterFinalObservation_ShouldNeverCertify(bool afterObserver)
    {
        using var scene = new Scene(wallKind: 2);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        Func<GridTraceSlab, bool> observer = immediate.ObserveSlab;
        bool changed = false;
        SetObserver(immediate, slab =>
        {
            bool keepGoing = true;
            if (afterObserver)
                keepGoing = observer(slab);
            if (!changed && (afterObserver ? !keepGoing : slab.XIndex == 3))
            {
                MutateDistantObstacle(scene);
                changed = true;
            }
            return afterObserver ? keepGoing : observer(slab);
        });
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.Stale);
        changed.Should().BeTrue();
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
    }

    [Theory]
    [InlineData(256, 10)]
    [InlineData(8, 0)]
    public void WorldMutationOnAnAdmittedSlab_ShouldOverrideSuccessOrLaterGeometryFailure(int traceLimit, int mutationX)
    {
        using var scene = new Scene(endZ: 5);
        var budget = new NavigationWorkBudget(65536, 4096, 4096, 65536, 4096, 0, 0, 0, traceLimit, 256, 0);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        Func<GridTraceSlab, bool> observer = immediate.ObserveSlab;
        bool changed = false;
        SetObserver(immediate, slab =>
        {
            if (slab.XIndex == mutationX)
            {
                // Mutate after these intervals were captured, so mapping this admitted slab
                // cannot detect the change through its captured per-grid sequence.
                MutateDistantObstacle(scene);
                changed = true;
            }
            return observer(slab);
        });
        immediate.WorkMeter.Reset(budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.Stale);
        changed.Should().BeTrue();
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
    }

    private static void MutateDistantObstacle(Scene scene)
    {
        Exception? failure = null;
        var mutation = new System.Threading.Thread(() =>
        {
            try
            {
                scene.Grid.TryGetVoxel(new VoxelIndex(11, 0, 11), out Voxel? voxel).Should().BeTrue();
                scene.Grid.TryAddObstacle(voxel!, scene.Context.World.AllocateObstacleToken()).Should().BeTrue();
            }
            catch (Exception exception) { failure = exception; }
        });
        mutation.Start();
        mutation.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        failure.Should().BeNull();
    }

    private static void SetObserver(NavigationImmediateRayWorkspace immediate, Func<GridTraceSlab, bool> observer) =>
        typeof(NavigationImmediateRayWorkspace).GetField("<ObserveSlab>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(immediate, observer);

    [Fact]
    public void TerminalCertificate_ShouldNotReplayWorkAndResetShouldDropTheDecision()
    {
        using var scene = new Scene(wallKind: 2);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.Pending);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.CertifiedObstruction);
        int[] consumed = Debits(immediate.WorkMeter);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.CertifiedObstruction);
        Debits(immediate.WorkMeter).Should().Equal(consumed);
        immediate.RayWork.Reset();
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.Pending);
        immediate.RayWork.Result.Should().Be(default(NavigationRayResult));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidCertificate_MaySkipAnUnobservedSuffixTraceCeiling(bool scratchLimited)
    {
        using var scene = new Scene(wallKind: 2);
        var budget = new NavigationWorkBudget(65536, 0, 0, 65536, 4096, 0, 0, 0,
            scratchLimited ? 256 : 12, 256, 0);
        int capacity = scratchLimited ? 12 : 256;
        var complete = new NavigationRayWork(new NavigationRayWorkspace(1, 32, 32, 256, capacity));
        complete.Begin(scene.Request());
        complete.Advance(new NavigationWorkMeter(budget)).Should().Be(scratchLimited
            ? NavigationRayStatus.CapacityExceeded : NavigationRayStatus.BudgetExceeded);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, capacity);
        immediate.WorkMeter.Reset(budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.CertifiedObstruction);
        immediate.WorkMeter.TraceIntervals.Should().BeLessThan(12);
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
    }

    [Theory]
    [InlineData(13, 3, 2, true)]
    [InlineData(12, 3, 2, false)]
    [InlineData(13, 2, 2, false)]
    [InlineData(13, 3, 1, false)]
    public void CertificationHeadroom_ShouldReserveTheCompleteBoundBeforeObservation(
        int lookups, int pages, int components, bool eligible)
    {
        using var scene = new Scene(wallKind: 7);
        // Twelve possible outputs reserve thirteen lookups, three captured physical pages,
        // and two Solid component records, even though this wall's prefix consumes fewer.
        var budget = new NavigationWorkBudget(lookups, 0, 0, 65536, 4096, 0, 0, 0, 12, 256, 0);
        var immediate = new NavigationImmediateRayWorkspace(1, pages, components, 256, 256);
        immediate.WorkMeter.Reset(budget);
        immediate.RayWork.Begin(scene.Request());
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(eligible
            ? NavigationRayStatus.CertifiedObstruction : NavigationRayStatus.BudgetExceeded);
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
    }

    [Fact]
    public void ValidCertificate_MaySkipAnUnobservedClosedSuffixComponent()
    {
        using var scene = new Scene(wallKind: 7);
        scene.Graph.TryGetSurfaceComponent(new NavigationCellAddress("direct", new VoxelIndex(10, 0, 10)),
            TraversalMedium.Solid, out NavigationSurfaceComponentKey suffix, out _).Should().BeTrue();
        scene.Context.Pathing.NavigationGraphStore.TryPublish(scene.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(suffix), false, scene.Graph.GraphVersion + 1))
            .Should().Be(NavigationCandidatePublication.Published);
        var request = scene.Request();
        var complete = new NavigationRayWork(new NavigationRayWorkspace(1, 32, 32, 256, 256));
        complete.Begin(request);
        complete.Advance(new NavigationWorkMeter(Scene.Budget)).Should().Be(NavigationRayStatus.Stale);
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 32, 256, 256);
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(NavigationRayStatus.CertifiedObstruction);
        Workspace(immediate).Dependencies.Components.AsSpan(0, Workspace(immediate).Dependencies.ComponentCount)
            .ToArray().Should().NotContain(suffix);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void DirectModeCertificate_ShouldWaitForTheNextHeadingBeforeAcquisition(PathAlgorithm algorithm)
    {
        using var scene = new Scene(endZ: 5);
        var navigator = new TestNavigator(scene.Context);
        navigator.Setup(scene.Start, Scene.Profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid, SurfaceLevel = scene.Start.Y, GroundState = new GroundCondition()
        });
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(-1);
        navigator.ApplyGuidedTrekRequest(scene.Query(algorithm: algorithm));
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        steering.HasLineOfSightPath.Should().BeTrue();
        scene.PublishWall();
        int frame = scene.Context.FrameCount;

        steering.GetHeading(navigator, out var action).Should().Be(Vector3d.Zero);
        action.Should().BeNull();
        steering.HasLineOfSightPath.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        scene.Context.Pathing.ImmediateRayWorkspace.WorkMeter.TraceIntervals.Should().BeLessThan(16);

        steering.GetHeading(navigator, out action).Should().NotBe(Vector3d.Zero);
        action.Should().BeNull();
        steering.HasNavigationGuidance.Should().BeTrue();
        scene.Context.FrameCount.Should().Be(frame, "retry is the next heading boundary, not necessarily the next simulated frame");
        navigator.Reset();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void ObservedStale_ShouldSuppressSameCallAcquisitionAndRecoverAfterPublication(
        PathAlgorithm algorithm, bool hasGuide)
    {
        using var scene = new Scene(wallKind: hasGuide ? 2 : 0, endZ: 5);
        var navigator = new TestNavigator(scene.Context);
        navigator.Setup(scene.Start, Scene.Profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid, SurfaceLevel = scene.Start.Y, GroundState = new GroundCondition()
        });
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(-1);
        navigator.ApplyGuidedTrekRequest(scene.Query(algorithm: algorithm));
        if (hasGuide)
        {
            steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
            steering.HasNavigationGuidance.Should().BeTrue();
        }
        scene.Grid.TryGetVoxel(new VoxelIndex(11, 0, 11), out Voxel? voxel).Should().BeTrue();
        scene.Grid.TryAddObstacle(voxel!, scene.Context.World.AllocateObstacleToken()).Should().BeTrue();
        steering.GetHeading(navigator, out var action).Should().Be(Vector3d.Zero);
        action.Should().BeNull();
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.HasLineOfSightPath.Should().BeFalse();
        scene.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        scene.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        scene.PublishWall();
        Vector3d recovered = steering.GetHeading(navigator, out _);
        if (algorithm == PathAlgorithm.FlowField && hasGuide)
        {
            // The complete-ray control has the same cached-Flow stale-result retry boundary.
            recovered.Should().Be(Vector3d.Zero);
            steering.HasNavigationGuidance.Should().BeFalse();
            recovered = steering.GetHeading(navigator, out _);
        }
        recovered.Should().NotBe(Vector3d.Zero);
        steering.HasNavigationGuidance.Should().BeTrue();
        navigator.Reset();
        scene.Context.Reset();
        scene.Context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
        scene.Context.Pathing.ImmediateRayWorkspace.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnprovedTopologyOrLayering_ShouldUseTheCompleteAuthority(int kind)
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(12, kind < 2 ? 0 : 2, 12),
            topologyKind: kind < 2 ? GridTopologyKind.HexPrism : GridTopologyKind.RectangularPrism,
            topologyMetrics: kind < 2
                ? GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, kind == 0 ? HexOrientation.PointyTop : HexOrientation.FlatTop)
                : GridTopologyMetrics.Rectangular(Fixed64.One), storageKind: GridStorageKind.Sparse);
        VoxelIndex[] indices = { default, new(1, 0, 0), new(1, 0, 1), new(2, 0, 1) };
        if (kind == 3)
            indices[3] = new VoxelIndex(2, 1, 1);
        var fixture = NavigationAStarExitTestHarness.CreateSingleMap(world, configuration, indices, "unsupported");
        using NavigationWorldGraphStore store = NavigationAStarExitTestHarness.CreateStore(fixture.Graph);
        var request = new NavigationRayRequest(world, store, fixture.Graph, fixture.DefaultProfile,
            NavigationAStarExitTestHarness.Policy, TraversalMedium.Solid,
            GuidedPathTestScene.Anchor(fixture.Binding, indices[0]),
            GuidedPathTestScene.Anchor(fixture.Binding, indices[3]), NavigationRayEndpointAllowance.None);
        CompareFull(request, Scene.Budget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSolidComponents_ShouldRetainNegativePageDependencies(bool removeMap)
    {
        using var scene = new Scene(wallKind: 6);
        NavigationRayRequest request = scene.Request();
        var immediate = new NavigationImmediateRayWorkspace(1, 32, 0, 256, 256);
        Func<GridTraceSlab, bool> observer = immediate.ObserveSlab;
        SetObserver(immediate, slab =>
        {
            if (removeMap && slab.XIndex == 1)
                request.Store.TryPublish(new NavigationWorldGraph(scene.Graph.GraphVersion + 1,
                    Array.Empty<NavigationMapInstance>(), scene.Graph.AreaCatalog)).Should().Be(NavigationCandidatePublication.Published);
            return observer(slab);
        });
        immediate.WorkMeter.Reset(Scene.Budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(removeMap ? NavigationRayStatus.Stale : NavigationRayStatus.CertifiedObstruction);
        Workspace(immediate).Dependencies.PageCount.Should().BeGreaterThan(0);
        Workspace(immediate).Dependencies.ComponentCount.Should().Be(0);
    }

    private static int[] Debits(NavigationWorkMeter meter) => new[]
    {
        meter.LookupProbes, meter.CoveredVoxelIntervals, meter.TraceIntervals,
        meter.EvaluatedEdges, meter.ConnectionLegs, meter.ExpandedNodes,
        meter.EndpointCandidates, meter.TransitionCandidates, meter.TransitionPairs,
        meter.SimplificationRays, meter.GuidePortalChecks, meter.GuidePrismChecks
    };

    private static NavigationRayWorkspace Workspace(NavigationImmediateRayWorkspace immediate) =>
        (NavigationRayWorkspace)typeof(NavigationRayWork).GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(immediate.RayWork)!;

    private static void CompareFull(NavigationRayRequest request, NavigationWorkBudget budget,
        int maps = 1, int pages = 32, int components = 32, int covered = 256, int traces = 256)
    {
        var complete = new NavigationRayWork(new NavigationRayWorkspace(maps, pages, components, covered, traces));
        var meter = new NavigationWorkMeter(budget);
        complete.Begin(request);
        NavigationRayStatus expected = complete.Advance(meter);
        var immediate = new NavigationImmediateRayWorkspace(maps, pages, components, covered, traces);
        immediate.WorkMeter.Reset(budget);
        immediate.RayWork.Begin(request);
        immediate.RayWork.AdvanceDirect(immediate).Should().Be(expected);
        immediate.RayWork.Result.Should().BeEquivalentTo(complete.Result);
        if (expected is NavigationRayStatus.Success or NavigationRayStatus.Blocked)
            Debits(immediate.WorkMeter).Should().Equal(Debits(meter));
        immediate.SourceOrdinals.Should().OnlyContain(tag => tag == 0);
    }

    private sealed class Scene : IDisposable
    {
        internal static readonly NavigationAgentProfile Profile = new(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero,
            TraversalMedia.Solid, TraversalCapability.None);
        internal static readonly NavigationWorkBudget Budget = new(
            65536, 4096, 4096, 65536, 4096, 0, 0, 0, 256, 256, 0);
        internal static readonly NavigationAreaPolicy Policy = new(
            new NavigationAreaPolicyKey("direct", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });

        internal Scene(bool sparse = false, int wallKind = 0, int wallX = 3, int endZ = 10)
        {
            var world = new GridWorld();
            Context = TrailblazerWorldContext.Attach(world, takeOwnership: true);
            var configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(11, 0, 11),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: sparse ? GridStorageKind.Sparse : GridStorageKind.Dense);
            var indexes = new List<VoxelIndex>();
            for (int x = 0; x < 12; x++)
            for (int z = 0; z < 12; z++)
                if (wallKind != 3 || x != wallX || z == 11)
                    indexes.Add(new VoxelIndex(x, 0, z));
            world.TryAddGrid(configuration, indexes.ToArray(), out ushort gridIndex).Should().BeTrue();
            Grid = world.ActiveGrids[gridIndex];
            configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
            var builder = new NavigationMapBuilder("direct", binding);
            for (int x = 0; x < 12; x++)
            for (int z = 0; z < 12; z++)
            {
                var index = new VoxelIndex(x, 0, z);
                bool wall = x == wallX && (z < 11 || wallKind == 7);
                builder.AddCell(index, new NavigationCell(
                    wallKind == 6 || wall && wallKind is 4 or 7 ? TraversalMedia.Gas : TraversalMedia.Solid,
                    wall && wallKind == 2 ? TraversalCapability.Jump : TraversalCapability.None, default,
                    wall && wallKind == 5 ? Fixed64.One : Fixed64.Zero, Fixed64.One, Fixed64.One));
                if (wall && wallKind == 1)
                {
                    Grid.TryGetVoxel(index, out Voxel? voxel).Should().BeTrue();
                    Grid.TryAddObstacle(voxel!, world.AllocateObstacleToken()).Should().BeTrue();
                }
            }
            GuidedPathTestScene.PublishMapAndPolicy(Context, builder.Build(), 1,
                OverlayReplacementPolicy.Clear, 1, Policy, 2);
            Start = GuidedPathTestScene.Anchor(binding, default);
            End = GuidedPathTestScene.Anchor(binding, new VoxelIndex(10, 0, endZ));
        }

        internal TrailblazerWorldContext Context { get; }
        internal VoxelGrid Grid { get; }
        internal Vector3d Start { get; }
        internal Vector3d End { get; }
        internal NavigationWorldGraph Graph => Context.Pathing.NavigationGraphStore.Current;
        internal NavigationRayRequest Request(bool reverse = false) => new(
            Context.World, Context.Pathing.NavigationGraphStore, Graph, Profile, Policy,
            TraversalMedium.Solid, reverse ? End : Start, reverse ? Start : End,
            NavigationRayEndpointAllowance.None);
        internal PathQuery Query(bool reverse = false, PathAlgorithm algorithm = PathAlgorithm.AStar) => new(
            new NavigationEndpoint(reverse ? End : Start, "direct"),
            new NavigationEndpoint(reverse ? Start : End, "direct"), Profile, Policy.Key,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), algorithm, Budget,
            allowTransitions: false);

        internal void PublishWall()
        {
            var builder = new NavigationMapBuilder("direct", Graph.GetInstance(0).Map.GridBinding);
            for (int x = 0; x < 12; x++)
            for (int z = 0; z < 12; z++)
                builder.AddCell(new VoxelIndex(x, 0, z), new NavigationCell(TraversalMedia.Solid,
                    x == 3 && z < 11 ? TraversalCapability.Jump : TraversalCapability.None,
                    default, Fixed64.Zero, Fixed64.One, Fixed64.One));
            var operation = new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 2),
                OverlayReplacementPolicy.Clear, 3, Context.FrameCount + 1);
            Context.Pathing.Admit(operation).Should().BeTrue();
            GuidedPathTestScene.AdvanceUntilApplied(Context, operation.Receipt);
            // An equivalent map replacement can finish before independent physical maintenance.
            // Recovery assertions require both publications, not just the map receipt.
            for (int frame = 0; frame < 1024 && !IsCurrent(); frame++)
                Context.Simulate();
            IsCurrent().Should().BeTrue();
        }

        private bool IsCurrent() => Graph.GetInstance(0).GridLastChangeSequence == Context.World.ChangeSequence
            && Graph.TryGetSurfaceComponent(new NavigationCellAddress("direct", default), TraversalMedium.Solid,
                out NavigationSurfaceComponentKey component, out _)
            && !Graph.IsSurfaceComponentClosed(component);
        public void Dispose() => Context.Dispose();
    }
}
