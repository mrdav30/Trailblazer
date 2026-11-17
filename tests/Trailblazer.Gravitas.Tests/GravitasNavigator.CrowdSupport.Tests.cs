using FixedMathSharp;
using Gravitas.Queries;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigatorCrowdSupportTests
{
    private static readonly PhysicsQueryBudget Useful = new(8192, 8192, 8192);
    private static readonly PhysicsQueryBudget Pressured = new(8192, 8192, 1);

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    public void PlanarCrowd_ShouldReplayCompleteBatchesAndRecoverFromPressureAndReadiness(int actorCount)
    {
        using var source = new GravitasCrowdScenarioFixture2D(actorCount);
        using var bounded = new GravitasCrowdScenarioFixture2D(actorCount);
        var scratch = new PhysicsQueryScratch(128, 128);
        bounded.Configure(Useful, scratch);
        for (int frame = 0; frame < 4; frame++)
        {
            source.Step();
            bounded.Step();
            source.Verify();
            bounded.Verify();
            Assert.Equal(source.Physics.ComputeReplayHash(), bounded.Physics.ComputeReplayHash());
            for (int i = 0; i < actorCount; i++)
            {
                Assert.Equal(source.Navigators[i].Pose, bounded.Navigators[i].Pose);
                Assert.Equal(source.Navigators[i].Velocity, bounded.Navigators[i].Velocity);
                Assert.Equal(source.Navigators[i].Support, bounded.Navigators[i].Support);
                Assert.Equal(PhysicsQueryExecutionStatus.Completed, bounded.Navigators[i].LastSupportQueryExecutionStatus);
            }
        }

        for (int i = 0; i < actorCount; i++)
            bounded.Navigators[i].ConfigureSupportQueryBudget(i % 2 == 0 ? Pressured : Useful, scratch);
        bounded.Step();
        for (int i = 0; i < actorCount; i++)
        {
            var navigator = bounded.Navigators[i];
            Assert.Equal(navigator.Body.Position, navigator.Pose.Center);
            Assert.Equal(i % 2 == 0 ? NavigationSupportStatus.Unavailable : NavigationSupportStatus.Found,
                navigator.SupportStatus);
            Assert.Equal(i % 2 == 0 ? PhysicsQueryExecutionStatus.WorkBudgetExceeded : PhysicsQueryExecutionStatus.Completed,
                navigator.LastSupportQueryExecutionStatus);
            Assert.Equal(i % 2 != 0, navigator.IsGrounded);
            Assert.Equal(i % 2 == 0 ? (int?)null : 71, navigator.Support?.Carrier?.StableId);
        }
        bounded.Configure(Useful, scratch);
        bounded.Step();
        bounded.Verify();

        bounded.Configure(Pressured, scratch);
        for (int frame = 0; frame < 4; frame++)
        {
            if (frame > 0)
                bounded.Floor.Agent.Transform.LocalPosition += Vector3d.Right * Fixed64.FromFraction(1, 64);
            bounded.Prepare();
            if (frame > 0)
            {
                for (int i = 0; i < actorCount; i++)
                {
                    Assert.True(bounded.Bodies[i].Agent.Transform.WorldPosition.Z < bounded.Navigators[i].Pose.Center.Y);
                    Assert.Equal((Fixed64)(i * 2), bounded.Bodies[i].Agent.Transform.WorldPosition.X);
                }
            }
            bounded.Resolve();
            foreach (var navigator in bounded.Navigators)
            {
                Assert.Equal(navigator.Body.Position, navigator.Pose.Center);
                Assert.Equal(NavigationSupportStatus.Unavailable, navigator.SupportStatus);
                Assert.False(navigator.IsGrounded);
                Assert.Null(navigator.Support);
                Assert.Equal(PhysicsQueryExecutionStatus.WorkBudgetExceeded, navigator.LastSupportQueryExecutionStatus);
                Assert.True(navigator.LastSupportQueryWork!.Value.Discovery > 0);
                Assert.InRange(navigator.LastSupportQueryWork.Value.Geometry, 0UL, Pressured.GeometryLimit);
            }
        }

        bounded.Configure(Useful, scratch);
        bounded.Step();
        bounded.Verify();
        bounded.Prepare();
        bounded.Resolve(invalidateView: true);
        foreach (var navigator in bounded.Navigators)
        {
            Assert.Equal(navigator.Body.Position, navigator.Pose.Center);
            Assert.Equal(NavigationSupportStatus.Unavailable, navigator.SupportStatus);
            Assert.Null(navigator.Support);
            Assert.Equal(PhysicsQueryExecutionStatus.WorldNotReady, navigator.LastSupportQueryExecutionStatus);
            Assert.Equal(default(PhysicsQueryWork), navigator.LastSupportQueryWork);
        }
        bounded.Step();
        bounded.Verify();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    public void SpatialCrowd_ShouldReplayCompleteBatchesAndRecoverFromPressureAndReadiness(int actorCount)
    {
        using var source = new GravitasCrowdScenarioFixture3D(actorCount);
        using var bounded = new GravitasCrowdScenarioFixture3D(actorCount);
        var scratch = new PhysicsQueryScratch(128, 128);
        bounded.Configure(Useful, scratch);
        for (int frame = 0; frame < 4; frame++)
        {
            source.Step();
            bounded.Step();
            source.Verify();
            bounded.Verify();
            Assert.Equal(source.Physics.ComputeReplayHash(), bounded.Physics.ComputeReplayHash());
            for (int i = 0; i < actorCount; i++)
            {
                var expected = source.Navigators[i];
                var actual = bounded.Navigators[i];
                Assert.Equal(expected.Position, actual.Position);
                Assert.Equal(expected.Velocity, actual.Velocity);
                Assert.Equal(expected.Rotation, actual.Rotation);
                Assert.Equal(expected.Motor!.Handler.Platform.ActivePlatform, actual.Motor!.Handler.Platform.ActivePlatform);
                Assert.Equal(expected.Motor.Handler.Platform.ScoutLocalPoint, actual.Motor.Handler.Platform.ScoutLocalPoint);
                Assert.Equal(PhysicsQueryExecutionStatus.Completed, actual.LastSupportQueryExecutionStatus);
            }
        }

        for (int i = 0; i < actorCount; i++)
            bounded.Navigators[i].ConfigureSupportQueryBudget(i % 2 == 0 ? Pressured : Useful, scratch);
        bounded.Step();
        for (int i = 0; i < actorCount; i++)
        {
            var navigator = bounded.Navigators[i];
            Assert.Equal(navigator.Body.Position3d, navigator.Position);
            Assert.Equal(i % 2 == 0 ? NavigationSupportStatus.Unavailable : NavigationSupportStatus.Found,
                navigator.SupportStatus);
            Assert.Equal(i % 2 == 0 ? PhysicsQueryExecutionStatus.WorkBudgetExceeded : PhysicsQueryExecutionStatus.Completed,
                navigator.LastSupportQueryExecutionStatus);
            Assert.Equal(i % 2 != 0, navigator.Motor!.IsOnSolid);
            Assert.Equal(i % 2 == 0 ? (int?)null : 71, navigator.Motor.Handler.Platform.ActivePlatform?.Id);
        }
        bounded.Configure(Useful, scratch);
        bounded.Step();
        bounded.Verify();

        bounded.Configure(Pressured, scratch);
        for (int frame = 0; frame < 4; frame++)
        {
            if (frame > 0)
                bounded.Floor.Transform.LocalPosition += Vector3d.Right * Fixed64.FromFraction(1, 64);
            bounded.Prepare();
            if (frame > 0)
            {
                for (int i = 0; i < actorCount; i++)
                {
                    Assert.True(bounded.Bodies[i].PositionTransform.WorldPosition.Y < bounded.Navigators[i].Position.Y);
                    Assert.Equal(source.Navigators[i].Position.X, bounded.Bodies[i].PositionTransform.WorldPosition.X);
                }
            }
            bounded.Resolve();
            foreach (var navigator in bounded.Navigators)
            {
                Assert.Equal(navigator.Body.Position3d, navigator.Position);
                Assert.Equal(NavigationSupportStatus.Unavailable, navigator.SupportStatus);
                Assert.True(navigator.Motor!.IsInGas);
                Assert.Null(navigator.Motor.CurrentState.GroundState);
                Assert.Null(navigator.Motor.Handler.Platform.ActivePlatform);
                Assert.False(navigator.Motor.TraversalInProgress);
                Assert.Equal(PhysicsQueryExecutionStatus.WorkBudgetExceeded, navigator.LastSupportQueryExecutionStatus);
                Assert.True(navigator.LastSupportQueryWork!.Value.Discovery > 0);
                Assert.InRange(navigator.LastSupportQueryWork.Value.Geometry, 0UL, Pressured.GeometryLimit);
            }
        }

        bounded.Configure(Useful, scratch);
        bounded.Step();
        bounded.Verify();
        bounded.Prepare();
        bounded.Resolve(invalidateView: true);
        foreach (var navigator in bounded.Navigators)
        {
            Assert.Equal(navigator.Body.Position3d, navigator.Position);
            Assert.Equal(NavigationSupportStatus.Unavailable, navigator.SupportStatus);
            Assert.Null(navigator.Motor!.CurrentState.GroundState);
            Assert.Null(navigator.Motor.Handler.Platform.ActivePlatform);
            Assert.Equal(PhysicsQueryExecutionStatus.WorldNotReady, navigator.LastSupportQueryExecutionStatus);
            Assert.Equal(default(PhysicsQueryWork), navigator.LastSupportQueryWork);
        }
        bounded.Step();
        bounded.Verify();
    }
}
