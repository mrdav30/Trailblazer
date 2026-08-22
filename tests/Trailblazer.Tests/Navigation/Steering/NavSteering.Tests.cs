using System;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Navigation;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering;

[Collection("PathingCollection")]
public class NavSteeringTests : IDisposable
{
    public NavSteeringTests()
    {
        if (TestWorld.IsActive)
            TestWorld.Reset();

        TestWorld.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        TestWorld.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetHeading_ShouldArrive_WhenMovementIsRequestedWithoutAValidCurrentRequest()
    {
        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new TestableNavSteering();
        steer.ForceMissingRequestState(new Vector3d(2, 0, 0));

        Vector3d heading = steer.GetHeading(agent, out _);

        heading.Should().Be(Vector3d.Zero);
        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeTrue();
        steer.CurrentQuery.Should().BeNull();
    }

    [Fact]
    public void FindTargetDirection_ShouldKeepZeroHeadingUnchanged()
    {
        var steer = new TestableNavSteering();
        Vector3d position = new(3, 0, 4);
        steer.ForceDirectDestination(position);

        steer.InvokeFindTargetDirection(position).Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void FindTargetDirection_ShouldNormalizeNonZeroHeadingAndPreserveDistance()
    {
        var steer = new TestableNavSteering();
        steer.ForceDirectDestination(new Vector3d(3, 0, 4));

        Vector3d heading = steer.InvokeFindTargetDirection(Vector3d.Zero);

        heading.Magnitude.Should().BeApproximately(Fixed64.One, Fixed64.FromRaw(8));
        heading.X.Should().Be(Fixed64.FromFraction(3, 5));
        heading.Z.Should().Be(Fixed64.FromFraction(4, 5));
        steer.DistanceToTarget.Should().Be((Fixed64)5);
    }

    [Fact]
    public void NavSteering_Should_Apply_CombinedSteering()
    {
        var agent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Velocity = new Vector3d(1, 0, 0), Speed = (Fixed64)1 };
        var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0)) { Velocity = new Vector3d(1, 0, 0) };

        TestWorld.World.TryGetGrid(new Vector3d(1, 0, 0), out VoxelGrid? grid);
        grid!.TryAddVoxelOccupant(neighbor);

        var steer = new NavSteering(TestWorld.Context);

        var force = steer.ComputeCombinedSteering(
            agent.Position,
            agent.Velocity,
            agent.Speed,
            agent.Size,
            agent.GlobalId);
        force.Should().NotBe(Vector3d.Zero);

        grid!.TryRemoveVoxelOccupant(neighbor);
    }

    [Fact]
    public void ComputeCombinedSteering_Should_ReturnZero_When_NoNeighbors()
    {
        // Arrange
        var agent = new MockSteerAgent(new Vector3d(0, 0, 0))
        {
            Speed = Fixed64.One,           // non‐zero
            Velocity = Vector3d.Zero,      // irrelevant here
            Size = Fixed64.One
        };
        var steer = new NavSteering(TestWorld.Context);

        // Act
        var result = steer.ComputeCombinedSteering(
            agent.Position,
            agent.Velocity,
            agent.Speed,
            agent.Size,
            agent.GlobalId);

        // Assert
        result.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void ComputeCombinedSteering_Should_ReturnZero_When_SpeedIsZero()
    {
        // Arrange
        var agent = new MockSteerAgent(new Vector3d(0, 0, 0))
        {
            Speed = Fixed64.Zero,       // zero ⇒ immediate exit
            Velocity = new Vector3d(1, 0, 0),
            Size = Fixed64.One
        };
        var steer = new NavSteering(TestWorld.Context);

        // even if there’s a neighbor in range…
        var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Size = Fixed64.One
        };
        TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? grid);
        grid!.TryAddVoxelOccupant(neighbor);

        // Act
        var result = steer.ComputeCombinedSteering(
            agent.Position,
            agent.Velocity,
            agent.Speed,
            agent.Size,
            agent.GlobalId);

        // Assert
        result.Should().Be(Vector3d.Zero);

        // Cleanup
        grid!.TryRemoveVoxelOccupant(neighbor);
    }

    [Fact]
    public void ComputeCombinedSteering_ShouldAvoidSteadyStateAllocation()
    {
        var world = new GridWorld();
        TestWorld.Attach(world, takeOwnership: true);
        world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(10, 1, 5)),
            out _).Should().BeTrue();

        var agent = new MockSteerAgent(new Vector3d(4, 0, 2))
        {
            Speed = Fixed64.One,
            Velocity = Vector3d.Right,
            Size = Fixed64.One
        };

        var steer = new NavSteering(TestWorld.Context);
        var neighbors = new MockSteerAgent?[32];

        for (int i = 0; i < neighbors.Length; i++)
        {
            int x = i % 8;
            int z = i / 8;
            var neighbor = new MockSteerAgent(new Vector3d(x, 0, z))
            {
                Velocity = Vector3d.Right,
                Size = Fixed64.One
            };

            if (TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? grid))
            {
                grid!.TryAddVoxelOccupant(neighbor);
                neighbors[i] = neighbor;
            }
        }

        try
        {
            steer.ComputeCombinedSteering(
                agent.Position,
                agent.Velocity,
                agent.Speed,
                agent.Size,
                agent.GlobalId);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int iterations = 256;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
            {
                _ = steer.ComputeCombinedSteering(
                    agent.Position,
                    agent.Velocity,
                    agent.Speed,
                    agent.Size,
                    agent.GlobalId);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            allocated.Should().BeLessThan(512);
        }
        finally
        {
            for (int i = 0; i < neighbors.Length; i++)
            {
                MockSteerAgent? neighbor = neighbors[i];
                if (neighbor != null && TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? grid))
                    grid!.TryRemoveVoxelOccupant(neighbor);
            }
        }
    }

    [Fact]
    public void ScanRadiusInto_ShouldAvoidRepeatedAllocation_ForSteeringOccupants()
    {
        var world = new GridWorld();
        TestWorld.Attach(world, takeOwnership: true);
        world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(10, 1, 5)),
            out _).Should().BeTrue();

        var neighbors = new MockSteerAgent?[32];
        var results = new SwiftList<ISteer>();
        var scratch = new GridScanScratch();

        for (int i = 0; i < neighbors.Length; i++)
        {
            int x = i % 8;
            int z = i / 8;
            var neighbor = new MockSteerAgent(new Vector3d(x, 0, z))
            {
                Velocity = Vector3d.Right,
                Size = Fixed64.One
            };

            if (TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? grid))
            {
                grid!.TryAddVoxelOccupant(neighbor);
                neighbors[i] = neighbor;
            }
        }

        try
        {
            GridScanManager.ScanRadiusInto<ISteer>(world, new Vector3d(4, 0, 2), (Fixed64)5, results, scratch);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int iterations = 256;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
                GridScanManager.ScanRadiusInto<ISteer>(world, new Vector3d(4, 0, 2), (Fixed64)5, results, scratch);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            allocated.Should().BeLessThan(512);
        }
        finally
        {
            for (int i = 0; i < neighbors.Length; i++)
            {
                MockSteerAgent? neighbor = neighbors[i];
                if (neighbor != null && TestWorld.World.TryGetGrid(neighbor.Position, out VoxelGrid? grid))
                    grid!.TryRemoveVoxelOccupant(neighbor);
            }
        }
    }

    [Fact]
    public void ComputeCombinedSteering_ShouldAvoidSteadyStateAllocation_WhenNearbyOccupantsDoNotSteer()
    {
        var world = new GridWorld();
        TestWorld.Attach(world, takeOwnership: true);
        world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(10, 1, 5)),
            out _).Should().BeTrue();

        var agent = new MockSteerAgent(new Vector3d(4, 0, 2))
        {
            Speed = Fixed64.One,
            Velocity = Vector3d.Right,
            Size = Fixed64.One
        };

        var steer = new NavSteering(TestWorld.Context);
        var occupants = new NonSteeringOccupant?[32];

        for (int i = 0; i < occupants.Length; i++)
        {
            int x = i % 8;
            int z = i / 8;
            var occupant = new NonSteeringOccupant(new Vector3d(x, 0, z));

            if (TestWorld.World.TryGetGrid(occupant.Position, out VoxelGrid? grid))
            {
                grid!.TryAddVoxelOccupant(occupant);
                occupants[i] = occupant;
            }
        }

        try
        {
            steer.ComputeCombinedSteering(
                agent.Position,
                agent.Velocity,
                agent.Speed,
                agent.Size,
                agent.GlobalId);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = steer.ComputeCombinedSteering(
                agent.Position,
                agent.Velocity,
                agent.Speed,
                agent.Size,
                agent.GlobalId);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            allocated.Should().BeLessThan(512);
        }
        finally
        {
            for (int i = 0; i < occupants.Length; i++)
            {
                NonSteeringOccupant? occupant = occupants[i];
                if (occupant != null && TestWorld.World.TryGetGrid(occupant.Position, out VoxelGrid? grid))
                    grid!.TryRemoveVoxelOccupant(occupant);
            }
        }
    }

    [Fact]
    public void NavSteering_Should_PauseAutoStop_BasedOnCooldown()
    {
        var steer = new NavSteering(TestWorld.Context);
        steer.PauseAutoStop();
        steer.CanAutoStop.Should().BeFalse();

        var agent = new MockSteerAgent();
        for (int i = 0; i < TestWorld.Context.FrameRate / 8; i++)
            steer.GetHeading(agent, out _);

        steer.CanAutoStop.Should().BeTrue();
    }

    [Fact]
    public void WaypointTolerance_ShouldBeAnExplicitNonNegativeWorldDistance()
    {
        var steer = new NavSteering(TestWorld.Context);

        steer.WaypointTolerance.Should().Be(Fixed64.Half);

        Action setNegative = () => steer.WaypointTolerance = -Fixed64.One;

        setNegative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WaypointTolerance_ShouldControlOrdinaryStepAdvanceInWorldUnits()
    {
        var steer = new TestableNavSteering()
        {
            WaypointTolerance = Fixed64.Quarter
        };
        steer.ForceHeadingState(Vector3d.Right, distanceToTarget: Fixed64.Half);

        steer.ShouldAdvanceToNextWaypoint().Should().BeFalse();

        steer.WaypointTolerance = Fixed64.Half;
        steer.ShouldAdvanceToNextWaypoint().Should().BeTrue();
    }

    [Fact]
    public void ArrivalRadius_ShouldAdvanceIntermediateStepsOnlyAfterAHeadingReversal()
    {
        var steer = new TestableNavSteering
        {
            WaypointTolerance = Fixed64.Quarter
        };
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        steer.ApplyPathQuery(new PathQuery(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.Right),
            profile,
            new NavigationAreaPolicyKey("steering-test", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            allowTransitions: false));

        steer.ForceHeadingState(
            Vector3d.Right,
            profile.ArrivalRadius,
            lastTargetDirection: Vector3d.Zero);
        steer.ShouldAdvanceToNextWaypoint().Should().BeFalse();

        steer.ForceHeadingState(
            Vector3d.Right,
            profile.ArrivalRadius,
            lastTargetDirection: Vector3d.Forward);
        steer.ShouldAdvanceToNextWaypoint().Should().BeFalse();

        steer.ForceHeadingState(
            Vector3d.Right,
            profile.ArrivalRadius,
            lastTargetDirection: Vector3d.Left);
        steer.ShouldAdvanceToNextWaypoint().Should().BeTrue();
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldThrowForNullOwner()
    {
        var steer = new NavSteering(TestWorld.Context);

        steer.Invoking(s => s.PrewarmMovementGroup(null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("vessel");
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldNoOp_WhenSessionIsNotActive()
    {
        var owner = new MockSteerAgent(Vector3d.Zero);
        var steer = new TestableNavSteering();

        steer.PrewarmMovementGroup(owner);

        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                new MovementGroupSession { GroupId = 77 },
                owner.GlobalId,
                Vector3d.Zero,
                TestWorld.Context.FrameCount)
            .Should().BeFalse();
    }

    [Fact]
    public void GetHeading_ShouldRaiseStartTraversalEvent_WhenIdle()
    {
        var steer = new NavSteering(TestWorld.Context);
        var agent = new MockSteerAgent(Vector3d.Zero);

        Vector3d startedDirection = Vector3d.Zero;
        steer.Events.OnStartTraversal += direction => startedDirection = direction;

        steer.GetHeading(agent, out _).Should().Be(Vector3d.Zero);
        startedDirection.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void SetDeceleration_ShouldUseBrakingPower_WhenAccelerationIsZero()
    {
        var steer = new TestableNavSteering();
        steer.ForceHeadingState(new Vector3d(1, 0, 0), distanceToTarget: (Fixed64)0.1f);

        steer.InvokeSetDeceleration(Vector3d.Zero, Fixed64.One);

        steer.TargetDirection.Magnitude.Should().BeLessThan(Fixed64.One);
    }

    [Fact]
    public void Arrive_ShouldRaiseEvent_EvenWhenAlreadyIdle()
    {
        var steer = new NavSteering(TestWorld.Context);
        bool arrived = false;
        steer.Events.OnArrive += () => arrived = true;

        steer.Arrive();

        arrived.Should().BeTrue();
        steer.IsAtDestination.Should().BeTrue();
    }

    [Fact]
    public void ComputeCombinedSteering_ShouldUseRightSideDodge_WhenNeighborIsBehind()
    {
        var steer = new NavSteering(TestWorld.Context);
        var agent = new MockSteerAgent(new Vector3d(0, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Speed = Fixed64.One
        };
        var neighbor = new MockSteerAgent(new Vector3d(-1, 0, 0))
        {
            Velocity = Vector3d.Zero
        };

        TestWorld.World.TryGetGrid(neighbor.Position, out var grid);
        grid!.TryAddVoxelOccupant(neighbor);

        Vector3d force = steer.ComputeCombinedSteering(
            agent.Position,
            agent.Velocity,
            agent.Speed,
            agent.Size,
            agent.GlobalId);

        force.Z.Should().BeLessThan(Fixed64.Zero);

        grid!.TryRemoveVoxelOccupant(neighbor);
    }

    private sealed class TestableNavSteering : NavSteering
    {
        public TestableNavSteering() : base(TestWorld.Context) { }

        public void ForceMissingRequestState(Vector3d destination)
        {
            _destination = destination;
            _targetDirection = new Vector3d(1, 0, 0);
            _shouldMove = true;
            _isAtDestination = false;
        }

        public int GetGroupIndex() => GroupIndex;

        public void ForceHeadingState(
            Vector3d targetDirection,
            Fixed64 distanceToTarget,
            Vector3d? lastTargetDirection = null)
        {
            _targetDirection = targetDirection;
            _lastTargetDirection = lastTargetDirection ?? targetDirection;
            _shouldMove = true;
            _isAtDestination = false;
            _hasLineOfSightPath = false;
            _distanceToTarget = distanceToTarget;
        }

        public void ForceDirectDestination(Vector3d destination)
        {
            _destination = destination;
            _hasLineOfSightPath = true;
        }

        public Vector3d InvokeFindTargetDirection(Vector3d position) =>
            FindTargetDirection(position, out _);

        public void InvokeSetDeceleration(Vector3d acceleration, Fixed64 speed) => SetDeceleration(acceleration, speed);
    }

    private sealed class FallbackNavSteering : NavSteering
    {
        private readonly Vector3d _movementDirection;

        public FallbackNavSteering(Vector3d movementDirection)
            : base(TestWorld.Context)
        {
            _movementDirection = movementDirection;
        }

        protected override bool ValidateMovementPath(Vector3d origin) => true;

        protected override Vector3d FindTargetDirection(
            Vector3d position,
            out NavigationTransitionInstruction? pendingTransition)
        {
            pendingTransition = null;
            return _movementDirection;
        }
    }

    private sealed class SequencedPathValidationNavSteering : NavSteering
    {
        private readonly bool[] _results;
        private int _index;

        public SequencedPathValidationNavSteering(params bool[] results)
            : base(TestWorld.Context)
        {
            _results = results;
        }

        protected override bool ValidateMovementPath(Vector3d origin)
        {
            if (_index >= _results.Length)
                return _results[^1];

            return _results[_index++];
        }
    }

    private sealed class NonSteeringOccupant : IVoxelOccupant
    {
        public Guid GlobalId { get; } = Guid.NewGuid();

        public byte OccupantGroupId { get; } = 1;

        public Vector3d Position { get; }

        public NonSteeringOccupant(Vector3d position)
        {
            Position = position;
        }
    }
}
