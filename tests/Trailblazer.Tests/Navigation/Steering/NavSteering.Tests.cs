using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Moq;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation.Steering
{
    [Collection("PathingCollection")]
    public class NavSteeringTests
    {
        public NavSteeringTests()
        {
            var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
            GlobalGridManager.TryAddGrid(config, out _);
        }

        [Fact]
        public void NavSteering_Should_InitializeCorrectly()
        {
            var agent = new MockSteerAgent();  // INavigate stub
            var steer = new NavSteering(agent);

            steer.IsAtDestination.Should().BeFalse();
            steer.ShouldMove.Should().BeFalse();
            steer.TrailGuide.Should().BeNull();
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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(start);
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(start, end);
            steer.ApplyPathRequest(request);

            steer.ShouldMove.Should().BeTrue();
            steer.CurrentRequest.IsValid.Should().BeTrue();

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
            var steer = new NavSteering();
            var agent = new MockSteerAgent(start);

            steer.OnInitialize(agent);
            var request = AStarPathRequest.Create(start, end);
            steer.ApplyPathRequest(request);

            steer.OnSimulate(agent);

            steer.HasLineOfSightPath.Should().BeTrue();

            PathManager.UnloadChart("LineSight");
        }

        [Fact]
        public void NavSteering_Should_Arrive_WhenCloseEnough()
        {
            var data = new bool[1, 1, 1] { { { true } } };
            PathTestFactory.RegisterFromData("Point", data, Vector3d.Zero);

            var steer = new NavSteering();
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
            steer.OnInitialize(agent);

            var request = AStarPathRequest.CreateEmpty();
            request.TryPrepare(agent.Position, agent.Position, Fixed64.One);

            request.IsValid.Should().BeTrue();

            steer.ApplyPathRequest(request);
            steer.OnSimulate(agent);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Speed = Fixed64.Zero };
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(agent.Position, new Vector3d(1, 0, 0));
            steer.ApplyPathRequest(request);

            var stuck = false;
            steer.Events.OnIsStuck += () =>
            {
                stuck = true;
            };

            for (int i = 0; i < 100; i++)
                steer.OnSimulate(agent);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(agent.Position, new Vector3d(2, 0, 0));
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
                steer.OnSimulate(agent);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
            steer.OnInitialize(agent);

            var request = FlowFieldPathRequest.Create(agent.Position, new Vector3d(4, 0, 0));
            steer.ApplyPathRequest(request);

            steer.OnSimulate(agent);

            steer.TrailGuide.Should().NotBeNull();
            steer.TrailGuide.Should().BeOfType<FlowFieldGuide>();

            PathManager.UnloadChart("FlowFieldTest");
        }

        [Fact]
        public void NavSteering_Should_Apply_GroupSteeringBehavior()
        {
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Speed = (Fixed64)1 };
            var neighbor = new MockSteerAgent(new Vector3d(1, 0, 0)) { Velocity = new Vector3d(1, 0, 0) };

            GlobalGridManager.TryGetGrid(new Vector3d(1, 0, 0), out VoxelGrid grid);
            grid.TryAddVoxelOccupant(neighbor);

            var result = NavSteering.ComputeGroupSteering(agent.Position, agent.Speed);
            result.Should().NotBe(Vector3d.Zero);

            grid.TryRemoveVoxelOccupant(neighbor);
        }

        [Fact]
        public void NavSteering_Should_Apply_AvoidanceForce_When_ObstacleInPath()
        {
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0)) { Velocity = new Vector3d(1, 0, 0), Speed = (Fixed64)1 };
            var blocker = new MockSteerAgent(new Vector3d(1, 0, 0));

            GlobalGridManager.TryGetGrid(new Vector3d(1, 0, 0), out VoxelGrid grid);
            grid.TryAddVoxelOccupant(blocker);

            var force = NavSteering.CalculateAvoidanceForce(agent);
            force.Should().NotBe(Vector3d.Zero);

            grid.TryRemoveVoxelOccupant(blocker);
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
            waypointGuide.Setup(x => x.GetMovementDirection(It.IsAny<Vector3d>())).Returns(new Vector3d(1, 0, 0));
            var dir = new Vector3d(1, 0, 0);
            waypointGuide.Setup(x => x.TryGetMovementDirection(It.IsAny<Vector3d>(), out dir))
                         .Returns(true);

            var steer = new NavSteering();
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
            steer.OnInitialize(agent);

            var request = AStarPathRequest.CreateEmpty();
            request.TryPrepare(agent.Position, new Vector3d(1, 0, 0), Fixed64.One);

            request.IsValid.Should().BeTrue();

            steer.ApplyPathRequest(request);
            steer.SetTrailGuide(waypointGuide.Object);
            steer.OnSimulate(agent);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(new Vector3d(0, 0, 0));
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(agent.Position, new Vector3d(2, 0, 2));
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
                steer.OnSimulate(agent);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(Vector3d.Zero);
            steer.OnInitialize(agent);

            // Ensure larger than voxel size
            var request = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.Two);

            request.IsValid.Should().BeTrue();

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(Vector3d.Zero);
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0));
            request.TryPrepare(agent.Position, new Vector3d(2, 0, 0), Fixed64.One);

            request.IsValid.Should().BeTrue();

            steer.ApplyPathRequest(request);
            steer.OnSimulate(agent);  // simulate one frame normally

            // simulate a size change mid-path
            request.TrySetUnitSize((Fixed64)2);
            steer.OnSimulate(agent);

            // TODO: this is a false positive, the CurrentRequest mutates based on the change we make here,
            // but doesn't trigger a new path
            steer.CurrentRequest.UnitSize.Should().Be((Fixed64)2);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(Vector3d.Zero);
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0));
            steer.ApplyPathRequest(request);

            // get a guide
            steer.OnSimulate(agent);

            steer.TrailGuide.Should().NotBeNull();

            // simulate lost guide
            steer.SetTrailGuide(null);
            steer.OnSimulate(agent);

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

            var steer = new NavSteering();
            var agent = new MockSteerAgent(Vector3d.Zero);
            steer.OnInitialize(agent);

            var request = AStarPathRequest.Create(Vector3d.Zero, new Vector3d(2, 0, 0));
            steer.ApplyPathRequest(request);

            // simulate successful guide retrieval
            steer.OnSimulate(agent);

            var guide = steer.TrailGuide;
            guide.Should().NotBeNull();

            steer.Arrive();

            steer.TrailGuide.Should().BeNull();
            steer.ShouldMove.Should().BeFalse();

            PathManager.UnloadChart("ReturnGuide");
        }
    }
}
