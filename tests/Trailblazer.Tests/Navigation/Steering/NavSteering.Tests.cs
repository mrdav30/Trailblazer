using System;
using Chronicler;
using FixedMathSharp;
using FixedMathSharp.Assertions;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Moq;
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
        PathManager.Reset();

        TestWorld.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetHeading_ShouldArrive_WhenMovementIsRequestedWithoutAValidCurrentRequest()
    {
        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new TestableNavSteering(agent.Radius);
        steer.ForceMissingRequestState(new Vector3d(2, 0, 0));

        Vector3d heading = steer.GetHeading(agent);

        heading.Should().Be(Vector3d.Zero);
        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeTrue();
        steer.CurrentRequest.Should().BeNull();
    }

    [Fact]
    public void FindTargetDirection_ShouldKeepZeroHeadingUnchanged()
    {
        var steer = new TestableNavSteering(Fixed64.One);
        Vector3d position = new(3, 0, 4);
        steer.ForceDirectDestination(position);

        steer.InvokeFindTargetDirection(position).Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void FindTargetDirection_ShouldNormalizeNonZeroHeadingAndPreserveDistance()
    {
        var steer = new TestableNavSteering(Fixed64.One);
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

        var steer = new NavSteering(TestWorld.Context, agent.Radius);

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
        var steer = new NavSteering(TestWorld.Context, agent.Radius);

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
        var steer = new NavSteering(TestWorld.Context, agent.Radius);

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
        var data = new bool[1, 10, 5];
        for (int x = 0; x < 10; x++)
        {
            for (int z = 0; z < 5; z++)
                data[0, x, z] = true;
        }

        var world = new GridWorld();
        TestWorld.Attach(world, takeOwnership: true);
        NavigationChart chart = NavigationChart.From3D("CombinedSteeringAllocation", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        var agent = new MockSteerAgent(new Vector3d(4, 0, 2))
        {
            Speed = Fixed64.One,
            Velocity = Vector3d.Right,
            Size = Fixed64.One
        };

        var steer = new NavSteering(TestWorld.Context, agent.Radius);
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

            PathManager.UnloadChart("CombinedSteeringAllocation");
        }
    }

    [Fact]
    public void ScanRadiusInto_ShouldAvoidRepeatedAllocation_ForSteeringOccupants()
    {
        var data = new bool[1, 10, 5];
        for (int x = 0; x < 10; x++)
        {
            for (int z = 0; z < 5; z++)
                data[0, x, z] = true;
        }

        var world = new GridWorld();
        TestWorld.Attach(world, takeOwnership: true);
        NavigationChart chart = NavigationChart.From3D("ScanRadiusIntoSteerAllocation", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

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

            PathManager.UnloadChart("ScanRadiusIntoSteerAllocation");
        }
    }

    [Fact]
    public void ComputeCombinedSteering_ShouldAvoidSteadyStateAllocation_WhenNearbyOccupantsDoNotSteer()
    {
        var data = new bool[1, 10, 5];
        for (int x = 0; x < 10; x++)
        {
            for (int z = 0; z < 5; z++)
                data[0, x, z] = true;
        }

        var world = new GridWorld();
        TestWorld.Attach(world, takeOwnership: true);
        NavigationChart chart = NavigationChart.From3D("CombinedSteeringNonSteerAllocation", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        var agent = new MockSteerAgent(new Vector3d(4, 0, 2))
        {
            Speed = Fixed64.One,
            Velocity = Vector3d.Right,
            Size = Fixed64.One
        };

        var steer = new NavSteering(TestWorld.Context, agent.Radius);
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

            PathManager.UnloadChart("CombinedSteeringNonSteerAllocation");
        }
    }

    [Fact]
    public void NavSteering_Should_PauseAutoStop_BasedOnCooldown()
    {
        var steer = new NavSteering(TestWorld.Context, Fixed64.Half);
        steer.PauseAutoStop();
        steer.CanAutoStop.Should().BeFalse();

        var agent = new MockSteerAgent();
        for (int i = 0; i < TestWorld.Context.FrameRate / 8; i++)
            steer.GetHeading(agent);

        steer.CanAutoStop.Should().BeTrue();
    }

    [Fact]
    public void ApplyPathRequest_ShouldTreatNullRequestAsArrival()
    {
        var steer = new NavSteering(TestWorld.Context, Fixed64.One);

        steer.ApplyPathRequest(null);

        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeTrue();
        steer.CurrentRequest.Should().BeNull();
        steer.TargetDirection.Should().Be(Vector3d.Zero);
        steer.Destination.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldThrowForNullOwner()
    {
        var steer = new NavSteering(TestWorld.Context, Fixed64.One);

        steer.Invoking(s => s.PrewarmMovementGroup(null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("vessel");
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldNoOp_WhenSessionIsNotActive()
    {
        var owner = new MockSteerAgent(Vector3d.Zero);
        var steer = new TestableNavSteering(owner.Radius);

        steer.PrewarmMovementGroup(owner);

        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                new MovementGroupSession { GroupId = 77 },
                owner.GlobalId,
                Vector3d.Zero,
                TestWorld.Context.FrameCount)
            .Should().BeFalse();
    }

    [Fact]
    public void GetHeading_ShouldRaiseInvalidPath_WhenVolumeFallbackValidationFails()
    {
        GuidedPathTestScene.AddOpen(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(0, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(1, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 0, 0));
        GuidedPathTestScene.AddObstacle(TestWorld.Context, new Vector3d(1, 0, 0));


        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new SequencedPathValidationNavSteering(agent.Radius, true, false);

        VolumePathRequest.TryCreate(TestWorld.Context, agent.Position,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            out VolumePathRequest? request).Should().BeTrue();
        steer.ApplyPathRequest(TestRequire.NotNull(request));

        bool invalid = false;
        steer.Events.OnInvalidPath += () => invalid = true;

        steer.GetHeading(agent).Should().Be(Vector3d.Zero);
        invalid.Should().BeTrue();
        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeTrue();
    }

    [Fact]
    public void GetHeading_ShouldRaiseStartTraversalEvent_WhenIdle()
    {
        var steer = new NavSteering(TestWorld.Context, Fixed64.One);
        var agent = new MockSteerAgent(Vector3d.Zero);

        Vector3d startedDirection = Vector3d.Zero;
        steer.Events.OnStartTraversal += direction => startedDirection = direction;

        steer.GetHeading(agent).Should().Be(Vector3d.Zero);
        startedDirection.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void SetDeceleration_ShouldUseBrakingPower_WhenAccelerationIsZero()
    {
        var steer = new TestableNavSteering(Fixed64.Half);
        steer.ForceHeadingState(new Vector3d(1, 0, 0), distanceToTarget: (Fixed64)0.1f);

        steer.InvokeSetDeceleration(Vector3d.Zero, Fixed64.One);

        steer.TargetDirection.Magnitude.Should().BeLessThan(Fixed64.One);
    }

    [Fact]
    public void Arrive_ShouldRaiseEvent_EvenWhenAlreadyIdle()
    {
        var steer = new NavSteering(TestWorld.Context, Fixed64.One);
        bool arrived = false;
        steer.Events.OnArrive += () => arrived = true;

        steer.Arrive();

        arrived.Should().BeTrue();
        steer.IsAtDestination.Should().BeTrue();
    }

    [Fact]
    public void ComputeCombinedSteering_ShouldUseRightSideDodge_WhenNeighborIsBehind()
    {
        var steer = new NavSteering(TestWorld.Context, Fixed64.Half);
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
        public TestableNavSteering(Fixed64 radius = default) : base(TestWorld.Context, radius) { }

        public void ForceMissingRequestState(Vector3d destination)
        {
            _destination = destination;
            _targetDirection = new Vector3d(1, 0, 0);
            _shouldMove = true;
            _isAtDestination = false;
        }

        public int GetGroupIndex() => GroupIndex;

        public void ForceHeadingState(Vector3d targetDirection, Fixed64 distanceToTarget)
        {
            _targetDirection = targetDirection;
            _lastTargetDirection = targetDirection;
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

        public Vector3d InvokeFindTargetDirection(Vector3d position) => FindTargetDirection(position);

        public void InvokeSetDeceleration(Vector3d acceleration, Fixed64 speed) => SetDeceleration(acceleration, speed);
    }

    private sealed class FallbackNavSteering : NavSteering
    {
        private readonly Vector3d _movementDirection;

        public FallbackNavSteering(Vector3d movementDirection, Fixed64 radius)
            : base(TestWorld.Context, radius)
        {
            _movementDirection = movementDirection;
        }

        protected override bool ValidateMovementPath(Vector3d origin) => true;

        protected override Vector3d FindTargetDirection(Vector3d position) => _movementDirection;
    }

    private sealed class SequencedPathValidationNavSteering : NavSteering
    {
        private readonly bool[] _results;
        private int _index;

        public SequencedPathValidationNavSteering(Fixed64 radius, params bool[] results)
            : base(TestWorld.Context, radius)
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

    private sealed class StubGuide : IGuide
    {
        private readonly Vector3d _movementDirection;
        private readonly Vector3d _fallbackDirection;

        public StubGuide(Vector3d movementDirection, Vector3d fallbackDirection)
        {
            _movementDirection = movementDirection;
            _fallbackDirection = fallbackDirection;
        }

        public bool TryGetMovementDirection(Vector3d origin, out Vector3d direction)
        {
            direction = _movementDirection;
            return true;
        }

        public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
        {
            fallbackDirection = _fallbackDirection;
            return true;
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
