using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

public sealed class Navigator2DTests
{
    private static readonly NavigationAreaPolicy Policy = new(new NavigationAreaPolicyKey("ground", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });
    private static readonly NavigationWorkBudget Budget = new(8192, 128, 128, 512, 512, 128, 128, 128, 1024, 4096, 0);
    private static readonly NavigationCell Open = new(TraversalMedia.Solid, TraversalCapability.None, default,
        Fixed64.Zero, Fixed64.One, Fixed64.Two);

    [Theory]
    [InlineData(PathAlgorithm.AStar, false)]
    [InlineData(PathAlgorithm.FlowField, false)]
    [InlineData(PathAlgorithm.AStar, true)]
    [InlineData(PathAlgorithm.FlowField, true)]
    public void FollowsExactRouteUsingOnlyAcceptedMotion(PathAlgorithm algorithm, bool portal)
    {
        var first = Run();
        Run().Should().Equal(first);

        List<(Vector2d, Vector2d, NavigationCellAddress?)> Run()
        {
            using var context = CreateScene(portal);
            var start = new Vector2d(0, portal ? 1 : 0);
            var destination = Cell(4, portal ? 1 : 0);
            using var navigator = new Host(context, start, portal ? Fixed64.Half : Fixed64.Quarter);
            if (portal)
            {
                using var oversized = new Host(context, start, Fixed64.Half + Fixed64.MinIncrement, alternateId: true);
                oversized.ApplyGuidedTrekRequest(destination, Policy.Key, algorithm, Budget)
                    .Should().Be(NavigationGuideStatus.InvalidStart);
            }
            navigator.ApplyGuidedTrekRequest(destination, Policy.Key, algorithm, Budget)
                .Should().Be(NavigationGuideStatus.Success);
            navigator.GuideCost.Should().Be((Fixed64)(portal ? 4 : 6));
            var replay = new List<(Vector2d, Vector2d, NavigationCellAddress?)>();
            bool blocked = false;
            bool shortened = false;
            for (int i = 0; i < 40 && !navigator.HasArrived; i++)
            {
                context.Simulate();
                Vector2d prior = navigator.Pose.Center;
                navigator.PrepareFrameMotion();
                navigator.Pose.Center.Should().Be(prior);
                Vector2d target = navigator.Request.Target.Center;
                Vector2d accepted = target;
                if (target != prior && !blocked)
                {
                    accepted = prior;
                    blocked = true;
                }
                else if (target != prior && !shortened)
                {
                    accepted = prior + (target - prior) * Fixed64.Half;
                    shortened = true;
                }
                navigator.Accept(accepted);
                navigator.CommitFrameMotion();
                context.LateSimulate();
                navigator.Pose.Center.Should().Be(accepted);
                navigator.Velocity.Should().Be((accepted - prior) * (Fixed64)16);
                ((IVoxelOccupant)navigator).Position.Y.Should().Be(Fixed64.One);
                var committed = navigator.LastCommittedCell?.Address;
                committed.Should().NotBe(Cell(2, 0));
                replay.Add((navigator.Pose.Center, navigator.Velocity, committed));
            }
            blocked.Should().BeTrue();
            shortened.Should().BeTrue();
            navigator.HasArrived.Should().BeTrue("the final center was {0} and guidance was {1}",
                navigator.Pose.Center, navigator.GuidanceStatus);
            navigator.Pose.Center.Should().Be(new Vector2d(4, portal ? 1 : 0));
            navigator.LastCommittedCell!.Value.Address.Should().Be(destination);
            GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().ContainSingle();
            navigator.Dispose();
            GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
            return replay;
        }
    }

    [Fact]
    public void OccupancyDropsExitedTicketWhenPublicationIsUnavailable()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var changes = new List<NavigationCellAddress?>();
        navigator.CommittedCellChanged += state => changes.Add(state?.Address);
        var store = context.Pathing.NavigationGraphStore;
        Step(Vector2d.Zero, unavailable: false);
        Step(Vector2d.Zero, unavailable: true);
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().ContainSingle();
        Step(new Vector2d(9, 0), unavailable: true);
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
        navigator.LastCommittedCell!.Value.Address.Should().Be(Cell(0, 0));
        Step(new Vector2d(9, 0), unavailable: true);
        Step(new Vector2d(9, 0), unavailable: false);
        navigator.LastCommittedCell.Should().BeNull();

        // A physical cell without authored navigation semantics still owns occupancy.
        Step(new Vector2d(2, 0), unavailable: false);
        navigator.LastCommittedCell.Should().BeNull();
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().ContainSingle();
        Step(new Vector2d(9, 0), unavailable: false);
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
        changes.Should().Equal(Cell(0, 0), null);

        void Step(Vector2d accepted, bool unavailable)
        {
            context.Simulate();
            navigator.PrepareFrameMotion(Vector2d.Zero);
            if (unavailable)
                store.MarkSafetyPending();
            navigator.Accept(accepted);
            navigator.CommitFrameMotion();
            store.ClearSafetyPending();
            context.LateSimulate();
        }
    }

    [Fact]
    public void WarmOccupancyHandoffsKeepTheActorRecord()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        for (int i = 0; i < 32; i++)
            Step(i);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
            Step(i);
        long controllerBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator));
        navigator.Dispose();

        using var direct = new Host(context, Vector2d.Zero, Fixed64.Quarter, alternateId: true);
        context.World.TryGetGrid(new Vector3d(0, 1, 0), out var grid).Should().BeTrue();
        grid!.TryGetVoxel(Cell(0, 0).Index, out var left).Should().BeTrue();
        grid.TryGetVoxel(Cell(1, 0).Index, out var right).Should().BeTrue();
        grid.TryAddVoxelOccupant(left!, direct).Should().BeTrue();
        try
        {
            for (int i = 0; i < 32; i++)
                Direct(i);
            start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++)
                Direct(i);
            long gridForgeBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            controllerBytes.Should().Be(gridForgeBytes);
        }
        finally
        {
            grid.TryRemoveVoxelOccupant(left!, direct);
        }

        void Step(int index)
        {
            context.Simulate();
            navigator.PrepareFrameMotion(Vector2d.Zero);
            navigator.Accept((index & 1) == 0 ? Vector2d.Right : Vector2d.Zero);
            navigator.CommitFrameMotion();
            context.LateSimulate();
        }

        void Direct(int index)
        {
            var (from, to) = (index & 1) == 0 ? (left, right) : (right, left);
            if (!grid.TryAddVoxelOccupant(to!, direct) || !grid.TryRemoveVoxelOccupant(from!, direct))
                throw new InvalidOperationException("The direct occupancy handoff failed.");
        }
    }

    [Fact]
    public void ReplacingTheMapLayerRequiresANewControllerBinding()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        context.World.TryGetGrid(new Vector3d(0, 1, 0), out var grid).Should().BeTrue();
        var differentLayer = new NavigationMapBuilder("plane", grid!.Configuration)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(Open).Build();
        var removal = new NavigationMapRemoveOperation("plane", 3, context.FrameCount + 1);
        context.Pathing.Admit(removal).Should().BeTrue();
        AdvancePublication();
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var replacement = new NavigationMapCommitOperation(new PreparedNavigationMap(differentLayer, 2),
            OverlayReplacementPolicy.Clear, 4, context.FrameCount + 1);
        context.Pathing.Admit(replacement).Should().BeTrue();
        AdvancePublication();
        replacement.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        navigator.ApplyGuidedTrekRequest(new NavigationCellAddress("plane", new VoxelIndex(1, 0, 1)),
            Policy.Key, PathAlgorithm.AStar, Budget).Should().Be(NavigationGuideStatus.Stale);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        void AdvancePublication()
        {
            for (int i = 0; i < 8; i++)
            {
                context.Simulate();
                context.LateSimulate();
            }
        }
    }

    [Fact]
    public void OverflowAndRawWorldResetCannotPublishAcceptedState()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        using var maximum = new Host(context, new Vector2d(Fixed64.MaxValue, Fixed64.Zero),
            Fixed64.Quarter, alternateId: true);
        Assert.Throws<OverflowException>(() => maximum.PrepareFrameMotion(Vector2d.Right));
        Reject(new Vector2d(Fixed64.MaxValue, Fixed64.Zero));
        Reject(new Vector2d(Fixed64.Zero, Fixed64.MaxValue));

        context.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);
        navigator.OnRead = () => context.World.Reset();
        navigator.Accept(navigator.Request.Target.Center);
        Assert.Throws<InvalidOperationException>(navigator.CommitFrameMotion);
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
        navigator.Velocity.Should().Be(Vector2d.Zero);
        navigator.LastCommittedCell.Should().BeNull();
        navigator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => navigator.PrepareFrameMotion());
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(maximum.CommitFrameMotion);

        void Reject(Vector2d extreme)
        {
            context.Simulate();
            navigator.PrepareFrameMotion(Vector2d.Zero);
            navigator.Accept(extreme);
            Assert.Throws<OverflowException>(navigator.CommitFrameMotion);
            navigator.Pose.Center.Should().Be(Vector2d.Zero);
            navigator.LastCommittedCell.Should().BeNull();
            context.LateSimulate();
        }
    }

    [Fact]
    public void InvalidBindingAndStaleGuideLeaveTheHostInControl()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var store = context.Pathing.NavigationGraphStore;
        store.MarkSafetyPending();
        Assert.Throws<ArgumentException>(() => new Host(context, Vector2d.Zero, Fixed64.Quarter, alternateId: true));
        store.ClearSafetyPending();
        navigator.ApplyGuidedTrekRequest(Cell(4, 0), Policy.Key, PathAlgorithm.AStar, Budget).Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        context.World.TryGetGrid(new Vector3d(0, 1, 0), out var grid).Should().BeTrue();
        grid!.TryGetVoxel(Cell(0, 0).Index, out var blocked).Should().BeTrue();
        grid.TryAddObstacle(blocked!, context.World.AllocateObstacleToken()).Should().BeTrue();
        navigator.PrepareFrameMotion();
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        context.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Zero);
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
        context.LateSimulate();
        navigator.CancelGuidance();
        navigator.GuidanceStatus.Should().BeNull();
    }

    [Fact]
    public void AcceptedRotationAndRoundedSpeedStayInTheirNativeRanges()
    {
        new NavigationPose2D(Vector2d.Zero, Fixed64.Pi).RotationRadians.Should().Be(-Fixed64.Pi);
        new NavigationPose2D(Vector2d.Zero, -Fixed64.Pi - Fixed64.MinIncrement).RotationRadians
            .Should().Be(Fixed64.Pi - Fixed64.MinIncrement);
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter, speed: (Fixed64)16 * Fixed64.MinIncrement);
        context.Simulate();
        var diagonal = new Vector2d(Fixed64.Half + Fixed64.Eighth, Fixed64.Half + Fixed64.Eighth);
        navigator.PrepareFrameMotion(diagonal);
        navigator.Request.Target.Center.Should().Be(Vector2d.Zero);
        navigator.Accept(Vector2d.Zero, Fixed64.Pi * Fixed64.Half);
        navigator.CommitFrameMotion();
        navigator.Facing.Y.Should().BeGreaterThan(Fixed64.Half);
    }

    [Fact]
    public void FrameGuardsConsumeOneAcceptedResultAndUseThePreparedRate()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        Assert.Throws<InvalidOperationException>(navigator.CommitFrameMotion);
        context.Simulate();
        navigator.OnSubmit = () =>
        {
            Assert.Throws<InvalidOperationException>(() => navigator.PrepareFrameMotion());
            Assert.Throws<InvalidOperationException>(navigator.Dispose);
        };
        Assert.Throws<ArgumentException>(() => navigator.PrepareFrameMotion(new Vector2d(1, 1)));
        navigator.PrepareFrameMotion(Vector2d.Right);
        Assert.Throws<InvalidOperationException>(() => navigator.PrepareFrameMotion());
        context.SetFrameRate(8);
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        navigator.Velocity.Should().Be(new Vector2d(4, 0));
        Assert.Throws<InvalidOperationException>(navigator.CommitFrameMotion);
        Assert.Throws<InvalidOperationException>(() => navigator.PrepareFrameMotion());
        context.LateSimulate();

        context.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);
        navigator.Accept(navigator.Request.Target.Center);
        context.LateSimulate();
        context.Simulate();
        Assert.Throws<InvalidOperationException>(navigator.CommitFrameMotion);
        navigator.Pose.Center.Should().Be(new Vector2d(Fixed64.Quarter, Fixed64.Zero));

        navigator.PrepareFrameMotion(Vector2d.Right);
        long frame = context.FrameCount;
        navigator.OnRead = context.Reset;
        navigator.Accept(navigator.Request.Target.Center);
        Assert.Throws<InvalidOperationException>(navigator.CommitFrameMotion);
        navigator.Pose.Center.Should().Be(new Vector2d(Fixed64.Quarter, Fixed64.Zero));
        navigator.LastCommittedCell.Should().BeNull();
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
        navigator.OnRead = null;
        Assert.Throws<InvalidOperationException>(() => navigator.PrepareFrameMotion());
        for (int i = 0; i < frame; i++)
        {
            context.Simulate();
            context.LateSimulate();
        }
        navigator.PrepareFrameMotion(Vector2d.Zero);
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
    }

    [Fact]
    public void MotionProofUsesFiniteBudgetsAndRejectsUncertifiedEndpoints()
    {
        using var context = CreateScene(portal: false);
        context.Pathing.TryGetTopDownBinding("plane", out var binding).Should().BeTrue();
        Vector2d from = Vector2d.Zero;
        var to = new Vector2d(Fixed64.Quarter, Fixed64.Zero);
        var outside = new Vector2d(9, 0);
        Proof(outside, to, Budget).Should().Be(NavigationGuideStatus.NoPath);
        Proof(from, outside, Budget).Should().Be(NavigationGuideStatus.NoPath);
        Proof(from, to, new NavigationWorkBudget(512, 32, 0, 0, 0, 0, 0, 0, 0, 128, 0))
            .Should().Be(NavigationGuideStatus.BudgetExceeded);

        int firstSuccess = 0;
        for (; firstSuccess < 128; firstSuccess++)
        {
            var result = Proof(from, to, new NavigationWorkBudget(firstSuccess, 32, 0, 1, 0, 0, 0, 0, 0, 128, 0));
            if (result == NavigationGuideStatus.Success)
                break;
            result.Should().Be(NavigationGuideStatus.BudgetExceeded);
        }
        firstSuccess.Should().BeInRange(1, 127);

        NavigationGuideStatus Proof(Vector2d start, Vector2d end, NavigationWorkBudget budget) =>
            context.Pathing.ValidateTopDownMotion("plane", binding, start, end, Fixed64.Quarter, Policy.Key,
                TraversalMedium.Solid, TraversalCapability.None, budget);
    }

    [Theory]
    [InlineData(0, NavigationGuideStatus.NoPath)]
    [InlineData(1, NavigationGuideStatus.CostOverflow)]
    [InlineData(2, NavigationGuideStatus.CostOverflow)]
    public void HostDriftWithoutAProvenStepHoldsTheCurrentWaypoint(int driftCase, NavigationGuideStatus status)
    {
        using var context = CreateScene(portal: false);
        context.SetFrameRate(1);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        navigator.ApplyGuidedTrekRequest(Cell(4, 0), Policy.Key, PathAlgorithm.AStar, Budget).Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        navigator.PrepareFrameMotion();
        var drift = driftCase switch
        {
            0 => new Vector2d(2, 0),
            1 => new Vector2d(Fixed64.MinValue, Fixed64.Zero),
            _ => new Vector2d(Fixed64.MaxValue, Fixed64.MaxValue)
        };
        navigator.Accept(drift);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.GuidanceStatus.Should().Be(status);
        navigator.Request.Target.Center.Should().Be(drift);
        navigator.HasArrived.Should().BeFalse();
    }

    [Fact]
    public void RemovedGridCannotLeaveAnOldOccupancyTicket()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        var occupied = Assert.Single(GridOccupantManager.GetOccupiedIndices(context.World, navigator));
        context.Simulate();
        navigator.PrepareFrameMotion();
        context.World.TryRemoveGrid(occupied.GridIndex).Should().BeTrue();
        context.Pathing.NavigationGraphStore.MarkSafetyPending();
        navigator.Accept(Vector2d.Zero);
        navigator.CommitFrameMotion();
        GridOccupantManager.GetOccupiedIndices(context.World, navigator).Should().BeEmpty();
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void AlreadyAcceptedGoalArrivesWithZeroEdgeBudget(PathAlgorithm algorithm)
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter);
        var budget = new NavigationWorkBudget(512, 16, 16, 0, 0, 0, 0, 0, 64, 64, 0);
        navigator.ApplyGuidedTrekRequest(Cell(0, 0), Policy.Key, algorithm, budget).Should().Be(NavigationGuideStatus.Success);
        Assert.Throws<ArgumentException>(() => navigator.ApplyGuidedTrekRequest(Cell(0, 0), default, algorithm, budget));
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.HasArrived.Should().BeFalse();
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        navigator.HasArrived.Should().BeTrue();
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void StaleRouteCannotArriveButAcceptedMotionStillCommits()
    {
        using var context = CreateScene(portal: false);
        using var navigator = new Host(context, Vector2d.Zero, Fixed64.Quarter, speed: (Fixed64)16);
        navigator.ApplyGuidedTrekRequest(Cell(0, 1), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        Advance();
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().Be(Vector2d.Forward);
        navigator.OnRead = () =>
        {
            context.World.TryAddGrid(new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(12, 1, 12)), out _)
                .Should().BeTrue();
        };
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        navigator.Pose.Center.Should().Be(Vector2d.Forward);
        navigator.HasArrived.Should().BeFalse();
        navigator.GuidanceStatus.Should().Be(NavigationGuideStatus.Stale);
        context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        void Advance()
        {
            context.Simulate();
            navigator.PrepareFrameMotion();
            navigator.Accept(navigator.Request.Target.Center);
            navigator.CommitFrameMotion();
            context.LateSimulate();
        }
    }

    [Fact]
    public void OffAxisStartIsProvedAndTinyCardinalMotionDoesNotRoundAway()
    {
        using var context = CreateScene(portal: false);
        var offset = new Vector2d(Fixed64.Quarter, Fixed64.Quarter);
        using var navigator = new Host(context, offset, Fixed64.Quarter);
        navigator.ApplyGuidedTrekRequest(Cell(0, 0), Policy.Key, PathAlgorithm.FlowField, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Request.Target.Center.Should().NotBe(offset);
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        navigator.HasArrived.Should().BeFalse();
        context.Simulate();
        navigator.PrepareFrameMotion();
        navigator.Accept(navigator.Request.Target.Center);
        navigator.CommitFrameMotion();
        context.LateSimulate();
        navigator.HasArrived.Should().BeTrue();

        // For a long native cell, a one-raw-unit allowance must survive the scale.
        using var large = CreateScene(portal: false, width: (Fixed64)4);
        using var tiny = new Host(large, Vector2d.Zero, Fixed64.Quarter, speed: (Fixed64)16 * Fixed64.MinIncrement);
        tiny.ApplyGuidedTrekRequest(Cell(1, 0), Policy.Key, PathAlgorithm.AStar, Budget)
            .Should().Be(NavigationGuideStatus.Success);
        large.Simulate();
        tiny.PrepareFrameMotion();
        tiny.Accept(tiny.Request.Target.Center);
        tiny.CommitFrameMotion();
        large.LateSimulate();
        large.Simulate();
        tiny.PrepareFrameMotion();
        tiny.Request.Target.Center.Should().Be(new Vector2d(Fixed64.MinIncrement, Fixed64.Zero));
    }

    private static TrailblazerWorldContext CreateScene(bool portal, Fixed64? width = null)
    {
        var context = TrailblazerWorldContext.CreateOwned();
        context.SetFrameRate(16);
        Fixed64 cellWidth = width ?? Fixed64.One;
        var grid = new GridConfiguration(new Vector3d(-cellWidth, Fixed64.Zero, -Fixed64.One), new Vector3d(5 * cellWidth, Fixed64.One, Fixed64.Two),
            topologyMetrics: GridForge.Grids.Topology.GridTopologyMetrics.Rectangular(cellWidth, Fixed64.One, Fixed64.One));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= 4; x++)
        {
            if (!portal && x != 2)
                builder.AddCell(Cell(x, 0).Index, Open);
            builder.AddCell(Cell(x, 1).Index, Open);
        }
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        context.Pathing.Admit(new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear, 2, 1)).Should().BeTrue();
        for (int i = 0; i < 8; i++)
        {
            context.Simulate();
            context.LateSimulate();
        }
        return context;
    }

    private static NavigationCellAddress Cell(int x, int z) => new("plane", new VoxelIndex(x + 1, 1, z + 1));

    private sealed class Host : Navigator2D
    {
        internal Host(TrailblazerWorldContext context, Vector2d center, Fixed64 radius, bool alternateId = false,
            Fixed64? speed = null)
            : base(context, "plane", Guid.Parse(alternateId ? "7f2bd0a1-fd02-4cb9-bc66-5979d1ba48f3" : "7f2bd0a1-fd02-4cb9-bc66-5979d1ba48f2"),
                new NavigationPose2D(center, Fixed64.Zero), radius, speed ?? (Fixed64)4, TraversalMedium.Solid) { }

        internal NavigationMotionRequest2D Request { get; private set; }
        internal Action? OnSubmit { get; set; }
        internal Action? OnRead { get; set; }
        private NavigationPose2D _accepted;

        internal void Accept(Vector2d center, Fixed64? rotation = null) =>
            _accepted = new NavigationPose2D(center, rotation ?? Request.Target.RotationRadians);

        protected override void SubmitMotionTarget(in NavigationMotionRequest2D request)
        {
            Request = request;
            OnSubmit?.Invoke();
        }

        protected override NavigationPose2D ReadAcceptedMotion(in NavigationMotionRequest2D request)
        {
            OnRead?.Invoke();
            return _accepted;
        }
    }
}
