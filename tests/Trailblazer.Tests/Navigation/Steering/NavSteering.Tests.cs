using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using Moq;
using System;
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
        if (TrailblazerWorldManager.IsActive)
            TrailblazerWorldManager.Reset();
        else
            TrailblazerWorldManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TrailblazerWorldManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();

        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NavSteering_Should_InitializeCorrectly()
    {
        var agent = new MockSteerAgent();  // INavigate stub
        var steer = new NavSteering(agent.Radius);

        steer.IsAtDestination.Should().BeFalse();
        steer.ShouldMove.Should().BeFalse();
        steer.TrailGuide.Should().BeNull();
    }

    [Fact]
    public void GetHeading_ShouldReturnZero_WhenMovementIsDisabled()
    {
        var data = new bool[1, 2, 1]
        {
            {
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("MovementDisabledChart", data, Vector3d.Zero);

        var start = Vector3d.Zero;
        var end = new Vector3d(1, 0, 0);
        var agent = new MockSteerAgent(start);
        var steer = NavSteering.CreateNew(agent.Radius);
        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        steer.ApplyPathRequest(request);
        steer.CanMove = false;

        steer.GetHeading(agent).Should().Be(Vector3d.Zero);
        steer.ShouldMove.Should().BeTrue();

        PathManager.UnloadChart("MovementDisabledChart");
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
    public void NavSteering_Should_ApplyRequest_And_SetupPath()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(2, 0, 0);

        PathTestFactory.RegisterFromData("SimpleLine", data, start);

        var agent = new MockSteerAgent(start);
        var steer = NavSteering.CreateNew(agent.Radius);

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        steer.ShouldMove.Should().BeTrue();
        steer.CurrentRequest?.IsValid.Should().BeTrue();

        PathManager.UnloadChart("SimpleLine");
    }


    [Fact]
    public void NavSteering_ShouldUseLineOfSight_WhenPathIsClear()
    {
        var data = new bool[1, 3, 1];
        for (int i = 0; i < 3; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData("LineSight", data, Vector3d.Zero);

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(2, 0, 0);
        var agent = new MockSteerAgent(start);
        var steer = NavSteering.CreateNew(agent.Radius);

        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        steer.GetHeading(agent);

        steer.HasLineOfSightPath.Should().BeTrue();

        PathManager.UnloadChart("LineSight");
    }

    [Fact]
    public void GetHeading_ShouldPublishDirectRouteTopologyMetadata_WhenLineOfSightPathIsUsed()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("SteeringRouteTopologyDirect", data, Vector3d.Zero);

        var start = Vector3d.Zero;
        var end = new Vector3d(2, 0, 0);
        var agent = new MockSteerAgent(start);
        var steer = NavSteering.CreateNew(agent.Radius);
        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        steer.CurrentRouteTopologyVersion.Should().Be(1);

        steer.GetHeading(agent);

        steer.HasLineOfSightPath.Should().BeTrue();
        steer.CurrentRouteRequestsClimbIntent.Should().BeFalse();
        steer.CurrentRouteTopologyVersion.Should().Be(2);

        PathManager.UnloadChart("SteeringRouteTopologyDirect");
    }

    [Fact]
    public void GetHeading_ShouldPublishGuideBackedRouteTopologyMetadata_WhenTransitionAwareFallbackRequestsClimb()
    {
        GuidedPathTestScene.RegisterTransitionFallbackClimbScene();

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = NavSteering.CreateNew(agent.Radius);

        AStarPathRequest request = AStarPathRequest.Create(
            agent.Position,
            new Vector3d(4, 0, 0),
            agent.Size,
            HeuristicMethod.Manhattan,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true)!;

        steer.ApplyPathRequest(request);
        int versionAfterApply = steer.CurrentRouteTopologyVersion;

        steer.GetHeading(agent);

        steer.HasLineOfSightPath.Should().BeFalse();
        steer.TrailGuide.Should().BeOfType<AStarGuide>();
        steer.CurrentRouteRequestsClimbIntent.Should().BeTrue();
        steer.CurrentRouteTopologyVersion.Should().BeGreaterThan(versionAfterApply);

        int versionBeforeStop = steer.CurrentRouteTopologyVersion;
        steer.StopMove();
        steer.CurrentRouteRequestsClimbIntent.Should().BeFalse();
        steer.CurrentRouteTopologyVersion.Should().BeGreaterThan(versionBeforeStop);
    }

    [Fact]
    public void NavSteering_Should_GuideAerialRequests_WithVerticalOnlyTargets()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(0, 2, 0));
        AddOpen(new Vector3d(0, 3, 0));

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = NavSteering.CreateNew(agent.Radius);

        VolumePathRequest.TryCreate(
            agent.Position,
            new Vector3d(0, 3, 0),
            Fixed64.One,
            out VolumePathRequest request).Should().BeTrue();

        steer.ApplyPathRequest(request);

        Vector3d heading = steer.GetHeading(agent);

        heading.Should().Be(Vector3d.Up);
        steer.HasLineOfSightPath.Should().BeTrue();
        steer.TrailGuide.Should().BeNull();
        steer.IsAtDestination.Should().BeFalse();
    }

    [Fact]
    public void NavSteering_Should_RequestAerialGuide_WhenDirectFlightIsBlocked()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(2, 0, 0));
        AddObstacle(new Vector3d(1, 0, 0));

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = NavSteering.CreateNew(agent.Radius);

        VolumePathRequest.TryCreate(
            agent.Position,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            out VolumePathRequest request).Should().BeTrue();

        steer.ApplyPathRequest(request);

        steer.GetHeading(agent);

        steer.HasLineOfSightPath.Should().BeFalse();
        var guide = steer.TrailGuide.Should().BeOfType<VolumeGuide>().Subject;
        guide.TrailMap.HasPath.Should().BeTrue();
        guide.CurrentWaypointIndex.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NavSteering_Should_RequestVolumeGuide_WhenDirectSwimIsBlocked()
    {
        AddWater(new Vector3d(0, 0, 1));
        AddWater(new Vector3d(0, 0, 0));
        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));
        AddWater(new Vector3d(2, 0, 1));
        AddObstacle(new Vector3d(1, 0, 1));

        var agent = new MockSteerAgent(new Vector3d(0, 0, 1));
        var steer = NavSteering.CreateNew(agent.Radius);

        VolumePathRequest.TryCreate(
            agent.Position,
            new Vector3d(2, 0, 1),
            Fixed64.One,
            out VolumePathRequest request,
            medium: TraversalMedium.Liquid).Should().BeTrue();

        steer.ApplyPathRequest(request);

        steer.GetHeading(agent);

        steer.HasLineOfSightPath.Should().BeFalse();
        var guide = steer.TrailGuide.Should().BeOfType<VolumeGuide>().Subject;
        guide.TrailMap.HasPath.Should().BeTrue();
        guide.CurrentWaypointIndex.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NavSteering_ShouldReleaseVolumeGuide_WhenLineOfSightReturns()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(1, 0, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(2, 0, 0));

        Vector3d obstaclePosition = new(1, 0, 0);
        TrailblazerWorldManager.TryGetGridAndVoxel(obstaclePosition, out VoxelGrid? obstacleGrid, out Voxel? obstacleVoxel).Should().BeTrue();
        var obstacleKey = new BoundsKey(obstaclePosition, obstaclePosition);
        obstacleGrid!.TryAddObstacle(obstacleVoxel!, obstacleKey).Should().BeTrue();

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = NavSteering.CreateNew(agent.Radius);
        steer.PathRecheckCooldownFrames = 1;

        VolumePathRequest.TryCreate(
            agent.Position,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            out VolumePathRequest request).Should().BeTrue();

        steer.ApplyPathRequest(request);

        steer.GetHeading(agent);
        steer.HasLineOfSightPath.Should().BeFalse();
        steer.TrailGuide.Should().BeOfType<VolumeGuide>();

        obstacleGrid!.TryRemoveObstacle(obstacleVoxel!, obstacleKey).Should().BeTrue();

        steer.GetHeading(agent);

        steer.HasLineOfSightPath.Should().BeTrue();
        steer.TrailGuide.Should().BeNull();
    }

    [Fact]
    public void NavSteering_ShouldUseGuideFallback_WhenRecoveringFromStuck()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(2, 0, 0));
        AddObstacle(new Vector3d(1, 0, 0));

        Vector3d movementDirection = new(1, 0, 0);
        Vector3d fallbackDirection = new(0, 0, 1);

        var agent = new MockSteerAgent(Vector3d.Zero)
        {
            Speed = Fixed64.Zero
        };

        var steer = new FallbackNavSteering(movementDirection, agent.Radius);
        VolumePathRequest.TryCreate(
            agent.Position,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            out VolumePathRequest request).Should().BeTrue();
        steer.ApplyPathRequest(request);
        steer.SetTrailGuide(new StubGuide(movementDirection, fallbackDirection));

        bool usedFallback = false;
        for (int i = 0; i < 64; i++)
        {
            Vector3d heading = steer.GetHeading(agent);
            if (heading == fallbackDirection)
            {
                usedFallback = true;
                break;
            }
        }

        usedFallback.Should().BeTrue();
        steer.IsStuck.Should().BeFalse();
    }

    [Fact]
    public void NavSteering_Should_Arrive_WhenCloseEnough()
    {
        var data = new bool[1, 1, 1] { { { true } } };
        PathTestFactory.RegisterFromData("Point", data, Vector3d.Zero);

        var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(agent.Position, agent.Position, Fixed64.One, out AStarPathRequest? request);

        request!.IsValid.Should().BeTrue();

        steer.ApplyPathRequest(request);
        steer.GetHeading(agent);

        steer.IsAtDestination.Should().BeTrue();

        PathManager.UnloadChart("Point");
    }

    [Fact]
    public void NavSteering_Should_DeclareStuck_AfterSeveralFailedFrames()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }

        };

        PathTestFactory.RegisterFromData("StuckTest", data, Vector3d.Zero);

        var agent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Speed = Fixed64.Zero };
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(agent.Position, new Vector3d(1, 0, 0), out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        var stuck = false;
        steer.Events.OnIsStuck += () =>
        {
            stuck = true;
        };

        for (int i = 0; i < 100; i++)
            steer.GetHeading(agent);

        stuck.Should().BeTrue();
        steer.IsStuck.Should().BeTrue();

        PathManager.UnloadChart("StuckTest");
    }

    [Fact]
    public void NavSteering_Should_Stop_IfBlocked()
    {
        var data = new bool[1, 3, 1];
        data[0, 0, 0] = true;  // Start
        data[0, 2, 0] = true;  // End (middle is blocked)

        PathTestFactory.RegisterFromData("BlockedMiddle", data, Vector3d.Zero);

        var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(agent.Position, new Vector3d(2, 0, 0), out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        var invalid = false;
        steer.Events.OnInvalidPath += () =>
        {
            invalid = true;
        };

        var stopped = false;
        steer.Events.OnStopMove += () =>
        {
            stopped = true;
        };

        for (int i = 0; i < 100; i++)
            steer.GetHeading(agent);

        invalid.Should().BeTrue();
        stopped.Should().BeTrue();  // no route

        PathManager.UnloadChart("BlockedMiddle");
    }

    [Fact]
    public void NavSteering_Should_Follow_FlowFieldPath()
    {
        bool[,,] data = new bool[1, 5, 3]
        {
            {
                { true, true, true },
                { true, true, true },
                { false, true, false },
                { true, true, true },
                { true, true, true }
            }
        };

        PathTestFactory.RegisterFromData("FlowFieldTest", data, new Vector3d(0, 0, 0));

        var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
        var steer = new NavSteering(agent.Radius);

        FlowFieldPathRequest.TryCreate(agent.Position, new Vector3d(4, 0, 0), out FlowFieldPathRequest request);
        steer.ApplyPathRequest(request);

        steer.GetHeading(agent);

        steer.TrailGuide.Should().NotBeNull();
        steer.TrailGuide.Should().BeOfType<FlowFieldGuide>();

        PathManager.UnloadChart("FlowFieldTest");
    }

    [Fact]
    public void NavSteering_Should_Apply_CombinedSteering()
    {
        var agent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Velocity = new Vector3d(1, 0, 0), Speed = (Fixed64)1 };
        var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0)) { Velocity = new Vector3d(1, 0, 0) };

        TrailblazerWorldManager.TryGetGrid(new Vector3d(1, 0, 0), out VoxelGrid? grid);
        grid!.TryAddVoxelOccupant(neighbor);

        var steer = new NavSteering(agent.Radius);

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
        var steer = new NavSteering(agent.Radius);

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
        var steer = new NavSteering(agent.Radius);

        // even if there’s a neighbor in range…
        var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Size = Fixed64.One
        };
        TrailblazerWorldManager.TryGetGrid(neighbor.Position, out VoxelGrid? grid);
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
    public void NavSteering_Should_AdvanceWaypoint_When_CloseAndAligned()
    {
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }

        };

        PathTestFactory.RegisterFromData("AdvanceWaypoint", data, Vector3d.Zero);

        var waypointGuide = new Mock<IWaypointGuide>();
        waypointGuide.Setup(x => x.GetCurrentWaypointDirection(It.IsAny<Vector3d>())).Returns(new Vector3d(1, 0, 0));
        var dir = new Vector3d(1, 0, 0);
        waypointGuide.Setup(x => x.TryGetMovementDirection(It.IsAny<Vector3d>(), out dir))
                     .Returns(true);

        var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(agent.Position, new Vector3d(1, 0, 0), Fixed64.One, out AStarPathRequest? request);

        request!.IsValid.Should().BeTrue();

        steer.ApplyPathRequest(request);
        steer.SetTrailGuide(waypointGuide.Object);
        steer.GetHeading(agent);

        PathManager.UnloadChart("AdvanceWaypoint");
        waypointGuide.Verify(x => x.AdvanceWaypoint(), Times.AtLeastOnce);
    }

    [Fact]
    public void NavSteering_Should_StopMove_Without_Arrival()
    {
        var data = new bool[1, 3, 3] {
            {
                { true, true, true },
                { true, false, true },
                { true, true, true }
            }
        };

        PathTestFactory.RegisterFromData("StopMove", data, Vector3d.Zero);

        var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(agent.Position, new Vector3d(2, 0, 2), out AStarPathRequest request);
        steer.ApplyPathRequest(request);
        steer.StopMove();

        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeFalse();

        PathManager.UnloadChart("StopMove");
    }

    [Fact]
    public void NavSteering_Should_PauseAutoStop_BasedOnCooldown()
    {
        var steer = new NavSteering();
        steer.PauseAutoStop();
        steer.CanAutoStop.Should().BeFalse();

        var agent = new MockSteerAgent();
        for (int i = 0; i < TrailblazerManager.FrameRate / 8; i++)
            steer.GetHeading(agent);

        steer.CanAutoStop.Should().BeTrue();
    }

    [Fact]
    public void NavSteering_Should_FindVoxel_ForLargeUnitSize()
    {
        var data = new bool[1, 3, 3] {
            {
                { true, true, true },
                { true, true, true },
                { true, true, true }
            }
        };

        PathTestFactory.RegisterFromData("LargeSize", data, Vector3d.Zero);

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new NavSteering(agent.Radius);

        // Ensure larger than voxel size
        var request = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.Two);

        request!.IsValid.Should().BeTrue();

        steer.ApplyPathRequest(request);

        steer.ShouldMove.Should().BeTrue();

        PathManager.UnloadChart("LargeSize");
    }

    [Fact]
    public void NavSteering_Should_Repath_When_UnitSizeChanges()
    {
        var data = new bool[1, 3, 1];
        for (int i = 0; i < 3; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData("RepathUnitSize", data, Vector3d.Zero);

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One, out AStarPathRequest? request);

        request!.IsValid.Should().BeTrue();

        steer.ApplyPathRequest(request);
        steer.GetHeading(agent);  // frame 1: UnitSize=1, LOS path found

        steer.HasLineOfSightPath.Should().BeTrue("unit size 1 fits the 1-wide corridor directly");
        steer.ShouldMove.Should().BeTrue();

        // Increase unit size to 2 mid-path. The 1-wide corridor is impassable for UnitSize=2,
        // so the repath should fail and trigger arrival.
        bool invalidPathFired = false;
        steer.Events.OnInvalidPath += () => invalidPathFired = true;

        request.TrySetUnitSize((Fixed64)2);
        steer.GetHeading(agent);  // frame 2: detects size change, reruns path validation

        // Repath ran: the invalid-path event must have fired because UnitSize=2 cannot
        // fit through the 1-voxel-wide corridor, proving validation executed with the new size.
        invalidPathFired.Should().BeTrue("the repath with UnitSize=2 should fail on a 1-wide corridor");
        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeTrue();
        steer.TrailGuide.Should().BeNull();

        PathManager.UnloadChart("RepathUnitSize");
    }

    [Fact]
    public void NavSteering_Should_Handle_MissingPathGracefully()
    {
        var data = new bool[1, 3, 2]
        {
            {
                { true, true },
                { false, true },
                { true, true }
            }
        };

        PathTestFactory.RegisterFromData("MissingPath", data, Vector3d.Zero);

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        // get a guide
        steer.GetHeading(agent);

        steer.TrailGuide.Should().NotBeNull();

        // simulate lost guide
        steer.SetTrailGuide(null);
        steer.GetHeading(agent);

        // shouldn't throw, should just move with Vector3d.Zero
        steer.TargetDirection.Should().Be(Vector3d.Zero);

        PathManager.UnloadChart("MissingPath");
    }

    [Fact]
    public void NavSteering_Should_ReturnGuide_OnArrive()
    {
        var data = new bool[1, 3, 2]
        {
            {
                { true, true },
                { false, true },
                { true, true }
            }
        };
        PathTestFactory.RegisterFromData("ReturnGuide", data, Vector3d.Zero);

        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new NavSteering(agent.Radius);

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), out AStarPathRequest request);
        steer.ApplyPathRequest(request);

        // simulate successful guide retrieval
        steer.GetHeading(agent);

        var guide = steer.TrailGuide;
        guide.Should().NotBeNull();

        steer.Arrive();

        steer.TrailGuide.Should().BeNull();
        steer.ShouldMove.Should().BeFalse();

        PathManager.UnloadChart("ReturnGuide");
    }

    [Fact]
    public void ComputeCombinedSteering_Should_OnlyUseNeighborsFromSameMovementGroup()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("MovementGroupSteering", data, Vector3d.Zero);

        var leader = new MockSteerAgent(new Vector3d(0, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Speed = Fixed64.One
        };
        var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Speed = Fixed64.One
        };

        var leaderSteer = new NavSteering(leader.Radius)
        {
            BehaviorWeights = new GroupBehaviorWeights
            {
                Separation = Fixed64.One,
                Alignment = Fixed64.One,
                Cohesion = Fixed64.One,
                Avoidance = Fixed64.Zero
            }
        };

        var neighborSteer = new NavSteering(neighbor.Radius);

        AStarPathRequest.TryCreate(leader.Position, new Vector3d(4, 0, 0), out AStarPathRequest leaderRequest);
        leaderSteer.ApplyPathRequest(leaderRequest, groupId: 1);

        AStarPathRequest.TryCreate(neighbor.Position, new Vector3d(4, 0, 0), out AStarPathRequest neighborRequest);
        neighborSteer.ApplyPathRequest(neighborRequest, groupId: 2);

        TrailblazerWorldManager.TryGetGrid(neighbor.Position, out var grid);
        grid!.TryAddVoxelOccupant(neighbor);

        neighborSteer.GetHeading(neighbor);
        leaderSteer.GetHeading(leader);

        var force = leaderSteer.ComputeCombinedSteering(
            leader.Position,
            leader.Velocity,
            leader.Speed,
            leader.Radius,
            leader.GlobalId);

        force.Should().Be(Vector3d.Zero);

        AStarPathRequest.TryCreate(neighbor.Position, new Vector3d(4, 0, 0), out AStarPathRequest neighborRequest2);
        neighborSteer.ApplyPathRequest(neighborRequest2, groupId: 1);
        neighborSteer.GetHeading(neighbor);

        force = leaderSteer.ComputeCombinedSteering(
            leader.Position,
            leader.Velocity,
            leader.Speed,
            leader.Radius,
            leader.GlobalId);

        force.Should().NotBe(Vector3d.Zero);

        grid!.TryRemoveVoxelOccupant(neighbor);
        PathManager.UnloadChart("MovementGroupSteering");
    }

    [Fact]
    public void NavSteering_Should_PreserveFormationOffsets_ForCohesiveGroups()
    {
        var data = new bool[1, 7, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("GroupFormation", data, Vector3d.Zero);

        var firstAgent = new MockSteerAgent(new Vector3d(1, 0, 0)) { Speed = Fixed64.One };
        var secondAgent = new MockSteerAgent(new Vector3d(2, 0, 0)) { Speed = Fixed64.One };

        var firstSteer = new NavSteering(firstAgent.Radius);

        var secondSteer = new NavSteering(secondAgent.Radius);

        var sharedDestination = new Vector3d(4, 0, 0);
        AStarPathRequest.TryCreate(firstAgent.Position, sharedDestination, out AStarPathRequest firstRequest);
        firstSteer.ApplyPathRequest(firstRequest, groupId: 5);

        AStarPathRequest.TryCreate(secondAgent.Position, sharedDestination, out AStarPathRequest secondRequest);
        secondSteer.ApplyPathRequest(secondRequest, groupId: 5);

        firstSteer.GetHeading(firstAgent);
        secondSteer.GetHeading(secondAgent);

        firstSteer.Destination.Should().Be(new Vector3d((Fixed64)3.5f, Fixed64.Zero, Fixed64.Zero));
        secondSteer.Destination.Should().Be(new Vector3d((Fixed64)4.5f, Fixed64.Zero, Fixed64.Zero));

        PathManager.UnloadChart("GroupFormation");
    }

    [Fact]
    public void NavSteering_Should_FallBackToSharedDestination_WhenGroupIsSpreadOut()
    {
        var data = new bool[1, 8, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("GroupFallback", data, Vector3d.Zero);

        var firstAgent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Speed = Fixed64.One };
        var secondAgent = new MockSteerAgent(new Vector3d(5, 0, 0)) { Speed = Fixed64.One };

        var firstSteer = new NavSteering(firstAgent.Radius);
        var secondSteer = new NavSteering(secondAgent.Radius);

        var sharedDestination = new Vector3d(7, 0, 0);
        AStarPathRequest.TryCreate(firstAgent.Position, sharedDestination, out AStarPathRequest firstRequest);
        firstSteer.ApplyPathRequest(firstRequest, groupId: 7);

        AStarPathRequest.TryCreate(secondAgent.Position, sharedDestination, out AStarPathRequest secondRequest);
        secondSteer.ApplyPathRequest(secondRequest, groupId: 7);

        firstSteer.GetHeading(firstAgent);
        secondSteer.GetHeading(secondAgent);

        firstSteer.Destination.Should().Be(sharedDestination);
        secondSteer.Destination.Should().Be(sharedDestination);

        PathManager.UnloadChart("GroupFallback");
    }

    [Fact]
    public void TrailblazerManager_Reset_Should_ClearMovementGroupCoordinatorState()
    {
        var data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("MovementGroupReset", data, Vector3d.Zero);

        var leader = new MockSteerAgent(new Vector3d(0, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Speed = Fixed64.One
        };
        var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Speed = Fixed64.One
        };

        var leaderSteer = new NavSteering(leader.Radius)
        {
            BehaviorWeights = new GroupBehaviorWeights
            {
                Separation = Fixed64.One,
                Alignment = Fixed64.One,
                Cohesion = Fixed64.One,
                Avoidance = Fixed64.Zero
            }
        };

        var neighborSteer = new NavSteering(neighbor.Radius);

        AStarPathRequest.TryCreate(leader.Position, new Vector3d(4, 0, 0), out AStarPathRequest leaderRequest);
        leaderSteer.ApplyPathRequest(leaderRequest, groupId: 9);

        AStarPathRequest.TryCreate(neighbor.Position, new Vector3d(4, 0, 0), out AStarPathRequest neighborRequest);
        neighborSteer.ApplyPathRequest(neighborRequest, groupId: 9);

        TrailblazerWorldManager.TryGetGrid(neighbor.Position, out var grid);
        grid!.TryAddVoxelOccupant(neighbor);

        neighborSteer.GetHeading(neighbor);
        leaderSteer.GetHeading(leader);

        leaderSteer.ComputeCombinedSteering(
            leader.Position,
            leader.Velocity,
            leader.Speed,
            leader.Radius,
            leader.GlobalId).Should().NotBe(Vector3d.Zero);

        TrailblazerManager.Reset();

        leaderSteer.ComputeCombinedSteering(
            leader.Position,
            leader.Velocity,
            leader.Speed,
            leader.Radius,
            leader.GlobalId).Should().Be(Vector3d.Zero);

        grid!.TryRemoveVoxelOccupant(neighbor);
        PathManager.UnloadChart("MovementGroupReset");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldResetToIdleState_WhenRequestCannotBeRebuilt(bool useMemoryPack)
    {
        // Arrange: register a chart and give the NavSteering an active request
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(2, 0, 0);
        PathTestFactory.RegisterFromData("RecordDataIdleChart", data, start);

        var agent = new MockSteerAgent(start);
        var source = new NavSteering(agent.Radius);
        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        source.ApplyPathRequest(request);

        TrailblazerManager.Simulate();
        source.GetHeading(agent);

        source.ShouldMove.Should().BeTrue();

        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        // Unload chart so TryCreateRequest will fail to rebuild the AStar request
        PathManager.UnloadChart("RecordDataIdleChart");

        // Act: populate into a fresh NavSteering — request factory returns null → reset branch
        var target = new NavSteering(agent.Radius);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        // Assert: steering reset to idle
        target.ShouldMove.Should().BeFalse();
        target.IsStuck.Should().BeFalse();
        target.HasLineOfSightPath.Should().BeFalse();
        target.Destination.Should().Be(Vector3d.Zero);
        target.TargetDirection.Should().Be(Vector3d.Zero);
        target.CurrentRequest.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldScheduleRepath_WhenRequestLoadedWithoutGuide(bool useMemoryPack)
    {
        // Arrange: register a chart and give the NavSteering an active request WITHOUT simulating
        // (so no guide has been assigned yet — HasGuide will be false in the record)
        var data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        var start = new Vector3d(0, 0, 0);
        var end = new Vector3d(2, 0, 0);
        PathTestFactory.RegisterFromData("RecordDataNoGuideChart", data, start);

        var agent = new MockSteerAgent(start);
        var source = new NavSteering(agent.Radius);
        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        source.ApplyPathRequest(request);

        // ShouldMove = true, _currentRequest != null, no guide, no LOS
        source.ShouldMove.Should().BeTrue();
        source.TrailGuide.Should().BeNull();

        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        // Act: populate — Kind=AStar, HasGuide=false → else-if branch sets _shouldRequestPathThisFrame=true
        var target = new NavSteering(agent.Radius);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        // Assert: request rebuilt, repath scheduled
        target.ShouldMove.Should().BeTrue();
        target.CurrentRequest.Should().NotBeNull();
        target.TrailGuide.Should().BeNull();

        PathManager.UnloadChart("RecordDataNoGuideChart");
    }

    [Fact]
    public void ApplyPathRequest_ShouldTreatNullRequestAsArrival()
    {
        var steer = new NavSteering(Fixed64.One);

        steer.ApplyPathRequest(null);

        steer.ShouldMove.Should().BeFalse();
        steer.IsAtDestination.Should().BeTrue();
        steer.CurrentRequest.Should().BeNull();
        steer.TargetDirection.Should().Be(Vector3d.Zero);
        steer.Destination.Should().Be(Vector3d.Zero);
    }

    [Fact]
    public void ApplyPathRequest_ShouldExposeGroupIndexAndFireMoveRequestEvent()
    {
        bool[,,] data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("ApplyPathRequestEvents", data, Vector3d.Zero);

        var steer = new TestableNavSteering();
        bool fired = false;
        steer.Events.OnMoveRequestApplied += () => fired = true;

        AStarPathRequest.TryCreate(Vector3d.Zero, new Vector3d(2, 0, 0), out AStarPathRequest request);
        steer.ApplyPathRequest(request, groupId: 3);

        fired.Should().BeTrue();
        steer.GetGroupIndex().Should().Be(0);

        PathManager.UnloadChart("ApplyPathRequestEvents");
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldThrowForNullOwner()
    {
        var steer = new NavSteering(Fixed64.One);

        steer.Invoking(s => s.PrewarmMovementGroup(null!))
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("navigator");
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldSeedCoordinatorMembershipForGroupedRequests()
    {
        bool[,,] data = new bool[1, 6, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("PrewarmMovementGroup", data, Vector3d.Zero);

        var owner = new MockSteerAgent(Vector3d.Zero);
        var steer = new NavSteering(owner.Radius);
        AStarPathRequest.TryCreate(owner.Position, new Vector3d(4, 0, 0), out AStarPathRequest request);

        steer.ApplyPathRequest(request, groupId: 12);
        steer.PrewarmMovementGroup(owner);

        MovementGroupCoordinator.IsNeighbor(
                new MovementGroupSession { GroupId = 12 },
                owner.GlobalId,
                request.TargetPosition,
                TrailblazerManager.FrameCount)
            .Should().BeTrue();

        PathManager.UnloadChart("PrewarmMovementGroup");
    }

    [Fact]
    public void PrewarmMovementGroup_ShouldNoOp_WhenSessionIsNotActive()
    {
        var owner = new MockSteerAgent(Vector3d.Zero);
        var steer = new TestableNavSteering(owner.Radius);

        steer.PrewarmMovementGroup(owner);

        MovementGroupCoordinator.IsNeighbor(
                new MovementGroupSession { GroupId = 77 },
                owner.GlobalId,
                Vector3d.Zero,
                TrailblazerManager.FrameCount)
            .Should().BeFalse();
    }

    [Fact]
    public void GetHeading_ShouldRaiseInvalidPath_WhenVolumeFallbackValidationFails()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(2, 0, 0));
        AddObstacle(new Vector3d(1, 0, 0));


        var agent = new MockSteerAgent(Vector3d.Zero);
        var steer = new SequencedPathValidationNavSteering(agent.Radius, true, false);

        VolumePathRequest.TryCreate(
            agent.Position,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            out VolumePathRequest request).Should().BeTrue();
        steer.ApplyPathRequest(request);

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
        var steer = new NavSteering(Fixed64.One);
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
        var steer = new NavSteering(Fixed64.One);
        bool arrived = false;
        steer.Events.OnArrive += () => arrived = true;

        steer.Arrive();

        arrived.Should().BeTrue();
        steer.IsAtDestination.Should().BeTrue();
    }

    [Fact]
    public void ComputeCombinedSteering_ShouldUseRightSideDodge_WhenNeighborIsBehind()
    {
        var steer = new NavSteering();
        var agent = new MockSteerAgent(new Vector3d(0, 0, 0))
        {
            Velocity = new Vector3d(1, 0, 0),
            Speed = Fixed64.One
        };
        var neighbor = new MockSteerAgent(new Vector3d(-1, 0, 0))
        {
            Velocity = Vector3d.Zero
        };

        TrailblazerWorldManager.TryGetGrid(neighbor.Position, out var grid);
        grid!.TryAddVoxelOccupant(neighbor);

        Vector3d force = steer.ComputeCombinedSteering(
            agent.Position,
            agent.Velocity,
            agent.Speed,
            agent.Size,
            agent.GlobalId);

        force.z.Should().BeLessThan(Fixed64.Zero);

        grid!.TryRemoveVoxelOccupant(neighbor);
    }

    [Fact]
    public void RoundTrip_ShouldScheduleRepath_WhenGuideRebuildFails()
    {
        var data = new bool[1, 3, 2]
        {
            {
                { true, true },
                { false, true },
                { true, true }
            }
        };

        var start = Vector3d.Zero;
        var end = new Vector3d(2, 0, 0);
        PathTestFactory.RegisterFromData("RecordDataGuideFailure", data, start);

        var agent = new MockSteerAgent(start);
        var source = new NavSteering(agent.Radius);
        AStarPathRequest.TryCreate(start, end, out AStarPathRequest request);
        source.ApplyPathRequest(request);
        TrailblazerManager.Simulate();
        source.GetHeading(agent);
        source.TrailGuide.Should().NotBeNull();

        string payload = JsonRecordSerializer.Serialize(source, writeIndented: true);

        PathManager.UnloadChart("RecordDataGuideFailure");
        bool[,,] blocked = new bool[1, 3, 1]
        {
            {
                { true },
                { false },
                { true }
            }
        };
        PathTestFactory.RegisterFromData("RecordDataGuideFailure", blocked, start);

        var target = new NavSteering(agent.Radius);
        JsonRecordSerializer.Populate(target, payload);

        target.ShouldMove.Should().BeTrue();
        target.CurrentRequest.Should().NotBeNull();
        target.TrailGuide.Should().BeNull();

        PathManager.UnloadChart("RecordDataGuideFailure");
    }

    private static void AddObstacle(Vector3d position)
    {
        TrailblazerWorldManager.TryGetGridAndVoxel(position, out VoxelGrid? grid, out Voxel? voxel).Should().BeTrue();
        grid!.TryAddObstacle(
            voxel!,
            new BoundsKey(position, position)).Should().BeTrue();
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "NavSteeringWater");
    }

    private static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Gas, "NavSteeringOpen");
    }

    private sealed class TestableNavSteering : NavSteering
    {
        public TestableNavSteering(Fixed64 radius = default) : base(radius) { }

        public void ForceMissingRequestState(Vector3d destination)
        {
            Destination = destination;
            TargetDirection = new Vector3d(1, 0, 0);
            ShouldMove = true;
            IsAtDestination = false;
        }

        public int GetGroupIndex() => GroupIndex;

        public void ForceHeadingState(Vector3d targetDirection, Fixed64 distanceToTarget)
        {
            TargetDirection = targetDirection;
            LastTargetDirection = targetDirection;
            ShouldMove = true;
            IsAtDestination = false;
            HasLineOfSightPath = false;
            _distanceToTarget = distanceToTarget;
        }

        public void InvokeSetDeceleration(Vector3d acceleration, Fixed64 speed) => SetDeceleration(acceleration, speed);
    }

    private sealed class FallbackNavSteering : NavSteering
    {
        private readonly Vector3d _movementDirection;

        public FallbackNavSteering(Vector3d movementDirection, Fixed64 radius)
            : base(radius)
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
            : base(radius)
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
}
